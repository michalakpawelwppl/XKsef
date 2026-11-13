using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKsefAuthenticationContexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KsefCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KsefCredentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KsefAuthenticationContexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CredentialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KsefAuthenticationContexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KsefAuthenticationContexts_KsefCredentials_CredentialId",
                        column: x => x.CredentialId,
                        principalTable: "KsefCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KsefAuthenticationContexts_TaxEntities_TaxEntityId",
                        column: x => x.TaxEntityId,
                        principalTable: "TaxEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KsefAuthenticationContexts_CredentialId_TaxEntityId",
                table: "KsefAuthenticationContexts",
                columns: new[] { "CredentialId", "TaxEntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KsefAuthenticationContexts_TaxEntityId",
                table: "KsefAuthenticationContexts",
                column: "TaxEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_KsefCredentials_UserId",
                table: "KsefCredentials",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KsefAuthenticationContexts");

            migrationBuilder.DropTable(
                name: "KsefCredentials");
        }
    }
}
