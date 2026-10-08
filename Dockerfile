# Build the API
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /source

COPY global.json ./
COPY src/ ./src/

RUN dotnet publish src/Cardflow.Api/Cardflow.Api.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

# Run the published API
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Used by the Compose health check later
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Cardflow.Api.dll"]