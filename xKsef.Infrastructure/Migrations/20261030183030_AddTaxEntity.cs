using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var taxEntityId = new Guid("a3b4e5d6-1111-4222-8333-123456789012");

            migrationBuilder.CreateTable(
                name: "TaxEntities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nip = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxEntities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaxEntities_Nip",
                table: "TaxEntities",
                column: "Nip",
                unique: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaxEntityId",
                table: "Tenants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.InsertData(
                table: "TaxEntities",
                columns: new[] { "Id", "Nip", "Name" },
                values: new object[] { taxEntityId, "1234567890", "Firma testowa" });

            migrationBuilder.Sql("UPDATE Tenants SET TaxEntityId = 'a3b4e5d6-1111-4222-8333-123456789012' WHERE Id = '991223a5-3173-4c96-a8af-b515065eec49'");

            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM Tenants WHERE TaxEntityId IS NULL) THROW 50001, 'Istnieja tenanty bez przypisanego TaxEntity.', 1;");

            migrationBuilder.AlterColumn<Guid>(
                name: "TaxEntityId",
                table: "Tenants",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_TaxEntityId",
                table: "Tenants",
                column: "TaxEntityId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_TaxEntities_TaxEntityId",
                table: "Tenants",
                column: "TaxEntityId",
                principalTable: "TaxEntities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_TaxEntities_TaxEntityId",
                table: "Tenants");

            migrationBuilder.DropTable(
                name: "TaxEntities");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_TaxEntityId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TaxEntityId",
                table: "Tenants");
        }
    }
}
