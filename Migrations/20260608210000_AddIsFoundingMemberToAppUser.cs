using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ping.Migrations
{
    /// <inheritdoc />
    public partial class AddIsFoundingMemberToAppUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFoundingMember",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE "AspNetUsers"
                SET "IsVerified" = TRUE;

                UPDATE "AspNetUsers"
                SET "IsFoundingMember" = TRUE
                WHERE "Id" IN (
                    SELECT "Id"
                    FROM "AspNetUsers"
                    WHERE "EmailConfirmed" = TRUE
                    ORDER BY "CreatedUtc" ASC
                    LIMIT 20
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFoundingMember",
                table: "AspNetUsers");
        }
    }
}
