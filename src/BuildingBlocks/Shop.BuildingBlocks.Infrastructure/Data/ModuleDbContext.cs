using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.BuildingBlocks.Infrastructure.Inbox;
using Shop.BuildingBlocks.Infrastructure.Outbox;

namespace Shop.BuildingBlocks.Infrastructure.Data;

public abstract class ModuleDbContext : DbContext
{
    private readonly string _schema;

    protected ModuleDbContext(DbContextOptions options, string schema)
        : base(options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        _schema = schema.ToLowerInvariant();
    }

    public string Schema => _schema;

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(_schema);

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages", _schema);
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Id).HasColumnName("id");
            builder.Property(m => m.ContractName).HasColumnName("contract_name").IsRequired().HasMaxLength(200);
            builder.Property(m => m.Payload).HasColumnName("payload").IsRequired().HasColumnType("jsonb");
            builder.Property(m => m.OccurredOn).HasColumnName("occurred_on").IsRequired();
            builder.Property(m => m.TraceParent).HasColumnName("traceparent").HasMaxLength(256);
            builder.Property(m => m.TraceState).HasColumnName("tracestate").HasMaxLength(512);
            builder.Property(m => m.Attempts).HasColumnName("attempts").IsRequired();
            builder.Property(m => m.NextAttemptOn).HasColumnName("next_attempt_on").IsRequired();
            builder.Property(m => m.ProcessedOn).HasColumnName("processed_on");
            builder.Property(m => m.FailedOn).HasColumnName("failed_on");
            builder.Property(m => m.LastError).HasColumnName("last_error");

            builder.HasIndex(m => new { m.ProcessedOn, m.FailedOn, m.NextAttemptOn, m.OccurredOn })
                .HasDatabaseName($"ix_{_schema}_outbox_messages_processing");
        });

        modelBuilder.Entity<InboxMessage>(builder =>
        {
            builder.ToTable("inbox_messages", _schema);
            builder.HasKey(m => new { m.MessageId, m.HandlerName });
            builder.Property(m => m.MessageId).HasColumnName("message_id");
            builder.Property(m => m.HandlerName).HasColumnName("handler_name").IsRequired().HasMaxLength(200);
            builder.Property(m => m.ProcessedOn).HasColumnName("processed_on").IsRequired();
        });
    }
}

public static class ModuleDbContextRegistrationExtensions
{
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string moduleName)
        where TContext : ModuleDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

        var normalizedModuleName = moduleName.ToLowerInvariant();

        services.AddDbContext<TContext>((serviceProvider, options) =>
        {
            var useMigrator = configuration.GetValue<bool>("Database:UseMigratorConnection");
            var connectionString = useMigrator
                ? configuration.GetConnectionString("Migrator") ?? configuration.GetConnectionString(normalizedModuleName)
                : configuration.GetConnectionString(normalizedModuleName)
                  ?? configuration.GetConnectionString($"Modules:{normalizedModuleName}")
                  ?? configuration.GetConnectionString(moduleName);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string for module '{normalizedModuleName}' was not found. " +
                    $"Configure 'ConnectionStrings:{normalizedModuleName}' or 'ConnectionStrings:Migrator'.");
            }

            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", normalizedModuleName);
            });
        });

        services.AddHealthChecks()
            .AddDbContextCheck<TContext>($"db_{normalizedModuleName}", tags: ["ready", "db"]);

        return services;
    }
}
