namespace EprAzureStub;

public static class WasteOrganisationStubIds
{
    // LargeProducer and ComplianceScheme come from waste-organisations-stub.
    // Seeded organisation IDs come from epr-local-environment Organisations.ExternalId seed data.
    // Seeded compliance scheme external ID comes from epr-local-environment ComplianceSchemes.ExternalId seed data.
    public const string LargeProducer = "9d3c4d0f-8e5a-4b91-9f7a-2e8d6a1c5f42";
    public const string ComplianceScheme = "c71b2e84-3f9d-47aa-a8c6-5b4ef0139d8e";
    public const string SeededComplianceSchemeOrganisation =
        "94bfc917-b9b6-45d7-847b-e5f500bfe198";
    public const string SeededDirectProducerOrganisation =
        "e2316c5e-d434-41da-8274-494dc0762d20";
    public const string SeededComplianceSchemeExternalId =
        "d93376e3-0681-46be-aeb4-7450a2e784d8";

    public static readonly Guid LargeProducerGuid = Guid.Parse(LargeProducer);
    public static readonly Guid ComplianceSchemeGuid = Guid.Parse(ComplianceScheme);
    public static readonly Guid SeededComplianceSchemeOrganisationGuid = Guid.Parse(
        SeededComplianceSchemeOrganisation
    );
    public static readonly Guid SeededDirectProducerOrganisationGuid = Guid.Parse(
        SeededDirectProducerOrganisation
    );
    public static readonly Guid SeededComplianceSchemeExternalIdGuid = Guid.Parse(
        SeededComplianceSchemeExternalId
    );
}
