using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProConnect.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorSkillsAndJobAiFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReputationSummaryReviewCount",
                table: "VendorProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Skills",
                table: "VendorProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionImageUrl",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionVerdict",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Embedding",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalDescription",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalLanguage",
                table: "Jobs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReputationSummaryReviewCount",
                table: "VendorProfiles");

            migrationBuilder.DropColumn(
                name: "Skills",
                table: "VendorProfiles");

            migrationBuilder.DropColumn(
                name: "CompletionImageUrl",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CompletionVerdict",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "OriginalDescription",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "OriginalLanguage",
                table: "Jobs");
        }
    }
}
