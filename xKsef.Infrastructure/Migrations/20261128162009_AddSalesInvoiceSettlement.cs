using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SettlementAmounts",
                table: "SalesInvoices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoices_SettlementAmounts",
                table: "SalesInvoices",
                sql: "[SettlementAmounts] IS NULL OR ISJSON([SettlementAmounts]) = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoices_SettlementAmounts",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "SettlementAmounts",
                table: "SalesInvoices");
        }
    }
}
