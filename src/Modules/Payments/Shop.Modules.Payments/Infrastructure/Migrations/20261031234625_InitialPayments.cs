using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shop.Modules.Payments.Infrastructure.Migrations;

/// <inheritdoc />
internal partial class InitialPayments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "payments");

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "payments",
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
            schema: "payments",
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
            name: "payment_attempts",
            schema: "payments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payment_attempts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_payments_outbox_messages_processing",
            schema: "payments",
            table: "outbox_messages",
            columns: new[] { "processed_on", "failed_on", "next_attempt_on", "occurred_on" });

        migrationBuilder.CreateIndex(
            name: "ix_payments_payment_attempts_order_id",
            schema: "payments",
            table: "payment_attempts",
            column: "OrderId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "payments");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "payments");

        migrationBuilder.DropTable(
            name: "payment_attempts",
            schema: "payments");
    }
}
