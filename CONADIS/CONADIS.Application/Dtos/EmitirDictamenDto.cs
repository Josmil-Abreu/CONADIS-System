namespace CONADIS.Application.Dtos
{
    public record EmitirDictamenDto(
     Guid ExpedienteId,
     bool EsAprobado,
     string? MotivoRechazo,
     int? GradoFinal,
     int? TipoFinal,
     DateOnly? FechaVencimiento
 );
}
