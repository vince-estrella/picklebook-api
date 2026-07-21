using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;
using System.Security.Claims;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace PickleballApi.Controllers
{
    [ApiController]
    [Route("api/owner")]
    [Authorize(Roles = "CourtOwner")]
    public class OwnerController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;

        public OwnerController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        private int CurrentOwnerId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET: api/owner/profile
        [HttpGet("profile")]
        public async Task<ActionResult> GetProfile()
        {
            var owner = await _context.CourtOwners.FindAsync(CurrentOwnerId);
            if (owner == null) return NotFound();

            return Ok(new
            {
                owner.Email,
                owner.ProfileImageUrl
            });
        }

        // PUT: api/owner/email
        [HttpPut("email")]
        public async Task<ActionResult> UpdateEmail([FromBody] UpdateEmailDto dto)
        {
            var owner = await _context.CourtOwners.FindAsync(CurrentOwnerId);
            if (owner == null) return NotFound();

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, owner.PasswordHash))
                return BadRequest("Current password is incorrect.");

            bool emailTaken = await _context.CourtOwners.AnyAsync(o => o.Email == dto.Email && o.Id != owner.Id);
            if (emailTaken) return BadRequest("Email already in use.");

            owner.Email = dto.Email;
            await _context.SaveChangesAsync();

            return Ok(new { owner.Email });
        }

        // PUT: api/owner/password
        [HttpPut("password")]
        public async Task<ActionResult> UpdatePassword([FromBody] UpdatePasswordDto dto)
        {
            var owner = await _context.CourtOwners.FindAsync(CurrentOwnerId);
            if (owner == null) return NotFound();

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, owner.PasswordHash))
                return BadRequest("Current password is incorrect.");

            owner.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Password updated." });
        }

        // POST: api/owner/profile-picture
        [HttpPost("profile-picture")]
        public async Task<ActionResult> UploadProfilePicture(IFormFile image)
        {
            var owner = await _context.CourtOwners.FindAsync(CurrentOwnerId);
            if (owner == null) return NotFound();

            if (image == null || image.Length == 0)
                return BadRequest("No file uploaded.");

            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
            if (!allowedTypes.Contains(image.ContentType))
                return BadRequest("Only JPEG, PNG, and WebP images are allowed.");

            var cloudName = Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME") ?? _config["Cloudinary:CloudName"];
            var apiKey = Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY") ?? _config["Cloudinary:ApiKey"];
            var apiSecret = Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET") ?? _config["Cloudinary:ApiSecret"];

            var account = new Account(cloudName, apiKey, apiSecret);
            var cloudinary = new Cloudinary(account);

            using var stream = image.OpenReadStream();
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(image.FileName, stream),
                Folder = "picklebook/profile-pictures"
            };

            var uploadResult = await cloudinary.UploadAsync(uploadParams);
            if (uploadResult.Error != null)
                return BadRequest("Image upload failed.");

            owner.ProfileImageUrl = uploadResult.SecureUrl.ToString();
            await _context.SaveChangesAsync();

            return Ok(new { owner.ProfileImageUrl });
        }
    }
}   