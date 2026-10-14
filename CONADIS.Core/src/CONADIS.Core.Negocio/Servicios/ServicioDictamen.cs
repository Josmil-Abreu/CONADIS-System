using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Seguridad;

namespace CONADIS.Core.Negocio.Servicios;

/// <summary>
/// Dictamen del comité por quórum y apelaciones (RF-CORE-03, diagrama 4.3).
/// El grado calculado por el baremo es solo una propuesta: decide el comité (RINV-05).
/// </summary>
public sealed class ServicioDictamen(EjecutorSp db)
{
    public Task<List<DictamenPendiente>> PendientesAsync(CancellationToken ct = default)
        => db.ConsultarListaAsync("core.ppDictamen_Pendientes", f => new DictamenPendiente(f.Entero("EvaluacionId"),
            f.Entero("ExpedienteId"), f.Texto("NumeroExpediente"), f.Texto("NombreCompleto"), f.DecimalONulo("PuntajeWhodas"),
            f.TextoONulo("GradoPropuesto"), f.TextoONulo("TipoPropuesto"), f.FechaHora("FechaEvaluacion"), f.Entero("Votos")), ct: ct);

    public Task<List<VotoEmitido>> VotosAsync(int evaluacionId, CancellationToken ct = default)
        => db.ConsultarListaAsync("core.ppDictamen_Votos", f => new VotoEmitido(f.Texto("Miembro"), f.Texto("Voto"),
                f.TextoONulo("Grado"), f.TextoONulo("Tipo"), f.TextoONulo("Motivo"), f.FechaHora("FechaVoto")),
            [Parametro.Entero("@EvaluacionId", evaluacionId)], ct);

    /// <summary>Registra (o cambia) el voto del miembro. Si se alcanza el quórum, se emite el dictamen.</summary>
    public async Task<ResultadoVoto> VotarAsync(UsuarioSesion miembro, NuevoVoto v, CancellationToken ct = default)
    {
        Autorizacion.Exigir(miembro, Roles.Comite);
        if (v.Voto is not ("Aprobar" or "Rechazar")) throw new ReglaNegocioException(0, "El voto debe ser Aprobar o Rechazar.");
        if (v.Grado is not null && !Valores.Grados.Contains(v.Grado)) throw new ReglaNegocioException(0, "Grado no válido.");
        if (v.Tipo is not null && !Valores.TiposDiscapacidad.Contains(v.Tipo)) throw new ReglaNegocioException(0, "Tipo de discapacidad no válido.");
        if (v.FechaVencimiento is { } vence && vence <= DateOnly.FromDateTime(DateTime.Today))
            throw new ReglaNegocioException(0, "La fecha de vencimiento del carnet debe ser futura.");

        var tabla = await db.ConsultarAsync("core.ppDictamen_Votar",
        [
            Parametro.Entero("@EvaluacionId", v.EvaluacionId),
            Parametro.Entero("@UsuarioId", miembro.UsuarioId),
            Parametro.Texto("@Voto", v.Voto, 10),
            Parametro.Texto("@Grado", v.Grado, 20),
            Parametro.Texto("@Tipo", v.Tipo, 20),
            Parametro.Texto("@Motivo", string.IsNullOrWhiteSpace(v.Motivo) ? null : v.Motivo.Trim(), 500),
            Parametro.Booleano("@Permanente", v.Permanente),
            Parametro.Fecha("@FechaVencimiento", v.FechaVencimiento)
        ], ct);

        var f = tabla.Rows[0];
        return new ResultadoVoto(f.Texto("Resultado"), f.Entero("VotosCoincidentes"), f.Entero("Quorum"));
    }

    // ------------------------------------------------------------------ apelaciones

    /// <summary>El ciudadano apela un rechazo dentro del plazo (vía portal e Integración).</summary>
    public Task RegistrarApelacionAsync(string numero, string cedula, string motivo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 20)
            throw new ReglaNegocioException(0, "Explica el motivo de tu apelación (mínimo 20 caracteres).");

        return db.EjecutarAsync("core.ppApelacion_Registrar",
        [
            Parametro.TextoAnsi("@Numero", numero.Trim().ToUpperInvariant(), 20),
            Parametro.TextoAnsi("@Cedula", ValidadorCedula.Normalizar(cedula), 11),
            Parametro.Texto("@Motivo", motivo.Trim(), 1000)
        ], ct);
    }

    public Task<List<ApelacionPendiente>> ApelacionesPendientesAsync(CancellationToken ct = default)
        => db.ConsultarListaAsync("core.ppApelacion_Pendientes", f => new ApelacionPendiente(f.Entero("ApelacionId"),
            f.Entero("ExpedienteId"), f.Texto("NumeroExpediente"), f.Texto("NombreCompleto"), f.Texto("Motivo"),
            f.FechaHora("FechaRegistro")), ct: ct);

    /// <summary>Admitida: el expediente vuelve a evaluación. Denegada: el rechazo queda firme.</summary>
    public Task ResolverApelacionAsync(UsuarioSesion miembro, int apelacionId, bool admitir, string resolucion, CancellationToken ct = default)
    {
        Autorizacion.Exigir(miembro, Roles.Comite);
        if (string.IsNullOrWhiteSpace(resolucion) || resolucion.Trim().Length < 10)
            throw new ReglaNegocioException(0, "La resolución debe estar motivada (mínimo 10 caracteres).");

        return db.EjecutarAsync("core.ppApelacion_Resolver",
        [
            Parametro.Entero("@ApelacionId", apelacionId),
            Parametro.Booleano("@Admitir", admitir),
            Parametro.Texto("@Resolucion", resolucion.Trim(), 1000),
            Parametro.Entero("@UsuarioId", miembro.UsuarioId)
        ], ct);
    }
}