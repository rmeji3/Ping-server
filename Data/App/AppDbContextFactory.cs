using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ping.Data.App
{
    /// <summary>
    /// Design-time factory used by the EF Core CLI (e.g. `dotnet ef migrations add`).
    /// The app's runtime host wires connection strings from the environment and needs
    /// AI/other services that aren't available at design time, so the tooling can't boot
    /// it. This builds a standalone AppDbContext with a placeholder connection — enough to
    /// read the model and scaffold migrations (no database connection is made for that).
    /// </summary>
    public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var connection =
                Environment.GetEnvironmentVariable("AppConnection")
                ?? "Host=localhost;Port=5432;Database=ping_design_time;Username=postgres;Password=postgres";

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connection, x => x.UseNetTopologySuite())
                .Options;

            return new AppDbContext(options);
        }
    }
}
