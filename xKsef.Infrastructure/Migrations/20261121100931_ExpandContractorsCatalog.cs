using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xKsef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpandContractorsCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Historical acronyms are intentionally not generated. Reset only the contractor catalog.
            migrationBuilder.Sql("DELETE FROM [AddressesData]; DELETE FROM [Addresses]; DELETE FROM [ContractorsData]; DELETE FROM [Contractors];");

            migrationBuilder.DropIndex(
                name: "IX_Addresses_ContractorsDataId",
                table: "Addresses");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "ContractorsData",
                newName: "FullName");

            migrationBuilder.AddColumn<string>(
                name: "EuVatNumber",
                table: "ContractorsData",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEuVatRegistered",
                table: "ContractorsData",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "KrsNumber",
                table: "ContractorsData",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OtherTaxNumber",
                table: "ContractorsData",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Regon",
                table: "ContractorsData",
                type: "nvarchar(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortName",
                table: "ContractorsData",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "AddressType",
                table: "Addresses",
                type: "int",
                nullable: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ContractorsData_EuVatRegistration",
                table: "ContractorsData",
                sql: "([IsEuVatRegistered] = 0 AND [EuVatNumber] IS NULL) OR ([IsEuVatRegistered] = 1 AND [EuVatNumber] IS NOT NULL AND LEN(LTRIM(RTRIM([EuVatNumber]))) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ContractorsData_FullName_NotEmpty",
                table: "ContractorsData",
                sql: "LEN(LTRIM(RTRIM([FullName]))) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ContractorsData_ShortName_NotEmpty",
                table: "ContractorsData",
                sql: "LEN(LTRIM(RTRIM([ShortName]))) > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Addresses_ContractorsDataId_AddressType",
                table: "Addresses",
                columns: new[] { "ContractorsDataId", "AddressType" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Addresses_AddressType",
                table: "Addresses",
                sql: "[AddressType] IN (1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ContractorsData_EuVatRegistration",
                table: "ContractorsData");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ContractorsData_FullName_NotEmpty",
                table: "ContractorsData");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ContractorsData_ShortName_NotEmpty",
                table: "ContractorsData");

            migrationBuilder.DropIndex(
                name: "IX_Addresses_ContractorsDataId_AddressType",
                table: "Addresses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Addresses_AddressType",
                table: "Addresses");

            migrationBuilder.DropColumn(
                name: "EuVatNumber",
                table: "ContractorsData");

            migrationBuilder.DropColumn(
                name: "IsEuVatRegistered",
                table: "ContractorsData");

            migrationBuilder.DropColumn(
                name: "KrsNumber",
                table: "ContractorsData");

            migrationBuilder.DropColumn(
                name: "OtherTaxNumber",
                table: "ContractorsData");

            migrationBuilder.DropColumn(
                name: "Regon",
                table: "ContractorsData");

            migrationBuilder.DropColumn(
                name: "ShortName",
                table: "ContractorsData");

            migrationBuilder.DropColumn(
                name: "AddressType",
                table: "Addresses");

            migrationBuilder.RenameColumn(
                name: "FullName",
                table: "ContractorsData",
                newName: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Addresses_ContractorsDataId",
                table: "Addresses",
                column: "ContractorsDataId",
                unique: true);
        }
    }
}
