# Build the API
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /source

# Restore from the project files only, so this layer stays cached until a .csproj changes
COPY global.json ./
COPY src/Cardflow.Api/Cardflow.Api.csproj src/Cardflow.Api/
COPY src/Cardflow.App/Cardflow.App.csproj src/Cardflow.App/
COPY src/Cardflow.Domain/Cardflow.Domain.csproj src/Cardflow.Domain/
COPY src/Cardflow.Infrastructure/Cardflow.Infrastructure.csproj src/Cardflow.Infrastructure/
RUN dotnet restore src/Cardflow.Api/Cardflow.Api.csproj

COPY src/ ./src/
RUN dotnet publish src/Cardflow.Api/Cardflow.Api.csproj \
    --no-restore \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

# Run the published API
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Used by the Compose liveness check
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Cardflow.Api.dll"]
