using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireCalc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FirebaseAuthSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GoogleSubject",
                table: "Users",
                newName: "AuthSubject");

            migrationBuilder.RenameIndex(
                name: "IX_Users_GoogleSubject",
                table: "Users",
                newName: "IX_Users_AuthSubject");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AuthSubject",
                table: "Users",
                newName: "GoogleSubject");

            migrationBuilder.RenameIndex(
                name: "IX_Users_AuthSubject",
                table: "Users",
                newName: "IX_Users_GoogleSubject");
        }
    }
}
