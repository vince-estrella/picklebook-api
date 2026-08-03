namespace PickleballApi.Models
{
    public class Booking
    {
        
        public string BookingReference { get; set; } = string.Empty;

        // Proof-of-ownership token for the public GET /bookings/{id} lookup used
        // by the post-Xendit-redirect confirmation page. The numeric Id alone is
        // enumerable and isn't a security boundary — this is. Nullable because
        // bookings created before this field existed won't have one; GetBooking
        // treats a missing token (on either side) as "can't verify, deny."
        public string? PublicToken { get; set; }

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