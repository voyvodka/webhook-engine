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

## Baseline (v0.1.4)

| Scenario | Target rate | Sustained | p50 | p95 | p99 |
|---|---|---|---|---|---|
| `single-send` | 1000 req/s | **916 req/s** | 3.3 ms | 299 ms | 509 ms |
| `batch-send` (50 msg / batch) | 2500 msg/s | **2500 msg/s** | 79 ms | 324 ms | — |
| `mixed` (send + list) | 350 req/s | **350 req/s** | 2.7 ms | 10 ms | — |

Measurement details:
- All numbers from a 60 s window on Apple Silicon, 0 % HTTP failures across all scenarios.
- `single-send` p50→p95 jumps **~96×** (3.3 ms → 299 ms). Median is fast; the long tail comes from the bottlenecks inventoried below.
- Light mixed load is comfortably handled — list endpoint p95 stays under 10 ms.

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

## Planned optimizations

These follow as separate PRs so each can be measured in isolation:

1. **Memory cache for API key auth** — drops `SELECT applications` from every request to one per cache TTL.
2. **Memory cache for endpoint + event-type lookup** in the delivery path — drops `SELECT endpoints` / `event_types` from every enqueue.
3. **Production rate limit defaults** — raise the floor to a sensible self-host baseline (e.g. 500 burst, 100/s sustain) and document the knob.
4. **`AsNoTracking` on read-only navigation paths** — eliminate the FOR KEY SHARE locks taken by EF for ownership rows that are never mutated in the same scope.
5. **Connection pool tuning** — explicit `Max Pool Size`, `Connection Idle Lifetime`, evaluate `Multiplexing=true` (Npgsql 9.x).
6. **Pagination count short-circuit** — return cached / approximate counts on the dashboard list endpoint when the client is paging.

After each optimization the same three k6 scenarios are re-run; a before/after table is appended to this document.
