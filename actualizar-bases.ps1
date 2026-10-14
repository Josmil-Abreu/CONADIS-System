<#
  Crea o actualiza las 4 bases de datos de CONADIS corriendo todos sus scripts en orden, de una sola vez.
  Es seguro repetirlo: los scripts usan CREATE OR ALTER / IF NOT EXISTS y no borran datos.

  Uso (desde la raíz del repositorio, o con la ruta completa del archivo):
    powershell -ExecutionPolicy Bypass -File actualizar-bases.ps1              # 01..97 de los 4 módulos
    powershell -ExecutionPolicy Bypass -File actualizar-bases.ps1 -Demo        # además 98_DatosDemo (Caja)
    powershell -ExecutionPolicy Bypass -File actualizar-bases.ps1 -Pruebas     # además los 99_Prueba* (no dejan datos)
    powershell -ExecutionPolicy Bypass -File actualizar-bases.ps1 -Modulo Caja # solo un módulo

  Requisito: sqlcmd (viene con SQL Server / SSMS; si falta: winget install Microsoft.Sqlcmd).
#>
param(
    [string]$Servidor = '(localdb)\MSSQLLocalDB',
    [ValidateSet('Todos', 'Core', 'Integracion', 'Web', 'Caja')][string]$Modulo = 'Todos',
    [switch]$Demo,
    [switch]$Pruebas
)

$ErrorActionPreference = 'Continue'   # los errores de sqlcmd se revisan con $LASTEXITCODE
$raiz = $PSScriptRoot

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    Write-Host 'No se encontró sqlcmd. Instálelo con:  winget install Microsoft.Sqlcmd  y abra una consola nueva.' -ForegroundColor Red
    exit 1
}

# Orden obligatorio: el Core primero (los demás no dependen de su base, pero así coincide con el README).
$modulos = 'Core', 'Integracion', 'Web', 'Caja'
if ($Modulo -ne 'Todos') { $modulos = @($Modulo) }

$errores = 0
foreach ($m in $modulos) {
    $carpeta = Join-Path $raiz "CONADIS.$m\BaseDatos"
    $scripts = Get-ChildItem $carpeta -Filter '*.sql' | Sort-Object Name | Where-Object {
        $n = [int]($_.Name.Substring(0, 2))
        ($n -lt 98) -or ($n -eq 98 -and $Demo) -or ($n -eq 99 -and $Pruebas)
    }

    Write-Host "`n=== $m ($($scripts.Count) scripts) ===" -ForegroundColor Cyan
    foreach ($s in $scripts) {
        # -b: se detiene si falla · -f 65001: UTF-8 (acentos y ñ) · -E: usuario de Windows
        # -I: QUOTED_IDENTIFIER ON, como SSMS (sin esto fallan los índices filtrados y los SP quedan mal creados)
        $salida = & sqlcmd -S $Servidor -E -I -b -f 65001 -i $s.FullName 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  OK     $($s.Name)" -ForegroundColor Green
        }
        else {
            $errores++
            Write-Host "  ERROR  $($s.Name)" -ForegroundColor Red
            $salida | Select-Object -Last 8 | ForEach-Object { Write-Host "         $_" -ForegroundColor DarkRed }
            Write-Host "  Se detiene ${m}: los scripts siguientes dependen de este." -ForegroundColor Yellow
            break
        }
    }
}

if ($errores -eq 0) { Write-Host "`nListo: bases al día." -ForegroundColor Green }
else { Write-Host "`nTerminó con $errores error(es). Revise el mensaje de arriba." -ForegroundColor Red; exit 1 }
