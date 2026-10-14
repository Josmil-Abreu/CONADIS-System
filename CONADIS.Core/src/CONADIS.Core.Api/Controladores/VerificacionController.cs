using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Servicios;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Core.Api.Controladores;

/// <summary>Verificación de carnets: pública por QR (RINV-02) e institucional por convenio (RF-INT-03).</summary>
[Route("api/v1/verificacion")]
public sealed class VerificacionController(ServicioCarnets carnets) : ControladorBase
{
    /// <summary>Lo que ve quien escanea el QR: estado, nombre enmascarado y fechas. Nunca la cédula ni el diagnóstico.</summary>
    [HttpGet("carnets/{codigo}")]
    public Task<VerificacionPublica> Publica(string codigo, CancellationToken ct)
        => carnets.VerificarPublicoAsync(codigo, ct);

    /// <summary>Para instituciones con convenio: si tiene certificación vigente y sus atributos mínimos.</summary>
    [HttpGet("institucional/{cedula}")]
    public Task<VerificacionInstitucional> Institucional(string cedula, CancellationToken ct)
        => carnets.VerificarInstitucionalAsync(cedula, ct);
}