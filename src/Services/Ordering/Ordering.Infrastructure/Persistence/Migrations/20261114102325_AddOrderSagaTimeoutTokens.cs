using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmberDelivery.Ordering.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderSagaTimeoutTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CourierPickupTimeoutTokenId",
                table: "order_saga_states",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "KitchenConfirmationTimeoutTokenId",
                table: "order_saga_states",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentHoldTimeoutTokenId",
                table: "order_saga_states",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CourierPickupTimeoutTokenId",
                table: "order_saga_states");

            migrationBuilder.DropColumn(
                name: "KitchenConfirmationTimeoutTokenId",
                table: "order_saga_states");

            migrationBuilder.DropColumn(
                name: "PaymentHoldTimeoutTokenId",
                table: "order_saga_states");
        }
    }
}
