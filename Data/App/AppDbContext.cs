using Conquest.Models.Activities;
using Conquest.Models.Events;
using Conquest.Models.Places;
using Microsoft.EntityFrameworkCore;
using Conquest.Models.Reviews;

namespace Conquest.Data.App
{
    public class AppDbContext(DbContextOptions<AppDbContext> opt) : DbContext(opt)
    {
        public DbSet<Place> Places => Set<Place>();
        public DbSet<ActivityKind> ActivityKinds => Set<ActivityKind>();
        public DbSet<PlaceActivity> PlaceActivities => Set<PlaceActivity>();
        public DbSet<Review> Reviews => Set<Review>();
        public DbSet<CheckIn> CheckIns => Set<CheckIn>();
        public DbSet<Tag> Tags => Set<Tag>();
        public DbSet<ReviewTag> ReviewTags => Set<ReviewTag>();
        public DbSet<Event> Events => Set<Event>();
        public DbSet<EventAttendee> EventAttendees => Set<EventAttendee>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            
            // ---------- Seed data ----------
            builder.Entity<ActivityKind>().HasData(
                new ActivityKind { Id = 1, Name = "Soccer" },
                new ActivityKind { Id = 2, Name = "Climbing" },
                new ActivityKind { Id = 3, Name = "Tennis" },
                new ActivityKind { Id = 4, Name = "Hiking" },
                new ActivityKind { Id = 5, Name = "Running" },
                new ActivityKind { Id = 6, Name = "Photography" },
                new ActivityKind { Id = 7, Name = "Coffee" },
                new ActivityKind { Id = 8, Name = "Gym" }
            );

            // ---------- Indexes ----------

            // Place: coords index for nearby searches
            builder.Entity<Place>()
                .HasIndex(p => new { p.Latitude, p.Longitude });

            // ActivityKind: unique name (e.g. "Soccer", "Rock climbing")
            builder.Entity<ActivityKind>()
                .HasIndex(ak => ak.Name)
                .IsUnique();

            // PlaceActivity: unique per place by name
            // (e.g. only one "Pickup soccer" at a given place)
            builder.Entity<PlaceActivity>()
                .HasIndex(pa => new { pa.PlaceId, pa.Name })
                .IsUnique();

            // Review: one review per user per activity
            builder.Entity<Review>()
                .HasIndex(r => new { r.PlaceActivityId, r.UserId });

            // Tag: unique normalized name
            builder.Entity<Tag>()
                .HasIndex(t => t.Name)
                .IsUnique();

            // ReviewTag: composite key
            builder.Entity<ReviewTag>()
                .HasKey(rt => new { rt.ReviewId, rt.TagId });

            // Optional: index for check-ins by activity and time
            builder.Entity<CheckIn>()
                .HasIndex(ci => new { ci.PlaceActivityId, ci.CreatedAt });


            // ---------- Relationships ----------

            // Place 1 - * PlaceActivities
            builder.Entity<PlaceActivity>()
                .HasOne(pa => pa.Place)
                .WithMany(p => p.PlaceActivities)
                .HasForeignKey(pa => pa.PlaceId)
                .OnDelete(DeleteBehavior.Cascade);

            // ActivityKind 1 - * PlaceActivities
            builder.Entity<PlaceActivity>()
                .HasOne(pa => pa.ActivityKind)
                .WithMany(ak => ak.PlaceActivities)
                .HasForeignKey(pa => pa.ActivityKindId)
                .OnDelete(DeleteBehavior.Restrict);

            // PlaceActivity 1 - * Reviews
            builder.Entity<Review>()
                .HasOne(r => r.PlaceActivity)
                .WithMany(pa => pa.Reviews)
                .HasForeignKey(r => r.PlaceActivityId)
                .OnDelete(DeleteBehavior.Cascade);

            // PlaceActivity 1 - * CheckIns
            builder.Entity<CheckIn>()
                .HasOne(ci => ci.PlaceActivity)
                .WithMany(pa => pa.CheckIns)
                .HasForeignKey(ci => ci.PlaceActivityId)
                .OnDelete(DeleteBehavior.Cascade);

            // Review 1 - * ReviewTags
            builder.Entity<ReviewTag>()
                .HasOne(rt => rt.Review)
                .WithMany(r => r.ReviewTags)
                .HasForeignKey(rt => rt.ReviewId)
                .OnDelete(DeleteBehavior.Cascade);

            // Tag 1 - * ReviewTags
            builder.Entity<ReviewTag>()
                .HasOne(rt => rt.Tag)
                .WithMany(t => t.ReviewTags)
                .HasForeignKey(rt => rt.TagId)
                .OnDelete(DeleteBehavior.Cascade);


            // ---------- Property config ----------

            // Review content + timestamps
            builder.Entity<Review>()
                .Property(r => r.Content)
                .HasMaxLength(2000);

            builder.Entity<Review>()
                .Property(r => r.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // CheckIn timestamps
            builder.Entity<CheckIn>()
                .Property(ci => ci.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Tag name length
            builder.Entity<Tag>()
                .Property(t => t.Name)
                .HasMaxLength(30);


            // ---------- Events / Attendees ----------

            builder.Entity<EventAttendee>()
                .HasKey(ea => new { ea.EventId, ea.UserId });

            builder.Entity<EventAttendee>()
                .HasOne(ea => ea.Event)
                .WithMany(e => e.Attendees)
                .HasForeignKey(ea => ea.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
