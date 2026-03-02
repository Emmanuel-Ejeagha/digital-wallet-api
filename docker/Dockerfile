# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies (layer caching)
COPY src/DigitalWallet.Domain/DigitalWallet.Domain.csproj DigitalWallet.Domain/
COPY src/DigitalWallet.Application/DigitalWallet.Application.csproj DigitalWallet.Application/
COPY src/DigitalWallet.Infrastructure/DigitalWallet.Infrastructure.csproj DigitalWallet.Infrastructure/
COPY src/DigitalWallet.API/DigitalWallet.API.csproj DigitalWallet.API/
COPY Directory.Packages.props .
RUN dotnet restore DigitalWallet.API/DigitalWallet.API.csproj

# Copy everything else and publish
COPY src/ .
WORKDIR /src/DigitalWallet.API
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Install curl for health checks (optional)
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Set environment variable for ASP.NET Core to listen on all interfaces
ENV ASPNETCORE_URLS=http://+:80

ENTRYPOINT ["dotnet", "DigitalWallet.API.dll"]