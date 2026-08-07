namespace PickleballApi.Models
{
    public class Court
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = "Outdoor"; // Indoor or Outdoor
        public string SurfaceType { get; set; } = string.Empty; // Cemented, Acrylic, Clay
        public int MaxPlayers { get; set; }
        public string Amenities { get; set; } = string.Empty; // comma-separated: "WiFi,Parking,Lighting"
        public string Rules { get; set; } = string.Empty;

        // Hours per day type
        public TimeSpan MonFriOpen { get; set; }
        public TimeSpan MonFriClose { get; set; }
        public TimeSpan SatOpen { get; set; }
        public TimeSpan SatClose { get; set; }
        public TimeSpan SunOpen { get; set; }
        public TimeSpan SunClose { get; set; }

        public decimal PricePerHour { get; set; }
        public string? ExternalBookingUrl { get; set; }

        // "PayAtVenue" or "Online" — determines whether bookers pay online at checkout
        // or pay in person when they arrive. Defaults to PayAtVenue so existing courts
        // keep working exactly as before.
        public string PaymentMethod { get; set; } = "PayAtVenue";
        public bool AllowOpenPlay { get; set; } = true;

        public int CourtOwnerId { get; set; }
        public CourtOwner? CourtOwner { get; set; }
        public List<CourtImage> Images { get; set; } = new();
    }
}
