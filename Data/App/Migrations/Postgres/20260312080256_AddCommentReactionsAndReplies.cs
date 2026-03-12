using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ping.Data.App.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddCommentReactionsAndReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DislikeCount",
                table: "EventComments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LikeCount",
                table: "EventComments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ParentCommentId",
                table: "EventComments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReplyCount",
                table: "EventComments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "EventCommentReactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommentId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventCommentReactions", x => x.Id);
                    table.CheckConstraint("CK_EventCommentReaction_Value", "\"Value\" IN (-1, 1)");
                    table.ForeignKey(
                        name: "FK_EventCommentReactions_EventComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "EventComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventComments_ParentCommentId",
                table: "EventComments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_EventCommentReactions_CommentId_UserId",
                table: "EventCommentReactions",
                columns: new[] { "CommentId", "UserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EventComments_EventComments_ParentCommentId",
                table: "EventComments",
                column: "ParentCommentId",
                principalTable: "EventComments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EventComments_EventComments_ParentCommentId",
                table: "EventComments");

            migrationBuilder.DropTable(
                name: "EventCommentReactions");

            migrationBuilder.DropIndex(
                name: "IX_EventComments_ParentCommentId",
                table: "EventComments");

            migrationBuilder.DropColumn(
                name: "DislikeCount",
                table: "EventComments");

            migrationBuilder.DropColumn(
                name: "LikeCount",
                table: "EventComments");

            migrationBuilder.DropColumn(
                name: "ParentCommentId",
                table: "EventComments");

            migrationBuilder.DropColumn(
                name: "ReplyCount",
                table: "EventComments");
        }
    }
}
