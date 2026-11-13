using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.CredentialService.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionCredentialContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContextNip",
                table: "KsefCredentials",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceCredentialId",
                table: "KsefCredentials",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContextNip",
                table: "KsefCredentials");

            migrationBuilder.DropColumn(
                name: "SourceCredentialId",
                table: "KsefCredentials");
        }
    }
}
