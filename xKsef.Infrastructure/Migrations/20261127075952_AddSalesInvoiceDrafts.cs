using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    PlannedIssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SaleDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    VatMethod = table.Column<int>(type: "int", nullable: false),
                    GlobalDiscount = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GlobalDiscountBasis = table.Column<int>(type: "int", nullable: false),
                    PdfLanguage = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CalculationVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CalculationComplete = table.Column<bool>(type: "bit", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    VatAmountPln = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    GlobalDiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoices", x => x.Id);
                    table.UniqueConstraint("AK_SalesInvoices_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_SalesInvoices_DiscountBasis", "[GlobalDiscountBasis] IN (0,1) AND ([VatMethod] <> 1 OR [GlobalDiscountBasis] = 0) AND ([VatMethod] <> 2 OR [GlobalDiscountBasis] = 1)");
                    table.CheckConstraint("CK_SalesInvoices_Kind", "[Kind] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_SalesInvoices_PdfLanguage", "[PdfLanguage] BETWEEN 0 AND 2");
                    table.CheckConstraint("CK_SalesInvoices_Status", "[Status] BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_SalesInvoices_VatMethod", "[VatMethod] BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_SalesInvoices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceDetails",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Data = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceDetails", x => new { x.TenantId, x.SalesInvoiceId });
                    table.CheckConstraint("CK_SalesInvoiceDetails_Json", "ISJSON([Data]) = 1");
                    table.ForeignKey(
                        name: "FK_SalesInvoiceDetails_SalesInvoices_TenantId_SalesInvoiceId",
                        columns: x => new { x.TenantId, x.SalesInvoiceId },
                        principalTable: "SalesInvoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceExchangeRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Data_FromCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Data_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Data_IsManualOverride = table.Column<bool>(type: "bit", nullable: false),
                    Data_OriginalRate = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    Data_OriginalSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Data_OverrideReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Data_QuotedUnits = table.Column<decimal>(type: "decimal(22,6)", precision: 22, scale: 6, nullable: false),
                    Data_Rate = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    Data_RateDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Data_ReferenceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Data_RetrievedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Data_SelectionRule = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Data_Source = table.Column<int>(type: "int", nullable: false),
                    Data_SourceReference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Data_ToCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceExchangeRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceExchangeRates_SalesInvoices_TenantId_SalesInvoiceId",
                        columns: x => new { x.TenantId, x.SalesInvoiceId },
                        principalTable: "SalesInvoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(22,6)", precision: 22, scale: 6, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(22,8)", precision: 22, scale: 8, nullable: true),
                    PriceBasis = table.Column<int>(type: "int", nullable: false),
                    TaxCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Discount = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProductCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Gtin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Pkwiu = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Cn = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Gtu = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    VatAmountPln = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    GlobalDiscountAllocation = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VatRoundingAdjustment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceLines", x => x.Id);
                    table.UniqueConstraint("AK_SalesInvoiceLines_TenantId_SalesInvoiceId_Id", x => new { x.TenantId, x.SalesInvoiceId, x.Id });
                    table.CheckConstraint("CK_SalesInvoiceLines_Inputs", "([Quantity] IS NULL OR [Quantity] >= 0) AND ([UnitPrice] IS NULL OR [UnitPrice] >= 0)");
                    table.CheckConstraint("CK_SalesInvoiceLines_Ordinal", "[Ordinal] > 0");
                    table.CheckConstraint("CK_SalesInvoiceLines_PriceBasis", "[PriceBasis] IN (0,1)");
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLines_SalesInvoices_TenantId_SalesInvoiceId",
                        columns: x => new { x.TenantId, x.SalesInvoiceId },
                        principalTable: "SalesInvoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceParties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceParties", x => x.Id);
                    table.CheckConstraint("CK_SalesInvoiceParties_Role", "[Role] IN (0,1)");
                    table.CheckConstraint("CK_SalesInvoicePartySnapshot_Json", "ISJSON([Data]) = 1");
                    table.ForeignKey(
                        name: "FK_SalesInvoiceParties_SalesInvoices_TenantId_SalesInvoiceId",
                        columns: x => new { x.TenantId, x.SalesInvoiceId },
                        principalTable: "SalesInvoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceTaxSummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VatAmountPln = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceTaxSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceTaxSummaries_SalesInvoices_TenantId_SalesInvoiceId",
                        columns: x => new { x.TenantId, x.SalesInvoiceId },
                        principalTable: "SalesInvoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ReferencedSalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferencedLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferencedRevision = table.Column<int>(type: "int", nullable: true),
                    ExternalNumber = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    KsefNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceReferences_SalesInvoiceLines_TenantId_ReferencedSalesInvoiceId_ReferencedLineId",
                        columns: x => new { x.TenantId, x.ReferencedSalesInvoiceId, x.ReferencedLineId },
                        principalTable: "SalesInvoiceLines",
                        principalColumns: new[] { "TenantId", "SalesInvoiceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceReferences_SalesInvoices_TenantId_ReferencedSalesInvoiceId",
                        columns: x => new { x.TenantId, x.ReferencedSalesInvoiceId },
                        principalTable: "SalesInvoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceReferences_SalesInvoices_TenantId_SalesInvoiceId",
                        columns: x => new { x.TenantId, x.SalesInvoiceId },
                        principalTable: "SalesInvoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceExchangeRates_TenantId_SalesInvoiceId_IsCurrent",
                table: "SalesInvoiceExchangeRates",
                columns: new[] { "TenantId", "SalesInvoiceId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_TenantId_SalesInvoiceId_Ordinal",
                table: "SalesInvoiceLines",
                columns: new[] { "TenantId", "SalesInvoiceId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceParties_TenantId_SalesInvoiceId_Role",
                table: "SalesInvoiceParties",
                columns: new[] { "TenantId", "SalesInvoiceId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceReferences_TenantId_ReferencedSalesInvoiceId_ReferencedLineId",
                table: "SalesInvoiceReferences",
                columns: new[] { "TenantId", "ReferencedSalesInvoiceId", "ReferencedLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceReferences_TenantId_SalesInvoiceId",
                table: "SalesInvoiceReferences",
                columns: new[] { "TenantId", "SalesInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_Status_PlannedIssueDate_Id",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "Status", "PlannedIssueDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceTaxSummaries_TenantId_SalesInvoiceId_TaxCode",
                table: "SalesInvoiceTaxSummaries",
                columns: new[] { "TenantId", "SalesInvoiceId", "TaxCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesInvoiceDetails");

            migrationBuilder.DropTable(
                name: "SalesInvoiceExchangeRates");

            migrationBuilder.DropTable(
                name: "SalesInvoiceParties");

            migrationBuilder.DropTable(
                name: "SalesInvoiceReferences");

            migrationBuilder.DropTable(
                name: "SalesInvoiceTaxSummaries");

            migrationBuilder.DropTable(
                name: "SalesInvoiceLines");

            migrationBuilder.DropTable(
                name: "SalesInvoices");
        }
    }
}
