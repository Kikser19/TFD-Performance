# Multi-stage, production-shaped build (architecture-guide §8): the same image is
# intended to later become a Helm deployment without being rewritten.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached layer) using only the project file.
COPY src/IdentityService.csproj ./
RUN dotnet restore

# Then copy the rest and publish.
COPY src/ ./
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app ./

# Run as the non-root user baked into the aspnet image.
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "IdentityService.dll"]
