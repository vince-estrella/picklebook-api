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
        private readonly IHttpClientFactory _httpClientFactory;

        // Bookings store local Philippine wall-clock times with no timezone info,
        // but the server (Railway) runs in UTC. Auto-completion needs to compare
        // against Philippine "now", not server "now", or bookings would flip to
        // Completed up to 8 hours early or late.
        private static readonly TimeZoneInfo PhilippineTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

        public BookingsController(AppDbContext context, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
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
            var nowLocal = NowInPhilippines();

            // "Currently booked" = distinct bookers whose Confirmed booking's
            // time window includes this exact moment (started, not yet ended).
            var usersCurrentlyBooked = bookings
                .Where(b => b.Status == "Confirmed"
                    && b.Date.Date == nowLocal.Date
                    && b.Date.Date + b.StartTime <= nowLocal
                    && b.Date.Date + b.EndTime > nowLocal)
                .Select(b => b.BookerPhone)
                .Distinct()
                .Count();

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
                usersCurrentlyBooked,
                activeBookings,
                monthlyRevenue,
                weeklyRevenue
            });
        }

        // POST: api/bookings
        [HttpPost]
        public async Task<ActionResult> CreateBooking(CreateBookingDto dto)
        {
            var court = await _context.Courts.FirstOrDefaultAsync(c => c.Id == dto.CourtId);
            if (court == null)
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

            int? userId = null;
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var roleClaim = User.FindFirstValue(ClaimTypes.Role);
            if (userIdClaim != null && roleClaim == "Player")
            {
                userId = int.Parse(userIdClaim);
            }

            // Court.PaymentMethod == "PayMongo" is the owner-facing flag meaning
            // "this court takes online payment" — kept as that literal string for
            // now since renaming it touches the owner court-settings UI too.
            bool requiresOnlinePayment = court.PaymentMethod == "PayMongo";

            var booking = new Booking
            {
                CourtId = dto.CourtId,
                UserId = userId,
                Date = dto.Date,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                BookerName = dto.BookerName,
                BookerPhone = dto.BookerPhone,
                PaymentMethod = requiresOnlinePayment ? "Online" : "PayAtVenue",
                PaymentStatus = requiresOnlinePayment ? "Unpaid" : "Paid",
                Status = requiresOnlinePayment ? "Pending" : "Confirmed"
            };
            booking.BookingReference = $"#PKL-{new Random().Next(1000, 9999)}-{(char)('A' + new Random().Next(0, 26))}";
            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            if (!requiresOnlinePayment)
            {
                return Ok(new { booking, checkoutUrl = (string?)null });
            }

            var hours = (decimal)(dto.EndTime - dto.StartTime).TotalHours;
            var amount = hours * court.PricePerHour;

            try
            {
                var (invoiceId, checkoutUrl) = await CreateXenditInvoice(booking.Id, amount, $"{court.Name} — {dto.Date:yyyy-MM-dd} {dto.StartTime}");
                booking.XenditInvoiceId = invoiceId;
                await _context.SaveChangesAsync();

                return Ok(new { booking, checkoutUrl });
            }
            catch
            {
                // Don't leave an unpayable booking sitting in the DB blocking the
                // slot forever — roll it back so the player can retry cleanly.
                _context.Bookings.Remove(booking);
                await _context.SaveChangesAsync();
                return StatusCode(502, "Could not start payment. Please try again.");
            }
        }

        private async Task<(string invoiceId, string checkoutUrl)> CreateXenditInvoice(int bookingId, decimal amount, string description)
        {
            var secretKey = Environment.GetEnvironmentVariable("XENDIT_SECRET_KEY")!;
            var client = _httpClientFactory.CreateClient();
            var authValue = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{secretKey}:"));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);

            var payload = new
            {
                external_id = $"booking-{bookingId}",
                amount = amount,
                description,
                currency = "PHP"
            };

            var response = await client.PostAsJsonAsync("https://api.xendit.co/v2/invoices", payload);
            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Failed to create Xendit invoice.");
            }

            var invoiceId = json.GetProperty("id").GetString()!;
            var checkoutUrl = json.GetProperty("invoice_url").GetString()!;

            return (invoiceId, checkoutUrl);
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