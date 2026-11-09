using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmberDelivery.Ordering.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderLineModifierPriceCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "price_currency",
                table: "order_line_modifiers",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            // Existing rows would otherwise keep an empty currency, which Money rejects on materialization; a modifier
            // is always priced in the currency of the line that owns it, so that is the only correct backfill source.
            migrationBuilder.Sql(
                """
                UPDATE order_line_modifiers AS m
                SET price_currency = l.base_unit_price_currency
                FROM order_lines AS l
                WHERE m."OrderLineId" = l."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "price_currency",
                table: "order_line_modifiers");
        }
    }
}
