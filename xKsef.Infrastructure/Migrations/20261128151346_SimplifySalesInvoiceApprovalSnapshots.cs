using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SimplifySalesInvoiceApprovalSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM SalesInvoiceApprovals) THROW 50002, N'Zmiana formatu wymaga pustej tabeli zatwierdzeń. Nie konwertowano ani nie usunięto danych. Odtwórz powiązane dane testowe przed migracją.', 1;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoiceApprovals_Snapshot",
                table: "SalesInvoiceApprovals");

            migrationBuilder.DropColumn(
                name: "SnapshotVersion",
                table: "SalesInvoiceApprovals");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceApprovals_TenantId_SalesInvoiceId",
                table: "SalesInvoiceApprovals",
                columns: new[] { "TenantId", "SalesInvoiceId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoiceApprovals_Snapshot",
                table: "SalesInvoiceApprovals",
                sql: "ISJSON([SnapshotJson]) = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM SalesInvoiceApprovals) THROW 50002, N'Nie można przywrócić starych formatów, gdy istnieją aktualne snapshoty.', 1;");
            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceApprovals_TenantId_SalesInvoiceId",
                table: "SalesInvoiceApprovals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoiceApprovals_Snapshot",
                table: "SalesInvoiceApprovals");

            migrationBuilder.AddColumn<int>(
                name: "SnapshotVersion",
                table: "SalesInvoiceApprovals",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoiceApprovals_Snapshot",
                table: "SalesInvoiceApprovals",
                sql: "[SnapshotVersion] IN (1, 2) AND ISJSON([SnapshotJson]) = 1");
        }
    }
}
