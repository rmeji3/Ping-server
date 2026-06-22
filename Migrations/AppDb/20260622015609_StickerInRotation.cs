using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ping.Migrations.AppDb
{
    /// <inheritdoc />
    public partial class StickerInRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InRotation",
                table: "Stickers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InRotation",
                table: "Stickers");
        }
    }
}
