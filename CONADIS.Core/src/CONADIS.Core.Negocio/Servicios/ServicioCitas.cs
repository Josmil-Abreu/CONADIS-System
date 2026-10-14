using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Seguridad;

namespace CONADIS.Core.Negocio.Servicios;

/// <summary>Agenda de citas de valoración por centro (RF-CORE-01 paso 7, CU-01 pasos 8-9).</summary>
public sealed class ServicioCitas(EjecutorSp db)
{
	/// <summary>Días con cupo en un centro, sin fines de semana.</summary>
	public Task<List<CupoDisponible>> DisponibilidadAsync(int centroId, DateOnly desde, int dias = 15, CancellationToken ct = default)
	{
		if (dias is < 1 or > 60) throw new ReglaNegocioException(0, "El rango de días debe estar entre 1 y 60.");
		return db.ConsultarListaAsync("core.ppCita_Disponibilidad",
			f => new CupoDisponible(f.FechaONula("Fecha")!.Value, f.Entero("CapacidadDiaria"), f.Entero("CuposLibres")),
			[Parametro.Entero("@CentroId", centroId), Parametro.Fecha("@Desde", desde), Parametro.Entero("@Dias", dias)], ct);
	}

	/// <summary>Asigna la cita: el expediente pasa de Solicitado a En Evaluación. Devuelve el Id de la cita.</summary>
	public async Task<int> AsignarAsync(UsuarioSesion usuario, int expedienteId, int centroId, DateTimeOffset fechaHora,
		bool domiciliaria, CancellationToken ct = default)
	{
		Autorizacion.Exigir(usuario, Roles.AnalistaRegistro, Roles.Administrador);

		var tabla = await db.ConsultarAsync("core.ppCita_Asignar",
		[
			Parametro.Entero("@ExpedienteId", expedienteId),
			Parametro.Entero("@CentroId", centroId),
			Parametro.FechaHora("@FechaHora", fechaHora),
			Parametro.Booleano("@Domiciliaria", domiciliaria),
			Parametro.Texto("@Usuario", usuario.Username, 100)
		], ct);
		return tabla.Rows[0].Entero("CitaId");
	}

	/// <summary>El ciudadano pide otra fecha desde el portal (vía Integración).</summary>
	public Task ReprogramarCiudadanoAsync(string numero, string cedula, DateTimeOffset nuevaFecha, CancellationToken ct = default)
		=> db.EjecutarAsync("core.ppCita_ReprogramarCiudadano",
		[
			Parametro.TextoAnsi("@Numero", numero.Trim().ToUpperInvariant(), 20),
			Parametro.TextoAnsi("@Cedula", ValidadorCedula.Normalizar(cedula), 11),
			Parametro.FechaHora("@NuevaFecha", nuevaFecha)
		], ct);

	/// <summary>Agenda del día. Un evaluador solo puede ver la de su centro.</summary>
	public Task<List<CitaAgenda>> AgendaAsync(UsuarioSesion usuario, int centroId, DateOnly fecha, CancellationToken ct = default)
	{
		if (usuario.Rol == Roles.Evaluador && usuario.CentroId != centroId)
			throw new AccesoDenegadoException("Solo puedes ver la agenda de tu centro de valoración.");

		return db.ConsultarListaAsync("core.ppCita_Agenda", f => new CitaAgenda(f.Entero("CitaId"), f.FechaHora("FechaHora"),
				f.Booleano("Domiciliaria"), f.Texto("Estado"), f.Entero("ExpedienteId"), f.Texto("NumeroExpediente"),
				f.Texto("NombreCompleto")),
			[Parametro.Entero("@CentroId", centroId), Parametro.Fecha("@Fecha", fecha)], ct);
	}
}