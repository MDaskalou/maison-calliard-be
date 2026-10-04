using MaisonCalliard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaisonCalliard.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261004190600_RemoveUnpaidStripeDrafts")]
    public partial class RemoveUnpaidStripeDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AwaitingPayment = 3. Paid live orders stay: they are status 2 and their Stripe ids do not contain _test_.
            // CartItems are removed by the existing FK cascade.
            migrationBuilder.Sql(
                """
                DELETE FROM "Orders"
                WHERE (
                        "Status" = 3
                        AND (
                            COALESCE("StripePaymentIntentId", '') <> ''
                            OR COALESCE("StripeSessionId", '') <> ''
                        )
                    )
                    OR POSITION('_test_' IN COALESCE("StripePaymentIntentId", '')) > 0
                    OR POSITION('_test_' IN COALESCE("StripeSessionId", '')) > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
