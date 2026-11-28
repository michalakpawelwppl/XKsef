using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceNumberSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceNumberSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Format = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    MinimumDigits = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ResetMode = table.Column<int>(type: "int", nullable: false),
                    StartingNumber = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceNumberSeries", x => x.Id);
                    table.UniqueConstraint("AK_InvoiceNumberSeries_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_InvoiceNumberSeries_Configuration", "[MinimumDigits] BETWEEN 1 AND 19 AND [ResetMode] IN (0,1) AND [StartingNumber] > 0 AND [Revision] > 0");
                    table.ForeignKey(
                        name: "FK_InvoiceNumberSeries_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceNumberSeriesDefaults",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceKind = table.Column<int>(type: "int", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceNumberSeriesDefaults", x => new { x.TenantId, x.InvoiceKind });
                    table.CheckConstraint("CK_InvoiceNumberSeriesDefaults_Kind", "[InvoiceKind] BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_InvoiceNumberSeriesDefaults_InvoiceNumberSeries_TenantId_SeriesId",
                        columns: x => new { x.TenantId, x.SeriesId },
                        principalTable: "InvoiceNumberSeries",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceNumberSeriesHistory",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Configuration = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Change = table.Column<int>(type: "int", nullable: false),
                    InvoiceKind = table.Column<int>(type: "int", nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceNumberSeriesHistory", x => new { x.TenantId, x.SeriesId, x.Revision });
                    table.CheckConstraint("CK_InvoiceNumberSeriesHistory_Json", "ISJSON([Configuration]) = 1");
                    table.CheckConstraint("CK_InvoiceNumberSeriesHistory_Values", "[Revision] > 0 AND [Change] BETWEEN 0 AND 5 AND ([InvoiceKind] IS NULL OR [InvoiceKind] BETWEEN 0 AND 3)");
                    table.ForeignKey(
                        name: "FK_InvoiceNumberSeriesHistory_InvoiceNumberSeries_TenantId_SeriesId",
                        columns: x => new { x.TenantId, x.SeriesId },
                        principalTable: "InvoiceNumberSeries",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceNumberSeries_TenantId_NormalizedName",
                table: "InvoiceNumberSeries",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceNumberSeriesDefaults_TenantId_SeriesId",
                table: "InvoiceNumberSeriesDefaults",
                columns: new[] { "TenantId", "SeriesId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceNumberSeriesDefaults");

            migrationBuilder.DropTable(
                name: "InvoiceNumberSeriesHistory");

            migrationBuilder.DropTable(
                name: "InvoiceNumberSeries");
        }
    }
}
