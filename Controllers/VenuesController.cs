using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;
using System.Security.Claims;

namespace PickleballApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VenuesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public VenuesController(AppDbContext context)
        {
            _context = context;
        }

        [Authorize(Roles = "CourtOwner")]
        [HttpGet("owner")]
        public async Task<ActionResult> GetOwnerVenues()
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var venues = await _context.Venues
                .Include(v => v.Courts)
                .Where(v => v.CourtOwnerId == ownerId)
                .OrderBy(v => v.Name)
                .Select(v => new
                {
                    v.Id,
                    v.Name,
                    v.Address,
                    v.Latitude,
                    v.Longitude,
                    v.Description,
                    v.Amenities,
                    v.ExternalBookingUrl,
                    courtCount = v.Courts.Count
                })
                .ToListAsync();

            return Ok(venues);
        }

        [Authorize(Roles = "CourtOwner")]
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteVenue(int id)
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var venue = await _context.Venues
                .Include(v => v.Courts)
                .FirstOrDefaultAsync(v => v.Id == id && v.CourtOwnerId == ownerId);

            if (venue == null) return NotFound();

            if (venue.Courts.Count > 0)
            {
                return BadRequest("This venue still has courts. Move or delete those courts before deleting the venue.");
            }

            _context.Venues.Remove(venue);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
