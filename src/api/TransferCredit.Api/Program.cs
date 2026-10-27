using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransferCredit.Api;
using TransferCredit.Api.Agent;
using TransferCredit.Api.Auth;
using TransferCredit.Api.Documents;
using TransferCredit.Api.Endpoints;
using TransferCredit.Api.Intake;
using TransferCredit.Api.Rules;
using TransferCredit.Api.Services;
using TransferCredit.Data;
using TransferCredit.Data.Entities;
using TransferCredit.Data.Seed;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Configuration (env var names match the deployment guide) ----------
builder.Services.Configure<AgentOptions>(o =>
{
    config.GetSection("Agent").Bind(o);
    o.BaseUrl = config["AGENT_BASE_URL"] is { Length: > 0 } url ? url : o.BaseUrl;
});
builder.Services.Configure<StorageOptions>(o =>
{
    config.GetSection("Storage").Bind(o);
    o.AccountUrl = config["STORAGE_ACCOUNT_URL"] is { Length: > 0 } url ? url : o.AccountUrl;
});
builder.Services.Configure<IntakeOptions>(o =>
{
    config.GetSection("Intake").Bind(o);
    if (bool.TryParse(config["INTAKE_ENABLED"], out var enabled)) o.Enabled = enabled;
    if (config["INTAKE_MAILBOX"] is { Length: > 0 } mailbox) o.Mailbox = mailbox;
    if (int.TryParse(config["INTAKE_POLL_SECONDS"], out var poll)) o.PollSeconds = poll;
    if (int.TryParse(config["MAX_UPLOAD_MB"], out var max)) o.MaxUploadMb = max;
    if (config["ALLOWED_FILE_TYPES"] is { Length: > 0 } types) o.AllowedFileTypes = types;
});

// ---------- Data ----------
var connectionString = config.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "ConnectionStrings:Default is not configured. Set ConnectionStrings__Default (Key Vault secret sql-conn in Azure).");
builder.Services.AddDbContext<TransferCreditDbContext>(o =>
    o.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

// ---------- Identity: username and password, roles from the Users table, HttpOnly cookie session ----------
builder.Services.Configure<AuthOptions>(config.GetSection("Auth"));
var authOptions = config.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<UserImportService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "tc.session";
        o.Cookie.HttpOnly = true;
        // Strict: the browser never sends the session on a request started by another site (CSRF).
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = builder.Environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(authOptions.SessionHours);
        o.SlidingExpiration = true;
        // An API answers with status codes; the React app shows the sign-in page.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var accounts = ctx.HttpContext.RequestServices.GetRequiredService<AccountService>();
            if (ctx.Principal is null || !await accounts.IsSessionValidAsync(ctx.Principal, ctx.HttpContext.RequestAborted))
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddAuthorization(Policies.Register);
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy(AuthEndpoints.SignInRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authOptions.SignInAttemptsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await ctx.HttpContext.Response.WriteAsJsonAsync(new
        {
            title = "Too many attempts", status = 429, detail = "Too many sign-in attempts. Wait a minute and try again."
        }, ct);
    };
});

// ---------- Application services ----------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<RuleSettingsService>();
builder.Services.AddScoped<CaseService>();
builder.Services.AddScoped<MatchingService>();
builder.Services.AddScoped<EvaluationService>();
builder.Services.AddScoped<DocumentIntakeService>();
builder.Services.AddScoped<ExtractionService>();
builder.Services.AddScoped<CaseMatcher>();
builder.Services.AddScoped<EmailIntakeProcessor>();
builder.Services.AddSingleton(sp => new FileValidator(sp.GetRequiredService<IOptions<IntakeOptions>>().Value));
builder.Services.AddSingleton<MatchQueue>();

builder.Services.AddSingleton<IDocumentStorage>(sp =>
{
    var o = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(o.AccountUrl)) return BlobDocumentStorage.Create(o.AccountUrl, o.ScanMode);
    var root = Path.IsPathRooted(o.LocalRoot) ? o.LocalRoot : Path.Combine(builder.Environment.ContentRootPath, o.LocalRoot);
    return new LocalDocumentStorage(root, o.ScanMode);
});

var agentUrl = config["AGENT_BASE_URL"] is { Length: > 0 } u ? u : config["Agent:BaseUrl"];
if (string.IsNullOrWhiteSpace(agentUrl))
{
    builder.Services.AddSingleton<IAgentClient, StubAgentClient>();
}
else
{
    builder.Services.AddHttpClient<IAgentClient, HttpAgentClient>((sp, http) =>
    {
        var o = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
        http.BaseAddress = new Uri(agentUrl.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
    });
}

builder.Services.AddHostedService<ScanWorker>();
builder.Services.AddHostedService<MatchWorker>();
builder.Services.AddHostedService<GraphMailPoller>();

// ---------- Web ----------
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await InitialiseDatabaseAsync(app);

// One-off admin command instead of the web server: dotnet run -- import-users <file.csv> [--dry-run] [--no-password-change]
// Host options such as --contentRoot may come first, so look for the command anywhere.
if (Array.IndexOf(args, UserImportCommand.Name) is var command and >= 0)
{
    Environment.ExitCode = await UserImportCommand.RunAsync(app.Services, args[(command + 1)..]);
    return;
}

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// The React build is copied into wwwroot by docker/api.Dockerfile: one origin, no CORS.
// Locally there is no wwwroot, so fall back to the Vite build output (src/ui/dist) when it exists.
var uiDist = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "ui", "dist"));
if (app.Environment.IsDevelopment() && !Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot"))
    && File.Exists(Path.Combine(uiDist, "index.html")))
{
    app.Environment.WebRootPath = uiDist;
    app.Environment.WebRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uiDist);
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseRateLimiter();
// An account given a temporary password can only change it, until it does.
app.Use(async (http, next) =>
{
    var path = http.Request.Path;
    if (http.User.HasClaim(AppClaims.MustChangePassword, "true") && path.StartsWithSegments("/api")
        && !path.StartsWithSegments("/api/auth") && !path.StartsWithSegments("/api/me"))
    {
        http.Response.StatusCode = StatusCodes.Status403Forbidden;
        await http.Response.WriteAsJsonAsync(new
        {
            title = "Password change required", status = 403, detail = "Choose a new password before you continue."
        });
        return;
    }
    await next();
});
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapCaseEndpoints();
app.MapCourseEndpoints();
app.MapDocumentEndpoints();
app.MapReferenceEndpoints();
// Unknown /api paths get a 404, not the SPA page (which the UI would fail to parse as JSON).
app.MapFallback("/api/{**path}", () => Results.NotFound(new { title = "No such API endpoint." }));
app.MapFallbackToFile("index.html");

app.Run();

static async Task InitialiseDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TransferCreditDbContext>();
    var config = app.Configuration;

    if (config.GetValue("Database:AutoMigrate", true))
    {
        if (db.Database.IsRelational()) await db.Database.MigrateAsync();
        else await db.Database.EnsureCreatedAsync();
    }
    await SeedData.SeedReferenceDataAsync(db);
    await scope.ServiceProvider.GetRequiredService<AccountService>().SeedPasswordsAsync(CancellationToken.None);

    if (config.GetValue("Demo:SeedDemoCase", true) && !await db.Cases.AnyAsync())
    {
        var cases = scope.ServiceProvider.GetRequiredService<CaseService>();
        await cases.CreateDemoCaseAsync("DS-0001", "system", CancellationToken.None);
    }
}

public partial class Program;
