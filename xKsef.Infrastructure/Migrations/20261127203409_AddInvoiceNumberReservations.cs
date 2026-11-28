using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceNumberReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FinalNumberReservationId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "SalesInvoices",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KsefNumber",
                table: "SalesInvoices",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedInvoiceNumber",
                table: "SalesInvoices",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<Guid>(
                name: "NumberSeriesId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SalesInvoiceApprovals_TenantId_SalesInvoiceId_Id_ApprovedRevision",
                table: "SalesInvoiceApprovals",
                columns: new[] { "TenantId", "SalesInvoiceId", "Id", "ApprovedRevision" });

            migrationBuilder.CreateTable(
                name: "InvoiceNumberCounters",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    StartingNumber = table.Column<long>(type: "bigint", nullable: false),
                    NextValue = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceNumberCounters", x => new { x.TenantId, x.SeriesId, x.Period });
                    table.CheckConstraint("CK_InvoiceNumberCounters_Values", "[Period] BETWEEN 0 AND 9999 AND [StartingNumber] > 0 AND ([NextValue] IS NULL OR [NextValue] >= [StartingNumber])");
                    table.ForeignKey(
                        name: "FK_InvoiceNumberCounters_InvoiceNumberSeries_TenantId_SeriesId",
                        columns: x => new { x.TenantId, x.SeriesId },
                        principalTable: "InvoiceNumberSeries",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceNumberReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedRevision = table.Column<int>(type: "int", nullable: false),
                    InvoiceVersion = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeriesRevision = table.Column<int>(type: "int", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    NumericValue = table.Column<long>(type: "bigint", nullable: false),
                    FullNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    HoldsNumber = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    BlockedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BlockedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UncertaintyReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    EvidenceId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    KsefNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceNumberReservations", x => x.Id);
                    table.UniqueConstraint("AK_InvoiceNumberReservations_TenantId_InvoiceId_Id", x => new { x.TenantId, x.InvoiceId, x.Id });
                    table.CheckConstraint("CK_InvoiceNumberReservations_Resolution", "([State] IN (0,3) AND [EvidenceId] IS NULL AND [KsefNumber] IS NULL AND [ResolvedAt] IS NULL AND [ResolvedBy] IS NULL AND [ConfirmedAt] IS NULL) OR ([State] IN (1,2) AND [EvidenceId] IS NOT NULL AND [ResolvedAt] IS NOT NULL AND [ResolvedBy] IS NOT NULL AND [ConfirmedAt] IS NOT NULL AND (([State] = 1 AND [KsefNumber] IS NOT NULL) OR ([State] = 2 AND [KsefNumber] IS NULL)))");
                    table.CheckConstraint("CK_InvoiceNumberReservations_Values", "[NumericValue] > 0 AND [Period] BETWEEN 0 AND 9999 AND [State] BETWEEN 0 AND 3 AND (([State] = 2 AND [HoldsNumber] = 0) OR ([State] <> 2 AND [HoldsNumber] = 1))");
                    table.ForeignKey(
                        name: "FK_InvoiceNumberReservations_InvoiceNumberCounters_TenantId_SeriesId_Period",
                        columns: x => new { x.TenantId, x.SeriesId, x.Period },
                        principalTable: "InvoiceNumberCounters",
                        principalColumns: new[] { "TenantId", "SeriesId", "Period" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceNumberReservations_InvoiceNumberSeriesHistory_TenantId_SeriesId_SeriesRevision",
                        columns: x => new { x.TenantId, x.SeriesId, x.SeriesRevision },
                        principalTable: "InvoiceNumberSeriesHistory",
                        principalColumns: new[] { "TenantId", "SeriesId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceNumberReservations_SalesInvoiceApprovals_TenantId_InvoiceId_ApprovalId_ApprovedRevision",
                        columns: x => new { x.TenantId, x.InvoiceId, x.ApprovalId, x.ApprovedRevision },
                        principalTable: "SalesInvoiceApprovals",
                        principalColumns: new[] { "TenantId", "SalesInvoiceId", "Id", "ApprovedRevision" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceNumberReservations_SalesInvoices_TenantId_InvoiceId",
                        columns: x => new { x.TenantId, x.InvoiceId },
                        principalTable: "SalesInvoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_Id_FinalNumberReservationId",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "Id", "FinalNumberReservationId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_KsefNumber",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "KsefNumber" },
                unique: true,
                filter: "[KsefNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_NormalizedInvoiceNumber",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "NormalizedInvoiceNumber" },
                unique: true,
                filter: "[NormalizedInvoiceNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_NumberSeriesId",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "NumberSeriesId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceNumberReservations_TenantId_InvoiceId",
                table: "InvoiceNumberReservations",
                columns: new[] { "TenantId", "InvoiceId" },
                unique: true,
                filter: "[HoldsNumber] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceNumberReservations_TenantId_InvoiceId_ApprovalId_ApprovedRevision",
                table: "InvoiceNumberReservations",
                columns: new[] { "TenantId", "InvoiceId", "ApprovalId", "ApprovedRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceNumberReservations_TenantId_NormalizedNumber",
                table: "InvoiceNumberReservations",
                columns: new[] { "TenantId", "NormalizedNumber" },
                unique: true,
                filter: "[HoldsNumber] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceNumberReservations_TenantId_OperationId",
                table: "InvoiceNumberReservations",
                columns: new[] { "TenantId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceNumberReservations_TenantId_SeriesId_Period_NumericValue",
                table: "InvoiceNumberReservations",
                columns: new[] { "TenantId", "SeriesId", "Period", "NumericValue" },
                unique: true,
                filter: "[HoldsNumber] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceNumberReservations_TenantId_SeriesId_SeriesRevision",
                table: "InvoiceNumberReservations",
                columns: new[] { "TenantId", "SeriesId", "SeriesRevision" });

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_InvoiceNumberReservations_TenantId_Id_FinalNumberReservationId",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "Id", "FinalNumberReservationId" },
                principalTable: "InvoiceNumberReservations",
                principalColumns: new[] { "TenantId", "InvoiceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_InvoiceNumberSeries_TenantId_NumberSeriesId",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "NumberSeriesId" },
                principalTable: "InvoiceNumberSeries",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoices_FinalNumber",
                table: "SalesInvoices",
                sql: "([FinalNumberReservationId] IS NULL AND [InvoiceNumber] IS NULL AND [NormalizedInvoiceNumber] IS NULL AND [KsefNumber] IS NULL) OR ([FinalNumberReservationId] IS NOT NULL AND [InvoiceNumber] IS NOT NULL AND [NormalizedInvoiceNumber] IS NOT NULL AND [KsefNumber] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "CK_SalesInvoices_FinalNumber", table: "SalesInvoices");
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_InvoiceNumberReservations_TenantId_Id_FinalNumberReservationId",
                table: "SalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_InvoiceNumberSeries_TenantId_NumberSeriesId",
                table: "SalesInvoices");

            migrationBuilder.DropTable(
                name: "InvoiceNumberReservations");

            migrationBuilder.DropTable(
                name: "InvoiceNumberCounters");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_TenantId_Id_FinalNumberReservationId",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_TenantId_KsefNumber",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_TenantId_NormalizedInvoiceNumber",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_TenantId_NumberSeriesId",
                table: "SalesInvoices");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SalesInvoiceApprovals_TenantId_SalesInvoiceId_Id_ApprovedRevision",
                table: "SalesInvoiceApprovals");

            migrationBuilder.DropColumn(
                name: "FinalNumberReservationId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "KsefNumber",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "NormalizedInvoiceNumber",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "NumberSeriesId",
                table: "SalesInvoices");
        }
    }
}
