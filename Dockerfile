# Small Linux image of the bot. Secrets come from the server's .env at run time (never baked in);
# chat history, goal settings and the Garmin login token live in the /app/data volume.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/MarathonCoach/MarathonCoach.csproj src/MarathonCoach/
RUN dotnet restore src/MarathonCoach/MarathonCoach.csproj
COPY src/ src/
RUN dotnet publish src/MarathonCoach -c Release -o /out --no-restore

# Debian-based runtime: includes tzdata, so TZ (e.g. America/Denver) sets the bot's local time.
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /out ./
# Owned by the non-root 'app' user before VOLUME, so a new named volume is writable by it.
RUN mkdir -p /app/data && chown app /app/data
ENV GARMIN_TOKEN_PATH=/app/data/garmin-token.json
VOLUME /app/data
USER app
ENTRYPOINT ["dotnet", "MarathonCoach.dll"]
