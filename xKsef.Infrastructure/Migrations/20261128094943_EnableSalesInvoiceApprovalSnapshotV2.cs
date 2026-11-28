using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnableSalesInvoiceApprovalSnapshotV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoiceApprovals_Snapshot",
                table: "SalesInvoiceApprovals");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoiceApprovals_Snapshot",
                table: "SalesInvoiceApprovals",
                sql: "[SnapshotVersion] IN (1, 2) AND ISJSON([SnapshotJson]) = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM SalesInvoiceApprovals WHERE SnapshotVersion = 2) THROW 50001, N'Nie można wycofać obsługi v2, gdy istnieją zatwierdzenia w tym formacie.', 1;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoiceApprovals_Snapshot",
                table: "SalesInvoiceApprovals");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoiceApprovals_Snapshot",
                table: "SalesInvoiceApprovals",
                sql: "[SnapshotVersion] = 1 AND ISJSON([SnapshotJson]) = 1");
        }
    }
}
