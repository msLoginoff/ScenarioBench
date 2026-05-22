# Contributing

Thanks for considering a contribution to ScenarioBench.

This project is early, so small focused pull requests are easier to review than
large redesigns.

## Development Setup

Requirements:

- .NET 10 SDK
- Docker-compatible runtime, for example OrbStack or Docker Desktop

Build:

```bash
dotnet restore ScenarioBench.sln
dotnet build ScenarioBench.sln --no-restore -m:1 -v:minimal
```

Run the demo:

```bash
docker compose -f examples/docker-compose.observability.yml up -d
docker compose -f examples/docker-compose.demo.yml up -d --build

dotnet run --project src/ScenarioBench.Cli -- \
  --config examples/http-smoke.json \
  --infra-config examples/infra/influxdb.json
```

## Pull Requests

Before opening a PR:

- keep the change focused;
- update docs when workflow/config changes;
- include verification commands in the PR description;
- avoid committing local artifacts, secrets, IDE files, or private target details.

## Public Repo Boundary

This repository must stay generic and public-safe.

Do not add:

- private endpoints;
- auth secrets or tokens;
- private database schemas;
- real production payloads;
- company-specific infrastructure details.

Project-specific adapters and sensitive benchmark scenarios should live in a
private repository.

