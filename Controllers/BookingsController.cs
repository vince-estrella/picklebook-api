using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace PickleballApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BookingsController : ControllerBase
    {
        private readonly AppDbContext _context;

        // Bookings store local Philippine wall-clock times with no timezone info,
        // but the server (Railway) runs in UTC. Auto-completion needs to compare
        // against Philippine "now", not server "now", or bookings would flip to
        // Completed up to 8 hours early or late.
        private static readonly TimeZoneInfo PhilippineTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

        public BookingsController(AppDbContext context)
        {
            _context = context;
        }

        private static DateTime NowInPhilippines()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhilippineTimeZone);
        }

        // Flips any Confirmed booking whose end time has already passed to
        // Completed, and persists the change. Called right after loading a
        // batch of bookings, before they're returned or used in calculations,
        // so callers never see a stale "Confirmed" status for something that's
        // already over.
        private async Task AutoCompleteExpiredBookings(List<Booking> bookings)
        {
            var nowLocal = NowInPhilippines();
            bool changed = false;

            foreach (var b in bookings)
            {
                if (b.Status == "Confirmed" && (b.Date.Date + b.EndTime) <= nowLocal)
                {
                    b.Status = "Completed";
                    changed = true;
                }
            }

            if (changed)
            {
                await _context.SaveChangesAsync();
            }
        }

        // GET: api/bookings/court/5?date=2026-07-01
        [HttpGet("court/{courtId}")]
        public async Task<ActionResult<List<Booking>>> GetBookingsForCourt(int courtId, DateTime date)
        {
            var bookings = await _context.Bookings
                .Where(b => b.CourtId == courtId && b.Date.Date == date.Date && b.Status != "Cancelled")
                .ToListAsync();

            await AutoCompleteExpiredBookings(bookings);

            return bookings;
        }

        // GET: api/bookings/owner
        [Authorize(Roles = "CourtOwner")]
        [HttpGet("owner")]
        public async Task<ActionResult> GetOwnerBookings()
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var bookings = await _context.Bookings
                .Include(b => b.Court)
                .Where(b => b.Court!.CourtOwnerId == ownerId && b.Status != "Cancelled")
                .OrderByDescending(b => b.Date)
                .ThenByDescending(b => b.StartTime)
                .ToListAsync();

            await AutoCompleteExpiredBookings(bookings);

            var result = bookings.Select(b => new
            {
                b.Id,
                b.BookingReference,
                b.Date,
                b.StartTime,
                b.EndTime,
                b.BookerName,
                b.BookerPhone,
                b.Status,
                courtName = b.Court!.Name,
                amount = (decimal)(b.EndTime - b.StartTime).TotalHours * b.Court.PricePerHour
            });

            return Ok(result);
        }

        // GET: api/bookings/stats
        [Authorize(Roles = "CourtOwner")]
        [HttpGet("stats")]
        public async Task<ActionResult> GetOwnerStats()
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var bookings = await _context.Bookings
                .Include(b => b.Court)
                .Where(b => b.Court!.CourtOwnerId == ownerId && b.Status != "Cancelled")
                .ToListAsync();

            await AutoCompleteExpiredBookings(bookings);

            var today = DateTime.Today;
            var totalUsers = bookings.Select(b => b.BookerPhone).Distinct().Count();
            var activeBookings = bookings.Count(b => b.Date.Date >= today && b.Status == "Confirmed");

            var monthlyRevenue = bookings
                .Where(b => b.Date.Month == today.Month && b.Date.Year == today.Year)
                .Sum(b => (decimal)(b.EndTime - b.StartTime).TotalHours * b.Court!.PricePerHour);

            var weekStart = today.AddDays(-6);
            var weeklyRevenue = Enumerable.Range(0, 7).Select(offset =>
            {
                var day = weekStart.AddDays(offset);
                var total = bookings
                    .Where(b => b.Date.Date == day.Date)
                    .Sum(b => (decimal)(b.EndTime - b.StartTime).TotalHours * b.Court!.PricePerHour);
                return new
                {
                    date = day.ToString("yyyy-MM-dd"),
                    day = day.DayOfWeek.ToString().Substring(0, 3).ToUpper(),
                    total
                };
            }).ToList();

            return Ok(new
            {
                totalUsers,
                activeBookings,
                monthlyRevenue,
                weeklyRevenue
            });
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

            // Optional auth: if a valid Player token was sent, link this booking
            // to that user. If not (guest checkout), UserId just stays null —
            // this endpoint has no [Authorize], so both cases are allowed.
            int? userId = null;
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var roleClaim = User.FindFirstValue(ClaimTypes.Role);
            if (userIdClaim != null && roleClaim == "Player")
            {
                userId = int.Parse(userIdClaim);
            }

            var booking = new Booking
            {
                CourtId = dto.CourtId,
                UserId = userId,
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
        [Authorize(Roles = "CourtOwner")]
        [HttpPatch("{id}/status")]
        public async Task<ActionResult> UpdateBookingStatus(int id, [FromBody] string status)
        {
            var booking = await _context.Bookings
                .Include(b => b.Court)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
            {
                return NotFound();
            }

            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (booking.Court?.CourtOwnerId != ownerId)
            {
                return Forbid();
            }

            var validStatuses = new[] { "Pending", "Confirmed", "Cancelled", "Completed" };
            if (!validStatuses.Contains(status))
            {
                return BadRequest("Invalid status. Must be Pending, Confirmed, Cancelled, or Completed.");
            }

            booking.Status = status;
            await _context.SaveChangesAsync();

            return Ok(new { booking.Id, booking.Status });
        }
    }
}