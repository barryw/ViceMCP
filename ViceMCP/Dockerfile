FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy solution and project files
COPY ["ViceMCP.sln", "./"]
COPY ["ViceMCP/ViceMCP.csproj", "ViceMCP/"]
COPY ["ViceMCP.Tests/ViceMCP.Tests.csproj", "ViceMCP.Tests/"]

# Restore dependencies
RUN dotnet restore "ViceMCP/ViceMCP.csproj"

# Copy everything else
COPY . .

# Build the application
WORKDIR "/src/ViceMCP"
RUN dotnet build "./ViceMCP.csproj" -c $BUILD_CONFIGURATION -o /app/build /p:PublishAot=false

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ViceMCP.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:PublishAot=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ViceMCP.dll"]