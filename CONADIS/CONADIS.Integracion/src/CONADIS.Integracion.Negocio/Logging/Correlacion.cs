namespace CONADIS.Integracion.Negocio.Logging;

/// <summary>
/// Identificador de correlación de la operación en curso. Viaja con el flujo async,
/// así todos los logs, la bitácora y los mensajes de una misma petición comparten el mismo id.
/// La API lo fija al recibir cada petición (encabezado X-Correlation-Id) y el Worker al
/// procesar cada mensaje.
/// </summary>
public sealed class Correlacion
{
    private static readonly AsyncLocal<Guid?> Actual = new();

    /// <summary>Objeto que Log4Net convierte a texto en %property{correlacion}.</summary>
    internal static readonly Correlacion Instancia = new();

    private Correlacion() { }

    public static Guid? Id => Actual.Value;

    /// <summary>Fija el id para el flujo actual. Devuelve un objeto que, al desecharse, restaura el anterior.</summary>
    public static IDisposable Iniciar(Guid? id = null)
    {
        var anterior = Actual.Value;
        Actual.Value = id ?? Guid.NewGuid();
        return new Restaurador(anterior);
    }

    public override string ToString() => Actual.Value?.ToString("N")[..12] ?? "-";

    private sealed class Restaurador(Guid? anterior) : IDisposable
    {
        public void Dispose() => Actual.Value = anterior;
    }
}
