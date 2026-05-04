using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;

namespace WebhookEngine.Infrastructure.Data;

public class WebhookDbContext : DbContext
{
    public WebhookDbContext(DbContextOptions<WebhookDbContext> options) : base(options)
    {
    }

    public DbSet<Application> Applications => Set<Application>();
    public DbSet<EventType> EventTypes => Set<EventType>();
    public DbSet<Endpoint> Endpoints => Set<Endpoint>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageAttempt> MessageAttempts => Set<MessageAttempt>();
    public DbSet<EndpointHealth> EndpointHealths => Set<EndpointHealth>();
    public DbSet<DashboardUser> DashboardUsers => Set<DashboardUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Applications
        modelBuilder.Entity<Application>(entity =>
        {
            entity.ToTable("applications");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.ApiKeyPrefix).HasColumnName("api_key_prefix").HasMaxLength(20).IsRequired();
            entity.Property(e => e.ApiKeyHash).HasColumnName("api_key_hash").HasMaxLength(64).IsRequired();
            entity.Property(e => e.SigningSecret).HasColumnName("signing_secret").HasMaxLength(64).IsRequired();
            entity.Property(e => e.RetryPolicyJson).HasColumnName("retry_policy").HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.IdempotencyWindowMinutes)
                .HasColumnName("idempotency_window_minutes")
                .HasDefaultValue(1440);

            entity.HasIndex(e => e.ApiKeyPrefix).IsUnique();
        });

        // Event Types
        modelBuilder.Entity<EventType>(entity =>
        {
            entity.ToTable("event_types");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.AppId).HasColumnName("app_id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.SchemaJson).HasColumnName("schema_json").HasColumnType("jsonb");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived").HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

            entity.HasIndex(e => e.AppId).HasDatabaseName("idx_event_types_app_id");
            entity.HasIndex(e => new { e.AppId, e.Name }).IsUnique();

            entity.HasOne(e => e.Application).WithMany(a => a.EventTypes).HasForeignKey(e => e.AppId).OnDelete(DeleteBehavior.Cascade);
        });

        // Endpoints
        modelBuilder.Entity<Endpoint>(entity =>
        {
            entity.ToTable("endpoints");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.AppId).HasColumnName("app_id");
            entity.Property(e => e.Url).HasColumnName("url").HasMaxLength(2048).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasConversion<string>().HasDefaultValue(EndpointStatus.Active);
            entity.Property(e => e.CustomHeadersJson).HasColumnName("custom_headers").HasColumnType("jsonb").HasDefaultValueSql("'{}'");
            entity.Property(e => e.SecretOverride).HasColumnName("secret_override").HasMaxLength(64);
            entity.Property(e => e.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb").HasDefaultValueSql("'{}'");
            entity.Property(e => e.TransformExpression).HasColumnName("transform_expression").HasMaxLength(4096);
            entity.Property(e => e.TransformEnabled).HasColumnName("transform_enabled").HasDefaultValue(false);
            entity.Property(e => e.TransformValidatedAt).HasColumnName("transform_validated_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

            entity.HasIndex(e => e.AppId).HasDatabaseName("idx_endpoints_app_id");
            entity.HasIndex(e => new { e.AppId, e.Status }).HasDatabaseName("idx_endpoints_status");

            entity.HasOne(e => e.Application).WithMany(a => a.Endpoints).HasForeignKey(e => e.AppId).OnDelete(DeleteBehavior.Cascade);

            // Many-to-many with EventTypes
            entity.HasMany(e => e.EventTypes).WithMany(et => et.Endpoints)
                .UsingEntity("endpoint_event_types",
                    l => l.HasOne(typeof(EventType)).WithMany().HasForeignKey("event_type_id").OnDelete(DeleteBehavior.Cascade),
                    r => r.HasOne(typeof(Endpoint)).WithMany().HasForeignKey("endpoint_id").OnDelete(DeleteBehavior.Cascade));
        });

        // Messages
        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.AppId).HasColumnName("app_id");
            entity.Property(e => e.EndpointId).HasColumnName("endpoint_id");
            entity.Property(e => e.EventTypeId).HasColumnName("event_type_id");
            entity.Property(e => e.EventId).HasColumnName("event_id").HasMaxLength(64);
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
            entity.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasConversion<string>().HasDefaultValue(MessageStatus.Pending);
            entity.Property(e => e.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
            entity.Property(e => e.MaxRetries).HasColumnName("max_retries").HasDefaultValue(7);
            entity.Property(e => e.ScheduledAt).HasColumnName("scheduled_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.LockedAt).HasColumnName("locked_at");
            entity.Property(e => e.LockedBy).HasColumnName("locked_by").HasMaxLength(64);
            entity.Property(e => e.DeliveredAt).HasColumnName("delivered_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

            // Queue polling index (critical for performance)
            entity.HasIndex(e => e.ScheduledAt)
                .HasDatabaseName("idx_messages_queue")
                .HasFilter("status = 'Pending'");

            entity.HasIndex(e => new { e.AppId, e.EndpointId }).HasDatabaseName("idx_messages_app_endpoint");
            entity.HasIndex(e => new { e.AppId, e.Status }).HasDatabaseName("idx_messages_app_status");
            entity.HasIndex(e => new { e.AppId, e.EventTypeId }).HasDatabaseName("idx_messages_app_event_type");
            entity.HasIndex(e => new { e.AppId, e.CreatedAt }).HasDatabaseName("idx_messages_created_at").IsDescending(false, true);
            entity.HasIndex(e => e.LockedAt).HasDatabaseName("idx_messages_stale_locks").HasFilter("status = 'Sending' AND locked_at IS NOT NULL");
            entity.HasIndex(e => new { e.AppId, e.IdempotencyKey }).HasDatabaseName("idx_messages_app_idempotency").HasFilter("idempotency_key IS NOT NULL");

            entity.HasOne(e => e.Application).WithMany(a => a.Messages).HasForeignKey(e => e.AppId);
            entity.HasOne(e => e.Endpoint).WithMany(ep => ep.Messages).HasForeignKey(e => e.EndpointId);
            entity.HasOne(e => e.EventType).WithMany().HasForeignKey(e => e.EventTypeId);
        });

        // Message Attempts
        modelBuilder.Entity<MessageAttempt>(entity =>
        {
            entity.ToTable("message_attempts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.MessageId).HasColumnName("message_id");
            entity.Property(e => e.EndpointId).HasColumnName("endpoint_id");
            entity.Property(e => e.AttemptNumber).HasColumnName("attempt_number");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasConversion<string>();
            entity.Property(e => e.StatusCode).HasColumnName("status_code");
            entity.Property(e => e.RequestHeadersJson).HasColumnName("request_headers").HasColumnType("jsonb");
            entity.Property(e => e.ResponseBody).HasColumnName("response_body");
            entity.Property(e => e.Error).HasColumnName("error");
            entity.Property(e => e.LatencyMs).HasColumnName("latency_ms");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

            entity.HasIndex(e => e.MessageId).HasDatabaseName("idx_attempts_message_id");
            entity.HasIndex(e => new { e.EndpointId, e.Status, e.CreatedAt }).HasDatabaseName("idx_attempts_endpoint_status").IsDescending(false, false, true);

            entity.HasOne(e => e.Message).WithMany(m => m.Attempts).HasForeignKey(e => e.MessageId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Endpoint).WithMany().HasForeignKey(e => e.EndpointId);
        });

        // Endpoint Health
        modelBuilder.Entity<EndpointHealth>(entity =>
        {
            entity.ToTable("endpoint_health");
            entity.HasKey(e => e.EndpointId);
            entity.Property(e => e.EndpointId).HasColumnName("endpoint_id");
            entity.Property(e => e.CircuitState).HasColumnName("circuit_state").HasMaxLength(20).HasConversion<string>().HasDefaultValue(CircuitState.Closed);
            entity.Property(e => e.ConsecutiveFailures).HasColumnName("consecutive_failures").HasDefaultValue(0);
            entity.Property(e => e.LastFailureAt).HasColumnName("last_failure_at");
            entity.Property(e => e.LastSuccessAt).HasColumnName("last_success_at");
            entity.Property(e => e.CooldownUntil).HasColumnName("cooldown_until");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.Endpoint).WithOne(ep => ep.Health).HasForeignKey<EndpointHealth>(e => e.EndpointId).OnDelete(DeleteBehavior.Cascade);
        });

        // Dashboard Users
        modelBuilder.Entity<DashboardUser>(entity =>
        {
            entity.ToTable("dashboard_users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Role).HasColumnName("role").HasMaxLength(20).HasDefaultValue("admin");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.LastLoginAt).HasColumnName("last_login_at");

            entity.HasIndex(e => e.Email).IsUnique();
        });
    }
}
