# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base

WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
# Force x64 platform for gRPC tools compatibility across ARM64 and x64 systems
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["SmartFleet.Telemetry/SmartFleet.Telemetry.csproj", "SmartFleet.Telemetry/"]
RUN dotnet restore "./SmartFleet.Telemetry/SmartFleet.Telemetry.csproj"
COPY . .
WORKDIR "/src/SmartFleet.Telemetry"
RUN dotnet build "./SmartFleet.Telemetry.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SmartFleet.Telemetry.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SmartFleet.Telemetry.dll"]