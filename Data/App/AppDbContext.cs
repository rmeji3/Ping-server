using Conquest.Models.Activities;
using Conquest.Models.Events;
using Conquest.Models.Places;
using Microsoft.EntityFrameworkCore;
using Conquest.Models.Reviews;

namespace Conquest.Data.App
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> opt) : base(opt) { }
        public DbSet<Place> Places => Set<Place>();
        public DbSet<Activity> Activities => Set<Activity>();
        public DbSet<Review> Reviews => Set<Review>();
        public DbSet<Event> Events { get; set; } = null!;
        public DbSet<EventAttendee> EventAttendees { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ---- Indexes ----
            builder.Entity<Place>()
                .HasIndex(p => new { p.Latitude, p.Longitude });

            builder.Entity<Activity>()
                .HasIndex(a => new { a.PlaceId, a.Type });

            builder.Entity<Review>()
                .HasIndex(r => new { r.PlaceId, r.UserId })
                .IsUnique(); // one review per user per place

            // ---- Relationships ----

            // Place 1 - * Reviews
            builder.Entity<Review>()
                .HasOne(r => r.Place)
                .WithMany(p => p.Reviews)
                .HasForeignKey(r => r.PlaceId)
                .OnDelete(DeleteBehavior.Cascade);
            
            // Optional extra config
            builder.Entity<Review>()
                .Property(r => r.Content)
                .HasMaxLength(1000);

            builder.Entity<Review>()
                .Property(r => r.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            
            // EventAttendee
            builder.Entity<EventAttendee>()
                .HasKey(ea => new { ea.EventId, ea.UserId });

            builder.Entity<EventAttendee>()
                .HasOne(ea => ea.Event)
                .WithMany(e => e.Attendees)
                .HasForeignKey(ea => ea.EventId);
            
        }

    }
}
