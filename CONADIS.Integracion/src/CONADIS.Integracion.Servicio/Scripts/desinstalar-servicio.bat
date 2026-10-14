@echo off
REM Detiene y elimina el servicio "CONADIS Integracion". Ejecutar como administrador.
set NOMBRE=CONADIS Integracion
sc.exe stop "%NOMBRE%"
timeout /t 3 /nobreak > nul
sc.exe delete "%NOMBRE%"
echo.
echo Servicio eliminado.
pause
