using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;

namespace PickleballApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CourtOwnersController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;

        public CourtOwnersController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        [HttpPost("register")]
        public async Task<ActionResult> Register(RegisterOwnerDto dto)
        {
            bool emailExists = await _context.CourtOwners.AnyAsync(o => o.Email == dto.Email);
            if (emailExists)
            {
                return BadRequest("Email already in use.");
            }

            var owner = new CourtOwner
            {
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Email = dto.Email,
                Phone = dto.Phone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
            };

            _context.CourtOwners.Add(owner);
            await _context.SaveChangesAsync();

            return Ok(new { owner.Id, owner.FirstName, owner.LastName, owner.Email });
        }

        [HttpPost("login")]
        public async Task<ActionResult> Login(LoginDto dto)
        {
            var owner = await _context.CourtOwners.FirstOrDefaultAsync(o => o.Email == dto.Email);

            if (owner == null || !BCrypt.Net.BCrypt.Verify(dto.Password, owner.PasswordHash))
            {
                return Unauthorized("Invalid email or password.");
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, owner.Id.ToString()),
                new Claim(ClaimTypes.Email, owner.Email)
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
                owner.Id,
                owner.FirstName,
                owner.LastName,
                owner.Email
            });
        }
    }
}