$ErrorActionPreference='Stop'
$moduleRoot=Split-Path $PSScriptRoot -Parent
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Caja__Demo='true'
Push-Location $moduleRoot
try { dotnet run --project src/CONADIS.Caja --urls http://localhost:5088 } finally { Pop-Location }
