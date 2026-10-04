using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireCalc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Onboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ChecklistHiddenAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                table: "Scenarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                table: "Homes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                table: "Goals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // People who already have accounts or a home are past getting started; the guide only opens for new ones.
            migrationBuilder.Sql("""
                UPDATE "Users" u SET "OnboardedAt" = now()
                WHERE EXISTS (SELECT 1 FROM "Accounts" a WHERE a."UserId" = u."Id")
                   OR EXISTS (SELECT 1 FROM "Homes" h WHERE h."UserId" = u."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChecklistHiddenAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OnboardedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                table: "Scenarios");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                table: "Homes");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                table: "Accounts");
        }
    }
}
