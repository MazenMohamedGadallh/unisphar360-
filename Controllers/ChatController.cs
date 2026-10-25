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
    private readonly IRagClient _rag;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        AppDbContext db,
        ChatService chat,
        IValidator<SendChatMessageRequest> messageValidator,
        IValidator<RenameConversationRequest> renameValidator,
        IRagClient rag,
        ILogger<ChatController> logger)
    {
        _db = db;
        _chat = chat;
        _messageValidator = messageValidator;
        _renameValidator = renameValidator;
        _rag = rag;
        _logger = logger;
    }

    /// <summary>Only providers that are currently available (keys set, Ollama up).
    /// Students also see the course-grounded RAG assistant.</summary>
    [HttpGet("providers")]
    public async Task<ActionResult<IReadOnlyList<ChatProviderDto>>> Providers(CancellationToken ct)
    {
        var providers = (await _chat.GetProvidersAsync(ct)).ToList();
        if (User.IsInRole("Student") && _rag.IsConfigured
            && !providers.Any(p => string.Equals(p.Name, "rag", StringComparison.OrdinalIgnoreCase)))
        {
            providers.Insert(0, new ChatProviderDto("rag", _rag.DisplayName, _rag.ModelName));
        }
        return Ok(providers);
    }

    /// <summary>All providers with availability + reason (no keys). Used by the UI
    /// to explain why the dropdown is empty.</summary>
    [HttpGet("providers/status")]
    public async Task<ActionResult<IReadOnlyList<ChatProviderStatusDto>>> ProviderStatuses(CancellationToken ct)
    {
        var providers = (await _chat.GetProviderStatusesAsync(ct)).ToList();
        if (User.IsInRole("Student") && _rag.IsConfigured
            && !providers.Any(p => string.Equals(p.Name, "rag", StringComparison.OrdinalIgnoreCase)))
        {
            providers.Insert(0, new ChatProviderStatusDto(
                "rag", _rag.DisplayName, _rag.ModelName, true, ""));
        }
        return Ok(providers);
    }

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
            .Select(m => new ChatMessageDto(m.Id, m.Role, m.Content, m.Provider, m.Model, m.CreatedAtUtc))
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
        var providerError = (string?)null;
        var wasCancelled = false;
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

            if (reply.Length == 0)
            {
                // Empty answer (e.g. the model refused or the provider sent
                // only reasoning/keep-alives): tell the user the real reason,
                // save nothing (an empty assistant turn would be noise).
                _logger.LogWarning("Chat provider {Provider} ({Model}) ended the stream without answer content",
                    prepared.Provider.Name, prepared.Request.Model);
                await WriteErrorEventAsync("The AI provider returned an empty answer. Please try again.");
                return;
            }

            await Response.WriteAsync("data: [DONE]\n\n", linkedToken);
            await Response.Body.FlushAsync(linkedToken);
        }
        catch (OperationCanceledException)
        {
            // Client went away (Stop button / disconnect): keep what the
            // user already saw and persist it below.
            wasCancelled = true;
        }
        catch (ChatProviderException ex)
        {
            // Real safe reason from the provider mapping (already logged
            // server-side with the response body, truncated to 500 chars).
            providerError = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TimeoutException)
        {
            // The connection dropped mid-answer: same treatment as a provider
            // error (safe reason to the UI, partial text kept below) instead
            // of letting the exception escape after the SSE response started.
            _logger.LogWarning(ex, "Chat provider {Provider} ({Model}) stream interrupted",
                prepared.Provider.Name, prepared.Request.Model);
            providerError = "The AI provider connection dropped while answering. Please try again.";
        }

        // Persist whatever was generated: the full answer, or the partial
        // text when the user pressed Stop. Regenerate replaces the last
        // assistant message instead of appending a duplicate.
        if (reply.Length > 0)
        {
            try
            {
                await _chat.CompleteAssistantMessageAsync(
                    userId, prepared.Conversation.Id, prepared.Provider.Name,
                    prepared.Request.Model, reply.ToString(), regenerate, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Saving the assistant reply failed (conversation {ConversationId})",
                    prepared.Conversation.Id);
            }
        }

        if (providerError is not null)
            await WriteErrorEventAsync(providerError);
        else if (wasCancelled)
            _logger.LogInformation("Chat stream stopped by the user (conversation {ConversationId}, {Chars} chars kept)",
                prepared.Conversation.Id, reply.Length);
    }

    /// <summary>SSE error frame with the JSON shape the chat UI reads:
    /// {"error":"..."}. Best-effort: the client may already be gone.</summary>
    private async Task WriteErrorEventAsync(string message)
    {
        try
        {
            await Response.WriteAsync(
                $"event: error\ndata: {JsonSerializer.Serialize(new { error = message })}\n\n",
                CancellationToken.None);
            await Response.Body.FlushAsync(CancellationToken.None);
        }
        catch
        {
            // Client disconnected: nothing to deliver.
        }
    }
}
