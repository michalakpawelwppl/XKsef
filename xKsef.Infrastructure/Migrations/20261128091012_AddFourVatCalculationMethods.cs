using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFourVatCalculationMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoices_DiscountBasis",
                table: "SalesInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoices_VatMethod",
                table: "SalesInvoices");

            migrationBuilder.AddColumn<string>(
                name: "CalculationDetails",
                table: "SalesInvoiceTaxSummaries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VatAmountOrigin",
                table: "SalesInvoiceLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatBaseRoundingAdjustment",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            // Only Draft calculations are invalidated. Historical approval JSON and its audit remain untouched.
            // The retired mixed method 0 becomes PerLineNet; no automatic reapproval is attempted.
            migrationBuilder.Sql("""
                UPDATE SalesInvoices SET
                    VatMethod = CASE VatMethod WHEN 1 THEN 2 WHEN 2 THEN 3 WHEN 3 THEN 0 WHEN 4 THEN 1 ELSE 0 END,
                    CalculationComplete = CASE WHEN Status = 0 THEN 0 ELSE CalculationComplete END,
                    NetAmount = CASE WHEN Status = 0 THEN NULL ELSE NetAmount END,
                    VatAmount = CASE WHEN Status = 0 THEN NULL ELSE VatAmount END,
                    GrossAmount = CASE WHEN Status = 0 THEN NULL ELSE GrossAmount END,
                    VatAmountPln = CASE WHEN Status = 0 THEN NULL ELSE VatAmountPln END,
                    GlobalDiscountAmount = CASE WHEN Status = 0 THEN 0 ELSE GlobalDiscountAmount END,
                    Revision = Revision + CASE WHEN Status = 0 THEN 1 ELSE 0 END;
                UPDATE l SET NetAmount=NULL, VatAmount=NULL, GrossAmount=NULL, VatAmountPln=NULL,
                    GlobalDiscountAllocation=0, VatRoundingAdjustment=0
                FROM SalesInvoiceLines l INNER JOIN SalesInvoices i ON i.TenantId=l.TenantId AND i.Id=l.SalesInvoiceId
                WHERE i.Status=0;
                DELETE s FROM SalesInvoiceTaxSummaries s INNER JOIN SalesInvoices i
                    ON i.TenantId=s.TenantId AND i.Id=s.SalesInvoiceId WHERE i.Status=0;
                """);
            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoices_DiscountBasis",
                table: "SalesInvoices",
                sql: "[GlobalDiscountBasis] IN (0,1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoices_VatMethod",
                table: "SalesInvoices",
                sql: "[VatMethod] BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoiceLines_VatOrigin",
                table: "SalesInvoiceLines",
                sql: "[VatAmountOrigin] IS NULL OR [VatAmountOrigin] IN (0,1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoices_DiscountBasis",
                table: "SalesInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoices_VatMethod",
                table: "SalesInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoiceLines_VatOrigin",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "CalculationDetails",
                table: "SalesInvoiceTaxSummaries");

            migrationBuilder.DropColumn(
                name: "VatAmountOrigin",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "VatBaseRoundingAdjustment",
                table: "SalesInvoiceLines");

            // Do not manufacture compatibility for the retired algorithm or alter approval snapshots.
            migrationBuilder.Sql("""
                UPDATE SalesInvoices SET VatMethod = CASE VatMethod WHEN 1 THEN 4 WHEN 2 THEN 1 WHEN 3 THEN 2 ELSE 0 END;
                """);
            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoices_DiscountBasis",
                table: "SalesInvoices",
                sql: "[GlobalDiscountBasis] IN (0,1) AND ([VatMethod] <> 1 OR [GlobalDiscountBasis] = 0) AND ([VatMethod] <> 2 OR [GlobalDiscountBasis] = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoices_VatMethod",
                table: "SalesInvoices",
                sql: "[VatMethod] BETWEEN 0 AND 4");
        }
    }
}
