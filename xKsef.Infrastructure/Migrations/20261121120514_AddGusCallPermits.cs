using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGusCallPermits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "GusCallPermits",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Environment = table.Column<byte>(type: "tinyint", nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GusCallPermits", x => x.Id);
                    table.CheckConstraint("CK_GusCallPermits_Environment", "[Environment] IN (0, 1)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_GusCallPermits_Environment_GrantedAtUtc",
                schema: "dbo",
                table: "GusCallPermits",
                columns: new[] { "Environment", "GrantedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GusCallPermits",
                schema: "dbo");
        }
    }
}
