namespace CONADIS.Core.Negocio.Modelos;

public sealed record EventoAuditoria(long EventoId, DateTimeOffset Fecha, string Usuario, string? Ip, string Accion,
    string Entidad, string? EntidadId, string? Detalle);

public sealed record ProblemaCadena(long EventoId, DateTimeOffset Fecha, string Usuario, string Accion, string Problema);

public sealed record RegistroLog(int Id, DateTimeOffset Fecha, string Nivel, string Origen, string Mensaje, string? Excepcion);