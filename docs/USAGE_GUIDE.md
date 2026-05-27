# ScenarioBench Usage Guide

This guide explains how to use ScenarioBench as a benchmark runner, how to read
Grafana, and what is still missing for serious load/performance testing.

## Mental Model

ScenarioBench has three moving parts:

- target system: the app you test, for example the demo API, old Unicorn, or new
  Unicorn;
- runner: `ScenarioBench.Cli`, which generates load through NBomber;
- observability stack: InfluxDB stores metrics, Grafana shows charts.

Grafana does not run tests. It only displays metrics already written by
NBomber. To change the actual test, edit the JSON config and run the CLI again.

## Quick Run

Start metrics UI and demo target:

```bash
docker compose -f examples/docker-compose.observability.yml up -d
docker compose -f examples/docker-compose.demo.yml up -d --build
```

Run benchmark:

```bash
dotnet run --project src/ScenarioBench.Cli -- \
  --config examples/http-smoke.json \
  --infra-config examples/infra/influxdb.json
```

Open Grafana:

```text
http://127.0.0.1:3000/d/scenariobench-nbomber-overview/scenariobench-nbomber-overview
```

Login:

```text
admin / admin
```

Stop:

```bash
docker compose -f examples/docker-compose.demo.yml down
docker compose -f examples/docker-compose.observability.yml down
```

## What You Can Change In Grafana

Grafana is for inspection and visualization.

You can change:

- dashboard time range;
- auto-refresh interval;
- `Target A` dashboard variable;
- `Target B` dashboard variable;
- `Step` dashboard variable, usually `request`;
- panel visualization settings;
- ad-hoc queries in Grafana Explore.

You cannot change the benchmark itself in Grafana.

Grafana cannot change:

- target URL;
- HTTP method/path/body;
- expected status codes;
- request rate;
- test duration;
- Docker containers;
- seed data;
- old/new application version.

Those belong to the JSON config, Docker Compose, and later private scenario
adapter/orchestration.

## Where Test Settings Live

Benchmark settings live in JSON config files, for example:

```text
examples/http-smoke.json
```

Important fields:

```json
{
  "runName": "http-smoke",
  "targets": [
    {
      "name": "local",
      "baseUrl": "http://127.0.0.1:5002"
    }
  ],
  "scenario": {
    "name": "http-smoke",
    "method": "GET",
    "path": "/health",
    "warmupSeconds": 0,
    "loadProfile": {
      "type": "inject",
      "ratePerSecond": 5,
      "durationSeconds": 5
    },
    "timeoutSeconds": 10,
    "expectedStatusCodes": [200],
    "thresholds": {
      "maxFailedRequests": 0,
      "maxP95Ms": 1000
    }
  }
}
```

To test another port, change `baseUrl`.

To change load, change:

- `loadProfile.type`;
- `loadProfile.ratePerSecond`;
- `loadProfile.copies`;
- `loadProfile.durationSeconds`;
- `warmupSeconds`.

Supported load profile types:

- `inject`: fixed request rate;
- `rampingInject`: ramp from zero to the configured request rate;
- `constant`: fixed number of scenario copies/users;
- `rampingConstant`: ramp from zero to the configured number of copies/users.

To test another endpoint, change:

- `method`;
- `path`;
- optional `body`;
- optional `headers`;
- `expectedStatusCodes`.

To define pass/fail rules, change:

- `thresholds.maxFailedRequests`;
- `thresholds.maxFailedPercent`;
- `thresholds.maxP95Ms`;
- `thresholds.maxP99Ms`;
- `thresholds.minRequestsPerSecond`.

## How To Compare Targets

A comparison run should contain two or more targets:

```json
{
  "runName": "visit-update-baseline",
  "targets": [
    {
      "name": "old",
      "baseUrl": "http://127.0.0.1:5002"
    },
    {
      "name": "new",
      "baseUrl": "http://127.0.0.1:5003"
    }
  ],
  "scenario": {
    "name": "update-visit",
    "method": "POST",
    "path": "/some-endpoint",
    "warmupSeconds": 10,
    "loadProfile": {
      "type": "inject",
      "ratePerSecond": 20,
      "durationSeconds": 60
    },
    "timeoutSeconds": 30,
    "expectedStatusCodes": [200],
    "thresholds": {
      "maxFailedRequests": 0,
      "maxP95Ms": 750,
      "maxP99Ms": 1500
    }
  }
}
```

The current Markdown comparison report uses the first target as the baseline.
Put `old` first and `new` second if you want delta vs old.

Grafana comparison works differently:

1. Run benchmark with `--infra-config`.
2. Open dashboard.
3. Select the concrete benchmark in `Run`.
4. Select the scenario in `Scenario`.
5. Select `old` in `Target A`.
6. Select `new` in `Target B`.
7. Select `request` in `Step`.
8. Set time range to cover the run, for example last 30 minutes.

The `Run` value looks like:

```text
http-compare-20260522-122358
```

The target values are the names from config, for example `old`, `new`, or
`local`.

## Run Metadata And Artifacts

ScenarioBench writes run metadata to both local artifacts and InfluxDB tags.

Useful config fields:

- `metadata.environment`: local, local-docker, dev, stage, prod-like.
- `metadata.branch`: source branch or comparison branch label.
- `metadata.commit`: git SHA.
- `metadata.version`: app version, image tag, or benchmark label.
- `metadata.build`: CI build number.
- `metadata.seed`: deterministic data seed version.
- `metadata.tags`: additional public generic tags.
- `targets[].tags`: target-specific tags; in InfluxDB they are prefixed with
  `target_`, for example `target_version`.

Each run writes:

```text
artifacts/<run-id>/config.json
artifacts/<run-id>/infra-config.json
artifacts/<run-id>/infra-config.generated/<target>.json
artifacts/<run-id>/manifest.json
artifacts/<run-id>/comparison.md
artifacts/<run-id>/<target>/result.json
```

`manifest.json` is the machine-readable run summary. It contains the run id,
timestamps, config paths, metadata, scenario, thresholds, every target result,
validation results, and the final pass/fail status.

Example:

```text
http-smoke/local/http-smoke
```

## Scenario Pack Contract

The public extension boundary is `ScenarioBench.Abstractions`.

It defines:

- `IScenarioPack`: a named pack of workflows;
- `IScenarioWorkflow`: prepare and validate hooks for a workflow;
- `ScenarioRunContext` and `ScenarioTargetContext`: public-safe run/target
  inputs;
- `ScenarioValidationResult` and `ValidationIssue`: machine-readable
  correctness results.

The CLI already carries validation results through `result.json`,
`manifest.json`, and `comparison.md`. The built-in HTTP scenario currently has
no private validation hook wired, so its validation array is empty.

Private adapters should keep auth, seed data, endpoint paths, business payloads,
and audit/database checks outside this public repository.

## Metrics Glossary

Latency means request duration: how long the target took to respond.

Common latency metrics:

- `mean`: average latency. Useful, but can hide spikes.
- `p50`: median latency. 50% of requests were faster than this value.
- `p95`: 95th percentile. 95% were faster, 5% were slower.
- `p99`: 99th percentile. Shows tail latency and rare slow responses.
- `max`: slowest observed request.

For user-facing APIs, `p95` and `p99` are usually more important than `mean`.

Request metrics:

- `Requests`: total measured requests.
- `OK`: successful requests that matched expected status codes.
- `Failed`: errors, timeouts, or unexpected status codes.
- `RPS`: requests per second.

Data transfer metrics:

- response/request payload sizes;
- total bytes transferred.

Resource metrics from NBomber:

- runner CPU usage;
- runner working set memory;
- .NET GC metrics;
- thread pool metrics.

Important: NBomber resource metrics describe the runner process, not the target
application. Target-side CPU/DB/container metrics require additional monitoring.

## What Is Enough For MVP

The current stack is enough for:

- proving the runner works;
- basic smoke and baseline tests;
- comparing two targets by latency/RPS/errors;
- storing time-series metrics;
- showing demo dashboards in Grafana;
- keeping local Markdown/JSON artifacts.

This is not yet enough for a strong production-grade performance conclusion.

## What We Still Need For Serious Load/Perf Testing

For serious application benchmarking, add:

- richer scenario definitions, not only one HTTP request;
- loading private scenario packs;
- constant load, stress, spike, and soak profiles;
- per-target scenario params, for example old/new URLs and auth;
- authentication and token refresh support;
- deterministic seed data;
- correctness validation after the load run;
- target-side metrics: app CPU, memory, GC, DB query count, DB CPU/IO;
- container metrics, for example cAdvisor/Prometheus later;
- database metrics, for example PostgreSQL exporter later;
- run metadata, for example commit SHA, branch, environment, DB seed version;
- stable benchmark environment for capacity tests.

For Unicorn specifically, audit correctness validation is mandatory. A run that
is fast but writes missing/wrong audit records should fail.

## Recommended Benchmark Types

Use different test types for different questions.

Smoke:

- short duration;
- low RPS;
- confirms wiring works.

Baseline:

- moderate fixed load;
- compares old vs new under normal expected traffic.

Stress:

- gradually increases load;
- finds where latency/errors degrade.

Spike:

- sudden traffic jump;
- checks short burst behavior.

Soak:

- long run;
- checks memory leaks, connection leaks, DB pool exhaustion, slow degradation.

For the current MVP, smoke and basic baseline are enough. Stress/spike/soak
should come after scenarios and validation are more realistic.

## How To Interpret Comparison

Prefer this order:

1. Failed requests.
2. p95/p99 latency.
3. RPS/throughput.
4. Mean latency.
5. Resource metrics.

If `new` has fewer failures and similar p95/p99, it is probably acceptable.

If `new` has better mean but worse p99, it may feel worse to users.

If `new` is faster but writes wrong audit records, the run failed.

If both targets run on different infrastructure, treat results carefully. The
comparison is only fair when everything except the version under test is as
similar as possible.

## Practical Rules

For fair old/new comparison:

- run old and new in the same mode: both Docker, both local, or both stage;
- use same machine class/resources;
- use same seed data;
- use same scenario config;
- avoid running unrelated heavy work during local tests;
- repeat runs and compare trends, not one lucky result;
- keep artifacts and run metadata.

Local Docker is good for fast relative comparison.

Stage is better for production-like behavior, but only if old/new environments
are actually comparable.

Running against two dev/stage domains is valid:

```json
"targets": [
  {
    "name": "old-stage",
    "baseUrl": "https://old-dev.example.com"
  },
  {
    "name": "new-stage",
    "baseUrl": "https://new-dev.example.com"
  }
]
```

Just remember that network, database, background jobs, and shared environment
noise can affect the result.
