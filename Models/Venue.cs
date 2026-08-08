namespace PickleballApi.Models
{
    public class Venue
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Amenities { get; set; } = string.Empty;
        public string? ExternalBookingUrl { get; set; }
        public int CourtOwnerId { get; set; }
        public CourtOwner? CourtOwner { get; set; }
        public List<Court> Courts { get; set; } = new();
    }
}
