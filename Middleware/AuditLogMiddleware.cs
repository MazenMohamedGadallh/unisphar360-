using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Middleware;

public class AuditLogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLogMiddleware> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Paths that are too noisy to log
    private static readonly HashSet<string> IgnoredPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health", "/swagger", "/favicon.ico"
    };

    public AuditLogMiddleware(RequestDelegate next, ILogger<AuditLogMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var path = context.Request.Path.Value ?? "";

        // Skip noisy paths
        if (IgnoredPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var isChatStream = HttpMethods.IsPost(context.Request.Method)
            && context.Request.Path.StartsWithSegments("/api/chat", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith("/messages", StringComparison.OrdinalIgnoreCase);

        // Capture response body (except SSE streams, which pass through)
        var originalBodyStream = context.Response.Body;
        MemoryStream? responseBody = null;
        if (!isChatStream)
        {
            responseBody = new MemoryStream();
            context.Response.Body = responseBody;
        }

        Exception? exception = null;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            sw.Stop();

            try
            {
                var userId = context.User.GetUserId();
                if (userId > 0) // Only log authenticated requests
                {
                    var audit = new AuditLog
                    {
                        UserId = userId,
                        Method = context.Request.Method,
                        Path = path,
                        StatusCode = context.Response.StatusCode,
                        DurationMs = (int)sw.ElapsedMilliseconds,
                        Timestamp = DateTime.UtcNow,
                        IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "",
                        UserAgent = context.Request.Headers.UserAgent.ToString()
                    };

                    // Written after the response is sent so the user never waits on the SQLite write lock.
                    var scopes = context.RequestServices.GetRequiredService<IServiceScopeFactory>();
                    context.Response.OnCompleted(async () =>
                    {
                        try
                        {
                            using var scope = scopes.CreateScope();
                            var auditDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                            auditDb.AuditLogs.Add(audit);
                            await auditDb.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to write audit log for {Path}", path);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write audit log for {Path}", path);
            }

            if (responseBody is not null)
            {
                responseBody.Seek(0, SeekOrigin.Begin);
                context.Response.Body = originalBodyStream;
                await responseBody.CopyToAsync(originalBodyStream);
                responseBody.Dispose();
            }
        }
    }
}

public static class AuditLogMiddlewareExtensions
{
    public static IApplicationBuilder UseAuditLogMiddleware(this IApplicationBuilder app) =>
        app.UseMiddleware<AuditLogMiddleware>();
}
