# Self-Hosting Guide

This guide covers deploying WebhookEngine in production with Docker Compose. WebhookEngine is designed to run as a single container alongside PostgreSQL -- no additional services required.

## Requirements

| Resource | Minimum | Recommended |
|----------|---------|-------------|
| CPU | 1 core | 2+ cores |
| RAM | 512 MB | 1 GB |
| Disk | 1 GB | 10+ GB (depends on retention) |
| PostgreSQL | 15+ | 17+ |
| Docker | 24+ | Latest |

## Quick Deploy

```bash
git clone https://github.com/voyvodka/webhook-engine.git
cd webhook-engine

# Configure environment
cp docker/.env.example docker/.env
# Edit docker/.env with your settings (see Configuration below)

# Start
docker compose -f docker/docker-compose.yml up -d
```

WebhookEngine starts on port `5100` (configurable). Database migrations run automatically on startup.

## Configuration

All settings are configured via environment variables. Set them in `docker/.env` or pass them directly to Docker Compose.

### Required Settings

| Variable | Default | Description |
|----------|---------|-------------|
| `POSTGRES_PASSWORD` | `webhookengine` | PostgreSQL password. **Change in production.** |
| `ADMIN_EMAIL` | `admin@example.com` | Dashboard admin email. **Change in production.** |
| `ADMIN_PASSWORD` | `changeme` | Dashboard admin password. **Change in production.** |

### Optional Settings

| Variable | Default | Description |
|----------|---------|-------------|
| `APP_PORT` | `5100` | Host port for the application |

### Application Settings

These are passed as environment variables to the WebhookEngine container using .NET's double-underscore notation:

```bash
# Connection string (set automatically in docker-compose.yml)
ConnectionStrings__Default=Host=postgres;Port=5432;Database=webhookengine;...

# Delivery settings
WebhookEngine__Delivery__TimeoutSeconds=30
WebhookEngine__Delivery__BatchSize=10
WebhookEngine__Delivery__PollIntervalMs=1000

# Retry policy
WebhookEngine__RetryPolicy__MaxRetries=7
WebhookEngine__RetryPolicy__BackoffSchedule__0=5
WebhookEngine__RetryPolicy__BackoffSchedule__1=30
WebhookEngine__RetryPolicy__BackoffSchedule__2=120
WebhookEngine__RetryPolicy__BackoffSchedule__3=900
WebhookEngine__RetryPolicy__BackoffSchedule__4=3600
WebhookEngine__RetryPolicy__BackoffSchedule__5=21600
WebhookEngine__RetryPolicy__BackoffSchedule__6=86400

# Circuit breaker
WebhookEngine__CircuitBreaker__FailureThreshold=5
WebhookEngine__CircuitBreaker__CooldownMinutes=5

# Data retention
WebhookEngine__Retention__DeliveredRetentionDays=30
WebhookEngine__Retention__DeadLetterRetentionDays=90

# Dashboard auth
WebhookEngine__DashboardAuth__AdminEmail=admin@example.com
WebhookEngine__DashboardAuth__AdminPassword=changeme
```

## Security Checklist

Before going to production, ensure the following:

### Credentials

- [ ] Change `POSTGRES_PASSWORD` from default
- [ ] Change `ADMIN_EMAIL` and `ADMIN_PASSWORD` from defaults
- [ ] Store secrets in a secrets manager or environment-specific `.env` file (never commit to git)

### Network

- [ ] Put WebhookEngine behind a reverse proxy (nginx, Caddy, Traefik) with TLS
- [ ] Do **not** expose PostgreSQL to the public internet (docker-compose.yml already keeps it internal)
- [ ] Consider firewall rules to restrict access to the dashboard
- [ ] API keys are transmitted as Bearer tokens -- always use HTTPS in production

### Application Security

- [ ] API keys are stored as SHA256 hashes in the database (never in plaintext)
- [ ] Webhook signatures use HMAC-SHA256 with per-application secrets
- [ ] The dashboard uses cookie-based session authentication (HttpOnly, Secure flags)

## Reverse Proxy Setup

WebhookEngine should be behind a reverse proxy in production for TLS termination and additional security.

### Nginx

```nginx
server {
    listen 443 ssl http2;
    server_name webhooks.example.com;

    ssl_certificate     /etc/ssl/certs/webhooks.example.com.pem;
    ssl_certificate_key /etc/ssl/private/webhooks.example.com.key;

    location / {
        proxy_pass http://localhost:5100;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # SignalR WebSocket support
    location /hubs/ {
        proxy_pass http://localhost:5100;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

### Caddy

```
webhooks.example.com {
    reverse_proxy localhost:5100
}
```

Caddy handles TLS automatically via Let's Encrypt.

## PostgreSQL Tuning

The default `docker-compose.yml` includes basic PostgreSQL tuning for webhook workloads:

```
shared_buffers=128MB
effective_cache_size=256MB
work_mem=4MB
max_connections=100
log_min_duration_statement=1000
```

For high-throughput deployments (>100 deliveries/sec), consider:

| Parameter | Default | High-Throughput |
|-----------|---------|-----------------|
| `shared_buffers` | 128MB | 256MB - 1GB (25% of RAM) |
| `effective_cache_size` | 256MB | 1GB - 3GB (75% of RAM) |
| `work_mem` | 4MB | 8MB - 16MB |
| `max_connections` | 100 | 200 |
| `checkpoint_completion_target` | 0.9 | 0.9 |

## Data Retention

WebhookEngine automatically cleans up old data:

- **Delivered messages**: Deleted after 30 days (configurable)
- **Dead-letter messages**: Deleted after 90 days (configurable)
- Cleanup runs daily at **03:00 UTC** in batches to avoid table locks

To adjust retention periods:

```bash
WebhookEngine__Retention__DeliveredRetentionDays=60
WebhookEngine__Retention__DeadLetterRetentionDays=180
```

### Disk Usage Estimates

| Throughput | Monthly Storage (30-day retention) |
|------------|-----------------------------------|
| 1,000 messages/day | ~100 MB |
| 10,000 messages/day | ~1 GB |
| 100,000 messages/day | ~10 GB |

These are rough estimates. Actual usage depends on payload sizes and number of delivery attempts.

## Backups

### PostgreSQL Backups

```bash
# Manual backup
docker compose -f docker/docker-compose.yml exec postgres \
  pg_dump -U webhookengine webhookengine > backup.sql

# Restore
docker compose -f docker/docker-compose.yml exec -i postgres \
  psql -U webhookengine webhookengine < backup.sql
```

For automated backups, consider running `pg_dump` on a cron schedule or using a tool like [pgBackRest](https://pgbackrest.org/).

### Volume Backup

PostgreSQL data is stored in a Docker volume (`pgdata`). Back up the volume directly:

```bash
docker run --rm \
  -v webhook-engine_pgdata:/data \
  -v $(pwd):/backup \
  alpine tar czf /backup/pgdata-backup.tar.gz -C /data .
```

## Monitoring

### Health Check

```bash
curl http://localhost:5100/health
# {"status":"Healthy"}
```

The Docker container includes a built-in health check that polls this endpoint every 30 seconds.

### Prometheus Metrics

Scrape `http://localhost:5100/metrics` for delivery performance metrics:

```yaml
# prometheus.yml
scrape_configs:
  - job_name: webhookengine
    scrape_interval: 15s
    static_configs:
      - targets: ["webhook-engine:8080"]  # Use container name in Docker network
```

Key metrics to monitor:

| Metric | Alert When |
|--------|------------|
| `webhookengine_deliveries_failed` | High failure rate |
| `webhookengine_deadletter_total` | Increasing dead letters |
| `webhookengine_queue_depth` | Queue growing faster than draining |
| `webhookengine_delivery_duration` | p95 latency exceeding timeout |
| `webhookengine_circuit_opened` | Circuits opening frequently |

### Structured Logs

WebhookEngine uses Serilog with JSON output. Logs include correlation context:

```json
{
  "Timestamp": "2026-02-27T10:00:00Z",
  "Level": "Information",
  "Message": "Message delivered successfully",
  "Properties": {
    "MessageId": "msg_abc123",
    "EndpointId": "ep_def456",
    "AttemptNumber": 1,
    "LatencyMs": 142
  }
}
```

Pipe logs to any log aggregator (Loki, Elasticsearch, CloudWatch, etc.) for centralized monitoring.

## Scaling Considerations

WebhookEngine is designed to handle **100-500 deliveries/sec** on a single instance. For most use cases, this is sufficient.

### When to Scale

- Queue depth consistently growing (messages arriving faster than delivery)
- Delivery latency increasing beyond acceptable thresholds
- CPU/memory consistently at capacity

### Scaling Options

1. **Vertical scaling**: Increase container resources (CPU, memory) and PostgreSQL tuning
2. **Multiple instances**: Run multiple WebhookEngine containers pointing at the same PostgreSQL -- the `SKIP LOCKED` queue ensures no duplicate deliveries
3. **Dedicated queue**: For sustained >1000/sec, consider migrating to Redis or RabbitMQ via the `IMessageQueue` interface (requires code changes)

## Upgrading

```bash
cd webhook-engine
git pull
docker compose -f docker/docker-compose.yml up -d --build
```

Database migrations are applied automatically on startup. The application will not start until migrations complete successfully.

## Troubleshooting

### Container won't start

```bash
# Check logs
docker compose -f docker/docker-compose.yml logs webhook-engine

# Common causes:
# - PostgreSQL not ready yet (wait for healthcheck)
# - Invalid connection string
# - Port already in use
```

### Messages stuck in "Sending"

Messages stuck in `Sending` status for more than 5 minutes are automatically recovered by the Stale Lock Recovery worker. Check logs for worker crash indicators.

### High failure rate

1. Check endpoint health in the dashboard
2. Verify the target URL is accessible from the WebhookEngine container
3. Check circuit breaker status -- endpoints with open circuits skip deliveries
4. Review delivery attempt logs for HTTP status codes and error details

### Database connection issues

```bash
# Test PostgreSQL connectivity
docker compose -f docker/docker-compose.yml exec postgres \
  pg_isready -U webhookengine -d webhookengine

# Check PostgreSQL logs
docker compose -f docker/docker-compose.yml logs postgres
```
