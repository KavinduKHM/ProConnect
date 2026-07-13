using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProConnect.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorReputationSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReputationSummary",
                table: "VendorProfiles",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReputationSummary",
                table: "VendorProfiles");
        }
    }
}
