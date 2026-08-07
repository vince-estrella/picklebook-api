using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;
using PickleballApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace PickleballApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BookingsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IEmailService _emailService;

        private static readonly TimeZoneInfo PhilippineTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

        public BookingsController(AppDbContext context, IHttpClientFactory httpClientFactory, IEmailService emailService)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _emailService = emailService;
        }

        private static DateTime NowInPhilippines()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhilippineTimeZone);
        }

        private static (TimeSpan Open, TimeSpan Close) GetCourtHoursForDate(Court court, DateTime date)
        {
            var hours = date.DayOfWeek switch
            {
                DayOfWeek.Saturday => (court.SatOpen, court.SatClose),
                DayOfWeek.Sunday => (court.SunOpen, court.SunClose),
                _ => (court.MonFriOpen, court.MonFriClose)
            };

            if (hours.Item1 == TimeSpan.Zero && hours.Item2 == TimeSpan.Zero)
            {
                return date.DayOfWeek switch
                {
                    DayOfWeek.Saturday => (new TimeSpan(7, 0, 0), new TimeSpan(21, 0, 0)),
                    DayOfWeek.Sunday => (new TimeSpan(8, 0, 0), new TimeSpan(20, 0, 0)),
                    _ => (new TimeSpan(6, 0, 0), new TimeSpan(22, 0, 0))
                };
            }

            return hours;
        }

        private static string GenerateRoomCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            Span<char> code = stackalloc char[5];
            for (var i = 0; i < code.Length; i++)
            {
                code[i] = chars[Random.Shared.Next(chars.Length)];
            }
            return new string(code);
        }

        private async Task<OpenPlaySession> ActivateOpenPlaySession(Booking booking)
        {
            var existing = await _context.OpenPlaySessions
                .FirstOrDefaultAsync(s => s.BookingId == booking.Id);

            if (existing != null)
            {
                if (existing.Status != "Active")
                {
                    existing.Status = "Active";
                    existing.ActivatedAt ??= DateTime.UtcNow;
                }
                return existing;
            }

            if (booking.UserId == null)
            {
                throw new InvalidOperationException("Open play bookings require a host player account.");
            }

            string roomCode;
            do
            {
                roomCode = GenerateRoomCode();
            } while (await _context.OpenPlaySessions.AnyAsync(s => s.RoomCode == roomCode));

            var session = new OpenPlaySession
            {
                BookingId = booking.Id,
                HostUserId = booking.UserId.Value,
                RoomCode = roomCode,
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                ActivatedAt = DateTime.UtcNow
            };

            _context.OpenPlaySessions.Add(session);
            await _context.SaveChangesAsync();

            _context.OpenPlayParticipants.Add(new OpenPlayParticipant
            {
                OpenPlaySessionId = session.Id,
                UserId = booking.UserId.Value,
                PaymentStatus = "PaidCash",
                CheckInStatus = "Joined",
                JoinedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return session;
        }

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

        // Frees up slots that were held by a booking that never actually
        // completed:
        //  - Online payments get a 15-minute window from CreatedAt to finish
        //    checkout. Someone who starts paying and abandons the tab (or a
        //    slow bank OTP flow) shouldn't hold the slot indefinitely — past
        //    the window, unpaid Online bookings auto-cancel.
        //  - Pay-at-venue bookings have no time limit while Pending — the
        //    owner confirms those manually when the player checks in, and
        //    that can legitimately happen right up to game time. But if the
        //    slot's own time has already passed and it's still sitting
        //    Pending, nobody ever confirmed it — auto-cancel it too, for
        //    either payment method, so it doesn't linger forever.
        // If a late Xendit "paid" webhook arrives for a booking this already
        // cancelled, PaymentsController.XenditWebhook handles resurrecting it
        // (if the slot's still free) or logging it for manual follow-up (if
        // someone else already took it).
        private async Task AutoExpireStalePendingBookings(List<Booking> bookings)
        {
            var nowUtc = DateTime.UtcNow;
            var nowLocal = NowInPhilippines();
            bool changed = false;

            foreach (var b in bookings)
            {
                if (b.Status != "Pending") continue;

                bool onlinePaymentWindowExpired = b.PaymentMethod == "Online"
                    && b.PaymentStatus != "Paid"
                    && (nowUtc - b.CreatedAt) >= TimeSpan.FromMinutes(15);

                bool slotTimeAlreadyPassed = (b.Date.Date + b.EndTime) <= nowLocal;

                if (onlinePaymentWindowExpired || slotTimeAlreadyPassed)
                {
                    b.Status = "Cancelled";
                    b.CancelledAt = nowUtc;
                    changed = true;
                }
            }

            if (changed)
            {
                await _context.SaveChangesAsync();
            }
        }

        // GET: api/bookings/5?token=... — used by BookingConfirmedPage after an
        // external redirect back from Xendit's hosted checkout, where React
        // Router state doesn't survive (the browser left the app entirely).
        // The numeric id is enumerable, so it's not treated as sensitive on its
        // own: the caller must also present the PublicToken issued at booking
        // creation. A missing/mismatched token — including bookings created
        // before this field existed, where PublicToken is null — returns
        // NotFound rather than Forbid, so we don't confirm the id exists.
        [HttpGet("{id}")]
        public async Task<ActionResult> GetBooking(int id, [FromQuery] string? token)
        {
            var booking = await _context.Bookings
                .Include(b => b.Court)
                .Include(b => b.OpenPlaySession)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();
            if (string.IsNullOrEmpty(booking.PublicToken) || booking.PublicToken != token)
            {
                return NotFound();
            }

            var hours = (decimal)(booking.EndTime - booking.StartTime).TotalHours;
            var amount = hours * (booking.Court?.PricePerHour ?? 0);

            return Ok(new
            {
                booking.Id,
                booking.BookingReference,
                booking.PublicToken,
                booking.Date,
                booking.StartTime,
                booking.EndTime,
                booking.PaymentMethod,
                booking.BookingType,
                booking.OpenPlayMaxPlayers,
                booking.OpenPlayPricePerPlayer,
                booking.OpenPlaySkillLevel,
                booking.OpenPlayNote,
                booking.OpenPlayReclubLink,
                booking.PaymentStatus,
                booking.Status,
                amount,
                openPlay = booking.BookingType == "OpenPlay"
                    ? new
                    {
                        active = booking.OpenPlaySession != null && booking.OpenPlaySession.Status == "Active",
                        roomCode = booking.OpenPlaySession != null && booking.OpenPlaySession.Status == "Active"
                            ? booking.OpenPlaySession.RoomCode
                            : null
                    }
                    : null,
                court = booking.Court == null ? null : new
                {
                    booking.Court.Id,
                    booking.Court.Name,
                    booking.Court.Address,
                    booking.Court.PricePerHour
                }
            });
        }

        // GET: api/bookings/owner/5 — full receipt detail for the court owner's
        // Bookings page. Unlike the public GET /bookings/{id} (used by the
        // player's confirmation page), this requires auth and verifies the
        // booking actually belongs to a court owned by the requester, since
        // it exposes the booker's name/phone.
        [Authorize(Roles = "CourtOwner")]
        [HttpGet("owner/{id}")]
        public async Task<ActionResult> GetOwnerBookingDetail(int id)
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var booking = await _context.Bookings
                .Include(b => b.Court)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();
            if (booking.Court?.CourtOwnerId != ownerId) return Forbid();

            var hours = (decimal)(booking.EndTime - booking.StartTime).TotalHours;
            var amount = hours * (booking.Court?.PricePerHour ?? 0);

            return Ok(new
            {
                booking.Id,
                booking.BookingReference,
                booking.PublicToken,
                booking.Date,
                booking.StartTime,
                booking.EndTime,
                booking.BookerName,
                booking.BookerPhone,
                booking.PaymentMethod,
                booking.BookingType,
                booking.OpenPlayMaxPlayers,
                booking.OpenPlayPricePerPlayer,
                booking.OpenPlaySkillLevel,
                booking.OpenPlayNote,
                booking.OpenPlayReclubLink,
                booking.PaymentStatus,
                booking.Status,
                amount,
                court = booking.Court == null ? null : new
                {
                    booking.Court.Id,
                    booking.Court.Name,
                    booking.Court.Address,
                    booking.Court.PricePerHour
                }
            });
        }

        // GET: api/bookings/court/5?date=2026-07-01
        [HttpGet("court/{courtId}")]
        public async Task<ActionResult<List<Booking>>> GetBookingsForCourt(int courtId, DateTime date)
        {
            var bookings = await _context.Bookings
                .Where(b => b.CourtId == courtId && b.Date.Date == date.Date && b.Status != "Cancelled")
                .ToListAsync();

            await AutoCompleteExpiredBookings(bookings);
            await AutoExpireStalePendingBookings(bookings);

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
            await AutoExpireStalePendingBookings(bookings);

            var result = bookings.Select(b => new
            {
                b.Id,
                b.BookingReference,
                b.Date,
                b.StartTime,
                b.EndTime,
                b.BookerName,
                b.BookerPhone,
                b.BookingType,
                b.OpenPlayPricePerPlayer,
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
            await AutoExpireStalePendingBookings(bookings);

            var nowLocal = NowInPhilippines();
            var today = nowLocal.Date;

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
                .Where(b => b.Date.Month == today.Month && b.Date.Year == today.Year && b.PaymentStatus == "Paid")
                .Sum(b => (decimal)(b.EndTime - b.StartTime).TotalHours * b.Court!.PricePerHour);

            var weekStart = today.AddDays(-6);
            var weeklyRevenue = Enumerable.Range(0, 7).Select(offset =>
            {
                var day = weekStart.AddDays(offset);
                var total = bookings
                    .Where(b => b.Date.Date == day.Date && b.PaymentStatus == "Paid")
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
        [EnableRateLimiting("booking")]
        [HttpPost]
        public async Task<ActionResult> CreateBooking(CreateBookingDto dto)
        {
            var court = await _context.Courts.FirstOrDefaultAsync(c => c.Id == dto.CourtId);
            if (court == null)
            {
                return BadRequest("Court does not exist.");
            }

            if (dto.EndTime <= dto.StartTime)
            {
                return BadRequest("End time must be after start time.");
            }

            var nowLocal = NowInPhilippines();
            if (dto.Date.Date < nowLocal.Date ||
                (dto.Date.Date == nowLocal.Date && dto.StartTime <= nowLocal.TimeOfDay))
            {
                return BadRequest("Cannot book a date or time slot that's already in the past.");
            }

            var (openTime, closeTime) = GetCourtHoursForDate(court, dto.Date);
            if (dto.StartTime < openTime || dto.EndTime > closeTime)
            {
                return BadRequest("This booking is outside the court's operating hours.");
            }

            int? userId = null;
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var roleClaim = User.FindFirstValue(ClaimTypes.Role);
            if (userIdClaim != null && roleClaim == "Player")
            {
                userId = int.Parse(userIdClaim);
            }

            var bookingType = dto.BookingType == "OpenPlay" ? "OpenPlay" : "Standard";
            if (bookingType == "OpenPlay")
            {
                if (userId == null)
                {
                    return Unauthorized("Log in as a player to create an open play booking.");
                }

                if (!court.AllowOpenPlay)
                {
                    return BadRequest("This court does not allow player-hosted open play.");
                }

                if (dto.OpenPlayMaxPlayers is < 2 or > 64)
                {
                    return BadRequest("Open play max players must be between 2 and 64.");
                }

                if (dto.OpenPlayPricePerPlayer is < 0)
                {
                    return BadRequest("Open play price per player cannot be negative.");
                }

                if (!string.IsNullOrWhiteSpace(dto.OpenPlayReclubLink) &&
                    (!Uri.TryCreate(dto.OpenPlayReclubLink.Trim(), UriKind.Absolute, out var reclubUri) ||
                     (reclubUri.Scheme != Uri.UriSchemeHttp && reclubUri.Scheme != Uri.UriSchemeHttps)))
                {
                    return BadRequest("Reclub link must be a valid web URL.");
                }
            }

            bool requiresOnlinePayment = court.PaymentMethod == "Online";

            // Serialize "check for overlap, then insert" per court+date so two
            // near-simultaneous requests can't both pass the overlap check
            // before either one has actually saved (the previous race that
            // allowed double-booking). Scoped to court+date, not the whole
            // table, so unrelated bookings never wait on each other.
            var lockName = $"booking:{dto.CourtId}:{dto.Date:yyyy-MM-dd}";
            var lockAcquired = await _context.Database
                .SqlQueryRaw<int?>("SELECT GET_LOCK({0}, {1}) AS `Value`", lockName, 5)
                .FirstAsync();

            if (lockAcquired != 1)
            {
                return StatusCode(409, "Someone else is booking this slot right now. Please try again.");
            }

            Booking booking;
            try
            {
                // Fetch actual candidate rows (not just AnyAsync) so we can run
                // AutoExpireStalePendingBookings against them first — this is
                // the moment a stale/abandoned hold on this exact slot actually
                // gets freed, right before we'd otherwise wrongly reject a new
                // booking because of it.
                var candidateBookings = await _context.Bookings
                    .Where(b => b.CourtId == dto.CourtId && b.Date.Date == dto.Date.Date && b.Status != "Cancelled")
                    .ToListAsync();

                await AutoExpireStalePendingBookings(candidateBookings);

                bool overlaps = candidateBookings.Any(b =>
                    b.Status != "Cancelled" &&
                    dto.StartTime < b.EndTime &&
                    dto.EndTime > b.StartTime
                );

                if (overlaps)
                {
                    return BadRequest("This time slot is already booked.");
                }

                booking = new Booking
                {
                    CourtId = dto.CourtId,
                    UserId = userId,
                    Date = dto.Date,
                    StartTime = dto.StartTime,
                    EndTime = dto.EndTime,
                    BookerName = dto.BookerName,
                    BookerPhone = dto.BookerPhone,
                    BookerEmail = dto.BookerEmail,
                    PaymentMethod = requiresOnlinePayment ? "Online" : "PayAtVenue",
                    BookingType = bookingType,
                    OpenPlayMaxPlayers = bookingType == "OpenPlay" ? dto.OpenPlayMaxPlayers ?? 8 : null,
                    OpenPlayPricePerPlayer = bookingType == "OpenPlay" ? dto.OpenPlayPricePerPlayer : null,
                    OpenPlaySkillLevel = bookingType == "OpenPlay" ? string.IsNullOrWhiteSpace(dto.OpenPlaySkillLevel) ? "All Levels" : dto.OpenPlaySkillLevel.Trim() : null,
                    OpenPlayNote = bookingType == "OpenPlay" ? dto.OpenPlayNote?.Trim() : null,
                    OpenPlayReclubLink = bookingType == "OpenPlay" ? dto.OpenPlayReclubLink?.Trim() : null,
                    PaymentStatus = "Unpaid",
                    // Every booking starts Pending now — online payments flip to
                    // Confirmed automatically via the Xendit webhook once paid;
                    // pay-at-venue bookings wait for the owner to confirm once
                    // the player checks in and actually pays on-site.
                    Status = "Pending",
                    PublicToken = Guid.NewGuid().ToString("N"),
                    CreatedAt = DateTime.UtcNow
                };
                _context.Bookings.Add(booking);
                await _context.SaveChangesAsync();

                // BookingReference is derived from the real, DB-assigned Id —
                // unique by definition, unlike the old random 4-digit + letter
                // reference, which had no uniqueness check and only ~234,000
                // possible values.
                booking.BookingReference = $"#PKL-{booking.Id:D5}";
                await _context.SaveChangesAsync();
            }
            finally
            {
                // Always release, even on the BadRequest/overlap path above —
                // otherwise this court+date would stay locked until the
                // connection is recycled.
                await _context.Database.ExecuteSqlRawAsync("SELECT RELEASE_LOCK({0})", lockName);
            }

            var hours = (decimal)(dto.EndTime - dto.StartTime).TotalHours;
            var amount = hours * court.PricePerHour;

            object BuildBookingResponse() => new
            {
                booking.Id,
                booking.BookingReference,
                booking.Date,
                booking.StartTime,
                booking.EndTime,
                booking.BookerName,
                booking.BookerPhone,
                booking.BookingType,
                booking.OpenPlayMaxPlayers,
                booking.OpenPlayPricePerPlayer,
                booking.OpenPlaySkillLevel,
                booking.OpenPlayNote,
                booking.OpenPlayReclubLink,
                booking.PaymentMethod,
                booking.PaymentStatus,
                booking.Status,
                amount
            };

            if (!requiresOnlinePayment)
            {
                // Pay-at-venue bookings have nothing further to wait on, so the
                // receipt goes out right away. Online bookings get theirs once
                // the Xendit webhook confirms the payment actually went through.
                await _emailService.SendBookingReceiptAsync(booking, court);
                return Ok(new { booking = BuildBookingResponse(), checkoutUrl = (string?)null });
            }

            try
            {
                var (invoiceId, checkoutUrl) = await CreateXenditInvoice(booking.Id, booking.PublicToken!, amount, $"{court.Name} — {dto.Date:yyyy-MM-dd} {dto.StartTime}");
                booking.XenditInvoiceId = invoiceId;
                await _context.SaveChangesAsync();

                return Ok(new { booking = BuildBookingResponse(), checkoutUrl });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Xendit invoice creation failed: {ex.Message}");
                _context.Bookings.Remove(booking);
                await _context.SaveChangesAsync();
                return StatusCode(502, "Could not start payment. Please try again.");
            }
        }

        private async Task<(string invoiceId, string checkoutUrl)> CreateXenditInvoice(int bookingId, string publicToken, decimal amount, string description)
        {
            var secretKey = Environment.GetEnvironmentVariable("XENDIT_SECRET_KEY")!;
            var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "https://www.thepicklebook.app";

            var client = _httpClientFactory.CreateClient();
            var authValue = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{secretKey}:"));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);

            var payload = new
            {
                external_id = $"booking-{bookingId}",
                amount = amount,
                description,
                currency = "PHP",
                success_redirect_url = $"{frontendUrl}/booking/confirmed?bookingId={bookingId}&token={Uri.EscapeDataString(publicToken)}"
            };

            var response = await client.PostAsJsonAsync("https://api.xendit.co/v2/invoices", payload);
            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Xendit API error ({(int)response.StatusCode}): {json}");
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

            // Confirming a pay-at-venue booking means the owner just collected
            // payment in person — reflect that so receipts/reports don't keep
            // showing it as unpaid.
            if (status == "Confirmed" && booking.PaymentMethod == "PayAtVenue" && booking.PaymentStatus != "Paid")
            {
                booking.PaymentStatus = "Paid";
                booking.PaidAt = DateTime.UtcNow;
            }

            OpenPlaySession? openPlaySession = null;
            if (status == "Confirmed" && booking.BookingType == "OpenPlay")
            {
                openPlaySession = await ActivateOpenPlaySession(booking);
            }

            if (status == "Cancelled" && booking.CancelledAt == null)
            {
                booking.CancelledAt = DateTime.UtcNow;
            }

            if (status != "Confirmed" && booking.BookingType == "OpenPlay")
            {
                openPlaySession ??= await _context.OpenPlaySessions
                    .FirstOrDefaultAsync(s => s.BookingId == booking.Id);

                if (openPlaySession != null)
                {
                    openPlaySession.Status = status == "Cancelled" ? "Cancelled" : "Closed";
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                booking.Id,
                booking.Status,
                booking.PaymentStatus,
                openPlay = openPlaySession == null
                    ? null
                    : new { openPlaySession.RoomCode, openPlaySession.Status }
            });
        }
    }
}
