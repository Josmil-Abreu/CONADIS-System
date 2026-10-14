@echo off
REM Detiene todos los programas de CONADIS iniciados con iniciar-todo.bat.
for %%p in (CONADIS.Inicio CONADIS.Core.Api CONADIS.Core.Web CONADIS.Integracion.Api CONADIS.Integracion.Servicio CONADIS.Web CONADIS.Caja.App) do (
    taskkill /IM %%p.exe /F >nul 2>&1 && echo Detenido %%p
)
echo Listo.
