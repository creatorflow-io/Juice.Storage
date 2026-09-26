# syntax=docker/dockerfile:1
# Build from the repository root:
#   docker build -f src/Juice.Storage.App/Dockerfile \
#     --secret id=github_user,env=GITHUB_PACKAGE_USERNAME --secret id=github_token,env=GITHUB_PACKAGE_TOKEN .
#
# To mount SMB shares from the storage endpoint credentials (Juice.Storage.Local.Linux),
# build with --build-arg INSTALL_CIFS=true and run the container with --cap-add SYS_ADMIN --cap-add DAC_READ_SEARCH.
# Alternatively mount the share on the host and bind it into the container as a local path.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
ARG INSTALL_CIFS=false
RUN if [ "$INSTALL_CIFS" = "true" ]; then apt-get update && apt-get install -y --no-install-recommends cifs-utils && rm -rf /var/lib/apt/lists/*; fi
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "nuget.config", "./"]
COPY ["src/Juice.Storage.Abstractions/Juice.Storage.Abstractions.csproj", "src/Juice.Storage.Abstractions/"]
COPY ["src/Juice.Storage/Juice.Storage.csproj", "src/Juice.Storage/"]
COPY ["src/Juice.Storage.Local/Juice.Storage.Local.csproj", "src/Juice.Storage.Local/"]
COPY ["src/Juice.Storage.Local.Linux/Juice.Storage.Local.Linux.csproj", "src/Juice.Storage.Local.Linux/"]
COPY ["src/Juice.Storage.Local.Windows/Juice.Storage.Local.Windows.csproj", "src/Juice.Storage.Local.Windows/"]
COPY ["src/Juice.Storage.App/Juice.Storage.App.csproj", "src/Juice.Storage.App/"]
RUN --mount=type=secret,id=github_user,env=GITHUB_PACKAGE_USERNAME \
    --mount=type=secret,id=github_token,env=GITHUB_PACKAGE_TOKEN \
    dotnet restore "src/Juice.Storage.App/Juice.Storage.App.csproj" -p:TargetFramework=net10.0
COPY src/ src/
WORKDIR "/src/src/Juice.Storage.App"

FROM build AS publish
RUN dotnet publish "Juice.Storage.App.csproj" -c Release -f net10.0 --no-restore -o /app/publish -p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Juice.Storage.App.dll"]
