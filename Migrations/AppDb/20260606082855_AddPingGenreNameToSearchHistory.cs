using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ping.Migrations.AppDb
{
    /// <inheritdoc />
    public partial class AddPingGenreNameToSearchHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PingGenreName",
                table: "SearchHistory",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PingGenreName",
                table: "SearchHistory");
        }
    }
}
