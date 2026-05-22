# Maintainer Guide

This guide is for maintaining the public GitHub repository.

## Suggested Labels

Create these labels in GitHub:

| Label | Purpose |
| --- | --- |
| `bug` | Something is broken. |
| `enhancement` | New feature or improvement. |
| `docs` | Documentation work. |
| `scenario` | Benchmark scenario or workload work. |
| `observability` | Grafana, InfluxDB, metrics, dashboards. |
| `runner` | CLI, config parsing, NBomber execution. |
| `docker` | Docker/Compose target or stack changes. |
| `good first issue` | Small scoped issue for new contributors. |
| `help wanted` | Maintainer wants outside input. |
| `question` | Needs clarification or discussion. |

## Suggested Milestones

Initial milestones:

- `MVP`
- `Serious Benchmarking`
- `Observability`
- `Scenario Packs`
- `Docs`

## Issue Style

Good issues should include:

- what is needed;
- why it matters;
- expected behavior;
- rough implementation notes if known;
- verification criteria.

Example:

```md
## Goal

Add ramping load profile support to benchmark config.

## Why

Baseline runs need constant load, but stress tests need increasing load.

## Acceptance Criteria

- Config supports constant and ramping profiles.
- README includes an example.
- Existing http-smoke config still works.
- `dotnet build ScenarioBench.sln --no-restore -m:1 -v:minimal` passes.
```

## Pull Request Review

Review priority:

1. Correctness and reproducibility.
2. Public repo safety.
3. Clear config/report behavior.
4. Docs and examples.
5. Code style.

