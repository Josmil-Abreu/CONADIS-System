@echo off
REM ==========================================================================
REM  CONADIS - arranca todo el sistema para probar o hacer la demo.
REM  Cada programa se abre en su propia ventana (cierrela para detenerlo).
REM  Orden: Core -> Integracion (API + servicio) -> Portal Web -> Caja.
REM  Requisitos: bases de datos creadas y "dotnet dev-certs https --trust".
REM ==========================================================================
cd /d "%~dp0"

echo Compilando las 4 soluciones...
dotnet build CONADIS.Core\CONADIS.Core.slnx -v q -nologo || goto error
dotnet build CONADIS.Integracion\CONADIS.Integracion.slnx -v q -nologo || goto error
dotnet build CONADIS.Web\CONADIS.Web.slnx -v q -nologo || goto error
dotnet build CONADIS.Caja\CONADIS.Caja.slnx -v q -nologo || goto error

echo Iniciando Core (https://localhost:7101)...
start "CONADIS Core" cmd /k dotnet run --no-build --project CONADIS.Core\src\CONADIS.Core.Api --launch-profile https
timeout /t 8 /nobreak >nul

echo Iniciando Integracion (https://localhost:7102) y su servicio...
start "CONADIS Integracion API" cmd /k dotnet run --no-build --project CONADIS.Integracion\src\CONADIS.Integracion.Api --launch-profile https
start "CONADIS Integracion Servicio" cmd /k dotnet run --no-build --project CONADIS.Integracion\src\CONADIS.Integracion.Servicio
timeout /t 8 /nobreak >nul

echo Iniciando Portal Web (https://localhost:7122)...
start "CONADIS Portal Web" cmd /k dotnet run --no-build --project CONADIS.Web\src\CONADIS.Web --launch-profile https

echo Iniciando la Caja...
start "CONADIS Caja" dotnet run --no-build --project CONADIS.Caja\src\CONADIS.Caja.App

timeout /t 6 /nobreak >nul
start "" https://localhost:7102/api/v1/salud
start "" https://localhost:7122

echo.
echo Listo. Portal: https://localhost:7122  ^|  Swagger Core: https://localhost:7101/swagger  ^|  Swagger Integracion: https://localhost:7102/swagger
echo Para detener todo, ejecute detener-todo.bat (o cierre cada ventana).
goto :eof

:error
echo.
echo *** La compilacion fallo. Revise el error de arriba. ***
pause
