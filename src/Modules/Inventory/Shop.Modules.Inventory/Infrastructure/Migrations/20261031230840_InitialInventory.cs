using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shop.Modules.Inventory.Infrastructure.Migrations;

/// <inheritdoc />
internal partial class InitialInventory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "inventory");

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "inventory",
            columns: table => new
            {
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                handler_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                processed_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inbox_messages", x => new { x.message_id, x.handler_name });
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "inventory",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                contract_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                occurred_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                traceparent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                tracestate = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                attempts = table.Column<int>(type: "integer", nullable: false),
                next_attempt_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                processed_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                failed_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "reservations",
            schema: "inventory",
            columns: table => new
            {
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reservations", x => x.OrderId);
            });

        migrationBuilder.CreateTable(
            name: "stock_items",
            schema: "inventory",
            columns: table => new
            {
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                OnHand = table.Column<int>(type: "integer", nullable: false),
                Reserved = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_stock_items", x => x.ProductId);
            });

        migrationBuilder.CreateTable(
            name: "reservation_lines",
            schema: "inventory",
            columns: table => new
            {
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reservation_lines", x => new { x.OrderId, x.ProductId });
                table.ForeignKey(
                    name: "FK_reservation_lines_reservations_OrderId",
                    column: x => x.OrderId,
                    principalSchema: "inventory",
                    principalTable: "reservations",
                    principalColumn: "OrderId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_inventory_outbox_messages_processing",
            schema: "inventory",
            table: "outbox_messages",
            columns: new[] { "processed_on", "failed_on", "next_attempt_on", "occurred_on" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "inventory");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "inventory");

        migrationBuilder.DropTable(
            name: "reservation_lines",
            schema: "inventory");

        migrationBuilder.DropTable(
            name: "stock_items",
            schema: "inventory");

        migrationBuilder.DropTable(
            name: "reservations",
            schema: "inventory");
    }
}
