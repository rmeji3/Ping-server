using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ping.Data.App.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddCollectionImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "Pings");

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Collections",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "Collections",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "Collections");

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "Pings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
