using System;
using System.Collections.Generic;
using System.Text;

namespace CONADIS.Application.Dtos
{
    public record ItemCifDto(
     string Codigo,
     string Componente,
     int Calificador
 );

    public record RegistrarEvaluacionDto(
        Guid ExpedienteId,
        Guid CitaId,
        List<ItemCifDto> CodificacionesCIF,
        decimal PuntajeWhodas,
        int GradoPropuesto,
        int TipoPropuesto
    );
}
