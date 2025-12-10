# 1) Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

COPY DevOpsSyncClient.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /out

# 2) Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /out .
COPY appsettings*.json .

ENTRYPOINT ["dotnet", "DevOpsSyncClient.dll"]
