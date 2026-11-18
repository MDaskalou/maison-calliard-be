
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaisonCalliard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnsureOrderEmailTimestampColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Orders"
                    ADD COLUMN IF NOT EXISTS "ReceiptSentAt"
                        timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS "CustomerEmailSentAt"
                        timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS "InternalNotificationSentAt"
                        timestamp with time zone NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Avsiktligt tom.
            // Kolumnerna finns redan i PROD och ska inte
            // raderas vid en eventuell rollback.
        }
    }
}
