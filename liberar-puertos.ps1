<#
  Libera los puertos de CONADIS cuando un proceso quedó vivo después de cerrar Visual Studio o una consola
  (el síntoma: "no se puede acceder a localhost" o "address already in use" al volver a arrancar).

  Solo cierra procesos de CONADIS o de dotnet. Si el puerto lo tiene otro programa, avisa y no lo toca.
  Uso:  powershell -ExecutionPolicy Bypass -File liberar-puertos.ps1
#>
$puertos = @{
    7101 = 'Core API';        5101 = 'Core API (http)'
    7103 = 'Core Web';        5103 = 'Core Web (http)'
    7102 = 'Integracion API'; 5102 = 'Integracion API (http)'
    7122 = 'Portal Web';      5119 = 'Portal Web (http)'
}

$libres = $true
foreach ($puerto in $puertos.Keys | Sort-Object) {
    $conexiones = Get-NetTCPConnection -LocalPort $puerto -State Listen -ErrorAction SilentlyContinue
    foreach ($pid_ in ($conexiones.OwningProcess | Sort-Object -Unique)) {
        $p = Get-Process -Id $pid_ -ErrorAction SilentlyContinue
        if ($null -eq $p) { continue }
        if ($p.ProcessName -like 'CONADIS*' -or $p.ProcessName -eq 'dotnet') {
            Stop-Process -Id $pid_ -Force -ErrorAction SilentlyContinue
            Write-Host ("Puerto {0} ({1}): cerrado {2} (PID {3})" -f $puerto, $puertos[$puerto], $p.ProcessName, $pid_)
        }
        else {
            $libres = $false
            Write-Warning ("Puerto {0} ({1}) lo usa '{2}' (PID {3}), que no es de CONADIS. Ciérrelo usted." -f $puerto, $puertos[$puerto], $p.ProcessName, $pid_)
        }
    }
}
if ($libres) { Write-Host 'Puertos de CONADIS libres.' }
