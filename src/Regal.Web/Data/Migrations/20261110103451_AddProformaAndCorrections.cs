using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Regal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProformaAndCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceLines_Quantity_Positive",
                table: "InvoiceLines");

            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                table: "Invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CorrectsInvoiceId",
                table: "Invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CorrectsLineId",
                table: "InvoiceLines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CorrectsInvoiceId",
                table: "Invoices",
                column: "CorrectsInvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_CorrectsLineId",
                table: "InvoiceLines",
                column: "CorrectsLineId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceLines_Quantity_Positive",
                table: "InvoiceLines",
                sql: "(\"CorrectsLineId\" IS NULL AND \"Quantity\" > 0) OR (\"CorrectsLineId\" IS NOT NULL AND \"Quantity\" <= 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceLines_InvoiceLines_CorrectsLineId",
                table: "InvoiceLines",
                column: "CorrectsLineId",
                principalTable: "InvoiceLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Invoices_CorrectsInvoiceId",
                table: "Invoices",
                column: "CorrectsInvoiceId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceLines_InvoiceLines_CorrectsLineId",
                table: "InvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Invoices_CorrectsInvoiceId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_CorrectsInvoiceId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLines_CorrectsLineId",
                table: "InvoiceLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceLines_Quantity_Positive",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CorrectsInvoiceId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CorrectsLineId",
                table: "InvoiceLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceLines_Quantity_Positive",
                table: "InvoiceLines",
                sql: "\"Quantity\" > 0");
        }
    }
}
