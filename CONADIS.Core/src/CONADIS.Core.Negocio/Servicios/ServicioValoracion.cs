using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Seguridad;

namespace CONADIS.Core.Negocio.Servicios;

/// <summary>
/// Valoración multidimensional CIF (RF-CORE-02). Cada evaluador trabaja solo con su disciplina
/// (la toma de su usuario, no de lo que se le envíe) y solo en expedientes de su centro.
/// </summary>
public sealed class ServicioValoracion(EjecutorSp db)
{
	/// <summary>Abre la evaluación en curso del expediente (o crea una). Devuelve su Id.</summary>
	public async Task<int> IniciarAsync(UsuarioSesion evaluador, int expedienteId, CancellationToken ct = default)
	{
		Autorizacion.Exigir(evaluador, Roles.Evaluador);
		var tabla = await db.ConsultarAsync("core.ppEvaluacion_Iniciar",
			[Parametro.Entero("@ExpedienteId", expedienteId), Parametro.Entero("@UsuarioId", evaluador.UsuarioId)], ct);
		return tabla.Rows[0].Entero("EvaluacionId");
	}

	public async Task<EvaluacionDetalle?> ObtenerAsync(int evaluacionId, CancellationToken ct = default)
	{
		var ds = await db.ConsultarVariosAsync("core.ppEvaluacion_Obtener", [Parametro.Entero("@EvaluacionId", evaluacionId)], ct);
		if (ds.Tables.Count < 3 || ds.Tables[0].Rows.Count == 0) return null;

		var c = ds.Tables[0].Rows[0];
		var whodas = c.DecimalONulo("WhodasCognicion") is null ? null : new PuntajesWhodas(
			c.Decimal("WhodasCognicion"), c.Decimal("WhodasMovilidad"), c.Decimal("WhodasAutocuidado"),
			c.Decimal("WhodasRelaciones"), c.Decimal("WhodasActividades"), c.Decimal("WhodasParticipacion"));

		return new EvaluacionDetalle(c.Entero("EvaluacionId"), c.Entero("ExpedienteId"), c.Texto("NumeroExpediente"),
			c.Texto("NombreCompleto"), c.EnteroONulo("CentroId"), c.FechaHora("FechaEvaluacion"), whodas,
			c.DecimalONulo("PuntajeWhodas"), c.TextoONulo("GradoPropuesto"), c.TextoONulo("TipoPropuesto"),
			c.TextoONulo("VersionBaremo"), c.Texto("Estado"), c.TextoONulo("Observaciones"),
			c.Binario("RowVersion") ?? [], c.TextoONulo("Dictamen"),
			ServicioExpedientes.Mapear(ds.Tables[1], f => new Codificacion(f.Texto("Codigo"), f.Texto("Componente"),
				f.Texto("Descripcion"), f.Texto("Disciplina"), f.Entero("Calificador1"), f.EnteroONulo("Calificador2"),
				f.Booleano("EsFacilitador"), f.TextoONulo("Observacion"), f.Texto("Evaluador"))),
			ServicioExpedientes.Mapear(ds.Tables[2], f => new FirmaDisciplina(f.Texto("Disciplina"), f.Texto("Evaluador"),
				f.FechaHora("FechaFirma"))));
	}

	/// <summary>Agrega o corrige un código CIF de la disciplina del evaluador.</summary>
	public Task GuardarCodificacionAsync(UsuarioSesion evaluador, int evaluacionId, NuevaCodificacion c, CancellationToken ct = default)
	{
		Autorizacion.Exigir(evaluador, Roles.Evaluador);
		if (string.IsNullOrWhiteSpace(c.Codigo)) throw new ReglaNegocioException(0, "Escribe el código CIF.");

		return db.EjecutarAsync("core.ppEvaluacion_GuardarCodificacion",
		[
			Parametro.Entero("@EvaluacionId", evaluacionId),
			Parametro.Entero("@UsuarioId", evaluador.UsuarioId),
			Parametro.TextoAnsi("@Codigo", c.Codigo.Trim(), 10),
			Parametro.Entero("@Calificador1", c.Calificador1),
			Parametro.Entero("@Calificador2", c.Calificador2),
			Parametro.Booleano("@EsFacilitador", c.EsFacilitador),
			Parametro.Texto("@Observacion", string.IsNullOrWhiteSpace(c.Observacion) ? null : c.Observacion.Trim(), 300)
		], ct);
	}

	public Task QuitarCodificacionAsync(UsuarioSesion evaluador, int evaluacionId, string codigo, CancellationToken ct = default)
	{
		Autorizacion.Exigir(evaluador, Roles.Evaluador);
		return db.EjecutarAsync("core.ppEvaluacion_QuitarCodificacion",
		[
			Parametro.Entero("@EvaluacionId", evaluacionId),
			Parametro.Entero("@UsuarioId", evaluador.UsuarioId),
			Parametro.TextoAnsi("@Codigo", codigo.Trim(), 10)
		], ct);
	}

	/// <summary>
	/// Registra el WHODAS 2.0 y calcula el grado propuesto con el baremo vigente.
	/// <paramref name="rowVersion"/> es la versión leída con <see cref="ObtenerAsync"/>: si otro evaluador
	/// guardó antes, se rechaza para no pisar su trabajo (concurrencia optimista).
	/// </summary>
	public async Task<ResultadoWhodas> RegistrarWhodasAsync(UsuarioSesion evaluador, int evaluacionId, byte[] rowVersion,
		PuntajesWhodas p, string? observaciones = null, CancellationToken ct = default)
	{
		Autorizacion.Exigir(evaluador, Roles.Evaluador);
		foreach (var valor in new[] { p.Cognicion, p.Movilidad, p.Autocuidado, p.Relaciones, p.Actividades, p.Participacion })
			if (valor is < 0 or > 100) throw new ReglaNegocioException(0, "Cada dominio WHODAS debe estar entre 0 y 100.");
		if (rowVersion is not { Length: 8 })
			throw new ReglaNegocioException(0, "Falta la versión de la evaluación. Vuelve a abrirla antes de guardar.");

		var tabla = await db.ConsultarAsync("core.ppEvaluacion_RegistrarWhodas",
		[
			Parametro.Entero("@EvaluacionId", evaluacionId),
			Parametro.Binario("@RowVersion", rowVersion, 8),
			Parametro.Decimal("@Cognicion", p.Cognicion, 5, 2),
			Parametro.Decimal("@Movilidad", p.Movilidad, 5, 2),
			Parametro.Decimal("@Autocuidado", p.Autocuidado, 5, 2),
			Parametro.Decimal("@Relaciones", p.Relaciones, 5, 2),
			Parametro.Decimal("@Actividades", p.Actividades, 5, 2),
			Parametro.Decimal("@Participacion", p.Participacion, 5, 2),
			Parametro.Texto("@Observaciones", string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim(), 1000),
			Parametro.Texto("@Usuario", evaluador.Username, 100)
		], ct);

		var f = tabla.Rows[0];
		return new ResultadoWhodas(f.Decimal("PuntajeWhodas"), f.TextoONulo("GradoPropuesto"), f.Texto("VersionBaremo"));
	}

	/// <summary>Firma de la disciplina del evaluador. Con las 3 firmas, la evaluación queda Completa.</summary>
	public Task FirmarAsync(UsuarioSesion evaluador, int evaluacionId, CancellationToken ct = default)
	{
		Autorizacion.Exigir(evaluador, Roles.Evaluador);
		return db.EjecutarAsync("core.ppEvaluacion_Firmar",
			[Parametro.Entero("@EvaluacionId", evaluacionId), Parametro.Entero("@UsuarioId", evaluador.UsuarioId)], ct);
	}

	/// <summary>Consolida la evaluación: queda lista para el dictamen del comité.</summary>
	public Task ConsolidarAsync(UsuarioSesion evaluador, int evaluacionId, string tipoPropuesto, CancellationToken ct = default)
	{
		Autorizacion.Exigir(evaluador, Roles.Evaluador);
		if (!Valores.TiposDiscapacidad.Contains(tipoPropuesto))
			throw new ReglaNegocioException(0, $"Tipo de discapacidad no válido. Opciones: {string.Join(", ", Valores.TiposDiscapacidad)}.");

		return db.EjecutarAsync("core.ppEvaluacion_Consolidar",
		[
			Parametro.Entero("@EvaluacionId", evaluacionId),
			Parametro.Texto("@TipoPropuesto", tipoPropuesto, 20),
			Parametro.Texto("@Usuario", evaluador.Username, 100)
		], ct);
	}
}