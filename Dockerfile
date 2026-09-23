# Build stage image
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:2fa828c68761b1b8c23d7662dc134421b9d3b59fe1425fdbc80804e390cdb24d AS build
WORKDIR /src
COPY . .
WORKDIR "/src"
RUN dotnet build EprAzureStub.Test --warnaserror && \
    dotnet test --test-modules EprAzureStub.Test/bin/Debug/net10.0/EprAzureStub.Test.dll --no-build
RUN dotnet publish EprAzureStub -c Release -o /app/publish /p:UseAppHost=false

# Final production image
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:2d584d8147faddb0d678c5748d47953e5b8e18621ed4fb7049a91381d9d7746f
WORKDIR /app

# Add curl to template, CDP PLATFORM HEALTHCHECK REQUIREMENT
RUN apt update && \
    apt install curl -y && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
EXPOSE 8085
ENTRYPOINT ["dotnet", "EprAzureStub.dll"]
