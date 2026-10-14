<#
.SYNOPSIS
  Simula actividad real contra la API de Integración con las claves de demostración del README.
  Llena: bitácora y logs de Integración, pagos recibidos/aplicados (y por tanto "Pagos" del Core),
  reportes de Integración y disponibilidad de servicios.

.DESCRIPTION
  Requisitos: PowerShell 7+ (pwsh), Core API (7101), Integración API (7102) y el servicio de Integración arriba.
  Las bases de datos deben estar creadas (README, paso 1). No necesita el certificado de desarrollo
  confiable porque usa -SkipCertificateCheck (solo para https://localhost).

  Los pagos que crea aparecen en el Core con origen CAJA y en "Pagos recibidos vs aplicados"; no tienen factura
  local en la app de Caja (para eso use CONADIS.Caja/BaseDatos/98_DatosDemo.sql o la propia app).

.EXAMPLE
  pwsh ./demo/simular-actividad.ps1
  pwsh ./demo/simular-actividad.ps1 -Pagos 10
#>
param(
    [string]$Integracion = 'https://localhost:7102',
    [ValidateRange(1, 50)][int]$Pagos = 5
)

$ErrorActionPreference = 'Stop'
$base = $Integracion.TrimEnd('/')

# Claves de demostración (README, sección 3)
$claveCaja = 'conadis-caja-demo-2026'
$claveWeb = 'conadis-web-demo-2026'
$claveInstitucion = 'institucion-demo-2026'
$claveVencida = 'institucion-vencida-2026'

function Llamar {
    param([string]$Metodo, [string]$Ruta, [string]$Clave, $Cuerpo = $null, [hashtable]$Cabeceras = @{})
    $h = @{} + $Cabeceras
    if ($Clave) { $h['X-Api-Key'] = $Clave }
    $parametros = @{
        Method = $Metodo; Uri = "$base/$Ruta"; Headers = $h
        SkipCertificateCheck = $true; SkipHttpErrorCheck = $true; TimeoutSec = 30
    }
    if ($null -ne $Cuerpo) { $parametros['Body'] = ($Cuerpo | ConvertTo-Json -Depth 5); $parametros['ContentType'] = 'application/json' }
    try { $r = Invoke-WebRequest @parametros }
    catch {
        Write-Host ("  {0,-4} {1,-55} SIN RESPUESTA ({2})" -f $Metodo, $Ruta, $_.Exception.Message) -ForegroundColor Red
        return $null
    }
    $color = if ([int]$r.StatusCode -lt 300) { 'Green' } elseif ([int]$r.StatusCode -lt 500) { 'Yellow' } else { 'Red' }
    Write-Host ("  {0,-4} {1,-55} {2}" -f $Metodo, $Ruta, [int]$r.StatusCode) -ForegroundColor $color
    return $r
}

function Json($respuesta) {
    if ($null -eq $respuesta -or [string]::IsNullOrWhiteSpace($respuesta.Content)) { return $null }
    try { return $respuesta.Content | ConvertFrom-Json } catch { return $null }
}

Write-Host "`n1) Estado del sistema" -ForegroundColor Cyan
Llamar GET 'api/v1/salud' $null | Out-Null

Write-Host "`n2) Seguridad: sin clave (401) y convenio vencido (403)" -ForegroundColor Cyan
Llamar GET 'api/v1/caja/tasas' $null | Out-Null
Llamar GET 'api/v1/verificacion/condicion/40123456788' $claveVencida | Out-Null
Llamar GET 'api/v1/verificacion/condicion/40123456788' $claveInstitucion | Out-Null

Write-Host "`n3) Caja: tasas y consulta de RNC" -ForegroundColor Cyan
$tasas = Json (Llamar GET 'api/v1/caja/tasas' $claveCaja)
Llamar GET 'api/v1/caja/rnc/401000001' $claveCaja | Out-Null

Write-Host "`n4) Caja: $Pagos cobros con su e-CF" -ForegroundColor Cyan
$catalogo = @(
    @{ Codigo = 'DUP-CARNET'; Monto = 500.00; Medio = 'Efectivo' },
    @{ Codigo = 'COPIA-CERT'; Monto = 300.00; Medio = 'Tarjeta' },
    @{ Codigo = 'CONST-REG'; Monto = 200.00; Medio = 'Transferencia' }
)
$documentos = @('40123456788', '40234567890', '40345678901')
$primero = $null
for ($i = 0; $i -lt $Pagos; $i++) {
    $t = $catalogo[$i % $catalogo.Count]
    $tx = [guid]::NewGuid().ToString()
    $numero = 'E32' + (Get-Date -Format 'MMddHHmmss') + ('{0:D2}' -f $i)     # <= 20 caracteres
    $fecha = (Get-Date).ToString('o')
    $pago = @{
        transaccionId = $tx; origen = 'CAJA'; numeroComprobante = $numero; tasaCodigo = $t.Codigo; cantidad = 1
        total = $t.Monto; medioPago = $t.Medio; documento = $documentos[$i % $documentos.Count]
        nombrePagador = "Cliente de demostración $($i + 1)"; numeroOrden = $null; referencia = "DEMO-$i"
        caja = 'CAJA-01'; cajero = 'cajero'; fechaPago = $fecha
    }
    $r = Llamar POST 'api/v1/caja/pagos' $claveCaja $pago @{ 'X-Usuario-Final' = 'cajero:cajero' }
    if ($i -eq 0 -and $r) { $primero = $pago }

    $ecf = @{
        encf = $numero; tipoEcf = '32'; rncEmisor = '401000001'; documentoComprador = $pago.documento
        total = $t.Monto; itbis = 0; fechaEmision = $fecha
    }
    Llamar POST 'api/v1/caja/ecf' $claveCaja $ecf | Out-Null
}

if ($primero) {
    Write-Host "`n5) Idempotencia: reenviar el primer cobro no debe duplicarlo (esNuevo = false)" -ForegroundColor Cyan
    $r = Llamar POST 'api/v1/caja/pagos' $claveCaja $primero @{ 'X-Usuario-Final' = 'cajero:cajero' }
    $j = Json $r
    if ($j) { Write-Host ("      esNuevo = {0}, estado = {1}" -f $j.esNuevo, $j.estado) }
    Llamar GET "api/v1/caja/pagos/$($primero.transaccionId)" $claveCaja | Out-Null
}

Write-Host "`n6) Portal: catálogos e identidad en la JCE" -ForegroundColor Cyan
Llamar GET 'api/v1/portal/catalogos/centros' $claveWeb | Out-Null
Llamar GET 'api/v1/portal/catalogos/tasas' $claveWeb | Out-Null
Llamar GET 'api/v1/portal/identidad/40123456788' $claveWeb | Out-Null

Write-Host "`nListo. En 1 o 2 minutos el servicio de Integración entrega los cobros al Core (pasan a 'Aplicado')." -ForegroundColor Cyan
Write-Host "Revise: $base/api/v1/salud · Core > Integración > Pagos recibidos · SELECT * FROM integ.Bitacora ORDER BY 1 DESC;"
