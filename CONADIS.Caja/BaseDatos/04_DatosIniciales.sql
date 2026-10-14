/* =============================================================================
   CONADIS.Caja - 04: Datos iniciales
   Usuarios de DEMOSTRACIÓN (contraseña de todos: Conadis2026!):
     cajero (Cajero)   cajero2 (Cajero)   supervisor.caja (SupervisorCaja)   admin.caja (Administrador)
   Las tasas son una copia del catálogo del Core: así la Caja puede cobrar desde el
   primer momento aunque todavía no se haya conectado a Integración.
   ============================================================================= */
USE CONADIS_CajaDB;
GO

MERGE caja.Parametro AS destino
USING (VALUES
    ('RncEmisor',            N'401000001',                N'RNC del CONADIS como emisor de los e-CF (demostración).'),
    ('RazonSocialEmisor',    N'Consejo Nacional de Discapacidad (CONADIS)', N'Razón social del emisor.'),
    ('ToleranciaArqueo',     N'0.00',                     N'Diferencia permitida en el arqueo sin justificación (RD$).'),
    ('LoginIntentosMaximos', N'5',                        N'Intentos fallidos antes de bloquear al usuario.'),
    ('LoginMinutosBloqueo',  N'15',                       N'Minutos de bloqueo.')
) AS origen (Clave, Valor, Descripcion)
ON destino.Clave = origen.Clave
WHEN NOT MATCHED THEN INSERT (Clave, Valor, Descripcion) VALUES (origen.Clave, origen.Valor, origen.Descripcion);
GO

MERGE seg.Usuario AS destino
USING (VALUES
    (N'cajero',          N'Carla Cajera',            N'Cajero'),
    (N'cajero2',         N'Carlos Cajero',           N'Cajero'),
    (N'supervisor.caja', N'Sonia Supervisora de Caja', N'SupervisorCaja'),
    (N'admin.caja',      N'Administrador de Caja',   N'Administrador')
) AS origen (Username, NombreCompleto, Rol)
ON destino.Username = origen.Username
WHEN NOT MATCHED THEN INSERT (Username, PasswordHash, NombreCompleto, Rol)
    VALUES (origen.Username, 'PBKDF2-SHA256$600000$aerm/xsBmOkLbMBzWHp6OA==$Xq5StB1nXMAs/4Qq4xj9hx4y1xLYbzWOXgrTfQ2k5w8=',
            origen.NombreCompleto, origen.Rol);
GO

MERGE caja.Caja AS destino
USING (VALUES ('CAJA-01', N'Sede Central'), ('CAJA-02', N'Sede Central'), ('CAJA-SANTIAGO', N'Santiago')) AS origen (Codigo, Sucursal)
ON destino.Codigo = origen.Codigo
WHEN NOT MATCHED THEN INSERT (Codigo, Sucursal) VALUES (origen.Codigo, origen.Sucursal);
GO

MERGE caja.SecuenciaEcf AS destino
USING (VALUES ('31', 1, 100000, CAST('2027-12-31' AS DATE)), ('32', 1, 1000000, CAST('2027-12-31' AS DATE)),
              ('34', 1, 100000, CAST('2027-12-31' AS DATE)), ('45', 1, 100000, CAST('2027-12-31' AS DATE))) AS origen (Tipo, Siguiente, Hasta, VenceEn)
ON destino.Tipo = origen.Tipo
WHEN NOT MATCHED THEN INSERT (Tipo, Siguiente, Hasta, VenceEn) VALUES (origen.Tipo, origen.Siguiente, origen.Hasta, origen.VenceEn);
GO

MERGE caja.Tasa AS destino
USING (VALUES
    ('DUP-CARNET', N'Duplicado de carnet PCD',                        500.00, 0.00),
    ('CERT-ACCES', N'Certificación de accesibilidad de obra/empresa', 5000.00, 0.00),
    ('COPIA-CERT', N'Copia certificada de certificación',             300.00, 0.00),
    ('CONST-REG',  N'Constancia de registro',                         200.00, 0.00)
) AS origen (Codigo, Descripcion, Monto, ItbisPorcentaje)
ON destino.Codigo = origen.Codigo
WHEN NOT MATCHED THEN INSERT (Codigo, Descripcion, Monto, ItbisPorcentaje) VALUES (origen.Codigo, origen.Descripcion, origen.Monto, origen.ItbisPorcentaje);
GO

PRINT N'04 - Datos iniciales de Caja cargados.';
GO
