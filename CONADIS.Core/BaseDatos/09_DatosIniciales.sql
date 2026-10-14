/* =============================================================================
   CONADIS.Core - 09: Datos iniciales (catálogos y usuarios de demostración)
   Contraseña de TODOS los usuarios de demostración: Conadis2026!
   Los hashes son PBKDF2-SHA256 reales (no texto plano). Cambie las contraseñas
   desde el panel (Usuarios > Cambiar contraseña) antes de cualquier uso real.
   ============================================================================= */
USE CONADIS_CoreDB;
GO

/* ----------------------------- ROLES (RBAC) -------------------------------- */
MERGE seg.Rol AS destino
USING (VALUES
    (N'Administrador',      N'Gestiona usuarios, roles, catálogos, tasas y parámetros.'),
    (N'AnalistaRegistro',   N'Recibe solicitudes presenciales, valida identidad, asigna citas y emite carnets.'),
    (N'SupervisorRegistro', N'Anula carnets y expedientes con motivo registrado.'),
    (N'Evaluador',          N'Registra la valoración CIF de su disciplina en su centro.'),
    (N'Comite',             N'Vota el dictamen de certificación y resuelve apelaciones.'),
    (N'Auditor',            N'Solo lectura de bitácoras y reportes.')
) AS origen (Nombre, Descripcion)
ON destino.Nombre = origen.Nombre
WHEN NOT MATCHED THEN INSERT (Nombre, Descripcion) VALUES (origen.Nombre, origen.Descripcion);
GO

/* ----------------------------- CENTROS DE VALORACIÓN ----------------------- */
MERGE cat.CentroValoracion AS destino
USING (VALUES
    (N'Centro de Valoración Sede Central', N'Distrito Nacional', 20),
    (N'Centro de Valoración Santiago',     N'Santiago',          15),
    (N'Centro de Valoración San Pedro',    N'San Pedro de Macorís', 10),
    (N'Centro de Valoración La Vega',      N'La Vega',           10)
) AS origen (Nombre, Provincia, CapacidadDiaria)
ON destino.Nombre = origen.Nombre
WHEN NOT MATCHED THEN INSERT (Nombre, Provincia, CapacidadDiaria) VALUES (origen.Nombre, origen.Provincia, origen.CapacidadDiaria);
GO

/* ----------------------------- CATÁLOGO CIF (extracto) --------------------- */
MERGE cat.CodigoCIF AS destino
USING (VALUES
    ('b110', 'b', N'Funciones de la conciencia'),
    ('b117', 'b', N'Funciones intelectuales'),
    ('b126', 'b', N'Funciones del temperamento y la personalidad'),
    ('b140', 'b', N'Funciones de la atención'),
    ('b144', 'b', N'Funciones de la memoria'),
    ('b152', 'b', N'Funciones emocionales'),
    ('b156', 'b', N'Funciones de la percepción'),
    ('b167', 'b', N'Funciones mentales del lenguaje'),
    ('b210', 'b', N'Funciones visuales'),
    ('b230', 'b', N'Funciones auditivas'),
    ('b280', 'b', N'Sensación de dolor'),
    ('b310', 'b', N'Funciones de la voz'),
    ('b710', 'b', N'Funciones relacionadas con la movilidad de las articulaciones'),
    ('b730', 'b', N'Funciones relacionadas con la fuerza muscular'),
    ('b760', 'b', N'Funciones relacionadas con el control de los movimientos voluntarios'),
    ('s110', 's', N'Estructura del cerebro'),
    ('s120', 's', N'Médula espinal y estructuras relacionadas'),
    ('s210', 's', N'Estructura de la órbita ocular'),
    ('s220', 's', N'Estructura del globo ocular'),
    ('s260', 's', N'Estructura del oído interno'),
    ('s730', 's', N'Estructura de la extremidad superior'),
    ('s750', 's', N'Estructura de la extremidad inferior'),
    ('s760', 's', N'Estructura del tronco'),
    ('d110', 'd', N'Mirar'),
    ('d115', 'd', N'Escuchar'),
    ('d166', 'd', N'Leer'),
    ('d310', 'd', N'Comunicación-recepción de mensajes hablados'),
    ('d330', 'd', N'Hablar'),
    ('d450', 'd', N'Andar'),
    ('d465', 'd', N'Desplazarse utilizando algún tipo de equipamiento'),
    ('d510', 'd', N'Lavarse'),
    ('d540', 'd', N'Vestirse'),
    ('d550', 'd', N'Comer'),
    ('d640', 'd', N'Realizar los quehaceres de la casa'),
    ('d720', 'd', N'Interacciones interpersonales complejas'),
    ('d820', 'd', N'Educación escolar'),
    ('d850', 'd', N'Trabajo remunerado'),
    ('e115', 'e', N'Productos y tecnología para uso personal en la vida diaria'),
    ('e120', 'e', N'Productos y tecnología para la movilidad y el transporte personal'),
    ('e125', 'e', N'Productos y tecnología para la comunicación'),
    ('e150', 'e', N'Diseño, construcción y productos de edificios de uso público'),
    ('e310', 'e', N'Familiares cercanos'),
    ('e410', 'e', N'Actitudes individuales de miembros de la familia cercana'),
    ('e580', 'e', N'Servicios, sistemas y políticas sanitarias')
) AS origen (Codigo, Componente, Descripcion)
ON destino.Codigo = origen.Codigo
WHEN NOT MATCHED THEN INSERT (Codigo, Componente, Descripcion) VALUES (origen.Codigo, origen.Componente, origen.Descripcion);
GO

/* ----------------------------- BAREMO INSTITUCIONAL ------------------------ */
-- Rangos alineados con la escala de calificadores CIF (SRS A.2, nota 2)
IF NOT EXISTS (SELECT 1 FROM cat.Baremo WHERE Version = 'BAR-2026.1')
BEGIN
    INSERT INTO cat.Baremo (Version, Vigente, FechaDesde) VALUES ('BAR-2026.1', 1, '2026-01-01');
    DECLARE @BaremoId INT = SCOPE_IDENTITY();
    INSERT INTO cat.BaremoRango (BaremoId, PuntajeDesde, PuntajeHasta, Grado) VALUES
        (@BaremoId,  5.00,  24.99, N'Leve'),
        (@BaremoId, 25.00,  49.99, N'Moderado'),
        (@BaremoId, 50.00,  95.99, N'Severo'),
        (@BaremoId, 96.00, 100.00, N'Profundo');
END
GO

/* ----------------------------- PARÁMETROS (RNF-MAN-05) --------------------- */
MERGE cat.Parametro AS destino
USING (VALUES
    ('PlazoApelacionDias',    N'30', N'Días calendario para apelar un rechazo.'),
    ('QuorumDictamen',        N'2',  N'Votos coincidentes del comité necesarios para emitir el dictamen.'),
    ('VigenciaCarnetAnios',   N'5',  N'Años de vigencia del carnet cuando no es permanente.'),
    ('LoginIntentosMaximos',  N'5',  N'Intentos fallidos antes de bloquear la cuenta.'),
    ('LoginMinutosBloqueo',   N'15', N'Minutos de bloqueo tras superar los intentos.'),
    ('HorasReintentoJce',     N'24', N'Horas de reintento automático de la validación JCE antes de pasar al analista.')
) AS origen (Clave, Valor, Descripcion)
ON destino.Clave = origen.Clave
WHEN NOT MATCHED THEN INSERT (Clave, Valor, Descripcion) VALUES (origen.Clave, origen.Valor, origen.Descripcion);
GO

/* ----------------------------- TASAS ADMINISTRATIVAS ----------------------- */
-- Montos ilustrativos. Las tasas institucionales no llevan ITBIS (0 %).
MERGE cat.Tasa AS destino
USING (VALUES
    ('DUP-CARNET', N'Duplicado de carnet PCD',                       500.00, 0.00, N'Resolución CONADIS 01-2026'),
    ('CERT-ACCES', N'Certificación de accesibilidad de obra/empresa', 5000.00, 0.00, N'Resolución CONADIS 02-2026'),
    ('COPIA-CERT', N'Copia certificada de certificación',            300.00, 0.00, N'Resolución CONADIS 01-2026'),
    ('CONST-REG',  N'Constancia de registro',                        200.00, 0.00, N'Resolución CONADIS 01-2026')
) AS origen (Codigo, Descripcion, Monto, ItbisPorcentaje, Resolucion)
ON destino.Codigo = origen.Codigo
WHEN NOT MATCHED THEN INSERT (Codigo, Descripcion, Monto, ItbisPorcentaje, Resolucion)
                      VALUES (origen.Codigo, origen.Descripcion, origen.Monto, origen.ItbisPorcentaje, origen.Resolucion);
GO

/* ----------------------------- USUARIOS DE DEMOSTRACIÓN -------------------- */
IF NOT EXISTS (SELECT 1 FROM seg.Usuario WHERE Username = N'admin')
    INSERT INTO seg.Usuario (Username, PasswordHash, NombreCompleto, Correo, RolId, Disciplina, CentroId)
    VALUES (N'admin', 'PBKDF2-SHA256$600000$aerm/xsBmOkLbMBzWHp6OA==$Xq5StB1nXMAs/4Qq4xj9hx4y1xLYbzWOXgrTfQ2k5w8=', N'Administrador General', N'admin@conadis.gob.do',
            (SELECT RolId FROM seg.Rol WHERE Nombre = N'Administrador'), NULL, NULL);
IF NOT EXISTS (SELECT 1 FROM seg.Usuario WHERE Username = N'analista')
    INSERT INTO seg.Usuario (Username, PasswordHash, NombreCompleto, Correo, RolId, Disciplina, CentroId)
    VALUES (N'analista', 'PBKDF2-SHA256$600000$9MlP9iwCp59hDFCC1K7FUQ==$2W5sSGiOzFTPPl/2KTrB+ZkJ0M6HJ42aH59uIFFy4Wo=', N'Ana Registro', N'analista@conadis.gob.do',
            (SELECT RolId FROM seg.Rol WHERE Nombre = N'AnalistaRegistro'), NULL, NULL);
IF NOT EXISTS (SELECT 1 FROM seg.Usuario WHERE Username = N'supervisor')
    INSERT INTO seg.Usuario (Username, PasswordHash, NombreCompleto, Correo, RolId, Disciplina, CentroId)
    VALUES (N'supervisor', 'PBKDF2-SHA256$600000$6IcRI6nt0l6PXqPDoeG7uQ==$L4ZWCwsub2bQculkNnxU46eg0vpI+Y2yltZ5hwEXPqo=', N'Sergio Supervisor', N'supervisor@conadis.gob.do',
            (SELECT RolId FROM seg.Rol WHERE Nombre = N'SupervisorRegistro'), NULL, NULL);
IF NOT EXISTS (SELECT 1 FROM seg.Usuario WHERE Username = N'medico')
    INSERT INTO seg.Usuario (Username, PasswordHash, NombreCompleto, Correo, RolId, Disciplina, CentroId)
    VALUES (N'medico', 'PBKDF2-SHA256$600000$sUZN9lvngA6vP39HaQFDkg==$Cc/QqvL3Rv0Ckf3BDHjSnHm0sAzMAotnhExI6idKO7A=', N'Dra. María Medina', N'medico@conadis.gob.do',
            (SELECT RolId FROM seg.Rol WHERE Nombre = N'Evaluador'), N'Medica', (SELECT CentroId FROM cat.CentroValoracion WHERE Nombre = N'Centro de Valoración Sede Central'));
IF NOT EXISTS (SELECT 1 FROM seg.Usuario WHERE Username = N'psicologo')
    INSERT INTO seg.Usuario (Username, PasswordHash, NombreCompleto, Correo, RolId, Disciplina, CentroId)
    VALUES (N'psicologo', 'PBKDF2-SHA256$600000$88/Arn0MxWI0JEX+wiUXMA==$n2nDh8knuI+PsOQg4+64sqkJ/pmaNvwYmvfwSlOBqNw=', N'Lic. Pedro Paredes', N'psicologo@conadis.gob.do',
            (SELECT RolId FROM seg.Rol WHERE Nombre = N'Evaluador'), N'Psicologica', (SELECT CentroId FROM cat.CentroValoracion WHERE Nombre = N'Centro de Valoración Sede Central'));
IF NOT EXISTS (SELECT 1 FROM seg.Usuario WHERE Username = N'social')
    INSERT INTO seg.Usuario (Username, PasswordHash, NombreCompleto, Correo, RolId, Disciplina, CentroId)
    VALUES (N'social', 'PBKDF2-SHA256$600000$bCXQXzv8xkkIVaZs4vUmAw==$ToQESd8x1Uwk5s7y8XQheNN6sWGqDXsuD3X71NDdrdo=', N'Lic. Sofía Santos', N'social@conadis.gob.do',
            (SELECT RolId FROM seg.Rol WHERE Nombre = N'Evaluador'), N'Social', (SELECT CentroId FROM cat.CentroValoracion WHERE Nombre = N'Centro de Valoración Sede Central'));
IF NOT EXISTS (SELECT 1 FROM seg.Usuario WHERE Username = N'comite1')
    INSERT INTO seg.Usuario (Username, PasswordHash, NombreCompleto, Correo, RolId, Disciplina, CentroId)
    VALUES (N'comite1', 'PBKDF2-SHA256$600000$D9TQ7jO/TIspb7INlwYiHA==$YLshjkUJiLWXv5RvcTyjwX0MentJbbPogp2HNwCIDQU=', N'Dr. Carlos Comité', N'comite1@conadis.gob.do',
            (SELECT RolId FROM seg.Rol WHERE Nombre = N'Comite'), NULL, NULL);
IF NOT EXISTS (SELECT 1 FROM seg.Usuario WHERE Username = N'comite2')
    INSERT INTO seg.Usuario (Username, PasswordHash, NombreCompleto, Correo, RolId, Disciplina, CentroId)
    VALUES (N'comite2', 'PBKDF2-SHA256$600000$8d84LnESumjohpjFD+qPvg==$lz7vNYne/srM+tPqo2aaDdnWvvDIaQ4P2MrCNIlywWw=', N'Dra. Carmen Comité', N'comite2@conadis.gob.do',
            (SELECT RolId FROM seg.Rol WHERE Nombre = N'Comite'), NULL, NULL);
IF NOT EXISTS (SELECT 1 FROM seg.Usuario WHERE Username = N'comite3')
    INSERT INTO seg.Usuario (Username, PasswordHash, NombreCompleto, Correo, RolId, Disciplina, CentroId)
    VALUES (N'comite3', 'PBKDF2-SHA256$600000$rk+YaEDFoBdSMfeZMklSbQ==$YDpN5l4OF4zE4oBnByMBDZK127UJQPjrVdGj2VhgfNw=', N'Lic. Ciro Comité', N'comite3@conadis.gob.do',
            (SELECT RolId FROM seg.Rol WHERE Nombre = N'Comite'), NULL, NULL);
IF NOT EXISTS (SELECT 1 FROM seg.Usuario WHERE Username = N'auditor')
    INSERT INTO seg.Usuario (Username, PasswordHash, NombreCompleto, Correo, RolId, Disciplina, CentroId)
    VALUES (N'auditor', 'PBKDF2-SHA256$600000$dCS9s5Wlv5UdAFDc0kti7Q==$JVzWx7+cYjKf4jHTtf5UPepZA++NC/Vq8SCV2Ub/bkA=', N'Aurora Auditoría', N'auditor@conadis.gob.do',
            (SELECT RolId FROM seg.Rol WHERE Nombre = N'Auditor'), NULL, NULL);
GO

PRINT N'09 - Datos iniciales cargados.';
GO