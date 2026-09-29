using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkTaskConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfirmedById",
                table: "WorkTasks",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentBackAtUtc",
                table: "WorkTasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SentBackReason",
                table: "WorkTasks",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_ConfirmedById",
                table: "WorkTasks",
                column: "ConfirmedById");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkTasks_AspNetUsers_ConfirmedById",
                table: "WorkTasks",
                column: "ConfirmedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkTasks_AspNetUsers_ConfirmedById",
                table: "WorkTasks");

            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_ConfirmedById",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "ConfirmedById",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "SentBackAtUtc",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "SentBackReason",
                table: "WorkTasks");
        }
    }
}
