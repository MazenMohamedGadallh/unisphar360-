using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using FluentValidation;
using UniSphere.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using UniSphere.Api.Data;
using UniSphere.Api.Middleware;
using UniSphere.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Local AI key convenience: an optional, git-ignored secret.json next to the
// app can carry the same keys that environment variables would (OPENAI_API_KEY,
// ANTHROPIC_API_KEY, GEMINI_API_KEY, OPENROUTER_API_KEY, *_MODEL, OLLAMA_BASE_URL,
// UNISPHERE_JWT_KEY). Only keys that are not already set are adopted, so
// environment variables and dotnet user-secrets always win. Never commit this
// file — it is ignored by .gitignore and documented in README "AI providers".
var secretPath = Path.Combine(builder.Environment.ContentRootPath, "secret.json");
if (File.Exists(secretPath))
{
    // Collected for diagnostics: entries present in the local file, even when
    // NOT adopted because another source (user-secrets/env) already set them.
    var secretEntries = new List<(string Name, string Value)>();
    try
    {
        using var secretDoc = JsonDocument.Parse(File.ReadAllText(secretPath));
        foreach (var property in secretDoc.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String) continue;
            var isAdoptable = Regex.IsMatch(property.Name,
                @"^[A-Za-z0-9]+_(API_KEY|MODEL|BASE_URL|JWT_KEY)$");
            var hasValue = !string.IsNullOrWhiteSpace(property.Value.GetString());
            if (isAdoptable && hasValue)
                secretEntries.Add((property.Name, property.Value.GetString()!));
            if (isAdoptable && hasValue && string.IsNullOrEmpty(builder.Configuration[property.Name]))
                builder.Configuration[property.Name] = property.Value.GetString();
        }

        // Stale-duplicate detection: when secret.json and another source define the
        // same credential with DIFFERENT values, the other source silently wins and
        // an outdated key can keep breaking AI calls. Surface that conflict (lengths
        // only — credential material is never printed) instead of failing silently.
        foreach (var (name, fileValue) in secretEntries)
        {
            var active = builder.Configuration[name];
            if (string.IsNullOrWhiteSpace(active)) continue;
            var trimmed = active.Trim();

            if (name.EndsWith("_API_KEY", StringComparison.Ordinal))
            {
                if (trimmed != fileValue)
                {
                    Console.Error.WriteLine(
                        $"[UniSphere AI] {name} is defined in secret.json AND another configuration source " +
                        "(dotnet user-secrets or environment variables) with DIFFERENT values " +
                        $"(lengths {fileValue.Length} vs {trimmed.Length}). The other source wins. " +
                        "If AI calls fail with 'API key invalid', remove the stale entry from the other source " +
                        "(e.g. 'dotnet user-secrets remove \"GEMINI_API_KEY\"') or update it to match secret.json.");
                }
                else if (trimmed.Contains(' ') || trimmed.Contains('"') ||
                         trimmed.Equals("changeme", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.Equals("your-api-key", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.Equals("your-api-key-here", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.Contains("<", StringComparison.Ordinal) || trimmed.Contains(">", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine(
                        $"[UniSphere AI] {name} looks like a placeholder (length {trimmed.Length}); " +
                        "replace it with the real key issued by the provider.");
                }
                if (name == "OPENROUTER_API_KEY" && !trimmed.StartsWith("sk-or-", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine(
                        "[UniSphere AI] OPENROUTER_API_KEY does not start with 'sk-or-' — OpenRouter keys always do, " +
                        "so the active value is almost certainly invalid. Generate a new key at openrouter.ai/keys.");
                }
            }
        }
    }
    catch (Exception ex)
    {
        // Malformed local file must never block startup.
        Console.Error.WriteLine($"Ignoring local secret.json ({ex.Message}).");
    }
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the token returned by POST /api/auth/login (no 'Bearer ' prefix)."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// HTTP client for Blazor to call the API
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("http://localhost:5080") });

// Auth state provider (single instance: the framework's
// AuthenticationStateProvider and injected BlazorAuthService must share
// the same token, so forward one registration to the other).
builder.Services.AddScoped<BlazorAuthService>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<BlazorAuthService>());

// Database
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=unisphere-v6.db"));

// Services
builder.Services.AddScoped<AcademicService>();
builder.Services.AddScoped<GraduationService>();
builder.Services.AddScoped<GraduateProfileService>();
builder.Services.AddScoped<CvFileService>();
builder.Services.AddScoped<GraduateAiService>();
// Doctor module
builder.Services.AddScoped<DoctorAccessService>();
builder.Services.AddScoped<DoctorService>();
builder.Services.Configure<DoctorAiOptions>(builder.Configuration.GetSection("DoctorAi"));
builder.Services.AddScoped<IDoctorAiService, DoctorAiService>();
builder.Services.AddScoped<JobCatalogService>();
builder.Services.AddScoped<JobMatchingService>();
builder.Services.AddScoped<JobApplicationService>();
builder.Services.AddScoped<RoadmapService>();
builder.Services.AddScoped<InterviewService>();
builder.Services.AddScoped<GraduateCareerService>();
builder.Services.AddScoped<GraduateNotificationService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<SharedUnreadBadgeState>();
builder.Services.AddSingleton(new PerformanceModelWeights());
builder.Services.AddScoped<IPerformanceModel, WeightedPerformanceModel>();
builder.Services.AddScoped<PerformanceAnalysisService>();

// AI chat (keys come only from env vars, dotnet user-secrets or the optional
// git-ignored secret.json above — never from committed appsettings)
builder.Services.Configure<ChatOptions>(builder.Configuration.GetSection("Chat"));
var chat = builder.Configuration.GetSection("Chat").Get<ChatOptions>() ?? new ChatOptions();
// Streaming owns its own cancellation via CancellationToken, so the
// HttpClient timeout must not cut long streams short.
builder.Services.AddHttpClient<OpenAiChatProvider>(c =>
{
    c.BaseAddress = new Uri("https://api.openai.com/v1/");
    c.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<AnthropicChatProvider>(c =>
{
    c.BaseAddress = new Uri("https://api.anthropic.com/");
    c.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<GeminiChatProvider>(c =>
{
    c.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
    c.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<OllamaChatProvider>(c =>
{
    var ollamaBaseUrl = (builder.Configuration["OLLAMA_BASE_URL"] ?? "http://localhost:11434").TrimEnd('/') + "/";
    c.BaseAddress = new Uri(ollamaBaseUrl);
    c.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<OpenRouterChatProvider>(c =>
{
    var openRouterBaseUrl = (builder.Configuration["OPENROUTER_BASE_URL"] ?? "https://openrouter.ai/api/v1/").TrimEnd('/') + "/";
    c.BaseAddress = new Uri(openRouterBaseUrl);
    c.Timeout = Timeout.InfiniteTimeSpan;
});
// Forward each typed client as IChatProvider so IEnumerable<IChatProvider>
// in ChatProviderRegistry actually resolves them (AddHttpClient alone only
// registers the concrete type).
builder.Services.AddTransient<IChatProvider>(sp => sp.GetRequiredService<OpenAiChatProvider>());
builder.Services.AddTransient<IChatProvider>(sp => sp.GetRequiredService<AnthropicChatProvider>());
builder.Services.AddTransient<IChatProvider>(sp => sp.GetRequiredService<GeminiChatProvider>());
builder.Services.AddTransient<IChatProvider>(sp => sp.GetRequiredService<OllamaChatProvider>());
builder.Services.AddTransient<IChatProvider>(sp => sp.GetRequiredService<OpenRouterChatProvider>());
builder.Services.AddScoped<ChatProviderRegistry>();
builder.Services.AddScoped<ChatService>();

// Live external job feed (no API key needed)
builder.Services.AddHttpClient("Remotive", c =>
{
    c.BaseAddress = new Uri("https://remotive.com");
    c.Timeout = TimeSpan.FromSeconds(60);
});

// Per-user chat rate limiting (429 when exceeded)
builder.Services.AddRateLimiter(opt =>
{
    opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opt.AddPolicy("chat-per-minute", context =>
    {
        var key = "chat-user-" + (context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anon");
        var limit = chat.PerMinuteLimit > 0 ? chat.PerMinuteLimit : 20;
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // Research Hub PDF uploads: max 10 uploads/updates per user per 10 minutes
    // (brute-force upload protection; metadata edits like publish toggles don't count).
    opt.AddPolicy("research-upload", context =>
    {
        var key = "research-user-" + (context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anon");
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(10),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
});

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// JWT authentication — key from environment variable or configuration
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

// Use environment variable for JWT key in production, fallback to config for dev
var jwtKey = Environment.GetEnvironmentVariable("UNISPHERE_JWT_KEY") ?? jwt.Key;
if (jwtKey.Length < 32)
    throw new InvalidOperationException("JWT key must be at least 32 characters. Set UNISPHERE_JWT_KEY environment variable or update appsettings.json.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        // Browsers navigating to protected Blazor pages send no JWT.
        // Send those page navigations to /login instead of a raw 401,
        // preserving the original path so login/refresh returns the user
        // to the exact page they were on (validated as a local URL).
        // API callers (under /api) keep the standard 401 + WWW-Authenticate.
        o.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                var path = context.Request.Path;
                if (!path.StartsWithSegments("/api")
                    && !path.StartsWithSegments("/_blazor")
                    && !path.StartsWithSegments("/_framework")
                    && HttpMethods.IsGet(context.Request.Method))
                {
                    context.HandleResponse();
                    var absolute = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
                    context.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(absolute)}");
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// CORS — restricted to known origins
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5080", "http://10.0.2.2:5080", "http://localhost:3000", "http://localhost:5173" };

builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.WithOrigins(allowedOrigins)
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()));

var app = builder.Build();

// Startup log: which chat providers are enabled and why others are hidden (no keys).
using (var scope = app.Services.CreateScope())
{
    try
    {
        var registry = scope.ServiceProvider.GetRequiredService<ChatProviderRegistry>();
        await registry.LogStartupAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Chat provider startup check failed");
    }
}

// Create the database and seed sample data on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    // EnsureCreated never alters an existing database: add objects that were
    // introduced after a database file was first created (no-op when fresh).
    // MUST run BEFORE seeding: SeedDemoJobs/patch inserts query new tables
    // (JobListings, GraduateProfiles, ...) that only exist after Patch().
    DbSchemaPatcher.Patch(db);
    DbSeeder.Seed(db);
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();
app.UseStaticFiles();

// Protected pages must never be served from cache: after logout, Back
// must re-request (and hit the auth challenge) instead of revealing
// a cached dashboard. API responses keep their own semantics.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
        }
        return Task.CompletedTask;
    });
    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

// Global exception handling
app.UseExceptionHandlerMiddleware();
app.UseAuditLogMiddleware();

app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));
app.Run();
