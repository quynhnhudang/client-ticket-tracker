using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Api.Controllers;
using TicketTracker.Api.Data;
using TicketTracker.Api.Data.Queries;
using TicketTracker.Api.Domain;
using TicketTracker.Api.Services;
using TicketTracker.Api.Services.Email;
using TicketTracker.Api.Services.Triage;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------------------------------------------------------------- Data
// The connection string is never in appsettings.json (it contains a password).
// Development: dotnet user-secrets. Server: an environment variable
// (ConnectionStrings__TicketTracker) in /etc/tickettracker/app.env, loaded by systemd.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(config.GetConnectionString("TicketTracker")));

builder.Services.AddSingleton<FullTextStatus>();
builder.Services.AddScoped<TicketSearchQuery>();
builder.Services.AddScoped<TicketService>();
builder.Services.AddSingleton<IPasswordHasher<Agent>, PasswordHasher<Agent>>();

// ---------------------------------------------------------------- Escalation email
// Email is on only when Email:Host is configured. Without it, escalations are
// just logged, so the app runs (and can be demoed) with no SMTP account.
// When it IS configured, ValidateOnStart checks the whole section at startup:
// a missing FromAddress fails loudly now, not on the first escalation.
var emailSection = config.GetSection(EmailOptions.Section);
if (!string.IsNullOrWhiteSpace(emailSection["Host"]))
{
    builder.Services.AddOptions<EmailOptions>().Bind(emailSection).ValidateDataAnnotations().ValidateOnStart();
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
    builder.Services.AddScoped<IEscalationNotifier, EmailEscalationNotifier>();
}
else
{
    builder.Services.AddScoped<IEscalationNotifier, LoggingEscalationNotifier>();
}

// ---------------------------------------------------------------- Optional LLM triage
// AddHttpClient registers a "typed client": TriageService gets an HttpClient
// from IHttpClientFactory, which pools and recycles the underlying connections.
// (new HttpClient() per call can exhaust sockets; one static instance never
// notices DNS changes. The factory avoids both.)
builder.Services.Configure<TriageOptions>(config.GetSection(TriageOptions.Section));
builder.Services.AddHttpClient<TriageService>(client => client.BaseAddress = new Uri("https://api.anthropic.com/"));

// ---------------------------------------------------------------- MVC + JSON
builder.Services.AddControllers().AddJsonOptions(options =>
{
    // Enums travel as names ("InProgress") instead of numbers (1): readable, and the
    // API doesn't leak the database's numbering. allowIntegerValues: false rejects
    // "status": 1 instead of accepting it.
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
});
builder.Services.AddProblemDetails(); // every error response uses the same JSON shape

// ---------------------------------------------------------------- Auth
// Cookie authentication. In production nginx serves the Angular app and proxies
// /api on the SAME origin, and in development the Angular dev server will proxy
// /api too (web/proxy.conf.json). So the browser sees one origin, no CORS setup is needed,
// and the cookie can be SameSite=Strict.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ctt.auth";
        options.Cookie.HttpOnly = true;              // not readable from JavaScript
        options.Cookie.SameSite = SameSiteMode.Strict; // not sent on cross-site requests: the CSRF defence
        // Secure flag follows the request. Behind nginx with HTTPS, UseForwardedHeaders
        // (below) makes the request look like https, so the cookie is marked Secure.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);  // one shift
        options.SlidingExpiration = true;

        // Cookie auth was built for server-rendered sites, where "not logged in"
        // means a 302 redirect to a login page. An API should answer 401/403 and
        // let the Angular app decide what to show.
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

// Every endpoint requires a logged-in agent unless it is marked [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The auth cookie is encrypted with Data Protection keys. If the keys aren't
// stored somewhere permanent, they can be regenerated on restart and every agent
// gets logged out. On the server this points at /var/lib/tickettracker/keys.
var keysPath = config["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
        .SetApplicationName("ClientTicketTracker");
}

// ---------------------------------------------------------------- Rate limiting
// Slows password guessing: at most 5 login attempts per minute per client IP.
// Behind nginx, "client IP" is only correct because of UseForwardedHeaders below;
// without it, every request would appear to come from nginx (127.0.0.1) and all
// users would share one limit.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Login, http =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // LLM calls cost money per request: 20 suggestions per minute per agent.
    options.AddPolicy(RateLimitPolicies.Triage, http =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

// ---------------------------------------------------------------- Reverse proxy
// nginx terminates HTTP(S) and forwards to Kestrel on localhost. It sends the real
// client IP and scheme in X-Forwarded-For / X-Forwarded-Proto. These headers are
// only trusted from loopback (the default KnownProxies), i.e. from nginx on the
// same machine; anyone else could fake them.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

// ---------------------------------------------------------------- Pipeline (order matters)
app.UseForwardedHeaders();         // first, so everything after sees the real IP and scheme

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler();     // unhandled exception -> 500 problem details, no stack trace

// No UseHttpsRedirection: in production nginx owns HTTPS and redirects; Kestrel
// only listens on localhost over HTTP.

app.UseAuthentication();           // who is this? (reads the cookie)
app.UseRateLimiter();              // after authentication, so the triage limit can partition by agent
app.UseAuthorization();            // are they allowed? (fallback policy)

app.MapControllers();

await DbSeeder.SeedInitialAgentAsync(app.Services, config, app.Logger);

app.Run();
