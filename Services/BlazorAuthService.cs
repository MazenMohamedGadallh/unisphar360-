using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.IdentityModel.JsonWebTokens;
using UniSphere.Api.Dtos;

namespace UniSphere.Api.Services;

/// <summary>Authentication state provider for Blazor Server that validates JWT tokens.
/// The token lives in browser local storage (not just scoped memory), so full
/// page reloads, reconnects and prerender-then-interactive transitions keep
/// the user signed in. The JWT itself is the session: expiry is validated
/// locally against the exp claim (UTC).</summary>
public class BlazorAuthService : AuthenticationStateProvider
{
    private const string TokenStorageKey = "unisphere.auth.token";
    private readonly HttpClient _http;
    private readonly ProtectedLocalStorage _storage;
    private readonly ILogger<BlazorAuthService> _logger;
    private string? _token;
    // Set once the persisted token has been read from storage (or the
    // in-memory token is usable): the auth state is final for this
    // circuit and can no longer turn from "loading" into authenticated.
    private bool _resolved;
    // Explicit "auth resolving" gate for UI: MainLayout and data pages
    // render the full shell (sidebar + Logout) / wait while this is false,
    // and only treat the user as anonymous after it becomes true.
    // Mirrors _resolved; kept as a separate public flag so layouts never
    // have to infer readiness from token presence.
    public bool IsInitialized => _resolved;
    // Single-flight restore: MainLayout, RedirectToLogin and data pages
    // may all ask for a restore on the first interactive render. They share
    // one storage read and therefore one NotifyAuthenticationStateChanged,
    // so the layout can never flip authenticated -> anonymous -> ... .
    private Task<bool>? _restoreTask;
    // Bumped on every explicit session change (login/register/logout). An
    // in-flight restore that started before the bump must not adopt the
    // stale stored token (e.g. resurrecting a session after logout).
    private int _generation;

    public BlazorAuthService(HttpClient http, ProtectedLocalStorage storage, ILogger<BlazorAuthService> logger)
    {
        _http = http;
        _storage = storage;
        _logger = logger;
    }

    public void SetToken(string? token)
    {
        _generation++;
        _token = token;
        _resolved = true; // explicit session: readiness is final
        ApplyTokenToHttpClient();
        _ = PersistTokenAsync();
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private void ApplyTokenToHttpClient()
    {
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(_token)
            ? null
            : new AuthenticationHeaderValue("Bearer", _token);
    }

    private async Task PersistTokenAsync()
    {
        // Must never break login/logout: storage needs JS interop, which is
        // unavailable during prerendering. Memory always wins for the circuit.
        try
        {
            if (string.IsNullOrEmpty(_token))
                await _storage.DeleteAsync(TokenStorageKey);
            else
                await _storage.SetAsync(TokenStorageKey, _token);
        }
        catch
        {
            // No-op: the in-memory token still serves this circuit.
        }
    }

    private static bool IsTokenUsable(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        try
        {
            return new JsonWebToken(token).ValidTo > DateTime.UtcNow;
        }
        catch
        {
            return false;
        }
    }

    private static string? JwtClaim(JsonWebToken jwt, params string[] types)
    {
        foreach (var type in types)
        {
            var claim = jwt.Claims.FirstOrDefault(c =>
                string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase));
            if (claim is not null && !string.IsNullOrEmpty(claim.Value))
                return claim.Value;
        }
        return null;
    }

    private static ClaimsPrincipal BuildPrincipal(string token)
    {
        // Claim names are matched tolerantly: some issuers emit full
        // ClaimTypes URIs, others short JWT names.
        var jwt = new JsonWebToken(token);
        if (jwt.ValidTo <= DateTime.UtcNow)
            return new ClaimsPrincipal(new ClaimsIdentity());

        var id = JwtClaim(jwt, ClaimTypes.NameIdentifier, "sub");
        if (id is null)
            return new ClaimsPrincipal(new ClaimsIdentity());

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id) };
        var email = JwtClaim(jwt, ClaimTypes.Email, "email");
        if (email is not null) claims.Add(new Claim(ClaimTypes.Email, email));
        var name = JwtClaim(jwt, ClaimTypes.Name, "unique_name", "name");
        if (name is not null) claims.Add(new Claim(ClaimTypes.Name, name));
        var role = JwtClaim(jwt, ClaimTypes.Role, "role");
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        var roleId = JwtClaim(jwt, "roleId");
        if (roleId is not null) claims.Add(new Claim("roleId", roleId));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT"));
    }

    /// <summary>Restores the session from browser storage (full reloads,
    /// reconnects). Call from OnAfterRenderAsync(firstRender): JS interop
    /// is available there but not during prerender. Raises
    /// NotifyAuthenticationStateChanged exactly once so the whole component
    /// tree (AuthorizeView / AuthorizeRouteView included) re-renders with
    /// the resolved state — authenticated, or truly anonymous.
    /// Single-flight: concurrent callers share one storage read.</summary>
    public Task<bool> RestoreSessionAsync()
    {
        if (IsTokenUsable(_token))
        {
            _resolved = true;
            return Task.FromResult(true);
        }
        if (_resolved) return Task.FromResult(false);
        return _restoreTask ??= RestoreSessionCoreAsync();
    }

    private async Task<bool> RestoreSessionCoreAsync()
    {
        var generation = _generation;
        try
        {
            var stored = await _storage.GetAsync<string>(TokenStorageKey);
            // Superseded by an explicit login/logout while reading: never
            // adopt the stale value, never resolve someone else's session.
            if (generation != _generation) return false;
            _resolved = true;
            if (stored.Success && IsTokenUsable(stored.Value))
            {
                _token = stored.Value;
                ApplyTokenToHttpClient();
                NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
                return true;
            }
            if (stored.Success)
            {
                // Drop the expired token everywhere, not just memory.
                _token = null;
                ApplyTokenToHttpClient();
                await PersistTokenAsync();
            }
            // Resolve the loading state (to anonymous when no usable
            // token exists) so consumers stop waiting and react.
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
            return false;
        }
        catch (InvalidOperationException)
        {
            return false; // prerendering: no JS interop yet
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> LoginAsync(string email, string password)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/api/auth/login", new { email, password });
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (body is null) return null;

            _generation++;
            _token = body.Token;
            _resolved = true; // explicit session: readiness is final
            ApplyTokenToHttpClient();
            await PersistTokenAsync();
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
            return body.Token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for {Email}", email);
            return null;
        }
    }

    public async Task LogoutAsync()
    {
        _generation++;
        _token = null;
        _resolved = true; // session definitively ended: resolve any loading state
        _http.DefaultRequestHeaders.Authorization = null;
        await PersistTokenAsync();
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task<RegisterResult> RegisterAsync(RegisterPayload payload)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/api/auth/register", payload);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
                if (body is null)
                    return new RegisterResult(false, null, null, null, "Registration failed. Please try again.");

                _generation++;
                _token = body.Token;
                _resolved = true; // explicit session: readiness is final
                ApplyTokenToHttpClient();
                await PersistTokenAsync();
                NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
                return new RegisterResult(true, body.Token, body.User, null, null);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var message = (await response.Content.ReadAsStringAsync()).Trim().Trim('"');
                return new RegisterResult(false, null, null, null,
                    string.IsNullOrEmpty(message) ? "This email is already registered." : message);
            }

            // 400 ValidationProblem ({"errors": {"Email": [...]}}) or other
            // failures: surface per-field errors when present.
            Dictionary<string, string[]>? fields = null;
            try
            {
                using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
                if (doc is not null
                    && doc.RootElement.TryGetProperty("errors", out var errs)
                    && errs.ValueKind == JsonValueKind.Object)
                {
                    fields = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in errs.EnumerateObject())
                        fields[prop.Name] = prop.Value.EnumerateArray()
                            .Select(e => e.GetString() ?? "")
                            .Where(s => s.Length > 0).ToArray();
                }
            }
            catch
            {
                // Fall through to the generic message below.
            }

            if (fields is { Count: > 0 })
                return new RegisterResult(false, null, null, fields, "Please fix the highlighted fields.");
            return new RegisterResult(false, null, null, null, "Registration failed. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed for {Email}", payload.Email);
            return new RegisterResult(false, null, null, null, "Registration failed. Please check your connection and try again.");
        }
    }

    /// <summary>Claim type marking the temporary principal returned
    /// while the persisted token is still being read (prerender and
    /// the first interactive render). It is NOT an authenticated
    /// identity: consumers show a small loading state instead of
    /// redirecting or locking the user out.</summary>
    public const string LoadingClaimType = "unisphere.auth.loading";

    private static readonly ClaimsPrincipal LoadingPrincipal =
        new(new ClaimsIdentity(new[] { new Claim(LoadingClaimType, bool.TrueString) }));

    /// <summary>True while the persisted token has not been read yet
    /// (prerender / first interactive render). Never redirect to
    /// /login and never log out while loading: the session may still
    /// be restored from storage a moment later.</summary>
    public bool IsLoading => !IsTokenUsable(_token) && !_resolved;

    /// <summary>True when the principal marks the auth state as still
    /// being resolved (see <see cref="LoadingClaimType"/>).</summary>
    public static bool IsLoadingState(ClaimsPrincipal user) =>
        user.HasClaim(LoadingClaimType, bool.TrueString);

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // No HTTP round-trip here on purpose: the JWT itself is the session.
        // (The old /api/auth/me call turned any blip into an anonymous
        // identity and bounced the user to /login.)

        // In-memory token (login on this circuit): fast path, no JS interop.
        if (IsTokenUsable(_token))
            return new AuthenticationState(BuildPrincipal(_token!));

        // Storage was already read on this circuit: the state is final.
        if (_resolved)
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

        try
        {
            var stored = await _storage.GetAsync<string>(TokenStorageKey);
            _resolved = true;
            if (stored.Success && IsTokenUsable(stored.Value))
            {
                _token = stored.Value;
                ApplyTokenToHttpClient();
                return new AuthenticationState(BuildPrincipal(_token!));
            }
            if (stored.Success)
            {
                // Stored token is expired or invalid: drop it everywhere.
                _token = null;
                ApplyTokenToHttpClient();
                await PersistTokenAsync();
            }
            // Nothing usable persisted: truly anonymous.
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
        catch (InvalidOperationException)
        {
            // Prerendering: JS interop is unavailable, so the token in
            // ProtectedLocalStorage cannot be read yet. Return a "loading"
            // state instead of anonymous — consumers must wait (show a
            // small loading state), not redirect to /login. The token is
            // re-read in OnAfterRenderAsync(firstRender), which then raises
            // NotifyAuthenticationStateChanged with the final state.
            return new AuthenticationState(LoadingPrincipal);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading the persisted auth token failed; keeping the auth state in loading.");
            return new AuthenticationState(LoadingPrincipal);
        }
    }

    public async Task<AuthUserDto?> GetUserAsync()
    {
        if (string.IsNullOrEmpty(_token)) return null;
        try
        {
            return await _http.GetFromJsonAsync<AuthUserDto>("/api/auth/me");
        }
        catch
        {
            return null;
        }
    }

    public async Task<int> GetUnreadCountAsync()
    {
        if (string.IsNullOrEmpty(_token)) return 0;
        try
        {
            var notifications = await _http.GetFromJsonAsync<List<NotificationDto>>("/api/notifications");
            return notifications?.Count(n => !n.IsRead) ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    public string? GetToken() => _token;
}

public record LoginResponse(string Token, DateTime ExpiresAtUtc, AuthUserDto User);

public record RegisterPayload(
    string FirstName, string? MiddleName, string LastName,
    string PhoneNumber, string Email, string Gender, int Age,
    string Role, string Password, string ConfirmPassword,
    int? AcademicYear, string? Department,
    string? Specialization, string? GraduatedFrom, int? GraduationYear,
    int? UniversityId);

public record RegisterResult(
    bool Succeeded, string? Token, AuthUserDto? User,
    Dictionary<string, string[]>? FieldErrors, string? ErrorMessage);
