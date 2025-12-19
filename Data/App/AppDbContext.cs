using Ping.Models.Events;
using Ping.Models.Pings;
using Microsoft.EntityFrameworkCore;
using Ping.Models.Reviews;
using Ping.Models.Reports;
using Ping.Models.Users;
using Ping.Models.Business;
using Ping.Models;
using NpgsqlTypes; // For NpgsqlTsVector
using Npgsql.EntityFrameworkCore.PostgreSQL; // For HasGeneratedTsVectorColumn extensions

namespace Ping.Data.App
{
    public class AppDbContext(DbContextOptions<AppDbContext> opt) : DbContext(opt)
    {
        public DbSet<Models.Pings.Ping> Pings => Set<Models.Pings.Ping>();
        public DbSet<PingGenre> PingGenres => Set<PingGenre>();
        public DbSet<EventGenre> EventGenres => Set<EventGenre>();
        public DbSet<PingActivity> PingActivities => Set<PingActivity>();
        public DbSet<Review> Reviews => Set<Review>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<Tag> Tags => Set<Tag>();
        public DbSet<ReviewTag> ReviewTags => Set<ReviewTag>();
        public DbSet<Event> Events => Set<Event>();
        public DbSet<EventAttendee> EventAttendees => Set<EventAttendee>();
        public DbSet<Favorited> Favorited => Set<Favorited>();
        public DbSet<ReviewLike> ReviewLikes => Set<ReviewLike>();
        public DbSet<Report> Reports => Set<Report>();
        public DbSet<PingClaim> PingClaims => Set<PingClaim>();
        public DbSet<PingDailyMetric> PingDailyMetrics => Set<PingDailyMetric>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            
            // ---------- Seed data ----------
            // ---------- Seed data ----------
            builder.Entity<PingGenre>().HasData(
                new PingGenre { Id = 1, Name = "Sports" },
                new PingGenre { Id = 2, Name = "Food" },
                new PingGenre { Id = 3, Name = "Outdoors" },
                new PingGenre { Id = 4, Name = "Art" },
                new PingGenre { Id = 5, Name = "Entertainment" },
                new PingGenre { Id = 6, Name = "Shopping" },
                new PingGenre { Id = 7, Name = "Wellness" },
                new PingGenre { Id = 8, Name = "Nightlife" },
                new PingGenre { Id = 9, Name = "Education" },
                new PingGenre { Id = 10, Name = "Work" },
                new PingGenre { Id = 11, Name = "Travel" },
                new PingGenre { Id = 12, Name = "Music" },
                new PingGenre { Id = 13, Name = "Tech" },
                new PingGenre { Id = 14, Name = "Gaming" },
                new PingGenre { Id = 15, Name = "Pets" },
                new PingGenre { Id = 16, Name = "Family" },
                new PingGenre { Id = 17, Name = "Dating" },
                new PingGenre { Id = 18, Name = "Fashion" },
                new PingGenre { Id = 19, Name = "Automotive" },
                new PingGenre { Id = 20, Name = "Home" }
            );

            // Seed EventGenres
            builder.Entity<EventGenre>().HasData(
                new EventGenre { Id = 1, Name = "Music" },
                new EventGenre { Id = 2, Name = "Sports" },
                new EventGenre { Id = 3, Name = "Arts" },
                new EventGenre { Id = 4, Name = "Nightlife" },
                new EventGenre { Id = 5, Name = "Networking" },
                new EventGenre { Id = 6, Name = "Education" },
                new EventGenre { Id = 7, Name = "Family" },
                new EventGenre { Id = 8, Name = "Comedy" },
                new EventGenre { Id = 9, Name = "Technology" },
                new EventGenre { Id = 10, Name = "Wellness" },
                new EventGenre { Id = 11, Name = "Food" },
                new EventGenre { Id = 12, Name = "Other" },
                new EventGenre { Id = 14, Name = "Cars"}
            );

            // ---------- Indexes ----------

            // Ping: Spatial index for nearby searches
             builder.Entity<Models.Pings.Ping>()
                 .HasIndex(p => p.Location);

            // Favorited: unique per user per ping
            builder.Entity<Favorited>()
                .HasIndex(f => new { f.UserId, f.PingId })
                .IsUnique();

            // PingGenre: unique name
            builder.Entity<PingGenre>()
                .HasIndex(pg => pg.Name)
                .IsUnique();

            // EventGenre: unique name
            builder.Entity<EventGenre>()
                .HasIndex(eg => eg.Name)
                .IsUnique();

            // PingDailyMetric: unique per ping per day
            builder.Entity<PingDailyMetric>()
                .HasIndex(m => new { m.PingId, m.Date })
                .IsUnique();

            // PingActivity: unique per ping by name
            // (e.g. only one "Pickup soccer" at a given ping)
            builder.Entity<PingActivity>()
                .HasIndex(pa => new { pa.PingId, pa.Name })
                .IsUnique();

            // Review: 
            // - one review per user per activity
            // - 1-5 rating only
            // - 1000 character limit
            builder.Entity<Review>()
                .HasIndex(r => new { r.PingActivityId, r.UserId });

            builder.Entity<Review>()
                .ToTable(t => t.HasCheckConstraint("CK_Review_Rating", "\"Rating\" >= 1 AND \"Rating\" <= 5"));

            builder.Entity<Review>()
                .ToTable(t => t.HasCheckConstraint("CK_Review_Content", "length(\"Content\") <= 1000"));

            // Tag: unique normalized name
            builder.Entity<Tag>()
                .HasIndex(t => t.Name)
                .IsUnique();

            // ReviewTag: composite key
            builder.Entity<ReviewTag>()
                .HasKey(rt => new { rt.ReviewId, rt.TagId });


            // ---------- Relationships ----------

            // Ping 1 - * PingActivities
            builder.Entity<PingActivity>()
                .HasOne(pa => pa.Ping)
                .WithMany(p => p.PingActivities)
                .HasForeignKey(pa => pa.PingId)
                .OnDelete(DeleteBehavior.Cascade);

            // PingGenre 1 - * Pings
            builder.Entity<Models.Pings.Ping>()
                .HasOne(p => p.PingGenre)
                .WithMany(pg => pg.Pings)
                .HasForeignKey(p => p.PingGenreId)
                .OnDelete(DeleteBehavior.Restrict);

            // PingActivity 1 - * Reviews
            builder.Entity<Review>()
                .HasOne(r => r.PingActivity)
                .WithMany(pa => pa.Reviews)
                .HasForeignKey(r => r.PingActivityId)
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

            // Favorited * - 1 Ping
            builder.Entity<Favorited>()
                .HasOne(f => f.Ping)
                .WithMany(p => p.FavoritedList)
                .HasForeignKey(f => f.PingId)
                .OnDelete(DeleteBehavior.Cascade);

            // ReviewLike * - 1 Review
            builder.Entity<ReviewLike>()
                .HasOne(rl => rl.Review)
                .WithMany(r => r.LikesList)
                .HasForeignKey(rl => rl.ReviewId)
                .OnDelete(DeleteBehavior.Cascade);

            // ReviewLike: unique per user per review
            builder.Entity<ReviewLike>()
                .HasIndex(rl => new { rl.ReviewId, rl.UserId })
                .IsUnique();


            // ---------- Property config ----------

            // Review content + timestamps
            builder.Entity<Review>()
                .Property(r => r.Content)
                .HasMaxLength(1000);

            builder.Entity<Review>()
                .Property(r => r.CreatedAt)
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

            // ---------- PostgreSQL Specific (Full Text Search) ----------
            // Only apply when running against Postgres
            if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                 // Full-Text Search Vector for Pings (Name + Address)
                 // We use a shadow property so we don't pollute the domain model
                 builder.Entity<Models.Pings.Ping>()
                     .Property<NpgsqlTsVector>("SearchVector")
                     .HasComputedColumnSql("to_tsvector('english', coalesce(\"Name\", '') || ' ' || coalesce(\"Address\", ''))", stored: true);

                 builder.Entity<Models.Pings.Ping>()
                     .HasIndex("SearchVector")
                     .HasMethod("GIN");
            }
        }
    }
}

