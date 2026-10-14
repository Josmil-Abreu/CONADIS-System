$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
 dotnet build CONADIS.Caja.slnx -c Release
 if($LASTEXITCODE -ne 0){throw 'Falló la compilación'}
 dotnet test tests/CONADIS.Caja.Tests -c Release --no-build --collect:'XPlat Code Coverage'
 if($LASTEXITCODE -ne 0){throw 'Fallaron las pruebas'}
 Get-ChildItem reports/*.rdl | ForEach-Object { [xml](Get-Content -LiteralPath $_.FullName -Raw) | Out-Null; Write-Host "XML válido: $($_.Name)" }
} finally { Pop-Location }
