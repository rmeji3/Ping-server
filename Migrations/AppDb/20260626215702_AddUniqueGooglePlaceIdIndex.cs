using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ping.Migrations.AppDb
{
    /// <inheritdoc />
    public partial class AddUniqueGooglePlaceIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Collapse any pre-existing live duplicates so the unique index can be
            // created. For each Google place keep the oldest live ping and soft-delete
            // the rest (non-destructive — their reviews remain, the place shows Closed).
            migrationBuilder.Sql(@"
                UPDATE ""Pings""
                SET ""IsDeleted"" = true
                WHERE ""GooglePlaceId"" IS NOT NULL
                  AND ""IsDeleted"" = false
                  AND ""Id"" NOT IN (
                      SELECT MIN(""Id"")
                      FROM ""Pings""
                      WHERE ""GooglePlaceId"" IS NOT NULL AND ""IsDeleted"" = false
                      GROUP BY ""GooglePlaceId""
                  );
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Pings_GooglePlaceId",
                table: "Pings",
                column: "GooglePlaceId",
                unique: true,
                filter: "\"GooglePlaceId\" IS NOT NULL AND \"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pings_GooglePlaceId",
                table: "Pings");
        }
    }
}
