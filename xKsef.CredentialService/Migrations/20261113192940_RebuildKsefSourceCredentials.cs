using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.CredentialService.Migrations
{
    /// <inheritdoc />
    public partial class RebuildKsefSourceCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KsefCredentials_TenantId_UserId_CredentialType_SourceCredentialId_ContextNip",
                table: "KsefCredentials");

            migrationBuilder.DropColumn(
                name: "SourceCredentialId",
                table: "KsefCredentials");

            migrationBuilder.DropColumn(
                name: "ValidUntil",
                table: "KsefCredentials");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "KsefCredentials",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<byte[]>(
                name: "Nonce",
                table: "KsefCredentials",
                type: "varbinary(12)",
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "varbinary(max)");

            migrationBuilder.AlterColumn<byte[]>(
                name: "AuthTag",
                table: "KsefCredentials",
                type: "varbinary(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "varbinary(max)");

            migrationBuilder.CreateIndex(
                name: "IX_KsefCredentials_UserId_CredentialType_IsActive_CreatedAt",
                table: "KsefCredentials",
                columns: new[] { "UserId", "CredentialType", "IsActive", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KsefCredentials_UserId_TenantId_ContextNip_CredentialType_IsActive_CreatedAt",
                table: "KsefCredentials",
                columns: new[] { "UserId", "TenantId", "ContextNip", "CredentialType", "IsActive", "CreatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_KsefCredentials_AuthTag",
                table: "KsefCredentials",
                sql: "DATALENGTH([AuthTag]) = 16");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KsefCredentials_EncryptedPayload",
                table: "KsefCredentials",
                sql: "DATALENGTH([EncryptedData]) > 0 AND DATALENGTH([EncryptedDek]) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KsefCredentials_Id",
                table: "KsefCredentials",
                sql: "[Id] <> '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KsefCredentials_Nonce",
                table: "KsefCredentials",
                sql: "DATALENGTH([Nonce]) = 12");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KsefCredentials_ProtectionType",
                table: "KsefCredentials",
                sql: "[ProtectionType] COLLATE Latin1_General_100_BIN2 = N'DPAPI-CurrentUser'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KsefCredentials_SourceContext",
                table: "KsefCredentials",
                sql: "([CredentialType] COLLATE Latin1_General_100_BIN2 = N'Certificate' AND [TenantId] IS NULL AND [ContextNip] IS NULL) OR ([CredentialType] COLLATE Latin1_General_100_BIN2 = N'Token' AND [TenantId] IS NOT NULL AND [TenantId] <> '00000000-0000-0000-0000-000000000000' AND [ContextNip] IS NOT NULL AND DATALENGTH([ContextNip]) = 20 AND [ContextNip] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KsefCredentials_UserId",
                table: "KsefCredentials",
                sql: "LEN(LTRIM(RTRIM([UserId]))) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Schema reversal only: certificates with a NULL tenant must not be assigned a synthetic tenant.
            migrationBuilder.DropIndex(
                name: "IX_KsefCredentials_UserId_CredentialType_IsActive_CreatedAt",
                table: "KsefCredentials");

            migrationBuilder.DropIndex(
                name: "IX_KsefCredentials_UserId_TenantId_ContextNip_CredentialType_IsActive_CreatedAt",
                table: "KsefCredentials");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KsefCredentials_AuthTag",
                table: "KsefCredentials");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KsefCredentials_EncryptedPayload",
                table: "KsefCredentials");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KsefCredentials_Id",
                table: "KsefCredentials");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KsefCredentials_Nonce",
                table: "KsefCredentials");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KsefCredentials_ProtectionType",
                table: "KsefCredentials");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KsefCredentials_SourceContext",
                table: "KsefCredentials");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KsefCredentials_UserId",
                table: "KsefCredentials");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "KsefCredentials",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "Nonce",
                table: "KsefCredentials",
                type: "varbinary(max)",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "varbinary(12)",
                oldMaxLength: 12);

            migrationBuilder.AlterColumn<byte[]>(
                name: "AuthTag",
                table: "KsefCredentials",
                type: "varbinary(max)",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "varbinary(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceCredentialId",
                table: "KsefCredentials",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidUntil",
                table: "KsefCredentials",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_KsefCredentials_TenantId_UserId_CredentialType_SourceCredentialId_ContextNip",
                table: "KsefCredentials",
                columns: new[] { "TenantId", "UserId", "CredentialType", "SourceCredentialId", "ContextNip" },
                unique: true,
                filter: "[CredentialType] IN ('AccessToken', 'RefreshToken')");
        }
    }
}
