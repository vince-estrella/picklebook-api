namespace PickleballApi.Models
{
    public class Report
    {
        public int Id { get; set; }
        public int CourtId { get; set; }
        public Court? Court { get; set; }

        // Set when a logged-in player submits the report; null for guests,
        // since ReportListingPage.jsx doesn't currently gate this behind login.
        public int? UserId { get; set; }
        public User? User { get; set; }

        public string Reason { get; set; } = string.Empty; // INACCURATE, UNSAFE, SCAM, INAPPROPRIATE, DUPLICATE, OTHER
        public string? Details { get; set; }

        public string Status { get; set; } = "Open"; // Open, Reviewed, Dismissed — for the owner-dashboard triage piece later
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}