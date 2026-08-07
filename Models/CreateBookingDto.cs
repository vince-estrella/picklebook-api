namespace PickleballApi.Models
{
    public class CreateBookingDto
    {
        public int CourtId { get; set; }
        public DateTime Date { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string BookerName { get; set; } = string.Empty;
        public string BookerPhone { get; set; } = string.Empty;
        public string BookerEmail { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = "PayAtVenue";
        public string BookingType { get; set; } = "Standard";
        public int? OpenPlayMaxPlayers { get; set; }
        public decimal? OpenPlayPricePerPlayer { get; set; }
        public string? OpenPlaySkillLevel { get; set; }
        public string? OpenPlayNote { get; set; }
        public string? OpenPlayReclubLink { get; set; }
    }
}
