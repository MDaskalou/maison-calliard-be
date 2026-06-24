using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaisonCalliard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderEmailSentAtFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CustomerEmailSentAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InternalNotificationSentAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Orders"
                SET "CustomerEmailSentAt" = "ReceiptSentAt"
                WHERE "ReceiptSentAt" IS NOT NULL
                  AND "CustomerEmailSentAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerEmailSentAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InternalNotificationSentAt",
                table: "Orders");
        }
    }
}
