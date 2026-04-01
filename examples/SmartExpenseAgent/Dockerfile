FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo

# Copy solution-level files first (needed for restore to work correctly)
COPY Directory.Build.props .
COPY PipelogiqSdk.sln .

# Copy csproj files for layer-cached restore
COPY src/Pipelogiq.Sdk/PipelogiqSDK.csproj          src/Pipelogiq.Sdk/
COPY examples/SmartExpenseAgent/SmartExpenseAgent.csproj examples/SmartExpenseAgent/

RUN dotnet restore examples/SmartExpenseAgent/SmartExpenseAgent.csproj

# Copy source and publish
COPY src/  src/
COPY examples/SmartExpenseAgent/ examples/SmartExpenseAgent/

RUN dotnet publish examples/SmartExpenseAgent/SmartExpenseAgent.csproj \
    -c Release -o /app

# ── Runtime ──────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENTRYPOINT ["dotnet", "SmartExpenseAgent.dll"]
