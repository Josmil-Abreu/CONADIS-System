/* =============================================================================
   CONADIS.Web - 99: Prueba de los procedimientos SIN dejar datos (ROLLBACK al final)
   ============================================================================= */
USE CONADIS_WebDB;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

PRINT N'1) Crear cuenta';
EXEC web.ppCuenta_Crear @Cedula = '40000000006', @Correo = N'prueba.portal@correo.do', @NombreCompleto = N'Prueba Portal',
     @PasswordHash = 'PBKDF2-SHA256$1$x$y', @EstadoIdentidad = N'Validada', @TokenVerificacion = 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA';
DECLARE @Cuenta INT = (SELECT CuentaId FROM web.Cuenta WHERE Cedula = '40000000006');

PRINT N'2) Login por cédula con guiones y por correo (misma cuenta)';
EXEC web.ppCuenta_ObtenerParaLogin @Usuario = N'400-0000000-6';
EXEC web.ppCuenta_ObtenerParaLogin @Usuario = N'prueba.portal@correo.do';

PRINT N'3) Cinco intentos fallidos bloquean la cuenta (BloqueadoHasta con valor en el último)';
DECLARE @i INT = 1;
WHILE @i <= 5 BEGIN EXEC web.ppCuenta_RegistrarIntentoFallido @CuentaId = @Cuenta; SET @i += 1; END

PRINT N'4) Verificar el correo con el token';
EXEC web.ppCuenta_VerificarCorreo @TokenHash = 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA';

PRINT N'5) Preferencias de accesibilidad y borrador de solicitud (la clave de idempotencia se conserva)';
EXEC web.ppPreferencia_Guardar @CuentaId = @Cuenta, @AltoContraste = 1, @TamanoTexto = 150;
EXEC web.ppBorrador_Guardar @CuentaId = @Cuenta, @Tipo = N'Certificacion', @Paso = 1, @Datos = N'{"cedula":"40000000006"}';
EXEC web.ppBorrador_Guardar @CuentaId = @Cuenta, @Tipo = N'Certificacion', @Paso = 2, @Datos = N'{"cedula":"40000000006","codigoCie10":"G80"}';
EXEC web.ppCuenta_Obtener @CuentaId = @Cuenta;

PRINT N'6) Recuperación de contraseña (correo inexistente: no devuelve nada)';
EXEC web.ppRecuperacion_Crear @Correo = N'no.existe@correo.do', @TokenHash = 'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB';
EXEC web.ppRecuperacion_Crear @Correo = N'prueba.portal@correo.do', @TokenHash = 'CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC';
EXEC web.ppRecuperacion_Usar @TokenHash = 'CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC', @PasswordHash = 'PBKDF2-SHA256$1$nuevo$hash';

PRINT N'7) Eventos de seguridad de la cuenta';
EXEC web.ppEvento_ListarPorCuenta @CuentaId = @Cuenta;

ROLLBACK TRANSACTION;
PRINT N'Prueba terminada. Todo se revirtió: la base quedó igual que antes.';
GO
