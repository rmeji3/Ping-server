using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ping.Migrations.AppDb
{
    /// <inheritdoc />
    public partial class AddProfileStickers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Stickers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stickers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProfileStickerPlacements",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    StickerId = table.Column<string>(type: "text", nullable: false),
                    X = table.Column<double>(type: "double precision", nullable: false),
                    Y = table.Column<double>(type: "double precision", nullable: false),
                    Scale = table.Column<double>(type: "double precision", nullable: false),
                    Rotation = table.Column<double>(type: "double precision", nullable: false),
                    ZIndex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileStickerPlacements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileStickerPlacements_Stickers_StickerId",
                        column: x => x.StickerId,
                        principalTable: "Stickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserStickers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    StickerId = table.Column<string>(type: "text", nullable: false),
                    AcquiredUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStickers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserStickers_Stickers_StickerId",
                        column: x => x.StickerId,
                        principalTable: "Stickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileStickerPlacements_StickerId",
                table: "ProfileStickerPlacements",
                column: "StickerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileStickerPlacements_UserId",
                table: "ProfileStickerPlacements",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Stickers_Key",
                table: "Stickers",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserStickers_StickerId",
                table: "UserStickers",
                column: "StickerId");

            migrationBuilder.CreateIndex(
                name: "IX_UserStickers_UserId_StickerId",
                table: "UserStickers",
                columns: new[] { "UserId", "StickerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfileStickerPlacements");

            migrationBuilder.DropTable(
                name: "UserStickers");

            migrationBuilder.DropTable(
                name: "Stickers");
        }
    }
}
