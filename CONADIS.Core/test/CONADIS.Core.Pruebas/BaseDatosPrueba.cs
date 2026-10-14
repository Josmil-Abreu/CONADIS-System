using CONADIS.Core.Negocio.Datos;

namespace CONADIS.Core.Pruebas;

/// <summary>
/// Conexión para las pruebas de integración: usa la base CONADIS_CoreDB de tu LocalDB.
/// Si quieres otra, define la variable de entorno CONADIS_CORE_PRUEBAS con la cadena de conexión.
/// </summary>
internal static class BaseDatosPrueba
{
    public static readonly string Cadena =
        Environment.GetEnvironmentVariable("CONADIS_CORE_PRUEBAS")
        ?? @"Server=(localdb)\MSSQLLocalDB;Database=CONADIS_CoreDB;Trusted_Connection=True;TrustServerCertificate=True";

    public static EjecutorSp Ejecutor() => new(Cadena);
}