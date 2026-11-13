using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.CredentialService.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueSessionTokenIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KsefCredentials_TenantId_UserId_CredentialType",
                table: "KsefCredentials");

            migrationBuilder.CreateIndex(
                name: "IX_KsefCredentials_TenantId_UserId_CredentialType",
                table: "KsefCredentials",
                columns: new[] { "TenantId", "UserId", "CredentialType" },
                unique: true,
                filter: "[CredentialType] IN ('AccessToken', 'RefreshToken')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KsefCredentials_TenantId_UserId_CredentialType",
                table: "KsefCredentials");

            migrationBuilder.CreateIndex(
                name: "IX_KsefCredentials_TenantId_UserId_CredentialType",
                table: "KsefCredentials",
                columns: new[] { "TenantId", "UserId", "CredentialType" });
        }
    }
}
