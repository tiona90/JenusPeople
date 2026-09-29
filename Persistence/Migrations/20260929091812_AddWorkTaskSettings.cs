using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkTaskSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkTaskSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DescriptionRequirement = table.Column<int>(type: "int", nullable: false),
                    DueDateRequirement = table.Column<int>(type: "int", nullable: false),
                    TargetHoursRequirement = table.Column<int>(type: "int", nullable: false),
                    AttachmentsRequirement = table.Column<int>(type: "int", nullable: false),
                    ProjectRequirement = table.Column<int>(type: "int", nullable: false),
                    BillableRequirement = table.Column<int>(type: "int", nullable: false),
                    ShowPriority = table.Column<bool>(type: "bit", nullable: false),
                    RequireCompletionConfirmation = table.Column<bool>(type: "bit", nullable: false),
                    MaxAttachmentsPerTask = table.Column<int>(type: "int", nullable: false),
                    MaxAttachmentSizeMb = table.Column<int>(type: "int", nullable: false),
                    AllowImages = table.Column<bool>(type: "bit", nullable: false),
                    AllowPdf = table.Column<bool>(type: "bit", nullable: false),
                    AllowWord = table.Column<bool>(type: "bit", nullable: false),
                    AllowExcel = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "WorkTaskSettings",
                columns: new[] { "Id", "AllowExcel", "AllowImages", "AllowPdf", "AllowWord", "AttachmentsRequirement", "BillableRequirement", "DescriptionRequirement", "DueDateRequirement", "MaxAttachmentSizeMb", "MaxAttachmentsPerTask", "ProjectRequirement", "RequireCompletionConfirmation", "ShowPriority", "TargetHoursRequirement" },
                values: new object[] { 1, true, true, true, true, 0, 1, 0, 0, 10, 10, 1, true, true, 0 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkTaskSettings");
        }
    }
}
