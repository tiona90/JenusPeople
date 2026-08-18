using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveDelegate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DelegateId",
                table: "AnnualLeaves",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnnualLeaves_DelegateId",
                table: "AnnualLeaves",
                column: "DelegateId");

            migrationBuilder.AddForeignKey(
                name: "FK_AnnualLeaves_AspNetUsers_DelegateId",
                table: "AnnualLeaves",
                column: "DelegateId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AnnualLeaves_AspNetUsers_DelegateId",
                table: "AnnualLeaves");

            migrationBuilder.DropIndex(
                name: "IX_AnnualLeaves_DelegateId",
                table: "AnnualLeaves");

            migrationBuilder.DropColumn(
                name: "DelegateId",
                table: "AnnualLeaves");
        }
    }
}
