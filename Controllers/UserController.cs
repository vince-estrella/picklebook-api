using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;
using PickleballApi.Services;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
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

        [EnableRateLimiting("auth")]
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
            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            AuthCookieHelper.SetAuthCookie(Response, "pb_player_token", tokenString);

            return Ok(new
            {
                token = tokenString,
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email,
                user.Phone
            });
        }

        [HttpPost("logout")]
        public ActionResult Logout()
        {
            AuthCookieHelper.ClearAuthCookie(Response, "pb_player_token");
            return NoContent();
        }

        // GET: api/users/bookings — the logged-in player's own booking history
        [Authorize(Roles = "Player")]
        [HttpGet("bookings")]
        public async Task<ActionResult> GetMyBookings()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var bookings = await _context.Bookings
                .Include(b => b.Court)
                .Include(b => b.OpenPlaySession)
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
                b.BookingType,
                b.OpenPlayPricePerPlayer,
                openPlay = b.BookingType == "OpenPlay"
                    ? new
                    {
                        active = b.OpenPlaySession != null && b.OpenPlaySession.Status == "Active",
                        roomCode = b.OpenPlaySession != null && b.OpenPlaySession.Status == "Active"
                            ? b.OpenPlaySession.RoomCode
                            : null
                    }
                    : null,
                courtId = b.CourtId,
                courtName = b.Court!.Name,
                courtAddress = b.Court.Address,
                amount = (decimal)(b.EndTime - b.StartTime).TotalHours * b.Court.PricePerHour
            });

            return Ok(result);
        }
        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET: api/users/profile
        [Authorize(Roles = "Player")]
        [HttpGet("profile")]
        public async Task<ActionResult> GetProfile()
        {
            var user = await _context.Users.FindAsync(CurrentUserId);
            if (user == null) return NotFound();

            return Ok(new
            {
                user.Email,
                user.ProfileImageUrl
            });
        }

        // PUT: api/users/email
        [Authorize(Roles = "Player")]
        [HttpPut("email")]
        public async Task<ActionResult> UpdateEmail([FromBody] UpdateEmailDto dto)
        {
            var user = await _context.Users.FindAsync(CurrentUserId);
            if (user == null) return NotFound();

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                return BadRequest("Current password is incorrect.");

            bool emailTaken = await _context.Users.AnyAsync(u => u.Email == dto.Email && u.Id != user.Id);
            if (emailTaken) return BadRequest("Email already in use.");

            user.Email = dto.Email;
            await _context.SaveChangesAsync();

            return Ok(new { user.Email });
        }

        // PUT: api/users/password
        [Authorize(Roles = "Player")]
        [HttpPut("password")]
        public async Task<ActionResult> UpdatePassword([FromBody] UpdatePasswordDto dto)
        {
            var user = await _context.Users.FindAsync(CurrentUserId);
            if (user == null) return NotFound();

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                return BadRequest("Current password is incorrect.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Password updated." });
        }

        // POST: api/users/profile-picture
        [Authorize(Roles = "Player")]
        [HttpPost("profile-picture")]
        public async Task<ActionResult> UploadProfilePicture(IFormFile image)
        {
            var user = await _context.Users.FindAsync(CurrentUserId);
            if (user == null) return NotFound();

            if (image == null || image.Length == 0)
                return BadRequest("No file uploaded.");

            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
            if (!allowedTypes.Contains(image.ContentType))
                return BadRequest("Only JPEG, PNG, and WebP images are allowed.");

            var cloudName = Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME") ?? _config["Cloudinary:CloudName"];
            var apiKey = Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY") ?? _config["Cloudinary:ApiKey"];
            var apiSecret = Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET") ?? _config["Cloudinary:ApiSecret"];

            var account = new CloudinaryDotNet.Account(cloudName, apiKey, apiSecret);
            var cloudinary = new CloudinaryDotNet.Cloudinary(account);

            using var stream = image.OpenReadStream();
            var uploadParams = new CloudinaryDotNet.Actions.ImageUploadParams
            {
                File = new CloudinaryDotNet.FileDescription(image.FileName, stream),
                Folder = "picklebook/profile-pictures"
            };

            var uploadResult = await cloudinary.UploadAsync(uploadParams);
            if (uploadResult.Error != null)
                return BadRequest("Image upload failed.");

            user.ProfileImageUrl = uploadResult.SecureUrl.ToString();
            await _context.SaveChangesAsync();

            return Ok(new { user.ProfileImageUrl });
        }
    }
}
