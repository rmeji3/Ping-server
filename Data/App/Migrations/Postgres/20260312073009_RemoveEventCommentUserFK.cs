using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ping.Data.App.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class RemoveEventCommentUserFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EventComments_AppUser_UserId",
                table: "EventComments");

            migrationBuilder.DropIndex(
                name: "IX_EventComments_UserId",
                table: "EventComments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_EventComments_UserId",
                table: "EventComments",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_EventComments_AppUser_UserId",
                table: "EventComments",
                column: "UserId",
                principalTable: "AppUser",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
