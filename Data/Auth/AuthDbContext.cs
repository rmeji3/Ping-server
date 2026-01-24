using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Ping.Models.AppUsers;
using Ping.Models.Follows;
using Ping.Models.Users; // Added for UserBlock

namespace Ping.Data.Auth
{
    public class AuthDbContext(DbContextOptions<AuthDbContext> options) : IdentityDbContext<AppUser>(options)
    {
        public DbSet<Follow> Follows => Set<Follow>();
        public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
        public DbSet<IpBan> IpBans => Set<IpBan>();
        public DbSet<Ping.Models.Analytics.UserActivityLog> UserActivityLogs => Set<Ping.Models.Analytics.UserActivityLog>();
        public DbSet<Ping.Models.Analytics.DailySystemMetric> DailySystemMetrics => Set<Ping.Models.Analytics.DailySystemMetric>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Follows
            builder.Entity<Follow>()
                .HasKey(f => new { f.FollowerId, f.FolloweeId });

            builder.Entity<Follow>()
                .HasOne(f => f.Follower)
                .WithMany()
                .HasForeignKey(f => f.FollowerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Follow>()
                .HasOne(f => f.Followee)
                .WithMany()
                .HasForeignKey(f => f.FolloweeId)
                .OnDelete(DeleteBehavior.Restrict);

            // AppUser - usually Identity handles this but maintaining existing index
            builder.Entity<AppUser>()
                .HasIndex(u => u.UserName)
                .IsUnique();

            builder.Entity<AppUser>(entity =>
            {
                entity.Property(e => e.FirstName).HasMaxLength(24);
                entity.Property(e => e.LastName).HasMaxLength(24);
                entity.Property(e => e.ProfileImageUrl).HasMaxLength(2048);
                entity.Property(e => e.ProfileThumbnailUrl).HasMaxLength(2048);
                entity.Property(e => e.Bio).HasMaxLength(256);
            });

            // UserBlock
            builder.Entity<UserBlock>()
                .HasKey(ub => new { ub.BlockerId, ub.BlockedId });

            builder.Entity<UserBlock>()
                .HasOne(ub => ub.Blocker)
                .WithMany()
                .HasForeignKey(ub => ub.BlockerId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserBlock>()
                .HasOne(ub => ub.Blocked)
                .WithMany()
                .HasForeignKey(ub => ub.BlockedId)
                .OnDelete(DeleteBehavior.Cascade);

            // Analytics
            builder.Entity<Ping.Models.Analytics.UserActivityLog>()
                .HasIndex(l => new { l.UserId, l.Date })
                .IsUnique(); // One log per user per day

             builder.Entity<Ping.Models.Analytics.UserActivityLog>()
                .HasIndex(l => l.Date); // Optimize aggregation by date

            builder.Entity<Ping.Models.Analytics.DailySystemMetric>()
                .HasIndex(m => m.Date);
        }
    }
}

