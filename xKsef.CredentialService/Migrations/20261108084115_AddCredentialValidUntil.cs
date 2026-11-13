using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.CredentialService.Migrations
{
    /// <inheritdoc />
    public partial class AddCredentialValidUntil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidUntil",
                table: "KsefCredentials",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValidUntil",
                table: "KsefCredentials");
        }
    }
}
