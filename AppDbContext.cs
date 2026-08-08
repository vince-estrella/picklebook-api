using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;

namespace PickleballApi
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        public DbSet<Conversation> Conversations { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<Court> Courts { get; set; }
        public DbSet<Venue> Venues { get; set; }
        public DbSet<CourtImage> CourtImages { get; set; }
        public DbSet<CourtOwner> CourtOwners { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<OpenPlaySession> OpenPlaySessions { get; set; }
        public DbSet<OpenPlayParticipant> OpenPlayParticipants { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Report> Reports { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Register's "does this email already exist?" check is a
            // check-then-insert, same shape as the booking-overlap race we
            // fixed earlier — two near-simultaneous registrations with the
            // same email could both pass that check before either saves.
            // These indexes are the DB-level backstop: even if the
            // application check gets raced, the second insert is rejected
            // outright instead of creating a duplicate account.
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<CourtOwner>()
                .HasIndex(o => o.Email)
                .IsUnique();

            modelBuilder.Entity<Court>()
                .Property(c => c.AllowOpenPlay)
                .HasDefaultValue(true);

            modelBuilder.Entity<Court>()
                .Property(c => c.BookingMode)
                .HasDefaultValue("PickleBook");

            modelBuilder.Entity<Venue>()
                .HasIndex(v => new { v.CourtOwnerId, v.Name });

            modelBuilder.Entity<Court>()
                .HasIndex(c => c.VenueId);

            modelBuilder.Entity<Booking>()
                .Property(b => b.BookingType)
                .HasDefaultValue("Standard");

            modelBuilder.Entity<OpenPlaySession>()
                .HasIndex(s => s.BookingId)
                .IsUnique();

            modelBuilder.Entity<OpenPlaySession>()
                .HasIndex(s => s.RoomCode)
                .IsUnique();

            modelBuilder.Entity<OpenPlayParticipant>()
                .HasIndex(p => new { p.OpenPlaySessionId, p.UserId })
                .IsUnique();
        }
    }
}
