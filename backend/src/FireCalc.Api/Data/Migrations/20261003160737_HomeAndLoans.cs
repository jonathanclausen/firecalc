using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireCalc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class HomeAndLoans : Migration
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

            migrationBuilder.AddColumn<decimal>(
                name: "Loan",
                table: "AccountBalances",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PartOfHome",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Loan",
                table: "AccountBalances");
        }
    }
}
