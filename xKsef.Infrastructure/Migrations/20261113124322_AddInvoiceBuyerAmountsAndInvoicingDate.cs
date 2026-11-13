using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceBuyerAmountsAndInvoicingDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerName",
                table: "invoices",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerNip",
                table: "invoices",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InvoicingDate",
                table: "invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetAmount",
                table: "invoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "invoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmountPln",
                table: "invoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyerName",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "BuyerNip",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "InvoicingDate",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "NetAmount",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "VatAmountPln",
                table: "invoices");
        }
    }
}
