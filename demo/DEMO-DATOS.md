# Cómo llenar las pestañas de Caja y Core con datos de demostración

Todos los usuarios usan la contraseña `Conadis2026!` (README, sección 4). Los pasos están ordenados de menos a más trabajo.
Con las bases y los servicios arriba (`iniciar-todo.bat`), haga esto en orden.

## 1. Caja: datos listos con SQL (1 minuto)

Abra `CONADIS.Caja/BaseDatos/98_DatosDemo.sql` en SSMS o en Visual Studio y ejecútelo.

Crea dos sesiones cerradas (`CAJA-01` con `cajero` y `CAJA-02` con `cajero2`): cobros en efectivo, tarjeta y transferencia,
una nota de crédito E34, un cierre sin diferencias y otro con un faltante de RD$10 justificado.

Después abra la app de Caja (entre por CONADIS.Inicio como `supervisor.caja`): el sincronizador envía los cobros y e-CF
pendientes y pasan a *Aplicado* / *Aceptado*. Dónde se ve:

| Pestaña de Caja | Qué muestra |
|---|---|
| Reportes y transacciones | Ingresos por servicio, transacciones del período, *Mi actividad (PDF)* |
| Comprobantes de la sesión / reimpresión | e-CF E32 y nota de crédito E34 en PDF |
| Cierre de caja | Reporte de cierre de cada sesión |
| Barra de estado | *En línea* y pendientes de sincronizar |

Para más movimiento, haga cobros a mano: `cajero` → abrir `CAJA-01` → cobrar `DUP-CARNET`, `COPIA-CERT` o `CONST-REG`.

## 2. Integración y pagos del Core: simulador por API (1 minuto)

Con PowerShell 7 (`pwsh`), desde la raíz del repositorio:

```powershell
pwsh ./demo/simular-actividad.ps1            # 5 cobros
pwsh ./demo/simular-actividad.ps1 -Pagos 12  # más cobros
```

Hace llamadas con las claves de demostración: salud, 401 sin clave, 403 con convenio vencido, tasas, consulta de RNC,
cobros con su e-CF, reenvío idempotente y catálogos del portal. Llena `integ.Bitacora`, los logs de Integración y los pagos
que el Core recibe. En 1 o 2 minutos el servicio de Integración los entrega al Core.

Dónde se ve: Core → *Integración* (transacciones, pagos recibidos vs aplicados, disponibilidad) con `admin` o `auditor`.

## 3. Core: expedientes, valoración y comité (flujo completo)

Este flujo llena *Expedientes*, *Valoración*, *Comité*, *Reportes*, *Auditoría* y *Mis transacciones*.
Cada acción queda en la bitácora con el usuario que la hizo.

1. **Portal** (`401-2345678-8` o `ciudadano.demo@correo.do`): enviar una solicitud de Certificación y, si hay orden de cobro, pagarla en línea.
2. **`analista`**: *Expedientes* → *Identidad* → aprobar; asignar la cita de valoración.
3. **`medico`, `psicologo`, `social`** (evaluadores): *Mi agenda de valoración* → iniciar, codificar CIF, WHODAS, firmar. Luego consolidar.
4. **`comite1`, `comite2`, `comite3`**: *Comité* → votar el dictamen.
5. **`analista`**: emitir el carnet (necesita Integración arriba para la firma digital).
6. **Portal**: *Mis trámites* y *Mi carnet*; también pruebe cambiar cita y apelar.
7. **`admin` / `auditor`**: *Reportes* (expedientes por estado, ingresos, carnets, indicadores), *Auditoría* (botón *Verificar integridad*) y *Mis transacciones*.

Repita con otras cédulas válidas (el dígito verificador se valida) para tener varios expedientes en distintos estados.
`CONADIS.Core/BaseDatos/99_PruebaFlujoCompleto.sql` recorre el mismo flujo, pero termina con ROLLBACK: no deja datos.

## 4. Logs: cómo generar entradas útiles

La pantalla *Logs del sistema* del Core (`admin` y `auditor`) lee `CONADIS_CoreDB.dbo.Log4NetLog`, es decir, solo los logs del Core
(API y sistema interno). Cada módulo guarda los suyos en su propia base.

| Para obtener | Haga |
|---|---|
| `INFO` | Cualquier uso normal del Core (login, solicitudes, pagos) |
| `WARN` | Contraseña incorrecta en el sistema interno; intentos fallidos hasta el bloqueo |
| `ERROR` | Detenga la base de datos o Integración un momento y use el Core; o llame a la API del Core con una clave inválida |

Los logs de Integración, Web y Caja se consultan con SQL: `SELECT TOP 100 * FROM dbo.Log4NetLog ORDER BY Id DESC;`
en `CONADIS_IntegracionDB`, `CONADIS_WebDB` y `CONADIS_CajaDB`.

## 5. Prueba de caídas (para la demostración)

Siga la sección 8 del README: detenga el Core o Integración, cobre en la Caja y envíe una solicitud desde el Portal,
y vea cómo todo se entrega al volver.
