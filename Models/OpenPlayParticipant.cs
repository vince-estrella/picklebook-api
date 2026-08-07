namespace PickleballApi.Models
{
    public class OpenPlayParticipant
    {
        public int Id { get; set; }
        public int OpenPlaySessionId { get; set; }
        public OpenPlaySession? OpenPlaySession { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public string PaymentStatus { get; set; } = "Unpaid"; // Unpaid, PaidCash, PaidReclub, Waived
        public string CheckInStatus { get; set; } = "Joined"; // Joined, CheckedIn, NoShow
    }
}
