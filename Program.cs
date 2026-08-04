using Microsoft.EntityFrameworkCore;
using PickleballApi;
using PickleballApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
var builder = WebApplication.CreateBuilder(args);
var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL")
    ?? "https://thepicklebook.vercel.app"; // fallback for local/dev runs where the var isn't set

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
            origin == frontendUrl ||
            origin == "http://localhost:5173" ||
            (origin.StartsWith("https://thepicklebook") && origin.EndsWith(".vercel.app")) ||
            (origin.StartsWith("https://picklebook-frontend") && origin.EndsWith(".vercel.app"))
        )
        .AllowAnyHeader()
        .AllowAnyMethod();
    });
});
builder.Services.AddHttpClient();
var host = Environment.GetEnvironmentVariable("MYSQLHOST") ?? "localhost";
var port = Environment.GetEnvironmentVariable("MYSQLPORT") ?? "3306";
var database = Environment.GetEnvironmentVariable("MYSQLDATABASE") ?? "dinkdb";
var user = Environment.GetEnvironmentVariable("MYSQLUSER") ?? "root";
var password = Environment.GetEnvironmentVariable("MYSQLPASSWORD")
    ?? throw new InvalidOperationException("MYSQLPASSWORD environment variable is not set.");

var connectionString = $"server={host};port={port};database={database};user={user};password={password}";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        connectionString,
        ServerVersion.AutoDetect(connectionString)
    )
);
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY") 
    ?? builder.Configuration["Jwt:Key"]!;
var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") 
    ?? builder.Configuration["Jwt:Issuer"]!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddSingleton<IEmailService, EmailService>();

// Rate limiting is per client IP (see GetClientIp below and the
// ForwardedHeaders setup right after app.Build() — without that, every
// request looks like it comes from Railway's proxy, and everyone would
// share one limit). RejectedStatusCode 429 with no Retry-After: fixed
// window resets on its own, and the frontend doesn't need the header today.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Login attempts: brute-force protection. 5 tries per minute per IP,
    // no queueing — once you're over, you're rejected immediately rather
    // than queued and delayed.
    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetClientIp(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    // Screenshot name extraction: each call costs real money (Anthropic
    // vision API), and — since it has no auth requirement, Queue Manager
    // itself is a public page — this is the main thing standing between an
    // open endpoint and an open wallet.
    options.AddPolicy("extract-names", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetClientIp(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0
        }));

    // Booking creation: coarse abuse protection, separate from the
    // court+date named-lock (which prevents double-booking, not spam).
    options.AddPolicy("booking", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetClientIp(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

static string GetClientIp(HttpContext httpContext) =>
    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

var app = builder.Build();

// Railway sits in front of this app as a reverse proxy, so without this,
// every request's RemoteIpAddress would be Railway's proxy, not the actual
// caller — which would make the per-IP rate limits above apply to
// everyone collectively instead of per client.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Auto-run migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.UseStaticFiles();
app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");
app.MapControllers();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}