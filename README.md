# CONADIS-System — Guía de ejecución y demostración

Proyecto final de Desarrollo de Software III. Son 4 módulos independientes; cada uno tiene su propia solución (`.slnx`) y su propia base de datos.

```
Web (Portal)  ─┐
               ├──►  Integración (SND, 7102)  ──►  Core (7101)
Caja          ─┘
```

Web y Caja **nunca** hablan con el Core directamente: siempre pasan por Integración.

**Login único — `CONADIS.Inicio`** (programa de Windows, en la raíz). Cada persona entra por ahí y se abre *su* módulo:

| Quién entra | Escribe | Se abre |
|---|---|---|
| Ciudadano | cédula (`401-2345678-8`) o correo | Portal Ciudadano en el navegador (https://localhost:7122) |
| Personal del CONADIS | su usuario (`admin`, `medico`, `comite1`…) | Sistema interno del Core en el navegador (https://localhost:7103), con el menú de su rol |
| Cajero / supervisor de caja | `cajero`, `supervisor.caja`… | Aplicación de Caja (Windows) |

La contraseña la verifica la lógica de cada módulo en su propia base; el módulo recibe un **pase firmado** (HMAC-SHA256, vence en 60 s)
para no pedirla dos veces. La clave del pase (`Inicio:ClaveTraspaso`) debe ser la misma en `CONADIS.Inicio`, `CONADIS.Core.Web`,
`CONADIS.Web` y `CONADIS.Caja.App`. Cada módulo conserva también su propio login.

| Módulo | Solución | Base de datos | Puerto |
|---|---|---|---|
| Core | `CONADIS.Core/CONADIS.Core.slnx` | `CONADIS_CoreDB` | API https://localhost:7101 · Sistema interno del personal https://localhost:7103 |
| Integración | `CONADIS.Integracion/CONADIS.Integracion.slnx` | `CONADIS_IntegracionDB` | https://localhost:7102 (API) + servicio de Windows |
| Web | `CONADIS.Web/CONADIS.Web.slnx` | `CONADIS_WebDB` | Portal https://localhost:7122 (lógica en `CONADIS.Web.Negocio`) |
| Caja | `CONADIS.Caja/CONADIS.Caja.slnx` | `CONADIS_CajaDB` | App Windows Forms `CONADIS.Caja.App` (lógica en `CONADIS.Caja.Negocio`) |

Requisitos: Visual Studio 2026 (.NET 10) y SQL Server LocalDB (`(localdb)\MSSQLLocalDB`).

## 1. Crear las bases de datos

En SSMS o en Visual Studio (Explorador de objetos de SQL Server), ejecute los scripts **en orden numérico**:

1. `CONADIS.Core/BaseDatos/01` … `11`, y para comprobar, `99_PruebaFlujoCompleto.sql`
2. `CONADIS.Integracion/BaseDatos/01` … `09`, y para comprobar, `99_PruebaIntegracion.sql`
3. `CONADIS.Web/BaseDatos/01` … `07`, y para comprobar, `99_PruebaPortal.sql`
4. `CONADIS.Caja/BaseDatos/01` … `07`, y para comprobar, `99_PruebaCaja.sql`

Los scripts se pueden volver a ejecutar sin problema (`CREATE OR ALTER` / `IF NOT EXISTS`).

## 2. Orden de arranque

**Atajo:** doble clic en `iniciar-todo.bat` (raíz del repositorio). Compila las 4 soluciones y abre cada programa en su propia ventana, en el orden correcto; `detener-todo.bat` los cierra todos. Así no hace falta abrir Visual Studio cuatro veces.

**Dentro de Visual Studio:** abra `CONADIS.Todo.slnx` (raíz), que contiene los 4 módulos y el Inicio. Una sola vez: clic derecho en la solución → *Configurar proyectos de inicio* → *Varios proyectos de inicio* → *Iniciar* en `CONADIS.Core.Api`, `CONADIS.Core.Web`, `CONADIS.Integracion.Api`, `CONADIS.Integracion.Servicio`, `CONADIS.Web` y `CONADIS.Inicio` (los web con perfil **https**). **No** inicie `CONADIS.Caja.App`: la abre el Inicio cuando entra un cajero (compílela una vez). Luego F5 arranca todo y aparece la ventana del login único.

Manualmente:

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

## 7. Reportes (RDLC)

Cada transacción tiene su reporte. Los diseños `.rdlc` están en la carpeta `Reportes/` de cada biblioteca de negocio
(incrustados en la DLL) y se generan en **PDF o Excel** con el paquete **ReportViewerCore.NETCore**; cada uno lo alimenta
un SP `ppRpt…`. Todos llevan el mismo encabezado (institución, módulo, filtros) y pie (quién lo generó, cuándo, página).

| Módulo | Comprobante de cada transacción | Documentos | Reportes por período | Dónde |
|---|---|---|---|---|
| Core | `rptComprobanteTransaccion` (bitácora con huella SHA-256) | acuse de solicitud, informe de valoración, certificado/carnet, recibo de pago | expedientes por estado, estadística anonimizada, ingresos, carnets, indicadores, transacciones | Sistema interno: *Mis transacciones*, *Reportes*, expediente, auditoría |
| Caja | `rptComprobanteTransaccionCaja` | e-CF impreso (factura y nota de crédito), cierre de caja | ingresos por servicio, transacciones | App de Caja: *Imprimir comprobante* y *Reportes y transacciones* |
| Portal | `rptComprobanteOperacionPortal` | acuse del trámite con historial, comprobante del pago en línea, carnet digital | mi actividad | Portal: *Mi actividad*, detalle del trámite, pago, mi carnet |
| Integración | `rptComprobanteIntegracion` | — | transacciones de interoperabilidad, pagos recibidos vs aplicados, disponibilidad de servicios | Sistema interno del Core → *Integración* (vía `api/v1/reportes`, ámbito `reportes`) |

Generar un reporte también queda registrado en la bitácora del módulo.

## 8. Prueba de caídas: "tumbar los demás módulos y que no se pierda nada"

| Módulo que queda arriba | Qué hace sin los demás | Dónde espera lo pendiente | Cuándo se sincroniza |
|---|---|---|---|
| **Caja** | Cobra con su base local; e-CF en *Contingencia* (E32 si no puede verificar RNC) | `caja.LineaFactura` (Pendiente) y `caja.Factura` (Contingencia) | Cada 60 s intenta; al subir Integración envía cobros y e-CF |
| **Portal** | Login, borradores y envío de solicitudes, apelaciones, cambios de cita, ayudas técnicas y duplicados; las cuentas nuevas quedan con identidad *Pendiente* | `web.Envio` (bandeja de salida), visible en *Mis trámites* | Cada 30 s; al subir Integración + Core se entregan solos (misma clave de idempotencia: sin duplicados) |
| **Integración** | Acepta cobros de la Caja (202) aunque el Core esté caído | `integ.Mensaje` (outbox) | Reintenta cada 30 s como máximo y **nunca** da un pago por perdido mientras el Core esté caído |
| **Core** | Su sistema interno funciona completo con su base | Notificaciones en el Core | Integración las recoge al volver |

No se dejan en cola, a propósito: el **pago con tarjeta en línea** (necesita la autorización del banco en el momento; el portal
avisa que no hubo cobro y que puede pagar luego o en caja) y la **emisión de carnet** (necesita la firma digital; el analista ve
el aviso y la repite).

**Guion para demostrarlo**
1. Arranque todo y compruebe `GET https://localhost:7102/api/v1/salud` → *Disponible*.
2. **Detenga Core, Integración (API y servicio)**; deje la Caja y el Portal.
3. Caja (`cajero`): abra caja y cobre un `DUP-CARNET` → e-CF *Contingencia*; la barra dice *Sin conexión*.
4. Portal (ciudadano demo): envíe una solicitud → aviso "quedó guardada (envío N.º …)"; en *Mis trámites* aparece *Pendiente de envío*.
5. Verifique en SQL que nada se perdió:
   `SELECT * FROM CONADIS_CajaDB.caja.LineaFactura WHERE EstadoSincronizacion = 'Pendiente'` y
   `SELECT * FROM CONADIS_WebDB.web.Envio WHERE Estado = 'Pendiente'`.
6. **Suba Integración (API + servicio) y el Core.** En 1 a 2 minutos, sin tocar nada:
   la Caja muestra *En línea* y el cobro pasa a *Aplicado*; el e-CF a *Aceptado*; el envío del portal a *Enviado*
   y el trámite aparece en *Mis trámites* (y en el sistema interno del Core).
7. Variante: deje Integración arriba y tumbe solo el Core: los cobros quedan en `integ.Mensaje` (Pendiente) y se aplican al subir el Core.

## 9. Pruebas automáticas

En cada solución: *Prueba → Ejecutar todas las pruebas*. Las clases cuyo nombre contiene `BaseDatos` necesitan que los scripts del paso 1 se hayan ejecutado. La cadena de conexión se puede cambiar con estas variables de entorno: `CONADIS_WEB_PRUEBAS`, `CONADIS_CAJA_PRUEBAS`, etc.

## 10b. Datos de demostración

Para llenar las pestañas de Caja y Core sin hacerlo todo a mano: `CONADIS.Caja/BaseDatos/98_DatosDemo.sql` (cobros, nota de crédito y cierres),
`demo/simular-actividad.ps1` (actividad por la API de Integración) y la guía `demo/DEMO-DATOS.md`.

## 10. Códigos de error de negocio (`THROW`)

| Módulo | Rango |
|---|---|
| Core | 50001–55xxx |
| Integración | 56xxx |
| Web | 57xxx |
| Caja | 58xxx |

Todos llegan a C# como `ReglaNegocioException`, con el mensaje listo para mostrar al usuario.
