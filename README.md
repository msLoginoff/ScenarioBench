# ScenarioBench

ScenarioBench is a small generic benchmark runner for comparing the same
scenario across one or more target versions of a system.

It uses NBomber and a JSON config to run one or more scenarios against each
configured target. A scenario can be either a built-in HTTP request or a
scenario-pack workflow. ScenarioBench writes NBomber artifacts plus normalized
JSON and Markdown reports.

## Physical Model

ScenarioBench has two separate parts:

- contract: `ScenarioBench.Abstractions`, public interfaces and DTOs for
  private scenario packs and validation results;
- runner: `ScenarioBench.Cli`, the load generator that runs on the host;
- target: the system under test, normally a Docker/Compose stack.

For this public MVP, the target is a tiny demo HTTP API in Docker. Later, a
private Unicorn scenario repository can use the same runner against real Unicorn
old/new stacks.

## Quick Start

Start the metrics UI and the demo target:

```bash
docker compose -f examples/docker-compose.observability.yml up -d
docker compose -f examples/docker-compose.demo.yml up -d --build
```

Run the benchmark and stream metrics to Grafana:

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

Stop containers:

```bash
docker compose -f examples/docker-compose.demo.yml down
docker compose -f examples/docker-compose.observability.yml down
```

The benchmark also writes local files to:

```text
artifacts/<run-id>/
```

The most important local file is:

```text
artifacts/<run-id>/comparison.md
```

For a fuller workflow and metric guide, see
[docs/USAGE_GUIDE.md](docs/USAGE_GUIDE.md).

## Run Demo Target

Start the demo API container:

```bash
docker compose -f examples/docker-compose.demo.yml up -d --build
```

By default it listens on:

```text
http://127.0.0.1:5002
```

Stop it:

```bash
docker compose -f examples/docker-compose.demo.yml down
```

To expose the container on another host port, set
`SCENARIOBENCH_DEMO_PORT` and make `baseUrl` in the JSON config match it:

```bash
SCENARIOBENCH_DEMO_PORT=5010 docker compose -f examples/docker-compose.demo.yml up -d --build
```

## Run Observability Stack

Start InfluxDB and Grafana:

```bash
docker compose -f examples/docker-compose.observability.yml up -d
```

Grafana is available at:

```text
http://127.0.0.1:3000
```

Default credentials:

```text
admin / admin
```

The stack provisions:

- InfluxDB database `nbomber`;
- Grafana datasource `ScenarioBench InfluxDB`;
- dashboard `ScenarioBench NBomber Overview`.

Stop observability containers:

```bash
docker compose -f examples/docker-compose.observability.yml down
```

Remove observability containers and stored dashboard/metric data:

```bash
docker compose -f examples/docker-compose.observability.yml down -v
```

Or keep containers/volumes and delete only ScenarioBench metrics through the
CLI:

```bash
dotnet run --project src/ScenarioBench.Cli -- \
  --infra-config examples/infra/influxdb.json \
  --clear-metrics
```

Delete one suite/run from Grafana history by `suite_id`:

```bash
dotnet run --project src/ScenarioBench.Cli -- \
  --infra-config examples/infra/influxdb.json \
  --clear-suite http-workflow-suite-20260528-100152
```

## Run Benchmark

```bash
dotnet run --project src/ScenarioBench.Cli -- --config examples/http-smoke.json
```

To also stream NBomber metrics to InfluxDB/Grafana:

```bash
dotnet run --project src/ScenarioBench.Cli -- \
  --config examples/http-smoke.json \
  --infra-config examples/infra/influxdb.json
```

Artifacts are written to:

```text
artifacts/<run-id>/
```

Each target gets its own NBomber reports and normalized `result.json`. The run
root gets `comparison.md`, copied input configs, and `manifest.json`.

`manifest.json` includes each target's performance summary and a validation
results array. The built-in HTTP scenario does not run private validations yet,
so this array is empty until a scenario pack is connected.

A config can contain either the legacy single `scenario` field or a `scenarios`
array. With `scenarios`, one run id becomes a suite id and all scenarios share
the same artifact root and InfluxDB `suite_id` tag.

Use CLI filters to run part of a suite without editing JSON:

```bash
dotnet run --project src/ScenarioBench.Cli -- \
  --config examples/http-workflow-suite.json \
  --scenario demo-multi-step,http-smoke \
  --target old,new
```

List configured scenarios, targets, and scenario-pack workflows:

```bash
dotnet run --project src/ScenarioBench.Cli -- \
  --config examples/http-workflow-suite.json \
  --list-scenarios
```

## Scenario Packs

Scenario packs are external .NET assemblies that reference
`ScenarioBench.Abstractions` and implement `IScenarioPack`. They can be used in
two ways:

- validation-only: keep `scenario.driver` as `http`, run the built-in HTTP
  request, then validate with `IScenarioWorkflow.ValidateAsync(...)`;
- load workflow: set `scenario.driver` to `workflow` and implement
  `IScenarioLoadWorkflow.ExecuteAsync(...)` so the pack owns each load
  iteration.

Configure one with `scenarioPack`:

```json
"scenarioPack": {
  "assemblyPath": "demo-scenario-pack/bin/Debug/net10.0/ScenarioBench.DemoScenarioPack.dll",
  "workflow": "request-count-validation",
  "properties": {
    "minTotalRequests": "1"
  }
}
```

`assemblyPath` is resolved relative to the benchmark config file. If the
assembly contains more than one `IScenarioPack` implementation, set `typeName`.
If the pack exposes multiple workflows, set `workflow`.

The public demo pack proves the loading and validation path:

```bash
dotnet build ScenarioBench.sln --no-restore -m:1 -v:minimal

dotnet run --project src/ScenarioBench.Cli -- \
  --config examples/http-smoke-with-pack.json
```

It also includes a multi-step workflow example:

```bash
dotnet run --project src/ScenarioBench.Cli -- \
  --config examples/http-workflow-with-pack.json
```

That config uses `scenario.driver = "workflow"` and the demo pack executes
`/health` and `/work` inside one measured NBomber iteration.

For a suite with multiple scenarios in one run:

```bash
dotnet run --project src/ScenarioBench.Cli -- \
  --config examples/http-workflow-suite.json
```

The private Unicorn adapter should follow the same shape but keep auth,
endpoint paths, payloads, seed logic, and audit/database checks in the private
repository.

## Example Config

```json
{
  "runName": "http-smoke",
  "metadata": {
    "environment": "local-docker",
    "branch": "demo",
    "version": "demo",
    "seed": "demo"
  },
  "targets": [
    {
      "name": "local",
      "baseUrl": "http://127.0.0.1:5002",
      "tags": {
        "kind": "demo"
      }
    }
  ],
  "scenario": {
    "name": "http-smoke",
    "driver": "http",
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

`scenarioPack.properties` apply to the whole suite. `scenario.properties`
override or extend them for one scenario.

In this example, ScenarioBench sends `GET /health` requests to the demo API at
`http://127.0.0.1:5002`. This endpoint only returns `200 OK` and `ok`; it exists
to prove the runner/config/artifact/report loop before adding real private
Unicorn scenarios.

## Full Demo Flow

```bash
docker compose -f examples/docker-compose.observability.yml up -d
docker compose -f examples/docker-compose.demo.yml up -d --build

dotnet run --project src/ScenarioBench.Cli -- \
  --config examples/http-smoke.json \
  --infra-config examples/infra/influxdb.json
```

Then open:

```text
http://127.0.0.1:3000/d/scenariobench-nbomber-overview/scenariobench-nbomber-overview
```

## Compare Demo

Start two demo targets with different `/work` latency:

```bash
docker compose -f examples/docker-compose.compare.yml up -d --build
```

Run comparison:

```bash
dotnet run --project src/ScenarioBench.Cli -- \
  --config examples/http-compare.json \
  --infra-config examples/infra/influxdb.json
```

The first target in the config is the baseline for `comparison.md`.

Stop compare targets:

```bash
docker compose -f examples/docker-compose.compare.yml down
```

## How To Use Grafana

Grafana is only the metrics viewer. It does not start benchmarks and it does
not change the load-test config. You start/stop targets with Docker Compose,
you run tests with `dotnet run`, and Grafana shows the metrics that NBomber
streamed to InfluxDB.

The dashboard has variables at the top:

- `Suite`: concrete suite/run id, for example `http-compare-20260522-122358`.
- `Scenario`: scenario name inside the run, for example `work-baseline`.
- `Target A`: first target to inspect, for example `old`.
- `Target B`: second target to inspect, for example `new`.
- `Step`: select `request` for built-in HTTP scenarios or `workflow` for
  scenario-pack workflows.

For a one-target smoke run, set both `Target A` and `Target B` to the same
value. For an old/new comparison, select the same `Suite`, then select `old` in
`Target A` and `new` in `Target B`.

The dashboard includes a recent-runs table, a target comparison table, RPS and
p95 delta stats, latency overlay, RPS overlay, and failure RPS overlay. Audit or
business validation results are written to local artifacts (`manifest.json` and
`result.json`); they are not streamed to InfluxDB yet.

What you can change in Grafana:

- selected target/run in dashboard variables;
- selected step;
- time range, for example last 5 minutes or last 1 hour;
- refresh interval;
- panel display settings;
- ad-hoc queries in Explore.

What you cannot change in Grafana:

- target URL;
- request path/method/body;
- load rate;
- duration;
- expected status codes.

Those are controlled by the JSON config, for example
`examples/http-smoke.json`.

Supported load profile types:

- `inject`: inject a fixed request rate;
- `rampingInject`: ramp from zero to the configured request rate;
- `constant`: keep a fixed number of scenario copies/users;
- `rampingConstant`: ramp from zero to the configured number of copies/users.

For `inject` and `rampingInject`, NBomber uses `ratePerSecond` with
`intervalSeconds`: `ratePerSecond: 1` and `intervalSeconds: 2` means one
request per 2-second interval, roughly 0.5 requests/sec.

## Reading Metrics

Latency means how long a request took.

Common latency values:

- `p50`: median request latency. Half of requests were faster than this.
- `p95`: 95th percentile. 95% of requests were faster than this, 5% slower.
- `p99`: 99th percentile. Useful for tail latency and user-visible spikes.
- `mean`: average latency. Useful, but can hide tail problems.
- `max`: slowest observed request.

Requests/RPS:

- `Requests`: number of measured requests.
- `OK`: requests that matched expected status codes.
- `Failed`: requests that returned unexpected status codes or errors.
- `RPS`: requests per second.

For performance comparison, `p95`, `p99`, `failed`, and `RPS` are usually more
important than only average latency.

## Comparison Reports

ScenarioBench currently produces two kinds of output:

- local Markdown/JSON artifacts;
- Grafana/InfluxDB time-series metrics.

Local artifacts are best for stable, shareable run summaries:

```text
artifacts/<run-id>/comparison.md
artifacts/<run-id>/manifest.json
artifacts/<run-id>/<target>/result.json
```

Grafana is best for interactive inspection:

- watch metrics during a run;
- compare target A vs target B on one screen;
- filter by `suite_id`, `scenario`, `target`, and `step`;
- change time range;
- inspect latency/RPS over time;
- use Explore for custom queries.

The current `comparison.md` uses the first target as the baseline. With a
two-target config, use target order intentionally:

```json
"targets": [
  { "name": "old", "baseUrl": "http://127.0.0.1:5002" },
  { "name": "new", "baseUrl": "http://127.0.0.1:5003" }
]
```

Then `old` becomes the baseline and `new` gets delta values.

## Typical Workflow

1. Start observability stack.
2. Start the target system or systems.
3. Edit the JSON benchmark config.
4. Run ScenarioBench.
5. Open `comparison.md` for the compact summary.
6. Open Grafana for interactive charts.
7. Stop target containers.
8. Keep or stop Grafana depending on whether you want to preserve the metrics
   database.

For repeatable comparison, keep everything except the version under test as
similar as possible: same machine, same Docker/Compose settings, same seed data,
same config, same time window, and same background load.

## Repo Boundary

This public repo should stay generic and public-safe. It should not contain
private endpoint paths, auth details, secrets, database schemas, operational
details, or real Unicorn payloads.

`ScenarioBench.Abstractions` is the public contract for that private side. It
defines scenario pack/workflow interfaces, target/run context records, and
validation result DTOs. Unicorn-specific scenarios, auth, seed data, Docker
overrides, and audit validation belong in a private scenario adapter repository
that references those abstractions.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for local setup and pull request
guidelines.

For repository labels, issue style, and maintainer workflow, see
[docs/MAINTAINER_GUIDE.md](docs/MAINTAINER_GUIDE.md).
