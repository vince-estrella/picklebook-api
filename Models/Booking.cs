namespace PickleballApi.Models
{
    public class Booking
    {
        
        public string BookingReference { get; set; } = string.Empty;
        public int Id { get; set; }
        public int CourtId { get; set; }
        public Court? Court { get; set; }
        public int? UserId { get; set; }
        public User? User { get; set; }
        public DateTime Date { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }

        public string BookerName { get; set; } = string.Empty;
        public string BookerPhone { get; set; } = string.Empty;
        public string BookerEmail { get; set; } = string.Empty;

        public string PaymentMethod { get; set; } = "PayAtVenue"; // PayAtVenue, Online
        public string PaymentStatus { get; set; } = "Unpaid"; // Unpaid, Pending, Paid, Refunded, Failed
        public string Status { get; set; } = "Pending"; // Pending, Confirmed, Cancelled, Completed, NoShow
        public DateTime? PaidAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? XenditInvoiceId { get; set; }
    }
}
