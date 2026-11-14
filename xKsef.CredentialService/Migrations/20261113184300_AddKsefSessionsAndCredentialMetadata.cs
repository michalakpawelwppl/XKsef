using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.CredentialService.Migrations
{
    /// <inheritdoc />
    public partial class AddKsefSessionsAndCredentialMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "KsefCredentials",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "KsefCredentials",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_KsefCredentials_Id_UserId",
                table: "KsefCredentials",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateTable(
                name: "KsefSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CredentialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContextNip = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    EncryptedData = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    EncryptedDek = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Nonce = table.Column<byte[]>(type: "varbinary(12)", maxLength: 12, nullable: false),
                    AuthTag = table.Column<byte[]>(type: "varbinary(16)", maxLength: 16, nullable: false),
                    ProtectionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AccessTokenValidUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RefreshTokenValidUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KsefSessions", x => x.Id);
                    table.CheckConstraint("CK_KsefSessions_AuthTag", "DATALENGTH([AuthTag]) = 16");
                    table.CheckConstraint("CK_KsefSessions_ContextNip", "DATALENGTH([ContextNip]) = 20 AND [ContextNip] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%'");
                    table.CheckConstraint("CK_KsefSessions_EncryptedPayload", "DATALENGTH([EncryptedData]) > 0 AND DATALENGTH([EncryptedDek]) > 0");
                    table.CheckConstraint("CK_KsefSessions_Identifiers", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [TenantId] <> '00000000-0000-0000-0000-000000000000' AND [CredentialId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_KsefSessions_Nonce", "DATALENGTH([Nonce]) = 12");
                    table.CheckConstraint("CK_KsefSessions_ProtectionType", "LEN(LTRIM(RTRIM([ProtectionType]))) > 0");
                    table.CheckConstraint("CK_KsefSessions_UserId", "LEN(LTRIM(RTRIM([UserId]))) > 0");
                    table.ForeignKey(
                        name: "FK_KsefSessions_KsefCredentials_CredentialId_UserId",
                        columns: x => new { x.CredentialId, x.UserId },
                        principalTable: "KsefCredentials",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KsefSessions_CredentialId_UserId",
                table: "KsefSessions",
                columns: new[] { "CredentialId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_KsefSessions_UserId_TenantId_CredentialId_ContextNip",
                table: "KsefSessions",
                columns: new[] { "UserId", "TenantId", "CredentialId", "ContextNip" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KsefSessions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_KsefCredentials_Id_UserId",
                table: "KsefCredentials");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "KsefCredentials");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "KsefCredentials");
        }
    }
}
