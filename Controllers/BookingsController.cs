using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;
using Microsoft.AspNetCore.Authorization;

namespace PickleballApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BookingsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public BookingsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/bookings/court/5?date=2026-07-01
        [HttpGet("court/{courtId}")]
        public async Task<ActionResult<List<Booking>>> GetBookingsForCourt(int courtId, DateTime date)
        {
            var bookings = await _context.Bookings
                .Where(b => b.CourtId == courtId && b.Date.Date == date.Date && b.Status != "Cancelled")
                .ToListAsync();

            return bookings;
        }

        // POST: api/bookings
        [HttpPost]
        public async Task<ActionResult<Booking>> CreateBooking(CreateBookingDto dto)
        {
            bool courtExists = await _context.Courts.AnyAsync(c => c.Id == dto.CourtId);
            if (!courtExists)
            {
                return BadRequest("Court does not exist.");
            }

            bool overlaps = await _context.Bookings.AnyAsync(b =>
                b.CourtId == dto.CourtId &&
                b.Date.Date == dto.Date.Date &&
                b.Status != "Cancelled" &&
                dto.StartTime < b.EndTime &&
                dto.EndTime > b.StartTime
            );

            if (overlaps)
            {
                return BadRequest("This time slot is already booked.");
            }

            var booking = new Booking
            {
                CourtId = dto.CourtId,
                Date = dto.Date,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                BookerName = dto.BookerName,
                BookerPhone = dto.BookerPhone,
                Status = "Confirmed"
            };
            booking.BookingReference = $"#PKL-{new Random().Next(1000, 9999)}-{(char)('A' + new Random().Next(0, 26))}";
            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            return Ok(booking);
        }
        // PATCH: api/bookings/5/status
[Authorize]
[HttpPatch("{id}/status")]
public async Task<ActionResult> UpdateBookingStatus(int id, [FromBody] string status)
{
    var booking = await _context.Bookings.FindAsync(id);

    if (booking == null)
    {
        return NotFound();
    }

    var validStatuses = new[] { "Pending", "Confirmed", "Cancelled" };
    if (!validStatuses.Contains(status))
    {
        return BadRequest("Invalid status. Must be Pending, Confirmed, or Cancelled.");
    }

    booking.Status = status;
    await _context.SaveChangesAsync();

    return Ok(new { booking.Id, booking.Status });
}
    }
}