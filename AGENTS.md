# Stub endpoints for services in Azure

- CDP is the new platform in which EPR services will run.
- Outgoing services currently run in Azure and this repository replicates some services and specific endpoints, which are needed by services in CDP.
- You will find other repositories in the parent folder of this repository on disk.
- Confirm you can see all repositories listed below before starting work.

## Repository references

### [epr-prn-common-backend](https://github.com/DEFRA/epr-prn-common-backend)

Endpoints to replicate are:

- /api/v1/prn/obligationcalculation/{year}
  - The organisation ID is taken from HTTP header `X-EPR-ORGANISATION`
  - Resolve organisation labels to IDs using the `waste-organisations-stub` section below
  - Stub variants:
    - LargeProducer
    - ComplianceScheme

### [epr-backend-account-microservice](https://github.com/DEFRA/epr-backend-account-microservice)

Endpoints to replicate are:

- /api/organisations/person-emails?organisationId={organisationId}&entityTypeCode={entityTypeCode}
  - Resolve organisation labels to IDs using the `waste-organisations-stub` section below and use the resolved ID as the `organisationId` query parameter
  - Stub variants:
    - LargeProducer where `entityTypeCode=DR`
    - ComplianceScheme where `entityTypeCode=CS`
- /api/users/user-organisations?userId={userId}
  - User IDs should be taken from the epr-local-environment seeded users

## Replicating stub endpoints

1. Confirm the required repositories exist in the parent folder before starting:
   - epr-prn-common-backend
   - epr-backend-account-microservice
   - waste-organisations
   - waste-organisations-stub
   - epr-local-environment
2. Find each endpoint within the relevant source repository.
3. Note the required stub variants and make sure you understand the request/response behaviour.
4. Base the stub response structure on the real endpoint implementation.
5. Each stub endpoint path should be prefixed with the source repository name.
   - Example: `/epr-prn-common-backend/api/v1/prn/obligationcalculation/{year}`
   - Example: `/epr-backend-account-microservice/api/organisations/person-emails?...`
6. Ensure you understand each endpoint and how it functions before implementing the stub.

## Project implementation guidance

- This repository is a .NET 10 ASP.NET Core minimal API.
- Keep `EprAzureStub/Endpoints.cs` as the top-level endpoint registration entry point using the `MapEndpoints` extension method.
- Give each upstream service being stubbed its own endpoint extension method, for example `MapEprPrnCommonBackendEndpoints` or `MapEprBackendAccountMicroserviceEndpoints`.
- Keep `EprAzureStub/Program.cs` focused on application setup, such as services, health checks, middleware, and endpoint registration.
- Preserve the existing `/health` endpoint.
- Use minimal API route groups for upstream service prefixes, then map service-local endpoint paths inside the group.
  - Example: `var group = app.MapGroup("/epr-prn-common-backend"); group.MapGet("/api/v1/prn/obligationcalculation/{year}", ...)`
- Stub routes should return deterministic responses based on request path, headers, and query parameters.
- Prefer simple minimal API handlers unless the endpoint logic becomes large enough to justify extracting private helper methods or small response models.
- Use nullable-aware C# and keep response DTOs explicit when they make the stub response shape easier to understand.
- Add or update tests in `EprAzureStub.Test` for each stub variant added.
- Tests should exercise the HTTP surface using `Microsoft.AspNetCore.Mvc.Testing`, including expected status codes, required headers/query parameters, and response body shape.
- Run `dotnet test` before considering endpoint work complete.

## Supporting repository references

### [waste-organisations](https://github.com/DEFRA/waste-organisations)

- Service accepts organisations with a single registration
- Registrations will be added to if they do not already exist for the organisation

### [waste-organisations-stub](https://github.com/DEFRA/waste-organisations-stub)

- This is a stub representation of repository `waste-organisations`
- See [README.md](../waste-organisations/tests/Api.IntegrationTests/Stubs/README.md) for how stubs are generated
- Organisations of interest, keyed by organisation label:
  - LargeProducer - 9d3c4d0f-8e5a-4b91-9f7a-2e8d6a1c5f42
  - ComplianceScheme - c71b2e84-3f9d-47aa-a8c6-5b4ef0139d8e
- Additional stub organisations should also be taken from the epr-local-environment seed data
  - There should be a valid response, using the seed data, for each organisation found in [seed.sql](../epr-local-environment/compose/epr-backend-account-microservice-migrations/seed.sql)
  - The organisation data used can be cross referenced with organisation data matching on organisation ID in [seed.sql](../epr-local-environment/compose/epr-common-data-api-migrations/seed.sql)

### [epr-local-environment](https://github.com/DEFRA/epr-local-environment)

- Full configuration of Azure and CDP services
- See [README.md](../epr-local-environment/README.md) for seeded users
  - The user definitions we need in this stub should be generated from the seeded users
  - The referenced [seed.sql](../epr-local-environment/compose/epr-backend-account-microservice-migrations/seed.sql) contains the definition for each user and we should use that data in the response data of this stub
  - Ensure you can read and understand what users are being seeded

### [waste-obligations-perf-tests](https://github.com/DEFRA/waste-obligations-perf-tests)

- Performance tests for waste-obligations service
- See [config.js](../waste-obligations-perf-tests/scenarios-k6/lib/config.js) for `ORG_IDS`
- All GUIDs should be noted in the waste-organisations-stub guidance above under `Organisations of interest`
  - If there are any missing then please highlight
