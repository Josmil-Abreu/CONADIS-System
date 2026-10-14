using System.Security.Cryptography;
using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Seguridad;
using log4net;

namespace CONADIS.Core.Negocio.Servicios;

/// <summary>
/// Emisión, anulación y verificación de carnets (RF-CORE-03).
/// Nunca se emite un carnet sin firma: si el servicio de firma falla, no se guarda nada.
/// </summary>
public sealed class ServicioCarnets(EjecutorSp db, IFirmaDigital firma)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioCarnets));

    /// <summary>
    /// Emite el carnet de un expediente Aprobado (o el duplicado de uno Carnetizado con la tasa pagada).
    /// 1) Genera un identificador opaco para el QR (no contiene datos personales, RINV-01).
    /// 2) Pide la firma al servicio de firma digital (Integración).
    /// 3) Solo si hay firma, el SP guarda el carnet y cambia el estado a Carnetizado.
    /// </summary>
    public async Task<CarnetEmitido> EmitirAsync(UsuarioSesion usuario, int expedienteId, CancellationToken ct = default)
    {
        Autorizacion.Exigir(usuario, Roles.AnalistaRegistro, Roles.Administrador);

        var codigo = GenerarCodigoVerificacion();
        string firmaDigital;
        try
        {
            firmaDigital = await firma.FirmarAsync($"CONADIS|CARNET|{expedienteId}|{codigo}", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"No se pudo firmar el carnet del expediente {expedienteId}. No se emitió.", ex);
            throw new FirmaNoDisponibleException(
                "El servicio de firma digital no está disponible. El carnet no se emitió; inténtalo de nuevo más tarde.", ex);
        }

        var tabla = await db.ConsultarAsync("core.ppCarnet_Emitir",
        [
            Parametro.Entero("@ExpedienteId", expedienteId),
            Parametro.TextoAnsi("@CodigoVerificacion", codigo, 64),
            Parametro.TextoAnsi("@Firma", firmaDigital, 600),
            Parametro.Texto("@Usuario", usuario.Username, 100)
        ], ct);

        var f = tabla.Rows[0];
        var carnet = new CarnetEmitido(f.Texto("NumeroCarnet"), f.Texto("NumeroCertificado"),
            f.FechaONula("FechaEmision")!.Value, f.FechaONula("FechaVencimiento"), codigo);

        Log.Info($"Carnet {carnet.NumeroCarnet} emitido para el expediente {expedienteId} por {usuario.Username}.");
        return carnet;
    }

    /// <summary>Anulación lógica con motivo (RINV-04). Solo el Supervisor de registro.</summary>
    public Task AnularAsync(UsuarioSesion usuario, int carnetId, string motivo, CancellationToken ct = default)
    {
        Autorizacion.Exigir(usuario, Roles.SupervisorRegistro);
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 10)
            throw new ReglaNegocioException(0, "La anulación exige un motivo (mínimo 10 caracteres).");

        return db.EjecutarAsync("core.ppCarnet_Anular",
        [
            Parametro.Entero("@CarnetId", carnetId),
            Parametro.Texto("@Motivo", motivo.Trim(), 300),
            Parametro.Texto("@Usuario", usuario.Username, 100)
        ], ct);
    }

    /// <summary>"Mi carnet digital" del portal.</summary>
    public Task<CarnetCiudadano?> ObtenerPorCedulaAsync(string cedula, CancellationToken ct = default)
        => db.ConsultarUnoAsync("core.ppCarnet_ObtenerVigentePorCedula", f => new CarnetCiudadano(f.Texto("NumeroCarnet"),
                f.Texto("NumeroExpediente"), f.Texto("Cedula"), f.Texto("NombreCompleto"), f.TextoONulo("TipoDiscapacidad"),
                f.TextoONulo("GradoDiscapacidad"), f.FechaONula("FechaEmision")!.Value, f.FechaONula("FechaVencimiento"),
                f.Texto("Estado"), f.Texto("CodigoVerificacion"), f.Texto("NumeroCertificado")),
            [Parametro.TextoAnsi("@Cedula", ValidadorCedula.Normalizar(cedula), 11)], ct);

    /// <summary>
    /// Verificación pública por QR: solo estado, nombre enmascarado y fechas (RINV-02).
    /// Un código inexistente responde "NoEncontrado", sin más detalles.
    /// </summary>
    public async Task<VerificacionPublica> VerificarPublicoAsync(string codigo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length > 64)
            return new VerificacionPublica("NoEncontrado", null, null, null);

        var tabla = await db.ConsultarAsync("core.ppCarnet_Verificar",
            [Parametro.TextoAnsi("@CodigoVerificacion", codigo.Trim(), 64)], ct);
        if (tabla.Rows.Count == 0) return new VerificacionPublica("NoEncontrado", null, null, null);

        var f = tabla.Rows[0];
        return new VerificacionPublica(f.Texto("Estado"),
            ValidadorCedula.EnmascararNombre($"{f.Texto("Nombres")} {f.Texto("Apellidos")}"),
            f.FechaONula("FechaEmision"), f.FechaONula("FechaVencimiento"));
    }

    /// <summary>Verificación para instituciones con convenio (RF-INT-03): atributos mínimos.</summary>
    public async Task<VerificacionInstitucional> VerificarInstitucionalAsync(string cedula, CancellationToken ct = default)
    {
        var tabla = await db.ConsultarAsync("core.ppCarnet_VerificarInstitucional",
            [Parametro.TextoAnsi("@Cedula", ValidadorCedula.Normalizar(cedula), 11)], ct);
        if (tabla.Rows.Count == 0) return new VerificacionInstitucional(false, null, null, null);

        var f = tabla.Rows[0];
        var vigente = f.Booleano("TieneCertificacionVigente");
        return new VerificacionInstitucional(vigente,
            vigente ? f.TextoONulo("TipoDiscapacidad") : null,
            vigente ? f.TextoONulo("GradoDiscapacidad") : null,
            vigente ? f.FechaONula("FechaVencimiento") : null);
    }

    /// <summary>Proceso diario (lo dispara el servicio de Windows de Integración). Devuelve cuántos venció.</summary>
    public async Task<int> MarcarVencidosAsync(ContextoAccion contexto, CancellationToken ct = default)
    {
        var tabla = await db.ConsultarAsync("core.ppProceso_MarcarVencidos", [Parametro.Texto("@Usuario", contexto.Usuario, 100)], ct);
        var cantidad = tabla.Rows.Count > 0 ? tabla.Rows[0].Entero("CarnetsVencidos") : 0;
        if (cantidad > 0) Log.Info($"Proceso diario: {cantidad} carnet(s) vencido(s).");
        return cantidad;
    }

    /// <summary>32 caracteres aleatorios seguros (base64url). Opaco: imposible de adivinar o enumerar.</summary>
    private static string GenerarCodigoVerificacion()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_');
}