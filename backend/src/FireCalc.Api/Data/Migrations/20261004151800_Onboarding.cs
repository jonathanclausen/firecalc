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
            // IF NOT EXISTS: an earlier version of this migration, under another id, already ran on the
            // preview database.
            migrationBuilder.Sql("""
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "ChecklistHiddenAt" timestamp with time zone NULL;
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "OnboardedAt" timestamp with time zone NULL;
                ALTER TABLE "Scenarios" ADD COLUMN IF NOT EXISTS "IsDemo" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE "Homes" ADD COLUMN IF NOT EXISTS "IsDemo" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE "Goals" ADD COLUMN IF NOT EXISTS "IsDemo" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE "Accounts" ADD COLUMN IF NOT EXISTS "IsDemo" boolean NOT NULL DEFAULT FALSE;
                """);

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
