using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ping.Data.App.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddOtherPingGenre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "PingGenres",
                columns: new[] { "Id", "Name" },
                values: new object[] { 23, "Other" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PingGenres",
                keyColumn: "Id",
                keyValue: 23);
        }
    }
}
