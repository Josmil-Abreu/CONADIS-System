using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;

namespace CONADIS.Core.Negocio.Servicios;

/// <summary>
/// Catálogos y parámetros que el administrador funcional edita sin recompilar (RNF-MAN-05):
/// parámetros de negocio, centros de valoración, códigos CIF, baremo y tasas.
/// </summary>
public sealed class ServicioCatalogos(EjecutorSp db)
{
    // ----------------------------- Parámetros -----------------------------
    public Task<List<ParametroNegocio>> ListarParametrosAsync(CancellationToken ct = default)
        => db.ConsultarListaAsync("cat.ppParametro_Listar",
            f => new ParametroNegocio(f.Texto("Clave"), f.Texto("Valor"), f.Texto("Descripcion")), ct: ct);

    public Task ActualizarParametroAsync(string clave, string valor, ContextoAccion contexto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(valor)) throw new ReglaNegocioException(0, "El valor del parámetro no puede quedar vacío.");
        return db.EjecutarAsync("cat.ppParametro_Actualizar",
        [
            Parametro.TextoAnsi("@Clave", clave, 50),
            Parametro.Texto("@Valor", valor.Trim(), 200),
            Parametro.Texto("@UsuarioAccion", contexto.Usuario, 100)
        ], ct);
    }

    // ----------------------------- Centros -----------------------------
    public Task<List<CentroValoracion>> ListarCentrosAsync(bool soloActivos = true, CancellationToken ct = default)
        => db.ConsultarListaAsync("cat.ppCentro_Listar",
            f => new CentroValoracion(f.Entero("CentroId"), f.Texto("Nombre"), f.Texto("Provincia"),
                f.Entero("CapacidadDiaria"), f.Booleano("Activo")),
            [Parametro.Booleano("@SoloActivos", soloActivos)], ct);

    public async Task<int> GuardarCentroAsync(CentroValoracion centro, ContextoAccion contexto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(centro.Nombre) || string.IsNullOrWhiteSpace(centro.Provincia))
            throw new ReglaNegocioException(0, "Escribe el nombre y la provincia del centro.");
        if (centro.CapacidadDiaria is < 1 or > 500)
            throw new ReglaNegocioException(0, "La capacidad diaria debe estar entre 1 y 500 citas.");

        var tabla = await db.ConsultarAsync("cat.ppCentro_Guardar",
        [
            Parametro.Entero("@CentroId", centro.CentroId == 0 ? null : centro.CentroId),
            Parametro.Texto("@Nombre", centro.Nombre.Trim(), 150),
            Parametro.Texto("@Provincia", centro.Provincia.Trim(), 60),
            Parametro.Entero("@CapacidadDiaria", centro.CapacidadDiaria),
            Parametro.Booleano("@Activo", centro.Activo),
            Parametro.Texto("@UsuarioAccion", contexto.Usuario, 100)
        ], ct);
        return tabla.Rows[0].Entero("CentroId");
    }

    // ----------------------------- Catálogo CIF -----------------------------
    /// <summary>Busca por código ("b2") o por texto ("visual"). Máximo 50 resultados.</summary>
    public Task<List<CodigoCif>> BuscarCodigosCifAsync(string? texto, string? componente = null, CancellationToken ct = default)
        => db.ConsultarListaAsync("cat.ppCodigoCif_Buscar",
            f => new CodigoCif(f.Texto("Codigo"), f.Texto("Componente"), f.Texto("Descripcion")),
            [
                Parametro.Texto("@Texto", string.IsNullOrWhiteSpace(texto) ? null : texto.Trim(), 100),
                Parametro.TextoAnsi("@Componente", componente?.ToLowerInvariant(), 1)
            ], ct);

    public Task GuardarCodigoCifAsync(string codigo, string descripcion, bool activo, ContextoAccion contexto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(descripcion))
            throw new ReglaNegocioException(0, "Escribe el código y la descripción.");
        return db.EjecutarAsync("cat.ppCodigoCif_Guardar",
        [
            Parametro.TextoAnsi("@Codigo", codigo.Trim(), 10),
            Parametro.Texto("@Descripcion", descripcion.Trim(), 300),
            Parametro.Booleano("@Activo", activo),
            Parametro.Texto("@UsuarioAccion", contexto.Usuario, 100)
        ], ct);
    }

    // ----------------------------- Baremo -----------------------------
    public Task<List<RangoBaremo>> ObtenerBaremoVigenteAsync(CancellationToken ct = default)
        => db.ConsultarListaAsync("cat.ppBaremo_ObtenerVigente",
            f => new RangoBaremo(f.Texto("Version"), f.Decimal("PuntajeDesde"), f.Decimal("PuntajeHasta"), f.Texto("Grado")), ct: ct);

    // ----------------------------- Tasas -----------------------------
    public Task<List<Tasa>> ListarTasasAsync(bool soloActivas = true, CancellationToken ct = default)
        => db.ConsultarListaAsync("cat.ppTasa_Listar",
            f => new Tasa(f.Entero("TasaId"), f.Texto("Codigo"), f.Texto("Descripcion"), f.Decimal("Monto"),
                f.Decimal("ItbisPorcentaje"), f.Texto("Resolucion"), f.Booleano("Activa")),
            [Parametro.Booleano("@SoloActivas", soloActivas)], ct);

    public async Task<int> GuardarTasaAsync(Tasa tasa, ContextoAccion contexto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tasa.Codigo) || string.IsNullOrWhiteSpace(tasa.Descripcion))
            throw new ReglaNegocioException(0, "Escribe el código y la descripción de la tasa.");
        if (string.IsNullOrWhiteSpace(tasa.Resolucion))
            throw new ReglaNegocioException(0, "Indica la resolución institucional que respalda la tasa.");

        var tabla = await db.ConsultarAsync("cat.ppTasa_Guardar",
        [
            Parametro.Entero("@TasaId", tasa.TasaId == 0 ? null : tasa.TasaId),
            Parametro.TextoAnsi("@Codigo", tasa.Codigo.Trim().ToUpperInvariant(), 20),
            Parametro.Texto("@Descripcion", tasa.Descripcion.Trim(), 200),
            Parametro.Decimal("@Monto", tasa.Monto),
            Parametro.Decimal("@ItbisPorcentaje", tasa.ItbisPorcentaje, 5, 2),
            Parametro.Texto("@Resolucion", tasa.Resolucion.Trim(), 100),
            Parametro.Booleano("@Activa", tasa.Activa),
            Parametro.Texto("@UsuarioAccion", contexto.Usuario, 100)
        ], ct);
        return tabla.Rows[0].Entero("TasaId");
    }
}