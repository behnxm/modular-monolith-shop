using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shop.Modules.Notifications.Infrastructure.Migrations;

/// <inheritdoc />
internal partial class InitialNotifications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "notifications");

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "notifications",
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
            name: "notifications",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notifications", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "notifications",
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

        migrationBuilder.CreateIndex(
            name: "IX_notifications_CustomerId_CreatedAt",
            schema: "notifications",
            table: "notifications",
            columns: new[] { "CustomerId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "ix_notifications_outbox_messages_processing",
            schema: "notifications",
            table: "outbox_messages",
            columns: new[] { "processed_on", "failed_on", "next_attempt_on", "occurred_on" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "notifications");

        migrationBuilder.DropTable(
            name: "notifications",
            schema: "notifications");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "notifications");
    }
}
