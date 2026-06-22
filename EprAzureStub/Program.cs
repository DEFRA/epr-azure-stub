using EprAzureStub;
using EprAzureStub.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();
builder.Services.AddCustomTrustStore(); // This must happen before Mongo and Http client connections
builder.ConfigureLoggingAndTracing();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapHealthChecks("/health");
app.MapEndpoints();
app.Run();

namespace EprAzureStub
{
    public partial class Program;
}
