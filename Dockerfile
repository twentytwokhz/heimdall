# Stage 1: build the embedded console SPA (src/Heimdall.Ui) into a self-contained dist.
FROM node:26-alpine AS ui-build
WORKDIR /ui
COPY src/Heimdall.Ui/package.json src/Heimdall.Ui/package-lock.json ./
RUN npm ci
COPY src/Heimdall.Ui/ ./
# build:docker overrides the dev outDir (which points at ../Heimdall.Api) so the build stays inside
# this stage, regardless of how the default build script is structured.
RUN npm run build:docker

# Stage 2: restore + publish the .NET gateway.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props Heimdall.slnx ./
COPY src ./src
COPY tests ./tests

RUN dotnet restore Heimdall.slnx
RUN dotnet publish src/Heimdall.Api/Heimdall.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Stage 3: runtime image = published gateway + the console served from wwwroot/console.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=ui-build /ui/dist ./wwwroot/console

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

USER 1000:1000
ENTRYPOINT ["dotnet", "Heimdall.Api.dll"]
