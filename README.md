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
  "organisationGrowthIntervalSeconds": 1800,
  "obligationChangeIntervalSeconds": 1800,
  "maximumAdditionalOrganisations": 288
}
```

The initial count is the total across both types. Initial count plus maximum
growth must fit `LoadTest__MaxOrganisationCount` (100000 by default, configurable
1–1000000). This bound applies to session-only allocations too, with count sums
validated before allocation. Years must be 2023–2050, growth must be
nonnegative, and each interval must be 1–86400 seconds. Duplicate years are
normalised. Repeating the same request keeps IDs and clocks; a changed
configuration returns 409. GET `/admin/load-test-polling/{runId}` reports the
current catalogue and start time. Legacy session-only initialisation is unchanged.

GET `/waste-organisations/organisations` returns the complete active catalogue.
Outside a polling run it returns the two existing seeded browser organisations,
so downstream refreshes can replace the previous generated catalogue.
Its IDs join detail responses, Account bulk lookups, person recipients, session
lookups and PRN calculations. One extra organisation appears per interval from
initialisation, with missed intervals caught up on the next request and growth
capped. The growth period defaults to 1800 seconds. Initial browser session
allocations stay fixed, including a 750-organisation pool. Each synthetic compliance scheme has its own Account operator ID, distinct from
its Waste Organisations ID. The scheme and operator share their allocated Companies
House and reference numbers. Every synthetic organisation has unique numbers,
including growth and later runs in the same process. Account lookups use the same
metadata for polling and session-only allocations. The seeded login users and their
contact details are reused for each type.

Each organisation/year starts its PRN clock on its first PRN request. Its independent
change period defaults to 1800 seconds. Numeric obligations increase by phase while
accepted/outstanding tonnage and status remain coherent across met/unmet phases,
so skipping several intervals still produces changed values. Phase zero and legacy
fixture responses stay unchanged. Discovery and Account reads do not start those
clocks. `changeIntervalSeconds` remains a compatibility fallback for either new
period that is omitted. Releasing or expiring the lease clears the catalogue and clocks.

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
