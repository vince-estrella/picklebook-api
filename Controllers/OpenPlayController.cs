using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;
using PickleballApi.Services;
using System.Security.Claims;

namespace PickleballApi.Controllers
{
    [ApiController]
    [Route("api/openplay")]
    public class OpenPlayController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IPushNotificationService _push;

        public OpenPlayController(AppDbContext context, IPushNotificationService push)
        {
            _context = context;
            _push = push;
        }

        [HttpGet("booking/{bookingId}")]
        public async Task<ActionResult> GetForBooking(int bookingId, [FromQuery] string? token)
        {
            var booking = await _context.Bookings
                .Include(b => b.Court)
                .Include(b => b.OpenPlaySession)
                .ThenInclude(s => s!.Participants)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null) return NotFound();
            if (string.IsNullOrEmpty(booking.PublicToken) || booking.PublicToken != token) return NotFound();
            if (booking.BookingType != "OpenPlay") return NotFound();

            return Ok(new
            {
                booking.Id,
                booking.Status,
                booking.OpenPlayMaxPlayers,
                booking.OpenPlayPricePerPlayer,
                booking.OpenPlaySkillLevel,
                booking.OpenPlayNote,
                booking.OpenPlayReclubLink,
                active = booking.OpenPlaySession?.Status == "Active",
                roomCode = booking.OpenPlaySession?.Status == "Active" ? booking.OpenPlaySession.RoomCode : null,
                joinedCount = booking.OpenPlaySession?.Participants.Count ?? 0,
                court = booking.Court == null ? null : new
                {
                    booking.Court.Id,
                    booking.Court.Name,
                    booking.Court.Address
                }
            });
        }

        [HttpGet("court/{courtId}")]
        public async Task<ActionResult> GetCourtOpenPlays(int courtId)
        {
            var today = DateTime.Today;
            var sessions = await _context.OpenPlaySessions
                .Include(s => s.Booking)
                .ThenInclude(b => b!.Court)
                .Include(s => s.Participants)
                .Where(s =>
                    s.Status == "Active" &&
                    s.Booking != null &&
                    s.Booking.CourtId == courtId &&
                    s.Booking.Status == "Confirmed" &&
                    s.Booking.Date >= today)
                .OrderBy(s => s.Booking!.Date)
                .ThenBy(s => s.Booking!.StartTime)
                .Take(10)
                .Select(s => new
                {
                    s.Id,
                    s.RoomCode,
                    s.Status,
                    s.Booking!.Date,
                    s.Booking.StartTime,
                    s.Booking.EndTime,
                    s.Booking.OpenPlayMaxPlayers,
                    s.Booking.OpenPlayPricePerPlayer,
                    s.Booking.OpenPlaySkillLevel,
                    s.Booking.OpenPlayNote,
                    s.Booking.OpenPlayReclubLink,
                    joinedCount = s.Participants.Count
                })
                .ToListAsync();

            return Ok(sessions);
        }

        [Authorize(Roles = "CourtOwner")]
        [HttpGet("owner/sessions")]
        public async Task<ActionResult> GetOwnerSessions()
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var sessions = await _context.OpenPlaySessions
                .Include(s => s.Booking)
                .ThenInclude(b => b!.Court)
                .Include(s => s.Participants)
                .ThenInclude(p => p.User)
                .Where(s => s.HostOwnerId == ownerId && s.Booking != null)
                .OrderByDescending(s => s.Booking!.Date)
                .ThenByDescending(s => s.Booking!.StartTime)
                .ToListAsync();

            return Ok(sessions.Select(ToOwnerSessionDto));
        }

        [Authorize(Roles = "CourtOwner")]
        [HttpGet("owner/sessions/{roomCode}")]
        public async Task<ActionResult> GetOwnerSession(string roomCode)
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var session = await LoadSession(roomCode);
            if (session == null || session.HostOwnerId != ownerId) return NotFound();
            return Ok(ToOwnerSessionDto(session));
        }

        [Authorize(Roles = "CourtOwner")]
        [HttpPost("owner/sessions")]
        public async Task<ActionResult> CreateOwnerSession(CreateOwnerOpenPlayDto dto)
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var owner = await _context.CourtOwners.FindAsync(ownerId);
            var court = await _context.Courts.FirstOrDefaultAsync(c => c.Id == dto.CourtId && c.CourtOwnerId == ownerId);
            if (owner == null || court == null) return NotFound("Court not found.");

            if (dto.EndTime <= dto.StartTime) return BadRequest("End time must be after start time.");
            if (dto.MaxPlayers is < 2 or > 64) return BadRequest("Max players must be between 2 and 64.");
            if (dto.PricePerPlayer < 0) return BadRequest("Price per player cannot be negative.");
            if (!string.IsNullOrWhiteSpace(dto.ReclubLink) && !IsHttpUrl(dto.ReclubLink))
            {
                return BadRequest("Reclub link must be a valid URL.");
            }

            var overlaps = await _context.Bookings.AnyAsync(b =>
                b.CourtId == dto.CourtId &&
                b.Date.Date == dto.Date.Date &&
                b.Status != "Cancelled" &&
                dto.StartTime < b.EndTime &&
                dto.EndTime > b.StartTime);
            if (overlaps) return Conflict("That court already has a booking or open play in this time slot.");

            var booking = new Booking
            {
                CourtId = dto.CourtId,
                Date = dto.Date.Date,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                CreatedAt = DateTime.UtcNow,
                BookerName = $"{owner.FirstName} {owner.LastName}".Trim(),
                BookerPhone = owner.Phone,
                BookerEmail = owner.Email,
                PaymentMethod = "PayAtVenue",
                PaymentStatus = "Paid",
                Status = "Confirmed",
                BookingType = "OpenPlay",
                PublicToken = Guid.NewGuid().ToString("N"),
                OpenPlayMaxPlayers = dto.MaxPlayers,
                OpenPlayPricePerPlayer = dto.PricePerPlayer,
                OpenPlaySkillLevel = string.IsNullOrWhiteSpace(dto.SkillLevel) ? "All Levels" : dto.SkillLevel.Trim(),
                OpenPlayNote = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
                OpenPlayReclubLink = string.IsNullOrWhiteSpace(dto.ReclubLink) ? null : dto.ReclubLink.Trim()
            };

            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            booking.BookingReference = $"PB-{booking.Id:D6}";
            var session = new OpenPlaySession
            {
                BookingId = booking.Id,
                HostOwnerId = ownerId,
                RoomCode = await GenerateUniqueRoomCode(),
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                ActivatedAt = DateTime.UtcNow
            };

            _context.OpenPlaySessions.Add(session);
            await _context.SaveChangesAsync();

            var created = await LoadSession(session.RoomCode);
            return CreatedAtAction(nameof(GetOwnerSession), new { roomCode = session.RoomCode }, ToOwnerSessionDto(created!));
        }

        [Authorize(Roles = "CourtOwner")]
        [HttpPatch("owner/sessions/{sessionId}/cancel")]
        public async Task<ActionResult> CancelOwnerSession(int sessionId)
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var session = await _context.OpenPlaySessions
                .Include(s => s.Booking)
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.HostOwnerId == ownerId);

            if (session?.Booking == null) return NotFound();

            session.Status = "Cancelled";
            session.Booking.Status = "Cancelled";
            session.Booking.CancelledAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { session.Id, session.Status });
        }

        [Authorize(Roles = "Player")]
        [HttpGet("sessions/{roomCode}")]
        public async Task<ActionResult> GetSession(string roomCode)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var session = await LoadSession(roomCode);
            if (session == null || session.Status != "Active") return NotFound();

            var maxPlayers = session.Booking?.OpenPlayMaxPlayers ?? 8;
            var participants = session.Participants
                .OrderBy(p => p.JoinedAt)
                .Select(p => new
                {
                    p.Id,
                    p.UserId,
                    playerName = p.User == null ? "Player" : $"{p.User.FirstName} {p.User.LastName}".Trim(),
                    playerEmail = p.User?.Email,
                    playerPhone = p.User?.Phone,
                    playerProfileImageUrl = p.User?.ProfileImageUrl,
                    p.PaymentStatus,
                    p.CheckInStatus,
                    p.JoinedAt,
                    isHost = session.HostUserId.HasValue && p.UserId == session.HostUserId
                })
                .ToList();

            return Ok(new
            {
                session.Id,
                session.RoomCode,
                session.Status,
                isHost = session.HostUserId == userId,
                joined = session.Participants.Any(p => p.UserId == userId),
                isFull = participants.Count >= maxPlayers,
                maxPlayers,
                joinedCount = participants.Count,
                booking = session.Booking == null ? null : new
                {
                    session.Booking.Id,
                    session.Booking.BookingReference,
                    session.Booking.Date,
                    session.Booking.StartTime,
                    session.Booking.EndTime,
                    session.Booking.OpenPlaySkillLevel,
                    session.Booking.OpenPlayPricePerPlayer,
                    session.Booking.OpenPlayNote,
                    session.Booking.OpenPlayReclubLink,
                    court = session.Booking.Court == null ? null : new
                    {
                        session.Booking.Court.Id,
                        session.Booking.Court.Name,
                        session.Booking.Court.Address
                    }
                },
                participants = session.HostUserId == userId
                    ? participants
                    : participants.Select(p => new
                    {
                        p.Id,
                        p.UserId,
                        p.playerName,
                        playerEmail = (string?)null,
                        playerPhone = (string?)null,
                        playerProfileImageUrl = p.playerProfileImageUrl,
                        p.PaymentStatus,
                        p.CheckInStatus,
                        p.JoinedAt,
                        p.isHost
                    })
            });
        }

        [Authorize(Roles = "Player")]
        [HttpPost("sessions/{roomCode}/join")]
        public async Task<ActionResult> JoinSession(string roomCode)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var session = await LoadSession(roomCode);
            if (session == null || session.Status != "Active") return NotFound();

            if (session.Participants.Any(p => p.UserId == userId))
            {
                return Ok(new { message = "Already joined." });
            }

            var maxPlayers = session.Booking?.OpenPlayMaxPlayers ?? 8;
            if (session.Participants.Count >= maxPlayers)
            {
                return BadRequest("This open play is already full.");
            }

            _context.OpenPlayParticipants.Add(new OpenPlayParticipant
            {
                OpenPlaySessionId = session.Id,
                UserId = userId,
                PaymentStatus = "Unpaid",
                CheckInStatus = "Joined",
                JoinedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            var player = await _context.Users.FindAsync(userId);
            var playerName = player == null ? "A player" : $"{player.FirstName} {player.LastName}".Trim();
            var courtName = session.Booking?.Court?.Name ?? "Open Play";
            var openPlayUrl = $"/open-play/{session.RoomCode}";

            if (session.HostUserId != null && session.HostUserId != userId)
            {
                await _push.SendToPlayerAsync(session.HostUserId.Value, new PushMessage(
                    "Player joined Open Play",
                    $"{playerName} joined {courtName}.",
                    openPlayUrl,
                    "openplay-join"));
            }

            if (session.HostOwnerId != null)
            {
                await _push.SendToOwnerAsync(session.HostOwnerId.Value, new PushMessage(
                    "Player joined Open Play",
                    $"{playerName} joined {courtName}.",
                    "/owner/open-play",
                    "openplay-join"));
            }

            return Ok(new { message = "Joined open play." });
        }

        [Authorize(Roles = "Player,CourtOwner")]
        [HttpPatch("participants/{participantId}")]
        public async Task<ActionResult> UpdateParticipant(int participantId, UpdateOpenPlayParticipantDto dto)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role);
            var participant = await _context.OpenPlayParticipants
                .Include(p => p.OpenPlaySession)
                .FirstOrDefaultAsync(p => p.Id == participantId);

            if (participant?.OpenPlaySession == null) return NotFound();
            var isPlayerHost = role == "Player" && participant.OpenPlaySession.HostUserId == userId;
            var isOwnerHost = role == "CourtOwner" && participant.OpenPlaySession.HostOwnerId == userId;
            if (!isPlayerHost && !isOwnerHost) return Forbid();

            var validPayment = new[] { "Unpaid", "PaidCash", "PaidReclub", "Waived" };
            var validCheckIn = new[] { "Joined", "CheckedIn", "NoShow" };

            if (!string.IsNullOrEmpty(dto.PaymentStatus))
            {
                if (!validPayment.Contains(dto.PaymentStatus)) return BadRequest("Invalid payment status.");
                participant.PaymentStatus = dto.PaymentStatus;
            }

            if (!string.IsNullOrEmpty(dto.CheckInStatus))
            {
                if (!validCheckIn.Contains(dto.CheckInStatus)) return BadRequest("Invalid check-in status.");
                participant.CheckInStatus = dto.CheckInStatus;
            }

            await _context.SaveChangesAsync();

            if (participant.UserId != userId)
            {
                await _push.SendToPlayerAsync(participant.UserId, new PushMessage(
                    "Open Play updated",
                    "Your Open Play payment or check-in status was updated.",
                    $"/open-play/{participant.OpenPlaySession.RoomCode}",
                    "openplay-update"));
            }

            return Ok(new { participant.Id, participant.PaymentStatus, participant.CheckInStatus });
        }

        [Authorize(Roles = "CourtOwner")]
        [HttpPatch("owner/participants/{participantId}")]
        public Task<ActionResult> UpdateOwnerParticipant(int participantId, UpdateOpenPlayParticipantDto dto)
        {
            return UpdateParticipant(participantId, dto);
        }

        private Task<OpenPlaySession?> LoadSession(string roomCode)
        {
            return _context.OpenPlaySessions
                .Include(s => s.Booking)
                .ThenInclude(b => b!.Court)
                .Include(s => s.Participants)
                .ThenInclude(p => p.User)
                .FirstOrDefaultAsync(s => s.RoomCode == roomCode.ToUpper());
        }

        private static object ToOwnerSessionDto(OpenPlaySession session)
        {
            var booking = session.Booking;
            var participants = session.Participants
                .OrderBy(p => p.JoinedAt)
                .Select(p => new
                {
                    p.Id,
                    p.UserId,
                    playerName = p.User == null ? "Player" : $"{p.User.FirstName} {p.User.LastName}".Trim(),
                    playerEmail = p.User?.Email,
                    playerPhone = p.User?.Phone,
                    p.PaymentStatus,
                    p.CheckInStatus,
                    p.JoinedAt
                })
                .ToList();

            return new
            {
                session.Id,
                session.RoomCode,
                session.Status,
                joinedCount = participants.Count,
                maxPlayers = booking?.OpenPlayMaxPlayers ?? 8,
                booking = booking == null ? null : new
                {
                    booking.Id,
                    booking.BookingReference,
                    booking.Date,
                    booking.StartTime,
                    booking.EndTime,
                    booking.OpenPlayMaxPlayers,
                    booking.OpenPlayPricePerPlayer,
                    booking.OpenPlaySkillLevel,
                    booking.OpenPlayNote,
                    booking.OpenPlayReclubLink,
                    court = booking.Court == null ? null : new
                    {
                        booking.Court.Id,
                        booking.Court.Name,
                        booking.Court.Address
                    }
                },
                participants
            };
        }

        private async Task<string> GenerateUniqueRoomCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            string code;
            do
            {
                code = new string(Enumerable.Range(0, 6).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
            } while (await _context.OpenPlaySessions.AnyAsync(s => s.RoomCode == code));

            return code;
        }

        private static bool IsHttpUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }

    public class CreateOwnerOpenPlayDto
    {
        public int CourtId { get; set; }
        public DateTime Date { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int MaxPlayers { get; set; } = 8;
        public decimal PricePerPlayer { get; set; }
        public string? SkillLevel { get; set; }
        public string? Note { get; set; }
        public string? ReclubLink { get; set; }
    }

    public class UpdateOpenPlayParticipantDto
    {
        public string? PaymentStatus { get; set; }
        public string? CheckInStatus { get; set; }
    }
}
