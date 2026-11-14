using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using UniSphere.Api;
using UniSphere.Api.Data;
using UniSphere.Api.Middleware;
using UniSphere.Api.Services;

var builder = WebApplication.CreateBuilder(args);

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
        Console.Error.WriteLine($"Ignoring local secret.json ({ex.Message}).");
    }
}

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

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=unisphere-v6.db"));

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
builder.Services.AddSingleton(new PerformanceModelWeights());
builder.Services.AddScoped<IPerformanceModel, WeightedPerformanceModel>();
builder.Services.AddScoped<PerformanceAnalysisService>();
builder.Services.AddScoped<ResearchFileStorage>();
builder.Services.AddScoped<CourseMaterialFileStorage>();

builder.Services.Configure<RagOptions>(builder.Configuration.GetSection("RagApi"));
builder.Services.AddHttpClient<IRagClient, RagClient>((sp, client) =>
{
    var rag = sp.GetRequiredService<IOptions<RagOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(rag.BaseUrl))
        client.BaseAddress = new Uri(rag.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(rag.TimeoutSeconds, 5, 600));
    if (!string.IsNullOrWhiteSpace(rag.ApiKey))
        client.DefaultRequestHeaders.Add("X-RAG-Api-Key", rag.ApiKey);
});
builder.Services.AddScoped<RagChatProvider>();

builder.Services.AddScoped<DoctorAccessService>();
builder.Services.AddScoped<DoctorService>();
builder.Services.Configure<DoctorAiOptions>(builder.Configuration.GetSection("DoctorAi"));
builder.Services.AddScoped<IDoctorAiService, DoctorAiService>();
builder.Services.AddScoped<IExamAssistantService, ExamAssistantService>();
builder.Services.AddScoped<IExamGeneratorService, ExamGeneratorService>();

builder.Services.Configure<ChatOptions>(builder.Configuration.GetSection("Chat"));
var chat = builder.Configuration.GetSection("Chat").Get<ChatOptions>() ?? new ChatOptions();
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
builder.Services.AddTransient<IChatProvider>(sp => sp.GetRequiredService<OpenAiChatProvider>());
builder.Services.AddTransient<IChatProvider>(sp => sp.GetRequiredService<AnthropicChatProvider>());
builder.Services.AddTransient<IChatProvider>(sp => sp.GetRequiredService<GeminiChatProvider>());
builder.Services.AddTransient<IChatProvider>(sp => sp.GetRequiredService<OllamaChatProvider>());
builder.Services.AddTransient<IChatProvider>(sp => sp.GetRequiredService<OpenRouterChatProvider>());
builder.Services.AddScoped<ChatProviderRegistry>();
builder.Services.AddScoped<ChatService>();

builder.Services.AddHttpClient("Remotive", c =>
{
    c.BaseAddress = new Uri("https://remotive.com");
    c.Timeout = TimeSpan.FromSeconds(60);
});

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
    opt.AddPolicy("research-upload", context =>
    {
        var userId = context.User.GetUserId();
        var key = userId > 0 ? $"research-user-{userId}" : $"research-ip-{context.Connection.RemoteIpAddress?.ToString() ?? "anon"}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
    opt.AddPolicy("exam-generator", context =>
    {
        var userId = context.User.GetUserId();
        var key = userId > 0 ? $"exam-user-{userId}" : $"exam-ip-{context.Connection.RemoteIpAddress?.ToString() ?? "anon"}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
});

builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
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
    });
builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5080", "http://10.0.2.2:5080", "http://localhost:3000", "http://localhost:5173" };
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

if (args.Any(a => a.Equals("--import-people", StringComparison.OrdinalIgnoreCase)))
{
    var dryRunMode = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
    var initialPassword = builder.Configuration["Import:InitialPassword"];
    if (string.IsNullOrWhiteSpace(initialPassword))
        Console.WriteLine("Import:InitialPassword user-secret is not set — new accounts will be bootstrapped with the built-in default password.");

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!dryRunMode)
        {
            db.Database.EnsureCreated();
            SchemaUpgrade.Apply(db, app.Logger);
            DbSeeder.Seed(db);
        }
        var report = PeopleImporter.ImportAll(db, app.Environment.ContentRootPath, initialPassword ?? string.Empty,
            dryRun: dryRunMode, logger: app.Logger);
        Console.WriteLine($"== People import {(dryRunMode ? "DRY RUN (nothing written)" : "COMPLETE")} ==");
        foreach (var file in report.Files)
            Console.WriteLine($"{file.File} -> {file.University}: users +{file.CreatedUsers}/~{file.UpdatedUsers}, enrollments +{file.EnrollmentsCreated}, instructor links +{file.LinksCreated}");
        Console.WriteLine($"TOTAL users created {report.CreatedUsers}, updated {report.UpdatedUsers}, existing {report.ExistingUsers}, invalid {report.InvalidRows}, row-skips {report.SkippedRows}");
    }
    return 0;
}

using (var scope = app.Services.CreateScope())
{
    try
    {
        await scope.ServiceProvider.GetRequiredService<ChatProviderRegistry>().LogStartupAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Chat provider startup check failed");
    }
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    SchemaUpgrade.Apply(db, app.Logger);
    DbSchemaUpgrade.Apply(db);
    DbSchemaPatcher.Patch(db);
    DbSeeder.Seed(db);
    var imported = CatalogImporter.ImportAll(db, app.Environment.ContentRootPath, app.Logger);
    DbSeeder.EnsurePostCatalog(db);
    app.Logger.LogInformation("Catalog import complete: {Inserted} inserted, {Updated} updated, {Universities} new universities",
        imported.Inserted, imported.Updated, imported.NewUniversities);
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseExceptionHandlerMiddleware();
app.UseAuditLogMiddleware();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));
app.Run();
return 0;