using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Regal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicePaymentsAndEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailedAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailedTo",
                table: "Invoices",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidAmount",
                table: "Invoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PaidDate",
                table: "Invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentCheckedAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "Invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PaymentStatus_PaymentCheckedAt",
                table: "Invoices",
                columns: new[] { "PaymentStatus", "PaymentCheckedAt" });

            // Payment and email state come from Fakturownia after issue, so the application may
            // write them; the rest of an issued invoice stays fixed (see AddInvoices).
            migrationBuilder.Sql("GRANT UPDATE (\"PaymentStatus\", \"PaidAmount\", \"PaidDate\", \"PaymentCheckedAt\", \"EmailedAt\", \"EmailedTo\") ON \"Invoices\" TO regal_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_PaymentStatus_PaymentCheckedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "EmailedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "EmailedTo",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PaidAmount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PaidDate",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PaymentCheckedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Invoices");
        }
    }
}
