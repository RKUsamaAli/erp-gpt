# Build context is this directory (api/ErpGpt.GraphQLApi) — the project has no
# ProjectReferences, so nothing outside it is needed to compile.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore before copying the source so edits to .cs files reuse the package
# layer instead of re-downloading NuGet on every push.
COPY ErpGpt.GraphQLApi.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Render's default PORT is 10000, and Program.cs binds to $PORT when the host
# sets it. This keeps `docker run -p 10000:10000` working on its own too.
ENV ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000

# Non-root user shipped in the .NET base images.
USER $APP_UID

ENTRYPOINT ["dotnet", "ErpGpt.GraphQLApi.dll"]
