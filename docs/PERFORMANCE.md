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

## Optimization journey: v0.1.4 baseline → after all P1-P3

Three optimization passes shipped:
1. **API key auth cache** (30 s TTL, sync invalidation) — replaces a DB round-trip on every public request.
2. **Delivery lookup cache** (30 s TTL) — same pattern for event-type-by-name, event-type-by-id, and subscribed-endpoints lookups on the public send path.
3. **Npgsql pool tuning** — `Maximum Pool Size=200`, `Minimum Pool Size=10`.

Headline: capacity is roughly **3× higher** before any failure mode shows up. The original target of 1000 req/s now lands well inside the comfortable zone; we can drive 1500 req/s with p95 in single-digit milliseconds.

| Scenario | Metric | Baseline | After auth cache | After all opts |
|---|---|---|---|---|
| `single-send` (sustainable) | rate | 916 req/s | 999 req/s | **1492 req/s** |
| `single-send` 1000/s → 1500/s | p50 | 3.3 ms | 1.75 ms | **1.22 ms** |
|  | p95 | **299 ms** | 3.57 ms | **5.42 ms** |
|  | p99 | 509 ms | 8.37 ms | 56.68 ms (at 1500 req/s) |
| `batch-send` (50 msg/batch) sustainable | rate | 50/s = 2500 msg/s | 50/s | **100/s = 5000 msg/s** |
|  | p50 | 79 ms | 52.5 ms | **25.8 ms** |
|  | p95 | 324 ms | 69.8 ms | **53.5 ms** |
| `mixed` (send + list) sustainable | rate | 350 req/s | 350 req/s | **950 req/s** |
|  | p95 | 10 ms | 8.16 ms | 11.64 ms (at 950 req/s) |

All numbers from a 60 s window on Apple Silicon, 0 % HTTP failures across all scenarios.

### Why the auth cache pass moved the needle most
Every public-API request previously round-tripped to PostgreSQL through `ApplicationRepository.GetByApiKeyPrefixAsync`. Under burst that drove Npgsql's connection pool to recycle aggressively (the original `DISCARD ALL` count was 987 K in 60 s), which in turn made every other query queue behind connection acquisition. The cache eliminates the auth round-trip on the hot path; the dominoes that were toppling because of it stop.

### Why the lookup-cache pass let us push to 1500 / 5000 / 950
With auth out of the way, the next-hottest queries were `SELECT FROM endpoints` and `SELECT FROM event_types` — both fired once per ingested event, so a single batch of 50 messages racked up 100 lookup queries. Caching them on a 30 s TTL drops the lookup count by roughly the cache-hit ratio. The pool tuning then makes sure the remaining queries don't queue.

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
| API-key auth memory cache (30 s TTL, sync invalidation) | ✅ shipped | single-send p95 −99 % (299 ms → 3.57 ms), sustained 916 → 999 req/s |
| Delivery lookup cache (event-type / subscribed endpoints, 30 s TTL) | ✅ shipped | sustained 999 → **1492 req/s**, batch sustainable 2500 → **5000 msg/s** |
| Default rate limit (raised from 2 req/s sustained to 100 req/s) | ✅ shipped | unblocks the bench numbers above for production deployments |
| `AsNoTracking` on hot-path read-only auth lookup | ✅ shipped | small absolute gain; cache already covers ~95 % of calls |
| Npgsql connection pool (`Maximum Pool Size=200`, `Minimum Pool Size=10`) | ✅ shipped | covers the burst headroom now that auth + lookup caches changed the load shape |
| Pagination count short-circuit | deferred | not the bottleneck after the caches landed; revisit when dashboard list latency becomes a complaint |
| EF Core `FOR KEY SHARE` navigation locks | deferred | small marginal gain after caches; revisit alongside any ownership-row hot path |
| Npgsql `Multiplexing=true` | deferred | risky with EF Core; benchmark separately before adopting |

After each optimization ships, the same k6 scenarios are re-run and a new column is added to the journey table above.
