using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireCalc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScenarioWithdrawalRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "WithdrawalPct",
                table: "Scenarios",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 4m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WithdrawalPct",
                table: "Scenarios");
        }
    }
}
