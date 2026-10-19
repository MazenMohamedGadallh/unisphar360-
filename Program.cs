using System.Security.Claims;
using System.Text;
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
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<ThemeService>();builder.Services.AddSingleton(new PerformanceModelWeights());
builder.Services.AddScoped<IPerformanceModel, WeightedPerformanceModel>();
builder.Services.AddScoped<PerformanceAnalysisService>();

// Doctor module
builder.Services.AddScoped<DoctorAccessService>();
builder.Services.AddScoped<DoctorService>();
builder.Services.Configure<DoctorAiOptions>(builder.Configuration.GetSection("DoctorAi"));
builder.Services.AddScoped<IDoctorAiService, DoctorAiService>();

// AI chat (keys come only from env vars / user-secrets, never appsettings)
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
        // Send those page navigations to /login instead of a raw 401.
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
                    context.Response.Redirect("/login");
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
    // Additive, idempotent upgrades for databases created by older builds
    // (EnsureCreated does not alter an existing schema). Never drops data.
    UniSphere.Api.Data.DbSchemaUpgrade.Apply(db);
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
