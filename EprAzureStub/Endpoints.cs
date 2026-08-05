using System.Diagnostics.CodeAnalysis;

namespace EprAzureStub;

[ExcludeFromCodeCoverage]
public static class Endpoints
{
    public static void MapEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapOAuthEndpoints();
        app.MapLoadTestControlEndpoints();
        app.MapEprPrnCommonBackendEndpoints();
        app.MapEprBackendAccountMicroserviceEndpoints();
        app.MapWasteOrganisationsEndpoints();
    }
}
