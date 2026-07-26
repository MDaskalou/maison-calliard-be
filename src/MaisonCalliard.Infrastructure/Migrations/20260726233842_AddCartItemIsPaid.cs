using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaisonCalliard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCartItemIsPaid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                table: "CartItems",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPaid",
                table: "CartItems");
        }
    }
}
