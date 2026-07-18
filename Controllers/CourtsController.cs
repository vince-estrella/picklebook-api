
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
        [HttpGet]
public async Task<ActionResult<List<Court>>> GetCourts()
{
    return await _context.Courts
        .Include(c => c.Images)
        .ToListAsync();
}
        // GET: api/courts/5
    [HttpGet("{id}")]
    public async Task<ActionResult<Court>> GetCourt(int id)
    {
        var court = await _context.Courts
            .Include(c => c.Images)
            .FirstOrDefaultAsync(c => c.Id == id);

    if (court == null)
        {
            return NotFound();
        }

    return court;
    }
// POST: api/courts/5/images
    [Authorize]
[HttpPost("{id}/images")]
public async Task<ActionResult> UploadImage(int id, IFormFile file)
{
    var court = await _context.Courts.FindAsync(id);
    if (court == null) return NotFound();

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
} [Authorize]
[HttpPut("{id}")]
public async Task<ActionResult> UpdateCourt(int id, Court court)
{
    var existing = await _context.Courts.FindAsync(id);
    if (existing == null) return NotFound();

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
        [Authorize]
        [HttpPost]
        public async Task<ActionResult<Court>> CreateCourt(Court court)
        {
            _context.Courts.Add(court);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetCourts), court);
        }
    
    [Authorize]
[HttpGet("owner")]
public async Task<ActionResult<List<Court>>> GetOwnerCourts()
{
    var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var courts = await _context.Courts
        .Include(c => c.Images)
        .Where(c => c.CourtOwnerId == ownerId)
        .ToListAsync();
    return courts;
}}
}