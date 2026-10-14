/* =============================================================================
   CONADIS.Integracion - 09: Permisos de la aplicación (mínimo privilegio)
   La API y el Servicio de Windows SOLO pueden ejecutar procedimientos almacenados.
   No tienen SELECT/INSERT/UPDATE/DELETE directo sobre ninguna tabla, así que
   aunque alguien escribiera SQL en el código C#, la base lo rechazaría.
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO

-- Necesarios para índices filtrados y JSON (Visual Studio ya los trae activos; sqlcmd no)
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DATABASE_PRINCIPAL_ID(N'rol_integracion_app') IS NULL
    CREATE ROLE rol_integracion_app AUTHORIZATION dbo;
GO

GRANT EXECUTE ON SCHEMA::snd TO rol_integracion_app;
GRANT EXECUTE ON SCHEMA::seg TO rol_integracion_app;
GRANT EXECUTE ON SCHEMA::cat TO rol_integracion_app;
GRANT EXECUTE ON OBJECT::dbo.ppLog_Insertar TO rol_integracion_app;
GRANT EXECUTE ON OBJECT::dbo.ppLog_Listar   TO rol_integracion_app;
GO

/* Cómo asignar el rol (ejemplo; ajustar la cuenta):

   -- Si la API corre como Servicio de Windows / IIS con una cuenta de Windows:
   CREATE LOGIN [DOMINIO\cuenta_integracion] FROM WINDOWS;
   CREATE USER  [DOMINIO\cuenta_integracion] FOR LOGIN [DOMINIO\cuenta_integracion];
   ALTER ROLE rol_integracion_app ADD MEMBER [DOMINIO\cuenta_integracion];

   En desarrollo con LocalDB la aplicación se conecta con tu propio usuario de
   Windows (que es administrador de LocalDB), así que no hace falta nada más.
*/

PRINT N'09 - Rol rol_integracion_app con permiso EXECUTE listo.';
GO
