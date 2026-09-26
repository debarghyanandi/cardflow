FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/Cardflow.Api/Cardflow.Api.csproj src/Cardflow.Api/
RUN dotnet restore src/Cardflow.Api/Cardflow.Api.csproj
COPY src/Cardflow.Api/ src/Cardflow.Api/
RUN dotnet publish src/Cardflow.Api/Cardflow.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Cardflow.Api.dll"]
