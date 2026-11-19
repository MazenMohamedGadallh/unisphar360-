using Microsoft.JSInterop;

namespace UniSphere.Api.Services;

/// <summary> Reads/writes the UI theme preference (light | dark | system)
/// via theme.js. The concrete theme applies instantly through the
/// documentElement dataset — no reload, no re-render needed.</summary>
public class ThemeService
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    /// <summary>Stored preference (light | dark | system).</summary>
    public string Current { get; private set; } = "system";

    /// <summary>Actually applied theme (light | dark).</summary>
    public string Effective { get; private set; } = "light";

    public event Action? Changed;

    public ThemeService(IJSRuntime js) => _js = js;

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/theme.js");

    /// <summary>Syncs state with browser storage. Safe to call when JS is
    /// unavailable (prerendering): keeps defaults instead of throwing.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var module = await ModuleAsync();
            Current = await module.InvokeAsync<string>("getTheme");
            Effective = await module.InvokeAsync<string>("applyTheme", Current);
            Changed?.Invoke();
        }
        catch
        {
            // Prerendering or no JS: the inline head script already set a
            // concrete theme to avoid a flash; sync on next render.
        }
    }

    public async Task SetAsync(string theme)
    {
        try
        {
            var module = await ModuleAsync();
            Effective = await module.InvokeAsync<string>("setTheme", theme);
            Current = theme is "light" or "dark" or "system" ? theme : "system";
            Changed?.Invoke();
        }
        catch
        {
            // Ignore: theme stays as-is for this session.
        }
    }

    public async Task ToggleAsync()
    {
        try
        {
            var module = await ModuleAsync();
            Effective = await module.InvokeAsync<string>("toggleTheme");
            Current = Effective;
            Changed?.Invoke();
        }
        catch
        {
            // Ignore.
        }
    }
}
