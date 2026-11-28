using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBasicSalesInvoiceFa3Data : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoices_VatMethod",
                table: "SalesInvoices");

            migrationBuilder.AddColumn<string>(
                name: "Fa3",
                table: "SalesInvoices",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "N'{\"Version\":1}'");

            migrationBuilder.AddColumn<string>(
                name: "IssuePlace",
                table: "SalesInvoices",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModelVersion",
                table: "SalesInvoices",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "SaleDateMode",
                table: "SalesInvoices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SalePeriodFrom",
                table: "SalesInvoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SalePeriodTo",
                table: "SalesInvoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalePeriodVersion",
                table: "SalesInvoices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Annex15",
                table: "SalesInvoiceLines",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "N'{}'");

            migrationBuilder.AddColumn<string>(
                name: "DomesticRateCode",
                table: "SalesInvoiceLines",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseAmount",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExemptionBasisKind",
                table: "SalesInvoiceLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExemptionBasisText",
                table: "SalesInvoiceLines",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExemptionBasisVersion",
                table: "SalesInvoiceLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ForeignVatRate",
                table: "SalesInvoiceLines",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pkob",
                table: "SalesInvoiceLines",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Procedure",
                table: "SalesInvoiceLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SaleDate",
                table: "SalesInvoiceLines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxCountryCode",
                table: "SalesInvoiceLines",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxTreatmentKind",
                table: "SalesInvoiceLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxTreatmentVersion",
                table: "SalesInvoiceLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesInvoiceParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: true),
                    OtherRoleDescription = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SharePercent = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Data = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceParticipants", x => x.Id);
                    table.UniqueConstraint("AK_SalesInvoiceParticipants_TenantId_SalesInvoiceId_Id", x => new { x.TenantId, x.SalesInvoiceId, x.Id });
                    table.CheckConstraint("CK_SalesInvoiceParticipant_Json", "ISJSON([Data]) = 1");
                    table.CheckConstraint("CK_SalesInvoiceParticipants_Ordinal", "[Ordinal] > 0");
                    table.CheckConstraint("CK_SalesInvoiceParticipants_Role", "[Role] IS NULL OR [Role] BETWEEN 0 AND 11");
                    table.CheckConstraint("CK_SalesInvoiceParticipants_Share", "[SharePercent] IS NULL OR [SharePercent] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_SalesInvoiceParticipants_SalesInvoices_TenantId_SalesInvoiceId",
                        columns: x => new { x.TenantId, x.SalesInvoiceId },
                        principalTable: "SalesInvoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoices_Fa3Json",
                table: "SalesInvoices",
                sql: "ISJSON([Fa3]) = 1 AND JSON_VALUE([Fa3], '$.Version') IS NOT NULL AND JSON_VALUE([Fa3], '$.Version') = '1'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoices_ModelVersion",
                table: "SalesInvoices",
                sql: "[ModelVersion] IN (1,2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoices_SaleDateMode",
                table: "SalesInvoices",
                sql: "[SaleDateMode] IS NULL OR [SaleDateMode] BETWEEN 0 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoices_VatMethod",
                table: "SalesInvoices",
                sql: "[VatMethod] BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoiceLines_Annex15Json",
                table: "SalesInvoiceLines",
                sql: "ISJSON([Annex15]) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceParticipants_TenantId_SalesInvoiceId_Ordinal",
                table: "SalesInvoiceParticipants",
                columns: new[] { "TenantId", "SalesInvoiceId", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesInvoiceParticipants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoices_Fa3Json",
                table: "SalesInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoices_ModelVersion",
                table: "SalesInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoices_SaleDateMode",
                table: "SalesInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoices_VatMethod",
                table: "SalesInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoiceLines_Annex15Json",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "Fa3",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "IssuePlace",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "ModelVersion",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "SaleDateMode",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "SalePeriodFrom",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "SalePeriodTo",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "SalePeriodVersion",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "Annex15",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "DomesticRateCode",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ExciseAmount",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ExemptionBasisKind",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ExemptionBasisText",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ExemptionBasisVersion",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ForeignVatRate",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "Pkob",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "Procedure",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "SaleDate",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "TaxCountryCode",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "TaxTreatmentKind",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "TaxTreatmentVersion",
                table: "SalesInvoiceLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoices_VatMethod",
                table: "SalesInvoices",
                sql: "[VatMethod] BETWEEN 0 AND 2");
        }
    }
}
