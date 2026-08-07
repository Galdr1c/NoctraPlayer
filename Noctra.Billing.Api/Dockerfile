# Google Cloud Run / herhangi bir Linux konteyner için çok aşamalı build.
# Build:  docker build -t noctra-billing-api -f Noctra.Billing.Api/Dockerfile .
# Deploy: gcloud run deploy noctra-billing-api --source . --region europe-west1 --allow-unauthenticated
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Noctra.Billing.Api/Noctra.Billing.Api.csproj Noctra.Billing.Api/
RUN dotnet restore Noctra.Billing.Api/Noctra.Billing.Api.csproj
COPY Noctra.Billing.Api/ Noctra.Billing.Api/
RUN dotnet publish Noctra.Billing.Api/Noctra.Billing.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV NOCTRA_BILLING_DB_PATH=/app/data/noctra-billing.db
EXPOSE 8080
ENTRYPOINT ["dotnet", "Noctra.Billing.Api.dll"]
