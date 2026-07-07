
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;

namespace PickleballApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CourtsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CourtsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/courts
        [HttpGet]
        public async Task<ActionResult<List<Court>>> GetCourts()
        {
            return await _context.Courts.ToListAsync();
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

        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
        Directory.CreateDirectory(uploadsFolder);

        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var filePath = Path.Combine(uploadsFolder, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var imageUrl = $"/uploads/{fileName}";

        var courtImage = new CourtImage
        {
            CourtId = id,
            ImageUrl = imageUrl
        };

        _context.CourtImages.Add(courtImage);
        await _context.SaveChangesAsync();

        return Ok(new { courtImage.Id, courtImage.ImageUrl });
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
    }
}