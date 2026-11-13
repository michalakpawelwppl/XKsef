using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_invoices_KsefNumber", table: "invoices");

            migrationBuilder.AddColumn<Guid>(name: "TenantId", table: "invoices", type: "uniqueidentifier", nullable: true);

            migrationBuilder.Sql("UPDATE [invoices] SET [TenantId] = '991223a5-3173-4c96-a8af-b515065eec49' WHERE [TenantId] IS NULL");

            migrationBuilder.AlterColumn<Guid>(name: "TenantId", table: "invoices", type: "uniqueidentifier", nullable: false, oldClrType: typeof(Guid), oldType: "uniqueidentifier", oldNullable: true);

            migrationBuilder.CreateIndex(name: "IX_invoices_TenantId_KsefNumber", table: "invoices", columns: new[] { "TenantId", "KsefNumber" }, unique: true);

            migrationBuilder.AddForeignKey(name: "FK_invoices_Tenants_TenantId", table: "invoices", column: "TenantId", principalTable: "Tenants", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_invoices_Tenants_TenantId", table: "invoices");

            migrationBuilder.DropIndex(name: "IX_invoices_TenantId_KsefNumber", table: "invoices");

            migrationBuilder.DropColumn(name: "TenantId", table: "invoices");

            migrationBuilder.CreateIndex(name: "IX_invoices_KsefNumber", table: "invoices", column: "KsefNumber", unique: true);
        }
    }
}
