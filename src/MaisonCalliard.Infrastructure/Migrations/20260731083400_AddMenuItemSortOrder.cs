using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaisonCalliard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuItemSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "MenuItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                WITH ordered AS (
                    SELECT "Id",
                           (ROW_NUMBER() OVER (ORDER BY "Name_Se", "Id") - 1) AS "NewSortOrder"
                    FROM "MenuItems"
                )
                UPDATE "MenuItems" AS m
                SET "SortOrder" = ordered."NewSortOrder"
                FROM ordered
                WHERE m."Id" = ordered."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "MenuItems");
        }
    }
}
