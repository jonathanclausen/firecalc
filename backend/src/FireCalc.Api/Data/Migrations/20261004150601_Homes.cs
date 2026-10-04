using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireCalc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Homes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Homes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Homes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Homes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HomeValuations",
                columns: table => new
                {
                    HomeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeValuations", x => new { x.HomeId, x.Date });
                    table.ForeignKey(
                        name: "FK_HomeValuations_Homes_HomeId",
                        column: x => x.HomeId,
                        principalTable: "Homes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Mortgages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HomeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InterestPct = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: true),
                    ContributionPct = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InterestOnlyUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mortgages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Mortgages_Homes_HomeId",
                        column: x => x.HomeId,
                        principalTable: "Homes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MortgageBalances",
                columns: table => new
                {
                    MortgageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MortgageBalances", x => new { x.MortgageId, x.Date });
                    table.ForeignKey(
                        name: "FK_MortgageBalances_Mortgages_MortgageId",
                        column: x => x.MortgageId,
                        principalTable: "Mortgages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Homes_UserId",
                table: "Homes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Mortgages_HomeId",
                table: "Mortgages",
                column: "HomeId");

            // Homes were accounts of type Property, with the restgæld on each balance, and loans taken for the
            // home were Loan accounts marked PartOfHome. Move them into Bolig, keeping their ids, then remove
            // the old accounts. A PartOfHome loan for a user without a home stays a plain loan.
            migrationBuilder.Sql("""
                INSERT INTO "Homes" ("Id", "UserId", "Name", "Archived", "CreatedAt")
                SELECT "Id", "UserId", "Name", "Archived", "CreatedAt" FROM "Accounts" WHERE "Type" = 'Property';

                INSERT INTO "HomeValuations" ("HomeId", "Date", "Value")
                SELECT b."AccountId", b."Date", b."Balance"
                FROM "AccountBalances" b JOIN "Accounts" a ON a."Id" = b."AccountId"
                WHERE a."Type" = 'Property';

                INSERT INTO "Mortgages" ("Id", "HomeId", "Name", "Archived", "CreatedAt")
                SELECT a."Id", a."Id", 'Realkreditlån', false, a."CreatedAt"
                FROM "Accounts" a
                WHERE a."Type" = 'Property'
                  AND EXISTS (SELECT 1 FROM "AccountBalances" b WHERE b."AccountId" = a."Id" AND b."Loan" > 0);

                INSERT INTO "MortgageBalances" ("MortgageId", "Date", "Balance")
                SELECT b."AccountId", b."Date", COALESCE(b."Loan", 0)
                FROM "AccountBalances" b
                WHERE b."AccountId" IN (SELECT "Id" FROM "Mortgages");

                INSERT INTO "Mortgages" ("Id", "HomeId", "Name", "Archived", "CreatedAt")
                SELECT l."Id",
                       (SELECT h."Id" FROM "Accounts" h
                        WHERE h."UserId" = l."UserId" AND h."Type" = 'Property'
                        ORDER BY h."Archived", h."CreatedAt" LIMIT 1),
                       l."Name", l."Archived", l."CreatedAt"
                FROM "Accounts" l
                WHERE l."Type" = 'Loan' AND l."PartOfHome"
                  AND EXISTS (SELECT 1 FROM "Accounts" h WHERE h."UserId" = l."UserId" AND h."Type" = 'Property');

                INSERT INTO "MortgageBalances" ("MortgageId", "Date", "Balance")
                SELECT b."AccountId", b."Date", b."Balance"
                FROM "AccountBalances" b JOIN "Accounts" l ON l."Id" = b."AccountId"
                WHERE l."Type" = 'Loan' AND b."AccountId" IN (SELECT "Id" FROM "Mortgages");

                DELETE FROM "SnapshotEntries"
                WHERE "AccountId" IN (SELECT "Id" FROM "Homes") OR "AccountId" IN (SELECT "Id" FROM "Mortgages");
                DELETE FROM "Accounts"
                WHERE "Id" IN (SELECT "Id" FROM "Homes") OR "Id" IN (SELECT "Id" FROM "Mortgages");
                """);

            migrationBuilder.DropColumn(
                name: "PartOfHome",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Loan",
                table: "AccountBalances");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HomeValuations");

            migrationBuilder.DropTable(
                name: "MortgageBalances");

            migrationBuilder.DropTable(
                name: "Mortgages");

            migrationBuilder.DropTable(
                name: "Homes");

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
    }
}
