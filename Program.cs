using Microsoft.EntityFrameworkCore;
using PickleballApi;
using PickleballApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
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

var app = builder.Build();
// Auto-run migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

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