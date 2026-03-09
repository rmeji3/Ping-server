using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ping.Data.App.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class BackfillAllCollectionIsSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data migration: mark all existing "All" collections as system collections.
            // These were created before the IsSystem column was introduced (default was false).
            migrationBuilder.Sql("""
                UPDATE "Collections"
                SET "IsSystem" = true
                WHERE "Name" = 'All';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert: clear IsSystem on all "All" collections (back to column default of false)
            migrationBuilder.Sql("""
                UPDATE "Collections"
                SET "IsSystem" = false
                WHERE "Name" = 'All';
                """);
        }
    }
}
