using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.CredentialService.Migrations
{
    /// <inheritdoc />
    public partial class InitialCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KsefCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CredentialType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EncryptedData = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    EncryptedDek = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Nonce = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    AuthTag = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ProtectionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KsefCredentials", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KsefCredentials_TenantId_UserId_CredentialType",
                table: "KsefCredentials",
                columns: new[] { "TenantId", "UserId", "CredentialType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KsefCredentials");
        }
    }
}
