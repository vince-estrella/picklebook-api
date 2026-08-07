namespace PickleballApi.Models
{
    public class OpenPlaySession
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public Booking? Booking { get; set; }
        public int? HostUserId { get; set; }
        public User? HostUser { get; set; }
        public int? HostOwnerId { get; set; }
        public CourtOwner? HostOwner { get; set; }
        public string RoomCode { get; set; } = string.Empty;
        public string Status { get; set; } = "Active"; // Active, Cancelled, Closed
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ActivatedAt { get; set; }
        public List<OpenPlayParticipant> Participants { get; set; } = new();
    }
}
