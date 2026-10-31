using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shop.Modules.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
internal partial class InitialCatalog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "catalog");

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "catalog",
            columns: table => new
            {
                MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                HandlerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ProcessedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inbox_messages", x => new { x.MessageId, x.HandlerName });
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ContractName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Payload = table.Column<string>(type: "jsonb", nullable: false),
                OccurredOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                TraceParent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                TraceState = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                NextAttemptOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ProcessedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                FailedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastError = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "products",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Sku = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                Price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_products", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_catalog_outbox_messages_processing",
            schema: "catalog",
            table: "outbox_messages",
            columns: new[] { "ProcessedOn", "FailedOn", "NextAttemptOn", "OccurredOn" });

        migrationBuilder.CreateIndex(
            name: "ix_catalog_products_keyset",
            schema: "catalog",
            table: "products",
            columns: new[] { "CreatedAt", "Id" });

        migrationBuilder.CreateIndex(
            name: "ix_catalog_products_sku",
            schema: "catalog",
            table: "products",
            column: "Sku",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "products",
            schema: "catalog");
    }
}
