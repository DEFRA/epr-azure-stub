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
dotnet build EprAzureStub.Test
dotnet test --test-modules EprAzureStub.Test/bin/Debug/net10.0/EprAzureStub.Test.dll --no-build
```

## Polling performance data

The runner acquires `/admin/load-test-runs` before posting to
`/admin/load-test-polling`. The polling request initialises one shared catalogue
and the browser session allocations together:

```json
{
  "runId": "a23c8152-2079-4f35-982a-51a137a937d6",
  "directProducerUserCount": 125,
  "complianceSchemeUserCount": 375,
  "registrationYears": [2025, 2026, 2027, 2028, 2029, 2030],
  "changeIntervalSeconds": 300,
  "maximumAdditionalOrganisations": 288
}
```

The initial count is the total across both types. It must be 1–1000; initial
count plus growth cannot exceed 1000. Years must be 2023–2050, growth must be
nonnegative, and the interval must be 1–86400 seconds. Duplicate years are
normalised. Repeating the same request keeps IDs and clocks; a changed
configuration returns 409. GET `/admin/load-test-polling/{runId}` reports the
current catalogue and start time. Legacy session-only initialisation is unchanged.

GET `/waste-organisations/organisations` returns the complete active catalogue.
Outside a polling run it returns the two existing seeded browser organisations,
so downstream refreshes can replace the previous generated catalogue.
Its IDs join detail responses, Account bulk lookups, person recipients, session
lookups and PRN calculations. One extra organisation appears per interval from
initialisation, with missed intervals caught up on the next request and growth
capped. Initial browser session allocations stay fixed. Compliance schemes
share the seeded Account operator; that operator is not a Waste Organisations ID.

Each organisation/year starts its PRN clock on its first PRN request. Accepted
tonnage, outstanding tonnage and status alternate between the fixture values
and a fully met state each interval. Discovery and Account reads do not start
those clocks. Releasing or expiring the run lease clears the catalogue and clocks.

Restrict `/admin/load-test-runs`, `/admin/load-test-sessions` and
`/admin/load-test-polling` to the performance runner at ingress. All state is
process-local: deploy one stub replica until lease and catalogue state are shared.

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
