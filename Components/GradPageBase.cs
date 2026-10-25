using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using UniSphere.Api.Services;

namespace UniSphere.Api.Components;

/// <summary>
/// Shared plumbing for graduate pages: role guard (bounces non-graduates to
/// the student dashboard), typed GET/POST helpers with consistent error
/// extraction from { error } / ProblemDetails bodies.
/// </summary>
public abstract class GradPageBase : ComponentBase
{
    [Inject] protected HttpClient Http { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    [Inject] protected BlazorAuthService AuthService { get; set; } = default!;

    protected bool _loading;
    protected string? _error;

    // Populated after GuardGraduateAsync succeeds (kept for potential
    // diagnostics); pages read the role from AuthService directly when needed.
    private string? _role;

    /// <summary>True when the signed-in account is a Graduate; else bounce.</summary>
    protected async Task<bool> GuardGraduateAsync()
    {
        try
        {
            if (!AuthService.IsInitialized && !await AuthService.RestoreSessionAsync())
            {
                Nav.NavigateTo("/login", replace: true);
                return false;
            }
        }
        catch
        {
            // Storage read failures fall through to the role check below.
        }
        var user = await AuthService.GetUserAsync();
        if (user?.Role != "Graduate")
        {
            Nav.NavigateTo("/dashboard", replace: true);
            return false;
        }
        _role = user.Role;
        return true;
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    protected async Task<T?> GetJsonAsync<T>(string url)
    {
        try
        {
            return await Http.GetFromJsonAsync<T>(url, JsonOpts);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            Nav.NavigateTo("/login?expired=1", replace: true);
            return default;
        }
        catch (Exception ex)
        {
            _error = Describe(ex, "Could not load data.");
            return default;
        }
    }

    /// <summary>POST with a JSON body; returns error text or null. Exceptions
    /// (network, circuit, serialization) are converted into error text so calling
    /// pages never abort before clearing their busy flags (no infinite loaders).</summary>
    protected async Task<(T? Value, string? Error)> PostJsonAsync<T>(string url, object payload)
    {
        try
        {
            using var response = await Http.PostAsJsonAsync(url, payload, JsonOpts);
            if (response.IsSuccessStatusCode)
                return (await response.Content.ReadFromJsonAsync<T>(JsonOpts), null);
            return (default, await DescribeErrorAsync(response, "Request failed."));
        }
        catch (Exception ex)
        {
            return (default, Describe(ex, "Request failed."));
        }
    }

    protected async Task<(T? Value, string? Error)> PutJsonAsync<T>(string url, object payload)
    {
        try
        {
            using var response = await Http.PutAsJsonAsync(url, payload, JsonOpts);
            if (response.IsSuccessStatusCode)
                return (await response.Content.ReadFromJsonAsync<T>(JsonOpts), null);
            return (default, await DescribeErrorAsync(response, "Update failed."));
        }
        catch (Exception ex)
        {
            return (default, Describe(ex, "Update failed."));
        }
    }

    /// <summary>POST multipart form data (file uploads); returns error text or null.</summary>
    protected async Task<(T? Value, string? Error)> PostMultipartAsync<T>(string url, MultipartFormDataContent content)
    {
        try
        {
            using var response = await Http.PostAsync(url, content);
            if (response.IsSuccessStatusCode)
            {
                try { return (await response.Content.ReadFromJsonAsync<T>(JsonOpts), null); }
                catch { return (default, null); }
            }
            return (default, await DescribeErrorAsync(response, "Upload failed."));
        }
        catch (Exception ex)
        {
            return (default, Describe(ex, "Upload failed."));
        }
    }

    /// <summary>POST with a JSON body; success may return an empty body.</summary>
    protected async Task<string?> PostExpectNoBodyAsync(string url, object payload)
    {
        try
        {
            using var response = await Http.PostAsJsonAsync(url, payload, JsonOpts);
            if (response.IsSuccessStatusCode) return null;
            return await DescribeErrorAsync(response, "Request failed.");
        }
        catch (Exception ex)
        {
            return Describe(ex, "Request failed.");
        }
    }

    /// <summary>PUT with a JSON body; success may return an empty body.</summary>
    protected async Task<string?> PutExpectNoBodyAsync(string url, object payload)
    {
        try
        {
            using var response = await Http.PutAsJsonAsync(url, payload, JsonOpts);
            if (response.IsSuccessStatusCode) return null;
            return await DescribeErrorAsync(response, "Update failed.");
        }
        catch (Exception ex)
        {
            return Describe(ex, "Update failed.");
        }
    }

    protected async Task<string?> DeleteAsync(string url)
    {
        try
        {
            using var response = await Http.DeleteAsync(url);
            if (response.IsSuccessStatusCode) return null;
            return await DescribeErrorAsync(response, "Delete failed.");
        }
        catch (Exception ex)
        {
            return Describe(ex, "Delete failed.");
        }
    }

    protected static string Describe(Exception ex, string fallback) =>
        $"{fallback} ({(ex.InnerException ?? ex).Message})";

    private static async Task<string> DescribeErrorAsync(HttpResponseMessage response, string fallback)
    {
        var body = "";
        try { body = await response.Content.ReadAsStringAsync(); }
        catch { /* body unavailable */ }

        string? Extract(string key)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
            }
            catch { /* not json */ }
            return null;
        }

        var message = Extract("detail") ?? Extract("error") ?? Extract("title") ?? body;
        if (string.IsNullOrWhiteSpace(message) || message.Length > 300)
            message = fallback;
        var reason = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Please sign in again.",
            HttpStatusCode.Forbidden => "You don't have permission for this action.",
            HttpStatusCode.UnprocessableEntity => message,
            HttpStatusCode.Conflict => message,
            _ => message
        };
        return reason.Length > 0 && reason.Equals(fallback, StringComparison.OrdinalIgnoreCase) && (int)response.StatusCode >= 500
            ? $"{fallback} (HTTP {(int)response.StatusCode})"
            : reason;
    }
}
