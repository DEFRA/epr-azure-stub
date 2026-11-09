# Build stage image
FROM mcr.microsoft.com/dotnet/sdk@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS build
WORKDIR /src
COPY . .
WORKDIR "/src"
RUN dotnet build EprAzureStub.Test --warnaserror && \
    dotnet test --test-modules EprAzureStub.Test/bin/Debug/net10.0/EprAzureStub.Test.dll --no-build
RUN dotnet publish EprAzureStub -c Release -o /app/publish /p:UseAppHost=false

# Final production image
FROM mcr.microsoft.com/dotnet/aspnet@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee
WORKDIR /app

# Add curl to template, CDP PLATFORM HEALTHCHECK REQUIREMENT
RUN apt update && \
    apt install curl -y && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
EXPOSE 8085
ENTRYPOINT ["dotnet", "EprAzureStub.dll"]
