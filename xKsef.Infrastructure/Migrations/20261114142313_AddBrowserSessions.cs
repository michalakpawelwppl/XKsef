using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBrowserSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrowserSessions",
                columns: table => new
                {
                    IdHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProtectedTicket = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RenewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IdleExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AbsoluteExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrowserSessions", x => x.IdHash);
                    table.CheckConstraint("CK_BrowserSessions_Lifetime", "[CreatedAt] <= [RenewedAt] AND [RenewedAt] < [IdleExpiresAt] AND [IdleExpiresAt] <= [AbsoluteExpiresAt]");
                    table.CheckConstraint("CK_BrowserSessions_Payload", "DATALENGTH([ProtectedTicket]) > 0");
                    table.CheckConstraint("CK_BrowserSessions_Version", "[Version] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_BrowserSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrowserSessions_AbsoluteExpiresAt",
                table: "BrowserSessions",
                column: "AbsoluteExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_BrowserSessions_IdleExpiresAt",
                table: "BrowserSessions",
                column: "IdleExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_BrowserSessions_UserId_RevokedAt",
                table: "BrowserSessions",
                columns: new[] { "UserId", "RevokedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrowserSessions");
        }
    }
}
