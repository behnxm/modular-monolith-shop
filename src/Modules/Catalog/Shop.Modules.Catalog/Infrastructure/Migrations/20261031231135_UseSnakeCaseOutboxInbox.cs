using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shop.Modules.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
internal partial class UseSnakeCaseOutboxInbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "TraceState",
            schema: "catalog",
            table: "outbox_messages",
            newName: "tracestate");

        migrationBuilder.RenameColumn(
            name: "TraceParent",
            schema: "catalog",
            table: "outbox_messages",
            newName: "traceparent");

        migrationBuilder.RenameColumn(
            name: "Payload",
            schema: "catalog",
            table: "outbox_messages",
            newName: "payload");

        migrationBuilder.RenameColumn(
            name: "Attempts",
            schema: "catalog",
            table: "outbox_messages",
            newName: "attempts");

        migrationBuilder.RenameColumn(
            name: "Id",
            schema: "catalog",
            table: "outbox_messages",
            newName: "id");

        migrationBuilder.RenameColumn(
            name: "ProcessedOn",
            schema: "catalog",
            table: "outbox_messages",
            newName: "processed_on");

        migrationBuilder.RenameColumn(
            name: "OccurredOn",
            schema: "catalog",
            table: "outbox_messages",
            newName: "occurred_on");

        migrationBuilder.RenameColumn(
            name: "NextAttemptOn",
            schema: "catalog",
            table: "outbox_messages",
            newName: "next_attempt_on");

        migrationBuilder.RenameColumn(
            name: "LastError",
            schema: "catalog",
            table: "outbox_messages",
            newName: "last_error");

        migrationBuilder.RenameColumn(
            name: "FailedOn",
            schema: "catalog",
            table: "outbox_messages",
            newName: "failed_on");

        migrationBuilder.RenameColumn(
            name: "ContractName",
            schema: "catalog",
            table: "outbox_messages",
            newName: "contract_name");

        migrationBuilder.RenameColumn(
            name: "ProcessedOn",
            schema: "catalog",
            table: "inbox_messages",
            newName: "processed_on");

        migrationBuilder.RenameColumn(
            name: "HandlerName",
            schema: "catalog",
            table: "inbox_messages",
            newName: "handler_name");

        migrationBuilder.RenameColumn(
            name: "MessageId",
            schema: "catalog",
            table: "inbox_messages",
            newName: "message_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "tracestate",
            schema: "catalog",
            table: "outbox_messages",
            newName: "TraceState");

        migrationBuilder.RenameColumn(
            name: "traceparent",
            schema: "catalog",
            table: "outbox_messages",
            newName: "TraceParent");

        migrationBuilder.RenameColumn(
            name: "payload",
            schema: "catalog",
            table: "outbox_messages",
            newName: "Payload");

        migrationBuilder.RenameColumn(
            name: "attempts",
            schema: "catalog",
            table: "outbox_messages",
            newName: "Attempts");

        migrationBuilder.RenameColumn(
            name: "id",
            schema: "catalog",
            table: "outbox_messages",
            newName: "Id");

        migrationBuilder.RenameColumn(
            name: "processed_on",
            schema: "catalog",
            table: "outbox_messages",
            newName: "ProcessedOn");

        migrationBuilder.RenameColumn(
            name: "occurred_on",
            schema: "catalog",
            table: "outbox_messages",
            newName: "OccurredOn");

        migrationBuilder.RenameColumn(
            name: "next_attempt_on",
            schema: "catalog",
            table: "outbox_messages",
            newName: "NextAttemptOn");

        migrationBuilder.RenameColumn(
            name: "last_error",
            schema: "catalog",
            table: "outbox_messages",
            newName: "LastError");

        migrationBuilder.RenameColumn(
            name: "failed_on",
            schema: "catalog",
            table: "outbox_messages",
            newName: "FailedOn");

        migrationBuilder.RenameColumn(
            name: "contract_name",
            schema: "catalog",
            table: "outbox_messages",
            newName: "ContractName");

        migrationBuilder.RenameColumn(
            name: "processed_on",
            schema: "catalog",
            table: "inbox_messages",
            newName: "ProcessedOn");

        migrationBuilder.RenameColumn(
            name: "handler_name",
            schema: "catalog",
            table: "inbox_messages",
            newName: "HandlerName");

        migrationBuilder.RenameColumn(
            name: "message_id",
            schema: "catalog",
            table: "inbox_messages",
            newName: "MessageId");
    }
}
