# Performance & Benchmarks

WebhookEngine ships a reproducible load-test harness under `tests/benchmark/`. The numbers below are captured from a single Apple Silicon laptop running the full stack inside Docker (PostgreSQL 17 + WebhookEngine API + a no-op echo receiver) — they reflect engine throughput, not network or downstream cost.

## Running the harness yourself

```bash
tests/benchmark/run-bench.sh up                   # start postgres + engine + receiver, seed app/endpoint
tests/benchmark/run-bench.sh single 1000 60s      # 1000 events/s for 60s
tests/benchmark/run-bench.sh batch  50 60s        # 50 batches/s × 50 messages = 2500 msg/s
tests/benchmark/run-bench.sh mixed  300 50 60s    # 300 sends/s + 50 lists/s
tests/benchmark/run-bench.sh down                 # tear down + drop volume
```

`run-bench.sh` wraps the [k6](https://k6.io/) Docker image (`grafana/k6`) and writes JSON summaries to `tests/benchmark/results/<scenario>-<timestamp>.json` (gitignored). The scripts under `tests/benchmark/k6/` are plain JS and easy to fork.

The bench compose file lifts the default rate limits (defaults are conservative and would shape the curve before any internal bottleneck appeared) so the benchmark measures the engine, not the rate limiter. See `tests/benchmark/docker-compose.bench.yml`.

## Baseline (v0.1.4) vs after API-key auth cache

The first optimization pass added an in-memory cache for API key prefix → application lookup, replacing a DB query on every public-API request with a 30 s cache hit.

| Scenario | Metric | Baseline | After auth cache | Δ |
|---|---|---|---|---|
| `single-send` 1000/s | sustained | 916 req/s | **999 req/s** | +9 % |
| | p50 | 3.3 ms | **1.75 ms** | -47 % |
| | p95 | **299 ms** | **3.57 ms** | **-99 %** (~84×) |
| | p99 | 509 ms | **8.37 ms** | -98 % (~61×) |
| `batch-send` (50 msg/batch) | p50 | 79 ms | 52.5 ms | -34 % |
| | p95 | 324 ms | **69.8 ms** | -78 % |
| `mixed` (send + list) | p95 | 10 ms | 8.16 ms | -18 % |

Why the gain is so large on `single-send`: every public-API request previously round-tripped to PostgreSQL through `ApplicationRepository.GetByApiKeyPrefixAsync`. Under burst that drove Npgsql's connection pool to recycle aggressively (the original `DISCARD ALL` count was 987 K in 60 s), which in turn made every other query queue behind connection acquisition. The cache eliminates the auth round-trip on the hot path; the dominoes that were toppling because of it stop.

All numbers from a 60 s window on Apple Silicon, 0 % HTTP failures across all scenarios.

## Bottleneck inventory (from `pg_stat_statements`)

After ~220 K messages enqueued during the baseline runs:

| Query | % of DB time | Calls | Mean | Notes |
|---|---|---|---|---|
| `INSERT INTO messages …` | 50.8 % | 222 K | 0.42 ms | Hot path, expected dominant share. |
| `SELECT count(*) FROM messages WHERE app_id = $1` | 12.9 % | 3 K | **7.88 ms** | Pagination count — dashboard list query. Parallel seq scan; could be cached or replaced with `ApproximateRowCount`. |
| `SELECT … FROM endpoints …` | 8.1 % | 222 K | 0.07 ms | Endpoint lookup once per message. Cacheable (memory cache + invalidation on update). |
| `DISCARD ALL` | 4.4 % | **987 K** | 0.01 ms | Npgsql connection-reset noise; suggests pool churn under burst load. Tune `Max Pool Size` and `Connection Idle Lifetime`. |
| Navigation `FOR KEY SHARE` locks (`applications`, `endpoints`, `event_types`) | ~11 % | 700 K | 0.02-0.04 ms | EF Core taking a row-share lock on referenced rows. Often unnecessary when reading; consider `.AsNoTracking()` on hot paths. |
| `SELECT … FROM event_types …` | 3.1 % | 222 K | 0.03 ms | Same caching opportunity as endpoints. |
| API key auth `SELECT … FROM applications …` | 2.8 % | 79 K | 0.07 ms | Validated on every public-API request. Memory cache with short TTL would eliminate this. |
| `WITH next_batch …` (queue dequeue) | 0.6 % | 391 | 2.66 ms | `SKIP LOCKED` query. OK on absolute terms, but 2.66 ms mean is room to improve when batch size grows. |
| `INSERT message_attempts` | 3.1 % | 33 K | 0.17 ms | Fine. |
| `UPDATE messages SET status …` | 1.2 % | 33 K | 0.06 ms | Fine. |

## Configuration findings

- **Default rate limit is too tight for production traffic.** `WebhookEngine:RateLimit` defaults to `PermitLimit=100, TokensPerPeriod=2, QueueLimit=0`. That sustains only 2 req/s per app long-term, with a 100-burst bucket. The bench compose lifts these to 20 000 to measure the engine; the production defaults should be revisited so a self-hosted operator does not hit a wall at 2 req/s.

## Optimization log

| Optimization | Status | Effect |
|---|---|---|
| In-memory cache for API key auth (30 s TTL, invalidated on app update/delete/rotate) | ✅ shipped | single-send p95 -99 %, p99 -98 %, sustained 916→999 req/s |
| Memory cache for endpoint + event-type lookup in the delivery path | planned | drops `SELECT endpoints` / `event_types` from every enqueue |
| Production rate limit defaults | planned | raise floor (e.g. 500 burst, 100/s sustain), document the knob |
| `AsNoTracking` on read-only navigation paths | planned | eliminate `FOR KEY SHARE` locks on ownership rows |
| Connection pool tuning | planned | explicit `Max Pool Size`, `Connection Idle Lifetime`; evaluate `Multiplexing=true` |
| Pagination count short-circuit | planned | cached / approximate counts on dashboard list endpoint |

After each optimization ships, the same three k6 scenarios are re-run and the before/after row is appended to the table above.
