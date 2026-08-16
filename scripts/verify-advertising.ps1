$ErrorActionPreference = "Stop"

Write-Host "== Noctra advertising policy / row tests =="
dotnet test .\Noctra.Tests\Noctra.Tests.csproj `
  -c Debug `
  --filter "FullyQualifiedName~Advertising|FullyQualifiedName~AdAwareIncrementalRowCollection"

Write-Host "== Android compile (includes Mobile XAML) =="
dotnet build .\Noctra.Android\Noctra.Android.csproj -c Debug

Write-Host "Advertising verification completed successfully."
