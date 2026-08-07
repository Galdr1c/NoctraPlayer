# Google Cloud Run source-deployment için çok aşamalı build.
#
# ÖNEMLİ: build context'i Noctra.Billing.Api klasörüdür (Cloud Run source
# deployment, --source <dizin> ile verilir ve Dockerfile o dizinde aranır).
# Yerel build:  docker build -t noctra-billing-api -f Noctra.Billing.Api/Dockerfile Noctra.Billing.Api
# Deploy:       gcloud run deploy noctra-billing-api --source Noctra.Billing.Api --region us-central1
# Tek komutlu deploy icin: bash deploy-billing.sh (Windows: .\deploy-billing.ps1)
#
# Not: Always Free katmani yalnizca us-central1/us-east1/us-west1'de uygulanir.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Noctra.Billing.Api.csproj .
RUN dotnet restore Noctra.Billing.Api.csproj
COPY . .
RUN dotnet publish Noctra.Billing.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
# Backend stateless: SQLite/Firestore yok. Play erişimi Cloud Run service
# identity (ADC) üzerinden metadata'dan token alır — private key env'de yok.
EXPOSE 8080
ENTRYPOINT ["dotnet", "Noctra.Billing.Api.dll"]
