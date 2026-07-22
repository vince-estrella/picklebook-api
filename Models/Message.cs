using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickleballApi.Models
{
    public class Message
    {
        public int Id { get; set; }

        [Required]
        public int ConversationId { get; set; }
        [ForeignKey(nameof(ConversationId))]
        public Conversation? Conversation { get; set; }

        // "Player" or "Owner" — who sent this message.
        [Required]
        public string SenderRole { get; set; } = "Player";

        [Required]
        public string Text { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Read state tracked per-side independently — owner and player each
        // need their own "unread" badge for the same thread.
        public bool ReadByOwner { get; set; } = false;
        public bool ReadByPlayer { get; set; } = false;
    }
}