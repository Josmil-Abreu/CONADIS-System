# CONADIS-System — Guía de ejecución y demostración

Proyecto final de Desarrollo de Software III. Son 4 módulos independientes; cada uno tiene su propia solución (`.slnx`) y su propia base de datos.

```
Web (Portal)  ─┐
               ├──►  Integración (SND, 7102)  ──►  Core (7101)
Caja          ─┘
```

Web y Caja **nunca** hablan con el Core directamente: siempre pasan por Integración.

| Módulo | Solución | Base de datos | Puerto |
|---|---|---|---|
| Core | `CONADIS.Core/CONADIS.Core.slnx` | `CONADIS_CoreDB` | https://localhost:7101 |
| Integración | `CONADIS.Integracion/CONADIS.Integracion.slnx` | `CONADIS_IntegracionDB` | https://localhost:7102 (API) + servicio de Windows |
| Web | `CONADIS.Web/CONADIS.Web.slnx` | `CONADIS_WebDB` | Portal https://localhost:7122 (lógica en `CONADIS.Web.Negocio`) |
| Caja | `CONADIS.Caja/CONADIS.Caja.slnx` | `CONADIS_CajaDB` | App Windows Forms `CONADIS.Caja.App` (lógica en `CONADIS.Caja.Negocio`) |

Requisitos: Visual Studio 2026 (.NET 10) y SQL Server LocalDB (`(localdb)\MSSQLLocalDB`).

## 1. Crear las bases de datos

En SSMS o en Visual Studio (Explorador de objetos de SQL Server), ejecute los scripts **en orden numérico**:

1. `CONADIS.Core/BaseDatos/01` … `10`, y para comprobar, `99_PruebaFlujoCompleto.sql`
2. `CONADIS.Integracion/BaseDatos/01` … `07`, y para comprobar, `99_PruebaIntegracion.sql`
3. `CONADIS.Web/BaseDatos/01` … `05`, y para comprobar, `99_PruebaPortal.sql`
4. `CONADIS.Caja/BaseDatos/01` … `05`, y para comprobar, `99_PruebaCaja.sql`

Los scripts se pueden volver a ejecutar sin problema (`CREATE OR ALTER` / `IF NOT EXISTS`).

## 2. Orden de arranque

1. **Core API**: proyecto `CONADIS.Core.Api` → https://localhost:7101/swagger
2. **Integración API**: proyecto `CONADIS.Integracion.Api` → https://localhost:7102/swagger. El estado del sistema se ve en `GET /api/v1/salud`.
3. **Servicio de Integración**: proyecto `CONADIS.Integracion.Servicio`. Entrega la cola de pagos y mensajes al Core y ejecuta las tareas programadas. Se puede correr como consola o instalarlo con `Scripts/instalar-servicio.bat` (como administrador).
4. **Portal Web**: proyecto `CONADIS.Web` (perfil **https**) → https://localhost:7122.
5. **Caja**: proyecto `CONADIS.Caja.App` (Windows Forms). Inicia sesión con `cajero` / `Conadis2026!`, abre la caja y sincroniza sola cada 60 s.

> Cada solución en su **propia ventana de Visual Studio** (o con *Iniciar sin depurar*, Ctrl+F5): al cerrar una solución se detiene lo que estaba ejecutando.
> Si el navegador o Integración no confían en `https://localhost`, ejecute `dotnet dev-certs https --trust`.

Para arrancar varios proyectos a la vez: en Visual Studio, clic derecho en la solución → *Configurar proyectos de inicio* → *Varios proyectos de inicio*.

## 3. Claves de API (encabezado `X-Api-Key`)

| Quién llama | A quién | Clave |
|---|---|---|
| Integración | Core | `conadis-integracion-demo-2026` |
| Core | Integración (firma, notificaciones) | `conadis-core-demo-2026` |
| Portal Web | Integración | `conadis-web-demo-2026` |
| Caja | Integración | `conadis-caja-demo-2026` |
| Institución con convenio | Integración (`verificacion/condicion`) | `institucion-demo-2026` |
| Institución con convenio vencido (debe dar 403) | Integración | `institucion-vencida-2026` |

## 4. Usuarios de demostración

Todos usan la contraseña **`Conadis2026!`**.

- **Core:** `admin`, `analista`, `supervisor`, `medico`, `psicologo`, `social`, `comite1`–`comite3`, `auditor`
- **Portal:** cédula `401-2345678-8` o correo `ciudadano.demo@correo.do`
- **Caja:** `cajero`, `cajero2`, `supervisor.caja`, `admin.caja`. Cajas: `CAJA-01`, `CAJA-02` y `CAJA-SANTIAGO`.

## 5. Simuladores de servicios externos (en Integración)

- **JCE:** una cédula con dígito verificador inválido se trata como inexistente. El prefijo `999` simula una cédula cancelada y el `888` una respuesta lenta, que dispara el *timeout* y el cortacircuito.
- **DGII:** consulta de RNC y recepción de e-CF.
- **MSP:** diagnósticos.
- **Pasarela de pagos:** pagos en línea del portal.
- **Correo:** los mensajes se escriben como archivos en la carpeta `buzon/` del servicio.
- **Firma digital:** ES256 real; la clave se genera en `claves/`, carpeta excluida de git.

## 6. Guion sugerido de demostración

1. Abra `GET /api/v1/salud` en Integración: debe mostrar *Disponible*.
2. **Portal:** inicie sesión con el ciudadano demo, consulte trámites, cree una solicitud y pague en línea.
3. **Caja:** con `cajero`, abra `CAJA-01` y cobre un `DUP-CARNET` en efectivo. Sale un e-CF E32 *Aceptado*; luego sincronice y el pago queda *Aplicado* en el Core.
4. **Resiliencia:** detenga el Core y cobre otra vez. La caja cobra igual, Integración responde 202 y el pago queda en cola. Encienda el Core: el servicio lo entrega y el pago pasa a *Aplicado*.
5. **Contingencia:** detenga Integración y cobre. El e-CF queda en *Contingencia*; al sincronizar se reenvía.
6. **Nota de crédito y cierre:** emita una nota de crédito (E34) autorizada por `supervisor.caja`. Después haga el arqueo ciego y el cierre de caja; muestre el reporte de cierre.
7. **Seguridad:** llame sin clave (responde 401) y con `institucion-vencida-2026` (responde 403). Revise la bitácora en `integ.Bitacora` y los logs en `dbo.Log4NetLog`.

## 7. Pruebas automáticas

En cada solución: *Prueba → Ejecutar todas las pruebas*. Las clases cuyo nombre contiene `BaseDatos` necesitan que los scripts del paso 1 se hayan ejecutado. La cadena de conexión se puede cambiar con estas variables de entorno: `CONADIS_WEB_PRUEBAS`, `CONADIS_CAJA_PRUEBAS`, etc.

## 8. Códigos de error de negocio (`THROW`)

| Módulo | Rango |
|---|---|
| Core | 50001–55xxx |
| Integración | 56xxx |
| Web | 57xxx |
| Caja | 58xxx |

Todos llegan a C# como `ReglaNegocioException`, con el mensaje listo para mostrar al usuario.
