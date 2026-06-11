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


## Replicating stub endpoints

1. Confirm the required repositories exist in the parent folder before starting:
   - epr-prn-common-backend
   - epr-backend-account-microservice
   - waste-organisations
   - waste-organisations-stub
2. Find each endpoint within the relevant source repository.
3. Note the required stub variants and make sure you understand the request/response behaviour.
4. Base the stub response structure on the real endpoint implementation.
5. Each stub endpoint path should be prefixed with the source repository name.
   - Example: `/epr-prn-common-backend/api/v1/prn/obligationcalculation/{year}`
   - Example: `/epr-backend-account-microservice/api/organisations/person-emails?...`
6. Ensure you understand each endpoint and how it functions before implementing the stub.


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
