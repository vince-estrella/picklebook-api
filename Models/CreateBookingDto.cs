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
    }
}