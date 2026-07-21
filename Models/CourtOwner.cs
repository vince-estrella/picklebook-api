namespace PickleballApi.Models
{
    public class CourtOwner
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string? ProfilePhotoUrl { get; set; }
        public List<Court> Courts { get; set; } = new();
        public string? ProfileImageUrl { get; set; }
    }
}