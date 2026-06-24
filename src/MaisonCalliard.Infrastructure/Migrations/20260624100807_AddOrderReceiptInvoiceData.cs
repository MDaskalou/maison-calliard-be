using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaisonCalliard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderReceiptInvoiceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerAddress",
                table: "Orders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptNumber",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripePaymentIntentId",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "CartItems",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ReceiptSequences",
                columns: table => new
                {
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptSequences", x => x.Year);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ReceiptNumber",
                table: "Orders",
                column: "ReceiptNumber",
                unique: true,
                filter: "\"ReceiptNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StripePaymentIntentId",
                table: "Orders",
                column: "StripePaymentIntentId",
                unique: true,
                filter: "\"StripePaymentIntentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StripeSessionId",
                table: "Orders",
                column: "StripeSessionId",
                unique: true,
                filter: "\"StripeSessionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReceiptSequences");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ReceiptNumber",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StripePaymentIntentId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StripeSessionId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CustomerAddress",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReceiptNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StripePaymentIntentId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "CartItems");
        }
    }
}
