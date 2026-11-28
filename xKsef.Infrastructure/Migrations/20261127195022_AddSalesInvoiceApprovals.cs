using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesInvoiceApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SourceRevision = table.Column<int>(type: "int", nullable: false),
                    ApprovedRevision = table.Column<int>(type: "int", nullable: false),
                    SnapshotVersion = table.Column<int>(type: "int", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceApprovals", x => x.Id);
                    table.CheckConstraint("CK_SalesInvoiceApprovals_Revisions", "[SourceRevision] > 0 AND [ApprovedRevision] = [SourceRevision] + 1");
                    table.CheckConstraint("CK_SalesInvoiceApprovals_Snapshot", "[SnapshotVersion] = 1 AND ISJSON([SnapshotJson]) = 1");
                    table.ForeignKey(
                        name: "FK_SalesInvoiceApprovals_SalesInvoices_TenantId_SalesInvoiceId",
                        columns: x => new { x.TenantId, x.SalesInvoiceId },
                        principalTable: "SalesInvoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceApprovals_TenantId_SalesInvoiceId_ApprovedRevision",
                table: "SalesInvoiceApprovals",
                columns: new[] { "TenantId", "SalesInvoiceId", "ApprovedRevision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesInvoiceApprovals");
        }
    }
}
