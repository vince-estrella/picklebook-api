using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickleballApi.Models
{
    // A single inquiry thread between one Player and one CourtOwner about one
    // Court. If the same player messages the same owner about a different
    // court, that's a separate Conversation — keeps context unambiguous in
    // the owner's inbox (they see which court each thread is about).
    public class Conversation
    {
        public int Id { get; set; }

        [Required]
        public int CourtId { get; set; }
        [ForeignKey(nameof(CourtId))]
        public Court? Court { get; set; }

        [Required]
        public int PlayerId { get; set; }
        [ForeignKey(nameof(PlayerId))]
        public User? Player { get; set; }

        [Required]
        public int CourtOwnerId { get; set; }
        [ForeignKey(nameof(CourtOwnerId))]
        public CourtOwner? CourtOwner { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<Message> Messages { get; set; } = new();
    }
}