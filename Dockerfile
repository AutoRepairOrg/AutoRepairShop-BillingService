# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY src/BillingService.Api/BillingService.Api.csproj                       src/BillingService.Api/
COPY src/BillingService.Application/BillingService.Application.csproj       src/BillingService.Application/
COPY src/BillingService.Domain/BillingService.Domain.csproj                 src/BillingService.Domain/
COPY src/BillingService.Infrastructure/BillingService.Infrastructure.csproj src/BillingService.Infrastructure/

RUN dotnet restore src/BillingService.Api/BillingService.Api.csproj

COPY src/ src/
RUN dotnet publish src/BillingService.Api/BillingService.Api.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "BillingService.Api.dll"]
