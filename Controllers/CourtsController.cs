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
    [Route("api/[controller]")]
    public class CourtsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;

        public CourtsController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // GET: api/courts
        [HttpGet]
        public async Task<ActionResult<List<Court>>> GetCourts()
        {
            return await _context.Courts
                .Include(c => c.Images)
                .ToListAsync();
        }

        // GET: api/courts/5
[HttpGet("{id}")]
public async Task<ActionResult> GetCourt(int id)
{
    var court = await _context.Courts
        .Include(c => c.Images)
        .Include(c => c.CourtOwner)
        .FirstOrDefaultAsync(c => c.Id == id);

    if (court == null)
    {
        return NotFound();
    }

    var result = new
    {
        court.Id,
        court.CourtOwnerId,
        court.Name,
        court.Address,
        court.Type,
        court.SurfaceType,
        court.MaxPlayers,
        court.PricePerHour,
        court.Description,
        court.Amenities,
        court.Rules,
        court.PaymentMethod,
        court.MonFriOpen,
        court.MonFriClose,
        court.SatOpen,
        court.SatClose,
        court.SunOpen,
        court.SunClose,
        court.ExternalBookingUrl,
        court.Latitude,
        court.Longitude,
        court.Images,
        ownerName = court.CourtOwner != null
            ? $"{court.CourtOwner.FirstName} {court.CourtOwner.LastName}".Trim()
            : null,
        ownerProfileImageUrl = court.CourtOwner != null ? court.CourtOwner.ProfileImageUrl : null
    };

    return Ok(result);
}

        // POST: api/courts/5/images
        [Authorize(Roles = "CourtOwner")]
        [HttpPost("{id}/images")]
        public async Task<ActionResult> UploadImage(int id, IFormFile file)
        {
            var court = await _context.Courts.FindAsync(id);
            if (court == null) return NotFound();

            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (court.CourtOwnerId != ownerId)
            {
                return Forbid();
            }

            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
            if (!allowedTypes.Contains(file.ContentType))
                return BadRequest("Only JPEG, PNG, and WebP images are allowed.");

            // Upload to Cloudinary
            var cloudName = Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME") ?? _config["Cloudinary:CloudName"];
            var apiKey = Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY") ?? _config["Cloudinary:ApiKey"];
            var apiSecret = Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET") ?? _config["Cloudinary:ApiSecret"];

            var account = new CloudinaryDotNet.Account(cloudName, apiKey, apiSecret);
            var cloudinary = new CloudinaryDotNet.Cloudinary(account);

            using var stream = file.OpenReadStream();
            var uploadParams = new CloudinaryDotNet.Actions.ImageUploadParams
            {
                File = new CloudinaryDotNet.FileDescription(file.FileName, stream),
                Folder = "picklebook"
            };

            var uploadResult = await cloudinary.UploadAsync(uploadParams);

            if (uploadResult.Error != null)
                return BadRequest("Image upload failed.");

            var courtImage = new CourtImage
            {
                CourtId = id,
                ImageUrl = uploadResult.SecureUrl.ToString()
            };

            _context.CourtImages.Add(courtImage);
            await _context.SaveChangesAsync();

            return Ok(new { courtImage.Id, courtImage.ImageUrl });
        }

        // PUT: api/courts/5
        [Authorize(Roles = "CourtOwner")]
        [HttpPut("{id}")]
        public async Task<ActionResult> UpdateCourt(int id, Court court)
        {
            var existing = await _context.Courts.FindAsync(id);
            if (existing == null) return NotFound();

            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (existing.CourtOwnerId != ownerId)
            {
                return Forbid();
            }
            existing.PaymentMethod = court.PaymentMethod;
            existing.Name = court.Name;
            existing.Address = court.Address;
            existing.Type = court.Type;
            existing.SurfaceType = court.SurfaceType;
            existing.MaxPlayers = court.MaxPlayers;
            existing.PricePerHour = court.PricePerHour;
            existing.Description = court.Description;
            existing.Amenities = court.Amenities;
            existing.Rules = court.Rules;
            existing.MonFriOpen = court.MonFriOpen;
            existing.MonFriClose = court.MonFriClose;
            existing.SatOpen = court.SatOpen;
            existing.SatClose = court.SatClose;
            existing.SunOpen = court.SunOpen;
            existing.SunClose = court.SunClose;
            existing.ExternalBookingUrl = court.ExternalBookingUrl;
            existing.Latitude = court.Latitude;
            existing.Longitude = court.Longitude;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        // POST: api/courts
        [Authorize(Roles = "CourtOwner")]
        [HttpPost]
        public async Task<ActionResult<Court>> CreateCourt(Court court)
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            court.CourtOwnerId = ownerId;

            _context.Courts.Add(court);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetCourts), court);
        }

        // GET: api/courts/owner
        [Authorize(Roles = "CourtOwner")]
        [HttpGet("owner")]
        public async Task<ActionResult<List<Court>>> GetOwnerCourts()
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var courts = await _context.Courts
                .Include(c => c.Images)
                .Where(c => c.CourtOwnerId == ownerId)
                .ToListAsync();
            return courts;
        }

        // DELETE: api/courts/5/images/12
        [Authorize(Roles = "CourtOwner")]
        [HttpDelete("{id}/images/{imageId}")]
        public async Task<ActionResult> DeleteImage(int id, int imageId)
        {
            var court = await _context.Courts.FindAsync(id);
            if (court == null) return NotFound();

            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (court.CourtOwnerId != ownerId)
            {
                return Forbid();
            }

            var image = await _context.CourtImages
                .FirstOrDefaultAsync(ci => ci.Id == imageId && ci.CourtId == id);
            if (image == null) return NotFound();

            // Best-effort cleanup on Cloudinary. If this fails (bad creds, network,
            // already deleted, etc.) we still remove the DB row below rather than
            // leaving a broken image reference stuck on the court.
            try
            {
                var cloudName = Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME") ?? _config["Cloudinary:CloudName"];
                var apiKey = Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY") ?? _config["Cloudinary:ApiKey"];
                var apiSecret = Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET") ?? _config["Cloudinary:ApiSecret"];
                var account = new CloudinaryDotNet.Account(cloudName, apiKey, apiSecret);
                var cloudinary = new CloudinaryDotNet.Cloudinary(account);

                var publicId = ExtractCloudinaryPublicId(image.ImageUrl);
                if (publicId != null)
                {
                    await cloudinary.DestroyAsync(new DeletionParams(publicId));
                }
            }
            catch
            {
                // Swallow: DB cleanup below is the source of truth for what
                // the app shows, Cloudinary storage cleanup is secondary.
            }

            _context.CourtImages.Remove(image);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/courts/5
        [Authorize(Roles = "CourtOwner")]
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteCourt(int id)
        {
            var court = await _context.Courts
                .Include(c => c.Images)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (court == null) return NotFound();

            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (court.CourtOwnerId != ownerId)
            {
                return Forbid();
            }

            // Don't let a court disappear out from under bookings players are
            // still relying on. Owner has to resolve those first.
            bool hasActiveBookings = await _context.Bookings.AnyAsync(b =>
                b.CourtId == id && b.Status != "Cancelled" && b.Status != "Completed");

            if (hasActiveBookings)
            {
                return BadRequest("This court has pending or confirmed bookings. Cancel or complete them before deleting the court.");
            }

            // Best-effort Cloudinary cleanup for every image on this court.
            try
            {
                var cloudName = Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME") ?? _config["Cloudinary:CloudName"];
                var apiKey = Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY") ?? _config["Cloudinary:ApiKey"];
                var apiSecret = Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET") ?? _config["Cloudinary:ApiSecret"];
                var account = new CloudinaryDotNet.Account(cloudName, apiKey, apiSecret);
                var cloudinary = new CloudinaryDotNet.Cloudinary(account);

                foreach (var image in court.Images)
                {
                    var publicId = ExtractCloudinaryPublicId(image.ImageUrl);
                    if (publicId != null)
                    {
                        await cloudinary.DestroyAsync(new DeletionParams(publicId));
                    }
                }
            }
            catch
            {
                // Non-fatal, see note above.
            }

            _context.CourtImages.RemoveRange(court.Images);
            _context.Courts.Remove(court);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // Pulls the Cloudinary public_id (folder/filename, no extension, no
        // version segment) out of a stored secure URL so we can call DestroyAsync.
        // e.g. https://res.cloudinary.com/xyz/image/upload/v169/picklebook/abc123.jpg
        //   -> "picklebook/abc123"
        private static string? ExtractCloudinaryPublicId(string imageUrl)
        {
            try
            {
                var uri = new Uri(imageUrl);
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var uploadIndex = Array.IndexOf(segments, "upload");
                if (uploadIndex == -1 || uploadIndex + 1 >= segments.Length) return null;

                var rest = segments.Skip(uploadIndex + 1).ToList();
                if (rest.Count > 0 && rest[0].Length > 1 && rest[0][0] == 'v' && rest[0].Substring(1).All(char.IsDigit))
                {
                    rest.RemoveAt(0);
                }

                var publicIdWithExt = string.Join("/", rest);
                var lastDot = publicIdWithExt.LastIndexOf('.');
                return lastDot > -1 ? publicIdWithExt.Substring(0, lastDot) : publicIdWithExt;
            }
            catch
            {
                return null;
            }
        }
    }
}
