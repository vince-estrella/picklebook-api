using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;

namespace PickleballApi
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Court> Courts { get; set; }
        public DbSet<CourtImage> CourtImages { get; set; }
        public DbSet<CourtOwner> CourtOwners { get; set; }
        public DbSet<Booking> Bookings { get; set; }
    }
}