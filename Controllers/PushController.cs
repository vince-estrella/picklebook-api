using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;
using PickleballApi.Services;
using System.Security.Claims;

namespace PickleballApi.Controllers
{
    public class PushSubscriptionDto
    {
        public string Endpoint { get; set; } = string.Empty;
        public PushKeysDto Keys { get; set; } = new();
    }

    public class PushPreferencesDto
    {
        public bool BookingNotifications { get; set; } = true;
        public bool MessageNotifications { get; set; } = true;
        public bool OpenPlayNotifications { get; set; } = true;
        public bool ReminderNotifications { get; set; } = true;
    }

    public class PushKeysDto
    {
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("api/push")]
    public class PushController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IPushNotificationService _push;

        public PushController(AppDbContext context, IPushNotificationService push)
        {
            _context = context;
            _push = push;
        }

        [HttpGet("public-key")]
        public ActionResult GetPublicKey()
        {
            return Ok(new
            {
                publicKey = _push.PublicKey,
                configured = !string.IsNullOrWhiteSpace(_push.PublicKey)
            });
        }

        [Authorize(Roles = "Player,CourtOwner")]
        [HttpPost("subscriptions")]
        public async Task<ActionResult> SaveSubscription(PushSubscriptionDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Endpoint) ||
                string.IsNullOrWhiteSpace(dto.Keys.P256dh) ||
                string.IsNullOrWhiteSpace(dto.Keys.Auth))
            {
                return BadRequest("Subscription endpoint and keys are required.");
            }

            var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var now = DateTime.UtcNow;

            var subscription = await _context.PushSubscriptions
                .FirstOrDefaultAsync(s => s.Endpoint == dto.Endpoint);

            if (subscription == null)
            {
                subscription = new PushSubscription
                {
                    Endpoint = dto.Endpoint,
                    CreatedAt = now
                };
                _context.PushSubscriptions.Add(subscription);
            }

            subscription.UserRole = role;
            subscription.UserId = role == "Player" ? userId : null;
            subscription.CourtOwnerId = role == "CourtOwner" ? userId : null;
            subscription.P256dh = dto.Keys.P256dh;
            subscription.Auth = dto.Keys.Auth;
            subscription.UserAgent = Request.Headers.UserAgent.ToString();
            subscription.UpdatedAt = now;

            await _context.SaveChangesAsync();

            return Ok(new { subscribed = true });
        }

        [Authorize(Roles = "Player,CourtOwner")]
        [HttpGet("preferences")]
        public async Task<ActionResult> GetPreferences()
        {
            var subscriptions = await GetCurrentAccountSubscriptions().ToListAsync();
            var latest = subscriptions
                .OrderByDescending(s => s.UpdatedAt)
                .FirstOrDefault();

            return Ok(new PushPreferencesDto
            {
                BookingNotifications = latest?.BookingNotifications ?? true,
                MessageNotifications = latest?.MessageNotifications ?? true,
                OpenPlayNotifications = latest?.OpenPlayNotifications ?? true,
                ReminderNotifications = latest?.ReminderNotifications ?? true
            });
        }

        [Authorize(Roles = "Player,CourtOwner")]
        [HttpPut("preferences")]
        public async Task<ActionResult> UpdatePreferences(PushPreferencesDto dto)
        {
            var subscriptions = await GetCurrentAccountSubscriptions().ToListAsync();
            foreach (var subscription in subscriptions)
            {
                subscription.BookingNotifications = dto.BookingNotifications;
                subscription.MessageNotifications = dto.MessageNotifications;
                subscription.OpenPlayNotifications = dto.OpenPlayNotifications;
                subscription.ReminderNotifications = dto.ReminderNotifications;
                subscription.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return Ok(dto);
        }

        [Authorize(Roles = "Player,CourtOwner")]
        [HttpDelete("subscriptions")]
        public async Task<ActionResult> DeleteSubscription(PushSubscriptionDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Endpoint)) return NoContent();

            var subscription = await _context.PushSubscriptions
                .FirstOrDefaultAsync(s => s.Endpoint == dto.Endpoint);
            if (subscription != null)
            {
                _context.PushSubscriptions.Remove(subscription);
                await _context.SaveChangesAsync();
            }

            return NoContent();
        }

        [Authorize(Roles = "Player,CourtOwner")]
        [HttpPost("test")]
        public async Task<ActionResult> SendTest()
        {
            var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var message = new PushMessage(
                "PickleBook notifications are on",
                "You will be ready for booking, message, and open-play updates.",
                "/my-bookings",
                "picklebook-test");

            if (role == "CourtOwner")
            {
                await _push.SendToOwnerAsync(userId, message with { Url = "/owner/dashboard" });
            }
            else
            {
                await _push.SendToPlayerAsync(userId, message);
            }

            return Ok(new { sent = true });
        }

        private IQueryable<PushSubscription> GetCurrentAccountSubscriptions()
        {
            var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            return role == "CourtOwner"
                ? _context.PushSubscriptions.Where(s => s.UserRole == "CourtOwner" && s.CourtOwnerId == userId)
                : _context.PushSubscriptions.Where(s => s.UserRole == "Player" && s.UserId == userId);
        }
    }
}
