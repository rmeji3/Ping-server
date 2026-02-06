using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ping.Data.App.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddPingGenres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "PingGenres",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 21, "Cafe" },
                    { 22, "Parking" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PingGenres",
                keyColumn: "Id",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "PingGenres",
                keyColumn: "Id",
                keyValue: 22);
        }
    }
}
