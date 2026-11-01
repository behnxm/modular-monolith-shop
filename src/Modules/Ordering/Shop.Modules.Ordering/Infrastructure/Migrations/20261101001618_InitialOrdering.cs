using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shop.Modules.Ordering.Infrastructure.Migrations;

/// <inheritdoc />
internal partial class InitialOrdering : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "ordering");

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "ordering",
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
            name: "order_idempotency_records",
            schema: "ordering",
            columns: table => new
            {
                CustomerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_idempotency_records", x => new { x.CustomerId, x.Key });
            });

        migrationBuilder.CreateTable(
            name: "orders",
            schema: "ordering",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                TotalAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_orders", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "ordering",
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
            name: "order_lines",
            schema: "ordering",
            columns: table => new
            {
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                UnitPriceSnapshot = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_lines", x => new { x.OrderId, x.ProductId });
                table.ForeignKey(
                    name: "FK_order_lines_orders_OrderId",
                    column: x => x.OrderId,
                    principalSchema: "ordering",
                    principalTable: "orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_ordering_orders_created",
            schema: "ordering",
            table: "orders",
            columns: new[] { "CreatedAt", "Id" });

        migrationBuilder.CreateIndex(
            name: "ix_ordering_orders_customer_created",
            schema: "ordering",
            table: "orders",
            columns: new[] { "CustomerId", "CreatedAt", "Id" });

        migrationBuilder.CreateIndex(
            name: "ix_ordering_outbox_messages_processing",
            schema: "ordering",
            table: "outbox_messages",
            columns: new[] { "processed_on", "failed_on", "next_attempt_on", "occurred_on" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "ordering");

        migrationBuilder.DropTable(
            name: "order_idempotency_records",
            schema: "ordering");

        migrationBuilder.DropTable(
            name: "order_lines",
            schema: "ordering");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "ordering");

        migrationBuilder.DropTable(
            name: "orders",
            schema: "ordering");
    }
}
