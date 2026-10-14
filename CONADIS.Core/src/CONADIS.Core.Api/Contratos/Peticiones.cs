namespace CONADIS.Core.Api.Contratos;

// Forma del JSON que reciben los endpoints cuando no coincide con un modelo de Negocio.
// Los modelos que sí coinciden (NuevaSolicitud, NuevoPago, NuevaSolicitudServicio) se reciben tal cual.
// [ApiController] responde 400 automáticamente si falta un campo obligatorio (no nulo).

public sealed record PeticionReprogramarCita(DateTimeOffset NuevaFecha);

public sealed record PeticionApelacion(string Motivo);

public sealed record PeticionOrden(string TasaCodigo, int Cantidad, string Documento, string NombrePagador, string? Referencia);

public sealed record RespuestaPago(string Estado);

public sealed record PeticionValidacionJce(string Cedula, bool EsValida, string? Nombres, string? Apellidos,
    DateOnly? FechaNacimiento, int? Sexo);

public sealed record PeticionValidacionMsp(string NumeroTramite, string Estado);

public sealed record PeticionResultadoNotificacion(bool Exito);

public sealed record RespuestaVencimientos(int CarnetsVencidos);

public sealed record RespuestaSalud(string Estado, bool BaseDatos, DateTimeOffset Hora);