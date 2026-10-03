using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireCalc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LoanAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PartOfHome",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PartOfHome",
                table: "Accounts");
        }
    }
}
