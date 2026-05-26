# syntax=docker/dockerfile:1.7

# ---- Build stage --------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files first so `dotnet restore` can be cached separately
# from the actual source. Layer is invalidated only when a project file changes.
COPY Domain/Domain.csproj                   Domain/
COPY Persistence/Persistence.csproj         Persistence/
COPY Application/Application.csproj         Application/
COPY Infrastructure/Infrastructure.csproj   Infrastructure/
COPY API/API.csproj                          API/

RUN dotnet restore API/API.csproj

# Copy the rest of the source and publish.
COPY Domain/         Domain/
COPY Persistence/    Persistence/
COPY Application/    Application/
COPY Infrastructure/ Infrastructure/
COPY API/            API/

RUN dotnet publish API/API.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ---- Runtime stage ------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Default to listening on 8080 inside the container; compose maps host:5000 → 8080.
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

# Drop privileges AFTER COPY so the unprivileged user doesn't need write
# access to /app. The base aspnet image ships an `app` user (uid 1654).
USER app
EXPOSE 8080

ENTRYPOINT ["dotnet", "API.dll"]
