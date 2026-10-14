@echo off
REM ==========================================================================
REM  Instala "CONADIS Integracion" como servicio de Windows.
REM  1) Compile el proyecto en Visual Studio (Compilar > Compilar solucion).
REM  2) Abra la carpeta bin\Debug\net10.0\Scripts en el Explorador de archivos.
REM  3) Clic derecho sobre este archivo > "Ejecutar como administrador".
REM ==========================================================================
set NOMBRE=CONADIS Integracion
set EXE=%~dp0..\CONADIS.Integracion.Servicio.exe

if not exist "%EXE%" (
  echo No se encontro %EXE%
  echo Compile el proyecto y ejecute este archivo desde bin\Debug\net10.0\Scripts.
  pause
  exit /b 1
)

sc.exe create "%NOMBRE%" binPath= "\"%EXE%\"" start= auto DisplayName= "CONADIS - Integracion (SND)"
sc.exe description "%NOMBRE%" "Cola de mensajes, reintentos, notificaciones y procesos diarios de CONADIS."
REM Si el servicio se cae, Windows lo reinicia a los 10 s (tolerancia a fallos)
sc.exe failure "%NOMBRE%" reset= 86400 actions= restart/10000/restart/10000/restart/60000
sc.exe start "%NOMBRE%"

echo.
echo Listo. Puede verlo en services.msc. Los logs quedan en la carpeta logs junto al ejecutable.
pause
