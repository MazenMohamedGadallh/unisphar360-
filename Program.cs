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
using Microsoft.Extensions.Options;
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
    try
    {
        using var secretDoc = JsonDocument.Parse(File.ReadAllText(secretPath));
        foreach (var property in secretDoc.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String) continue;
            var isAdoptable = Regex.IsMatch(property.Name,
                @"^[A-Za-z0-9]+_(API_KEY|MODEL|BASE_URL|JWT_KEY)$");
            var hasValue = !string.IsNullOrWhiteSpace(property.Value.GetString());
            if (isAdoptable && hasValue && string.IsNullOrEmpty(builder.Configuration[property.Name]))
                builder.Configuration[property.Name] = property.Value.GetString();
        }
    }
    catch (Exception ex)
    {
        // Malformed local file must never block startup.
        Console.Error.WriteLine($"Ignoring local secret.json ({ex.Message}).");
    }
}

// Per-provider config overrides (git-ignored appsettings.Local.json next to the
// app): Chat:Providers:<Name>:ApiKey / BaseUrl and Chat:DefaultProvider. Later
// config sources win, so this file beats appsettings.json; the flat
// environment keys (OPENAI_API_KEY etc.) still work and user-secrets /
// environment variables / secret.json (adopted above) keep their roles.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

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
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        // InputFile streams PDFs over the Blazor circuit. The 32 KB SignalR
        // default would break large uploads, so match the API's 25 MB request
        // cap (20 MB PDF plus multipart metadata).
        options.MaximumReceiveMessageSize = 25 * 1024 * 1024;
    });

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
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUniversityScope, UniversityScope>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AcademicService>();
builder.Services.AddScoped<GraduationService>();
builder.Services.AddScoped<GraduateProfileService>();
builder.Services.AddScoped<CvFileService>();
builder.Services.AddScoped<GraduateAiService>();
builder.Services.AddScoped<JobCatalogService>();
builder.Services.AddScoped<JobMatchingService>();
builder.Services.AddScoped<JobApplicationService>();
builder.Services.AddScoped<RoadmapService>();
builder.Services.AddScoped<InterviewService>();
builder.Services.AddScoped<GraduateCareerService>();
builder.Services.AddScoped<GraduateNotificationService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<ThemeService>();builder.Services.AddSingleton(new PerformanceModelWeights());
builder.Services.AddScoped<IPerformanceModel, WeightedPerformanceModel>();
builder.Services.AddScoped<PerformanceAnalysisService>();
builder.Services.AddScoped<ResearchFileStorage>();
builder.Services.AddScoped<CourseMaterialFileStorage>();

// Existing RAG service (rag-chatbot/rag-chatbot/api.py). The base URL is
// non-secret configuration; RagApi:ApiKey is optional and must come from
// environment variables/user-secrets. The typed client is used only by the
// student chat and doctor exam-generation paths.
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection("RagApi"));
builder.Services.AddHttpClient<IRagClient, RagClient>((sp, c) =>
{
    var rag = sp.GetRequiredService<IOptions<RagOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(rag.BaseUrl))
        c.BaseAddress = new Uri(rag.BaseUrl.TrimEnd('/') + "/");
    c.Timeout = TimeSpan.FromSeconds(Math.Clamp(rag.TimeoutSeconds, 5, 600));
    if (!string.IsNullOrWhiteSpace(rag.ApiKey))
        c.DefaultRequestHeaders.Add("X-RAG-Api-Key", rag.ApiKey);
});
builder.Services.AddScoped<RagChatProvider>();

// Doctor module: assignment-based course access, dashboard/students/analytics
// aggregation and the AI teaching assistant (AI keys come only from
// environment variables / user-secrets, never appsettings).
builder.Services.AddScoped<DoctorAccessService>();
builder.Services.AddScoped<DoctorService>();
builder.Services.Configure<DoctorAiOptions>(builder.Configuration.GetSection("DoctorAi"));
builder.Services.AddScoped<IDoctorAiService, DoctorAiService>();
builder.Services.AddScoped<IExamAssistantService, ExamAssistantService>();
builder.Services.AddScoped<IExamGeneratorService, ExamGeneratorService>();

// AI chat (keys come only from env vars, dotnet user-secrets, the optional
// git-ignored secret.json, or per-provider paths in appsettings.Local.json —
// never from a committed file)
builder.Services.Configure<ChatOptions>(builder.Configuration.GetSection("Chat"));
var chat = builder.Configuration.GetSection("Chat").Get<ChatOptions>() ?? new ChatOptions();
// Base URLs: Chat:Providers:<Name>:BaseUrl → <NAME>_BASE_URL → hard-coded default
// (see ProviderSettings.GetBaseUrl). Streaming owns its own cancellation via
// CancellationToken, so the HttpClient timeout must not cut long streams short.
builder.Services.AddHttpClient<OpenAiChatProvider>(c =>
{
    c.BaseAddress = new Uri(ProviderSettings.GetBaseUrl(builder.Configuration, "openai", "https://api.openai.com/v1/").TrimEnd('/') + "/");
    c.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<AnthropicChatProvider>(c =>
{
    c.BaseAddress = new Uri(ProviderSettings.GetBaseUrl(builder.Configuration, "anthropic", "https://api.anthropic.com/").TrimEnd('/') + "/");
    c.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<GeminiChatProvider>(c =>
{
    c.BaseAddress = new Uri(ProviderSettings.GetBaseUrl(builder.Configuration, "gemini", "https://generativelanguage.googleapis.com/v1beta/").TrimEnd('/') + "/");
    c.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<OllamaChatProvider>(c =>
{
    c.BaseAddress = new Uri(ProviderSettings.GetBaseUrl(builder.Configuration, "ollama", "http://localhost:11434").TrimEnd('/') + "/");
    c.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<OpenRouterChatProvider>(c =>
{
    c.BaseAddress = new Uri(ProviderSettings.GetBaseUrl(builder.Configuration, "openrouter", "https://openrouter.ai/api/v1/").TrimEnd('/') + "/");
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

    // Research uploads are expensive (multipart PDF up to 20 MB): throttle
    // them per authenticated doctor, falling back to the remote address.
    opt.AddPolicy("research-upload", context =>
    {
        var userId = context.User.GetUserId();
        var key = userId > 0
            ? $"research-user-{userId}"
            : $"research-ip-{context.Connection.RemoteIpAddress?.ToString() ?? "anon"}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // Exam generation: rate limit per doctor to prevent AI provider abuse.
    opt.AddPolicy("exam-generator", context =>
    {
        var userId = context.User.GetUserId();
        var key = userId > 0
            ? $"exam-user-{userId}"
            : $"exam-ip-{context.Connection.RemoteIpAddress?.ToString() ?? "anon"}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
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

// ---- On-demand people import (Data/students-*.csv, Data/doctors-*.csv) ----
// Runs ONLY with `dotnet run -- --import-people [--dry-run]`. Never at
// startup. The initial password comes from the user-secret
// Import:InitialPassword when set (never logged); if it is missing or empty,
// the importer bootstraps NEW accounts with its built-in default password
// and reports only the count of such users. Dry-run prints planned counts
// and writes nothing.
if (args.Any(a => a.Equals("--import-people", StringComparison.OrdinalIgnoreCase)))
{
    var dryRunMode = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
    var initialPassword = builder.Configuration["Import:InitialPassword"];
    if (string.IsNullOrWhiteSpace(initialPassword))
        Console.WriteLine(
            "Import:InitialPassword user-secret is not set — new accounts will be " +
            "bootstrapped with the built-in default password (count-only reporting).");

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!dryRunMode)
        {
            // Same startup maintenance a normal run performs (no-op on an
            // up-to-date database), so imports always hit a complete schema.
            db.Database.EnsureCreated();
            SchemaUpgrade.Apply(db, app.Logger);
            DbSeeder.Seed(db);
        }

        var report = PeopleImporter.ImportAll(
            db, app.Environment.ContentRootPath, initialPassword,
            dryRun: dryRunMode, logger: app.Logger);

        Console.WriteLine();
        Console.WriteLine($"== People import {(dryRunMode ? "DRY RUN (nothing written)" : "COMPLETE")} ==");
        foreach (var file in report.Files)
        {
            Console.WriteLine($"{file.File} -> {file.University}");
            Console.WriteLine($"   users:           created {file.CreatedUsers}, updated {file.UpdatedUsers}, existing {file.ExistingUsers}, invalid rows {file.InvalidRows}, skipped {file.SkippedRows}");
            Console.WriteLine($"   enrollments:     created {file.EnrollmentsCreated}, duplicate-skip {file.EnrollmentsSkippedExisting}, unknown codes {file.UnknownCourseCodes}");
            Console.WriteLine($"   instructor links: created {file.LinksCreated}, self-dup-skip {file.LinksSkippedSelf}, different-instructor-skip {file.LinksSkippedDifferentInstructor}");
        }
        Console.WriteLine();
        Console.WriteLine($"TOTAL users created {report.CreatedUsers}, updated {report.UpdatedUsers}, existing {report.ExistingUsers}, invalid {report.InvalidRows}, row-skips {report.SkippedRows}");
        Console.WriteLine($"TOTAL enrollments created {report.EnrollmentsCreated} (skipped {report.EnrollmentsSkippedExisting}), instructor links created {report.LinksCreated} (self-skip {report.LinksSkippedSelf}, other-instructor-skip {report.LinksSkippedDifferentInstructor})");
        if (report.Reasons.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"NOTES / SKIPPED ({report.Reasons.Count}):");
            foreach (var reason in report.Reasons.Take(80))
                Console.WriteLine("  " + reason);
            if (report.Reasons.Count > 80)
                Console.WriteLine($"  ... and {report.Reasons.Count - 80} more (see log)");
        }
    }

    return 0;
}

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

// Create/upgrade the database, then seed demo data and the CSV catalog.
// Existing installations get additive, idempotent schema changes only.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    SchemaUpgrade.Apply(db, app.Logger);
    // Doctor-module (friend backend) additive upgrades: CourseAssignments +
    // backfill, CourseMaterials.DeadlineUtc, Announcements.CourseId,
    // DoctorResearches table.
    DbSchemaUpgrade.Apply(db);
    // Graduates (merged PR): EnsureCreated never alters an existing database:
    // add objects that were introduced after a database file was first
    // created (no-op when fresh). MUST run BEFORE seeding: SeedDemoJobs/patch
    // inserts query new tables (JobListings, GraduateProfiles, ...) that only
    // exist after Patch().
    DbSchemaPatcher.Patch(db);
    DbSeeder.Seed(db);
    var imported = CatalogImporter.ImportAll(db, app.Environment.ContentRootPath, app.Logger);
    DbSeeder.EnsurePostCatalog(db);
    app.Logger.LogInformation(
        "Catalog import complete: {Inserted} inserted, {Updated} updated, {Universities} new universities",
        imported.Inserted, imported.Updated, imported.NewUniversities);
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

// Razor page endpoints stay anonymous at the HTTP layer on purpose: the
// session JWT lives in browser storage and can only be read once the Blazor
// circuit starts. Each page still declares [Authorize(Roles=...)] and the
// AuthorizeRouteView/RoleGate enforce it client-side; every /api endpoint is
// independently guarded and returns 401/403. Without AllowAnonymous a full
// reload (F5) of a protected page is challenged before the token can be read.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AllowAnonymous();

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));
app.Run();
return 0;
