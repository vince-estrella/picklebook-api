using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;
using PickleballApi.Services;
using System.Security.Claims;

namespace PickleballApi.Controllers
{
    public class StartConversationDto
    {
        public int CourtId { get; set; }
    }

    public class SendMessageDto
    {
        public string Text { get; set; } = "";
    }

    [ApiController]
    [Route("api/messages")]
    public class MessagesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IPushNotificationService _push;

        public MessagesController(AppDbContext context, IPushNotificationService push)
        {
            _context = context;
            _push = push;
        }

        // POST: api/messages/start
        // Called from CourtDetailPage's "Message Owner" button. Finds an
        // existing thread for this player+court if one exists (so re-clicking
        // "Message Owner" resumes the conversation instead of duplicating it),
        // otherwise creates a new one.
        [Authorize(Roles = "Player")]
        [HttpPost("start")]
        public async Task<ActionResult> StartConversation(StartConversationDto dto)
        {
            var playerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var court = await _context.Courts.FindAsync(dto.CourtId);
            if (court == null) return NotFound("Court not found.");

            var existing = await _context.Conversations.FirstOrDefaultAsync(c =>
                c.CourtId == dto.CourtId && c.PlayerId == playerId && c.CourtOwnerId == court.CourtOwnerId);

            if (existing != null)
            {
                return Ok(new { conversationId = existing.Id });
            }

            var conversation = new Conversation
            {
                CourtId = dto.CourtId,
                PlayerId = playerId,
                CourtOwnerId = court.CourtOwnerId,
            };
            _context.Conversations.Add(conversation);
            await _context.SaveChangesAsync();

            return Ok(new { conversationId = conversation.Id });
        }

        // GET: api/messages/conversations — the logged-in PLAYER's own threads
        // across every court they've messaged. Not wired to a page yet, but
        // here so a "My Messages" player page can use it later without
        // needing backend changes.
        [Authorize(Roles = "Player")]
        [HttpGet("conversations")]
        public async Task<ActionResult> GetPlayerConversations()
        {
            var playerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var conversations = await _context.Conversations
                .Include(c => c.Court)
                .Include(c => c.CourtOwner)
                .Include(c => c.Messages)
                .Where(c => c.PlayerId == playerId)
                .ToListAsync();

            var result = conversations
                .Select(c =>
                {
                    var last = c.Messages.OrderByDescending(m => m.CreatedAt).FirstOrDefault();
                    return new
                    {
                        id = c.Id,
                        courtId = c.CourtId,
                        courtName = c.Court?.Name,
                        ownerName = $"{c.CourtOwner?.FirstName} {c.CourtOwner?.LastName}".Trim(),
                        lastMessage = last?.Text,
                        lastMessageAt = last?.CreatedAt,
                        unreadCount = c.Messages.Count(m => m.SenderRole == "Owner" && !m.ReadByPlayer),
                    };
                })
                .OrderByDescending(c => c.lastMessageAt)
                .ToList();

            return Ok(result);
        }

        // GET: api/messages/owner/conversations — the logged-in OWNER's inbox,
        // across every court they own. This is what OwnerMessagesPage lists.
        [Authorize(Roles = "CourtOwner")]
        [HttpGet("owner/conversations")]
        public async Task<ActionResult> GetOwnerConversations()
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var conversations = await _context.Conversations
                .Include(c => c.Court)
                .Include(c => c.Player)
                .Include(c => c.Messages)
                .Where(c => c.CourtOwnerId == ownerId)
                .ToListAsync();

            var result = conversations
                .Select(c =>
                {
                    var last = c.Messages.OrderByDescending(m => m.CreatedAt).FirstOrDefault();
                    return new
                    {
                        id = c.Id,
                        courtId = c.CourtId,
                        courtName = c.Court?.Name,
                        customerName = $"{c.Player?.FirstName} {c.Player?.LastName}".Trim(),
                        customerAvatarUrl = c.Player?.ProfileImageUrl,
                        lastMessage = last?.Text,
                        lastMessageAt = last?.CreatedAt,
                        unreadCount = c.Messages.Count(m => m.SenderRole == "Player" && !m.ReadByOwner),
                    };
                })
                .OrderByDescending(c => c.lastMessageAt)
                .ToList();

            return Ok(result);
        }

        // Loads a conversation and checks that the calling player/owner is
        // actually a participant in it — prevents one player from reading
        // another player's thread, or an unrelated owner peeking in.
        private async Task<Conversation?> GetAuthorizedConversation(int conversationId, string requiredRole)
        {
            var conversation = await _context.Conversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == conversationId);
            if (conversation == null) return null;

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (requiredRole == "Player" && conversation.PlayerId != userId) return null;
            if (requiredRole == "Owner" && conversation.CourtOwnerId != userId) return null;

            return conversation;
        }

        private static object ProjectMessage(Message m) => new
        {
            id = m.Id,
            // Lowercased so it matches OwnerMessagesPage's existing
            // `m.sender === 'owner'` check without needing that page rewritten.
            sender = m.SenderRole.ToLower(),
            text = m.Text,
            createdAt = m.CreatedAt,
        };

        private static List<object> ProjectMessages(Conversation c) =>
            c.Messages.OrderBy(m => m.CreatedAt).Select(ProjectMessage).ToList();

        // ── Player-side thread endpoints ──────────────────────────────────

        [Authorize(Roles = "Player")]
        [HttpGet("conversations/{id}/messages")]
        public async Task<ActionResult> GetPlayerMessages(int id)
        {
            var conversation = await GetAuthorizedConversation(id, "Player");
            if (conversation == null) return NotFound();
            return Ok(ProjectMessages(conversation));
        }

        [Authorize(Roles = "Player")]
        [HttpPost("conversations/{id}/messages")]
        public async Task<ActionResult> SendPlayerMessage(int id, SendMessageDto dto)
        {
            var conversation = await GetAuthorizedConversation(id, "Player");
            if (conversation == null) return NotFound();
            if (string.IsNullOrWhiteSpace(dto.Text)) return BadRequest("Message can't be empty.");

            var message = new Message
            {
                ConversationId = id,
                SenderRole = "Player",
                Text = dto.Text.Trim(),
                ReadByPlayer = true, // the sender has obviously "read" their own message
            };
            _context.Messages.Add(message);
            await _context.SaveChangesAsync();
            await _push.SendToOwnerAsync(conversation.CourtOwnerId, new PushMessage(
                "New message",
                "A player sent you a message on PickleBook.",
                "/owner/messages",
                "message-owner"));

            return Ok(ProjectMessage(message));
        }

        [Authorize(Roles = "Player")]
        [HttpPatch("conversations/{id}/read")]
        public async Task<ActionResult> MarkReadByPlayer(int id)
        {
            var conversation = await GetAuthorizedConversation(id, "Player");
            if (conversation == null) return NotFound();

            foreach (var m in conversation.Messages.Where(m => m.SenderRole == "Owner" && !m.ReadByPlayer))
                m.ReadByPlayer = true;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // ── Owner-side thread endpoints (same shape, role flipped) ────────

        [Authorize(Roles = "CourtOwner")]
        [HttpGet("owner/conversations/{id}/messages")]
        public async Task<ActionResult> GetOwnerMessages(int id)
        {
            var conversation = await GetAuthorizedConversation(id, "Owner");
            if (conversation == null) return NotFound();
            return Ok(ProjectMessages(conversation));
        }

        [Authorize(Roles = "CourtOwner")]
        [HttpPost("owner/conversations/{id}/messages")]
        public async Task<ActionResult> SendOwnerMessage(int id, SendMessageDto dto)
        {
            var conversation = await GetAuthorizedConversation(id, "Owner");
            if (conversation == null) return NotFound();
            if (string.IsNullOrWhiteSpace(dto.Text)) return BadRequest("Message can't be empty.");

            var message = new Message
            {
                ConversationId = id,
                SenderRole = "Owner",
                Text = dto.Text.Trim(),
                ReadByOwner = true,
            };
            _context.Messages.Add(message);
            await _context.SaveChangesAsync();
            await _push.SendToPlayerAsync(conversation.PlayerId, new PushMessage(
                "New message",
                "A court owner replied to your message.",
                "/messages",
                "message-player"));

            return Ok(ProjectMessage(message));
        }

        [Authorize(Roles = "CourtOwner")]
        [HttpPatch("owner/conversations/{id}/read")]
        public async Task<ActionResult> MarkReadByOwner(int id)
        {
            var conversation = await GetAuthorizedConversation(id, "Owner");
            if (conversation == null) return NotFound();

            foreach (var m in conversation.Messages.Where(m => m.SenderRole == "Player" && !m.ReadByOwner))
                m.ReadByOwner = true;
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
