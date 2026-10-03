using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireCalc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CostCorrectionAsChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CostBasis",
                table: "PortfolioTransactions",
                newName: "CostChange");

            // Corrections so far held a total, which now reads as a change. They only exist on the preview
            // database, so they are dropped to be entered again rather than misread.
            migrationBuilder.Sql("""DELETE FROM "PortfolioTransactions" WHERE "Type" = 'CostCorrection';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CostChange",
                table: "PortfolioTransactions",
                newName: "CostBasis");
        }
    }
}
