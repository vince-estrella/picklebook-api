using System.Text.Json.Serialization;

namespace PickleballApi.Models
{
    public class CourtImage
    {
        public int Id { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public int CourtId { get; set; }

        [JsonIgnore]
        public Court? Court { get; set; }
    }
}