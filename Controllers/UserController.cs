using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;

namespace PickleballApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;

        public UsersController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        [HttpPost("register")]
        public async Task<ActionResult> Register(RegisterUserDto dto)
        {
            bool emailExists = await _context.Users.AnyAsync(u => u.Email == dto.Email);
            if (emailExists)
            {
                return BadRequest("Email already in use.");
            }

            var user = new User
            {
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Email = dto.Email,
                Phone = dto.Phone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { user.Id, user.FirstName, user.LastName, user.Email });
        }

        [HttpPost("login")]
        public async Task<ActionResult> Login(LoginDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            {
                return Unauthorized("Invalid email or password.");
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, "Player")
            };

            var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY") ?? _config["Jwt:Key"]!;
            var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? _config["Jwt:Issuer"]!;

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                claims: claims,
                expires: DateTime.UtcNow.AddDays(7),
                signingCredentials: creds
            );

            return Ok(new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token),
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email,
                user.Phone
            });
        }

        // GET: api/users/bookings — the logged-in player's own booking history
        [Authorize(Roles = "Player")]
        [HttpGet("bookings")]
        public async Task<ActionResult> GetMyBookings()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var bookings = await _context.Bookings
                .Include(b => b.Court)
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.Date)
                .ThenByDescending(b => b.StartTime)
                .ToListAsync();

            var result = bookings.Select(b => new
            {
                b.Id,
                b.BookingReference,
                b.Date,
                b.StartTime,
                b.EndTime,
                b.Status,
                courtId = b.CourtId,
                courtName = b.Court!.Name,
                courtAddress = b.Court.Address,
                amount = (decimal)(b.EndTime - b.StartTime).TotalHours * b.Court.PricePerHour
            });

            return Ok(result);
        }
    }
}