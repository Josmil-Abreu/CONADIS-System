using System.Net.Mail;
using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Seguridad;

namespace CONADIS.Core.Negocio.Servicios;

/// <summary>Ayudas técnicas y certificación de accesibilidad (RF-WEB-03).</summary>
public sealed class ServicioSolicitudesServicio(EjecutorSp db)
{
	private static readonly string[] Estados = ["Recibida", "EnRevision", "Aprobada", "Rechazada", "Entregada"];

	/// <summary>
	/// Registra la solicitud y la enruta a su unidad. Ayudas técnicas: exige carnet vigente.
	/// Accesibilidad: genera la orden de cobro de su tasa (la validación del RNC con la DGII la hace Integración antes).
	/// </summary>
	public async Task<ServicioRegistrado> RegistrarAsync(NuevaSolicitudServicio s, ContextoAccion contexto, CancellationToken ct = default)
	{
		if (s.Tipo is not (TiposServicio.AyudaTecnica or TiposServicio.CertificacionAccesibilidad))
			throw new ReglaNegocioException(0, "Tipo de solicitud de servicio no válido.");

		var documento = ValidadorCedula.Normalizar(s.Documento);
		if (s.Tipo == TiposServicio.AyudaTecnica && !ValidadorCedula.EsValida(documento))
			throw new ReglaNegocioException(0, "La cédula no es válida.");
		if (s.Tipo == TiposServicio.CertificacionAccesibilidad && documento.Length != 9)
			throw new ReglaNegocioException(0, "El RNC debe tener 9 dígitos.");
		if (string.IsNullOrWhiteSpace(s.NombreSolicitante) || string.IsNullOrWhiteSpace(s.Resumen) || string.IsNullOrWhiteSpace(s.Detalle))
			throw new ReglaNegocioException(0, "Completa el nombre, el resumen y el detalle de la solicitud.");
		if (!MailAddress.TryCreate(s.Correo?.Trim(), out _))
			throw new ReglaNegocioException(0, "El correo no es válido.");

		var tabla = await db.ConsultarAsync("core.ppServicio_Registrar",
		[
			Parametro.TextoAnsi("@Tipo", s.Tipo, 20),
			Parametro.TextoAnsi("@Documento", documento, 11),
			Parametro.Texto("@NombreSolicitante", s.NombreSolicitante.Trim(), 150),
			Parametro.Texto("@Correo", s.Correo.Trim(), 150),
			Parametro.Texto("@Resumen", s.Resumen.Trim(), 200),
			Parametro.Texto("@Detalle", s.Detalle.Trim()),
			Parametro.Texto("@Usuario", contexto.Usuario, 100)
		], ct);

		var f = tabla.Rows[0];
		return new ServicioRegistrado(f.Texto("NumeroTramite"), f.FechaHora("FechaRegistro"), f.TextoONulo("NumeroOrden"));
	}

	public Task<List<SolicitudServicio>> ListarAsync(string? tipo = null, string? estado = null, CancellationToken ct = default)
		=> db.ConsultarListaAsync("core.ppServicio_Listar", f => new SolicitudServicio(f.Entero("ServicioId"),
				f.Texto("NumeroTramite"), f.Texto("Tipo"), f.Texto("Documento"), f.Texto("NombreSolicitante"), f.Texto("Correo"),
				f.Texto("Resumen"), f.Texto("Detalle"), f.Texto("Unidad"), f.Texto("Estado"), f.TextoONulo("NumeroOrden"),
				f.TextoONulo("EstadoOrden"), f.FechaHora("FechaRegistro"), f.FechaHora("FechaActualizacion")),
			[Parametro.TextoAnsi("@Tipo", tipo, 20), Parametro.Texto("@Estado", estado, 20)], ct);

	public Task CambiarEstadoAsync(UsuarioSesion usuario, string numeroTramite, string estado, string? comentario, CancellationToken ct = default)
	{
		Autorizacion.Exigir(usuario, Roles.AnalistaRegistro, Roles.Administrador);
		if (!Estados.Contains(estado)) throw new ReglaNegocioException(0, "Estado no válido.");

		return db.EjecutarAsync("core.ppServicio_CambiarEstado",
		[
			Parametro.TextoAnsi("@NumeroTramite", numeroTramite.Trim(), 20),
			Parametro.Texto("@Estado", estado, 20),
			Parametro.Texto("@Comentario", string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim(), 500),
			Parametro.Texto("@Usuario", usuario.Username, 100)
		], ct);
	}
}