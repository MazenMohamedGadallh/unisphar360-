using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

/// <summary>AI chat (OpenAI + Claude). Every query is scoped to the JWT user id.</summary>
[Authorize]
[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ChatService _chat;
    private readonly IValidator<SendChatMessageRequest> _messageValidator;
    private readonly IValidator<RenameConversationRequest> _renameValidator;

    public ChatController(
        AppDbContext db,
        ChatService chat,
        IValidator<SendChatMessageRequest> messageValidator,
        IValidator<RenameConversationRequest> renameValidator)
    {
        _db = db;
        _chat = chat;
        _messageValidator = messageValidator;
        _renameValidator = renameValidator;
    }

    /// <summary>Only providers that are currently available (keys set, Ollama up).</summary>
    [HttpGet("providers")]
    public async Task<ActionResult<IReadOnlyList<ChatProviderDto>>> Providers(CancellationToken ct) =>
        Ok(await _chat.GetProvidersAsync(ct));

    /// <summary>All providers with availability + reason (no keys). Used by the UI
    /// to explain why the dropdown is empty.</summary>
    [HttpGet("providers/status")]
    public async Task<ActionResult<IReadOnlyList<ChatProviderStatusDto>>> ProviderStatuses(CancellationToken ct) =>
        Ok(await _chat.GetProviderStatusesAsync(ct));

    [HttpGet("conversations")]
    public async Task<IReadOnlyList<ChatConversationDto>> Conversations()
    {
        var userId = User.GetUserId();
        var conversations = await _db.ChatConversations.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAtUtc)
            .ToListAsync();

        var result = new List<ChatConversationDto>(conversations.Count);
        foreach (var c in conversations)
        {
            var count = await _db.ChatMessages.CountAsync(m => m.ConversationId == c.Id);
            result.Add(new ChatConversationDto(c.Id, c.Title, c.Provider, c.UpdatedAtUtc, count));
        }
        return result;
    }

    [HttpGet("conversations/{id:int}")]
    public async Task<ActionResult<ChatConversationDetailDto>> Conversation(int id)
    {
        var userId = User.GetUserId();
        var conversation = await _db.ChatConversations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (conversation is null) return NotFound();

        var messages = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.Id)
            .Select(m => new ChatMessageDto(m.Id, m.Role, m.Content, m.CreatedAtUtc))
            .ToListAsync();

        return new ChatConversationDetailDto(
            conversation.Id, conversation.Title, conversation.Provider,
            conversation.UpdatedAtUtc, messages);
    }

    [HttpDelete("conversations/{id:int}")]
    public async Task<IActionResult> DeleteConversation(int id)
    {
        var userId = User.GetUserId();
        var conversation = await _db.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (conversation is null) return NotFound();

        _db.ChatConversations.Remove(conversation);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("conversations/{id:int}")]
    public async Task<IActionResult> RenameConversation(int id, RenameConversationRequest req)
    {
        var validation = await _renameValidator.ValidateAsync(req);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        var userId = User.GetUserId();
        var conversation = await _db.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (conversation is null) return NotFound();

        conversation.Title = req.Title.Trim();
        conversation.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Streams the assistant reply with Server-Sent Events.
    /// Use id "new" to start a conversation (created + auto-titled).</summary>
    [HttpPost("conversations/{id}/messages")]
    [EnableRateLimiting("chat-per-minute")]
    public async Task SendMessage(string id, SendChatMessageRequest req, CancellationToken ct)
    {
        var validation = await _messageValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            }, ct);
            return;
        }

        var userId = User.GetUserId();
        var regenerate = string.Equals(
            HttpContext.Request.Query["regenerate"].ToString(), "true",
            StringComparison.OrdinalIgnoreCase);

        PreparedTurn prepared;
        try
        {
            prepared = await _chat.PrepareTurnAsync(userId, id, req.Provider, req.Content, regenerate, ct);
        }
        catch (ChatLimitExceededException ex)
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await Response.WriteAsJsonAsync(new { message = ex.Message }, ct);
            return;
        }
        catch (ChatProviderException ex)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await Response.WriteAsJsonAsync(new { message = ex.Message }, ct);
            return;
        }
        catch (KeyNotFoundException)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Append("X-Conversation-Id", prepared.Conversation.Id.ToString());
        await Response.Body.FlushAsync(ct);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, HttpContext.RequestAborted);
        var linkedToken = linked.Token;
        var reply = new StringBuilder();
        try
        {
            await foreach (var token in prepared.Provider
                .StreamAsync(prepared.Request, linkedToken)
                .WithCancellation(linkedToken))
            {
                reply.Append(token);
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(token)}\n\n", linkedToken);
                await Response.Body.FlushAsync(linkedToken);
            }
            await Response.WriteAsync("data: [DONE]\n\n", linkedToken);
            await Response.Body.FlushAsync(linkedToken);

            await _chat.CompleteAssistantMessageAsync(userId, prepared.Conversation.Id,
                prepared.Provider.Name, prepared.Request.Model, reply.ToString(), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Client went away (Stop button / disconnect): stop quietly.
        }
        catch (ChatProviderException ex)
        {
            await Response.WriteAsync(
                $"event: error\ndata: {JsonSerializer.Serialize(ex.Message)}\n\n",
                CancellationToken.None);
        }
    }
}
