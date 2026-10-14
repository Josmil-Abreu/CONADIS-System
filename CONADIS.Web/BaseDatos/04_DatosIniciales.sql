/* =============================================================================
   CONADIS.Web - 04: Datos iniciales
   Cuenta de DEMOSTRACIÓN del portal:
     Cédula 40123456788  (o correo ciudadano.demo@correo.do)   Contraseña: Conadis2026!
   ============================================================================= */
USE CONADIS_WebDB;
GO

MERGE web.Parametro AS destino
USING (VALUES
    ('LoginIntentosMaximos',     N'5',  N'Intentos fallidos antes de bloquear la cuenta del portal.'),
    ('LoginMinutosBloqueo',      N'15', N'Minutos de bloqueo de la cuenta.'),
    ('ContrasenaLongitudMinima', N'12', N'Longitud mínima de la contraseña del portal (RNF-SEG-03).'),
    ('RecuperacionMinutos',      N'30', N'Minutos de validez del enlace para restablecer la contraseña.'),
    ('VerificacionHoras',        N'48', N'Horas de validez del enlace de verificación del correo.')
) AS origen (Clave, Valor, Descripcion)
ON destino.Clave = origen.Clave
WHEN NOT MATCHED THEN INSERT (Clave, Valor, Descripcion) VALUES (origen.Clave, origen.Valor, origen.Descripcion);
GO

IF NOT EXISTS (SELECT 1 FROM web.Cuenta WHERE Cedula = '40123456788')
BEGIN
    INSERT INTO web.Cuenta (Cedula, Correo, Telefono, NombreCompleto, PasswordHash, EstadoIdentidad, CorreoVerificado)
    VALUES ('40123456788', N'ciudadano.demo@correo.do', N'809-555-0101', N'Ciudadano de Demostración',
            'PBKDF2-SHA256$600000$aerm/xsBmOkLbMBzWHp6OA==$Xq5StB1nXMAs/4Qq4xj9hx4y1xLYbzWOXgrTfQ2k5w8=', N'Validada', 1);
    INSERT INTO web.Preferencia (CuentaId) VALUES (SCOPE_IDENTITY());
END
GO

PRINT N'04 - Datos iniciales del Portal cargados.';
GO
