namespace CONADIS.Core.Negocio.Modelos;

public sealed record ParametroNegocio(string Clave, string Valor, string Descripcion);

public sealed record CentroValoracion(int CentroId, string Nombre, string Provincia, int CapacidadDiaria, bool Activo);

public sealed record CodigoCif(string Codigo, string Componente, string Descripcion);

public sealed record RangoBaremo(string Version, decimal PuntajeDesde, decimal PuntajeHasta, string Grado);

public sealed record Tasa(int TasaId, string Codigo, string Descripcion, decimal Monto, decimal ItbisPorcentaje,
    string Resolucion, bool Activa);