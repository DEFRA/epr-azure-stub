# epr-azure-stub

Stub endpoints for EPR services that currently run in Azure and are needed by services running in CDP.

- [Project structure](#project-structure)
- [Running](#running)
- [Testing](#testing)
- [Docker Compose](#docker-compose)

## Project structure

This is a .NET 10 ASP.NET Core minimal API.

- `EprAzureStub/Program.cs` configures the application, maps `/health`, and registers stub endpoints with `app.MapEndpoints()`.
- `EprAzureStub/Endpoints.cs` contains top-level endpoint registration, with route groups organised by upstream service.
- `EprAzureStub.Test` contains the test project for endpoint coverage.

## Running

```bash
dotnet run --project EprAzureStub --launch-profile EprAzureStub
```

The local launch profile listens on `http://localhost:8085`.

The health check endpoint is available at:

```text
GET /health
```

## Testing

Run the full test suite with:

```bash
dotnet test
```

## Docker Compose

A Docker Compose setup is available in [compose.yml](compose.yml).

It builds and runs this service on port `8085`, alongside local supporting services defined in the compose file.

```bash
docker compose up --build -d
```

### About the licence

The Open Government Licence (OGL) was developed by the Controller of Her Majesty's Stationery Office (HMSO) to enable
information providers in the public sector to license the use and re-use of their information under a common open
licence.

It is designed to encourage use and re-use of information freely and flexibly, with only a few conditions.
