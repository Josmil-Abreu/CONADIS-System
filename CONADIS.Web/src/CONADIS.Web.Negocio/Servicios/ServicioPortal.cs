using System.Text.Json;
using CONADIS.Web.Negocio.Datos;
using CONADIS.Web.Negocio.Externos;
using CONADIS.Web.Negocio.Modelos;
using CONADIS.Web.Negocio.Seguridad;

namespace CONADIS.Web.Negocio.Servicios;

/// <summary>
/// Todo lo que el ciudadano consulta o hace en el portal (RF-WEB-01 a RF-WEB-03), a través de Integración.
/// La cédula siempre es la de la sesión del ciudadano (CuentaSesion), nunca un dato que él escriba,
/// así nadie puede consultar trámites ajenos cambiando la URL.
/// </summary>
public sealed class ServicioPortal(ClienteSnd snd)
{
    private const string Base = "api/v1/portal";

    // ------------------------------------------------------------------ catálogos (no requieren sesión)

    public async Task<List<CentroValoracion>> CentrosAsync(CancellationToken ct = default)
        => await snd.GetAsync<List<CentroValoracion>>($"{Base}/catalogos/centros", new ContextoPortal(), ct) ?? [];

    public async Task<List<CupoDisponible>> DisponibilidadAsync(int centroId, int dias = 15, CancellationToken ct = default)
        => await snd.GetAsync<List<CupoDisponible>>($"{Base}/catalogos/centros/{centroId}/disponibilidad?dias={Math.Clamp(dias, 1, 60)}",
            new ContextoPortal(), ct) ?? [];

    public async Task<List<Tasa>> TasasAsync(CancellationToken ct = default)
        => await snd.GetAsync<List<Tasa>>($"{Base}/catalogos/tasas", new ContextoPortal(), ct) ?? [];

    /// <summary>Verificación pública de un carnet por su código QR (página pública, sin sesión).</summary>
    public async Task<VerificacionPublica> VerificarCarnetAsync(string codigo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length > 64) return new VerificacionPublica("NoEncontrado", null, null, null);
        return await snd.GetAsync<VerificacionPublica>($"api/v1/verificacion/carnets/{Uri.EscapeDataString(codigo.Trim())}", new ContextoPortal(), ct)
               ?? new VerificacionPublica("NoEncontrado", null, null, null);
    }

    // ------------------------------------------------------------------ mis trámites (RF-WEB-02)

    public async Task<List<TramiteResumen>> MisTramitesAsync(CuentaSesion c, string? ip = null, CancellationToken ct = default)
        => await snd.GetAsync<List<TramiteResumen>>($"{Base}/ciudadanos/{c.Cedula}/tramites", Ctx(c, ip), ct) ?? [];

    /// <summary>Detalle con su historial. Null si no existe o no pertenece al ciudadano.</summary>
    public Task<TramiteCiudadano?> TramiteAsync(CuentaSesion c, string numero, string? ip = null, CancellationToken ct = default)
        => snd.GetAsync<TramiteCiudadano>($"{Base}/ciudadanos/{c.Cedula}/tramites/{Uri.EscapeDataString(numero.Trim())}", Ctx(c, ip), ct);

    /// <summary>
    /// Consulta pública "¿cómo va mi trámite?" sin cuenta: exige la cédula del titular junto con el número,
    /// así no basta con adivinar números de trámite. Null si no coinciden.
    /// </summary>
    public Task<TramiteCiudadano?> ConsultarPublicoAsync(string cedula, string numero, string? ip = null, CancellationToken ct = default)
    {
        var c = ValidadorCedula.Normalizar(cedula);
        if (!ValidadorCedula.EsValida(c) || string.IsNullOrWhiteSpace(numero)) return Task.FromResult<TramiteCiudadano?>(null);
        return snd.GetAsync<TramiteCiudadano>($"{Base}/ciudadanos/{c}/tramites/{Uri.EscapeDataString(numero.Trim())}", new ContextoPortal(c, ip), ct);
    }

    /// <summary>Identidad en la JCE (vía Integración) para prellenar el asistente de solicitud. Null si no se pudo consultar.</summary>
    public Task<IdentidadJce?> IdentidadAsync(string cedula, string? ip = null, CancellationToken ct = default)
    {
        var c = ValidadorCedula.Normalizar(cedula);
        if (!ValidadorCedula.EsValida(c)) throw new ReglaNegocioException(0, "La cédula no es válida. Revise los 11 dígitos.");
        return snd.GetAsync<IdentidadJce>($"{Base}/identidad/{c}", new ContextoPortal(c, ip), ct);
    }

    public Task<CarnetCiudadano?> MiCarnetAsync(CuentaSesion c, string? ip = null, CancellationToken ct = default)
        => snd.GetAsync<CarnetCiudadano>($"{Base}/ciudadanos/{c.Cedula}/carnet", Ctx(c, ip), ct);

    public async Task<List<AvisoPortal>> AvisosAsync(CuentaSesion c, int maximo = 10, CancellationToken ct = default)
        => await snd.GetAsync<List<AvisoPortal>>($"{Base}/ciudadanos/{c.Cedula}/avisos?maximo={maximo}", Ctx(c, null), ct) ?? [];

    public Task ReprogramarCitaAsync(CuentaSesion c, string numero, DateTimeOffset nuevaFecha, string? ip = null, CancellationToken ct = default)
    {
        if (nuevaFecha <= DateTimeOffset.Now) throw new ReglaNegocioException(0, "Elija una fecha futura.");
        return snd.EnviarSinRespuestaAsync(HttpMethod.Put, $"{Base}/ciudadanos/{c.Cedula}/tramites/{Uri.EscapeDataString(numero)}/cita",
            new { nuevaFecha }, Ctx(c, ip), ct: ct);
    }

    public Task ApelarAsync(CuentaSesion c, string numero, string motivo, string? ip = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 20)
            throw new ReglaNegocioException(0, "Explique el motivo de su apelación (mínimo 20 caracteres).");
        return snd.EnviarSinRespuestaAsync(HttpMethod.Post, $"{Base}/ciudadanos/{c.Cedula}/tramites/{Uri.EscapeDataString(numero)}/apelacion",
            new { motivo = motivo.Trim() }, Ctx(c, ip), $"apelacion-{c.CuentaId}-{numero}", ct);
    }

    // ------------------------------------------------------------------ solicitudes (RF-WEB-01 y RF-WEB-03)

    /// <summary>
    /// Envía la solicitud. La cuenta que la presenta es la de la sesión (cedulaCuenta). Integración valida
    /// la identidad con la JCE y el diagnóstico con el MSP antes de registrarla en el Core.
    /// La clave de idempotencia (del borrador) evita duplicados si el ciudadano pulsa "Enviar" dos veces.
    /// </summary>
    public Task<SolicitudRegistrada> EnviarSolicitudAsync(CuentaSesion c, SolicitudPortal s, string claveIdempotencia, string? ip = null,
        CancellationToken ct = default)
    {
        ValidarSolicitud(s);
        var cuerpo = JsonSerializer.SerializeToNode(s, ClienteSnd.Json)!.AsObject();
        cuerpo["canal"] = "Web";
        cuerpo["cedulaCuenta"] = c.Cedula;
        cuerpo["estadoIdentidad"] = "Pendiente";   // lo decide Integración con la JCE
        if (s.Cedula is not null) cuerpo["cedula"] = ValidadorCedula.Normalizar(s.Cedula);
        return snd.PostAsync<SolicitudRegistrada>($"{Base}/solicitudes", cuerpo, Ctx(c, ip), claveIdempotencia, ct);
    }

    public Task<ServicioRegistrado> EnviarServicioAsync(CuentaSesion c, SolicitudServicioPortal s, string claveIdempotencia, string? ip = null,
        CancellationToken ct = default)
    {
        if (s.Tipo is not ("AYUDA_TECNICA" or "CERT_ACCESIBILIDAD")) throw new ReglaNegocioException(0, "Tipo de solicitud no válido.");
        if (!ValidacionesPortal.CorreoValido(s.Correo)) throw new ReglaNegocioException(0, "El correo electrónico no es válido.");
        if (string.IsNullOrWhiteSpace(s.Resumen) || string.IsNullOrWhiteSpace(s.Detalle))
            throw new ReglaNegocioException(0, "Complete el resumen y el detalle de la solicitud.");
        return snd.PostAsync<ServicioRegistrado>($"{Base}/servicios", s, Ctx(c, ip), claveIdempotencia, ct);
    }

    // ------------------------------------------------------------------ pagos en línea

    public Task<OrdenCobro?> OrdenAsync(CuentaSesion c, string numeroOrden, CancellationToken ct = default)
        => snd.GetAsync<OrdenCobro>($"{Base}/ordenes/{Uri.EscapeDataString(numeroOrden.Trim())}", Ctx(c, null), ct);

    /// <summary>
    /// Cobra una orden con el token de la tarjeta (la pasarela tokeniza en el navegador; el portal nunca ve el número).
    /// La clave de idempotencia es obligatoria: un reintento no debe cobrar dos veces.
    /// </summary>
    public async Task<PagoEnLinea> PagarAsync(CuentaSesion c, string numeroOrden, string tokenTarjeta, string claveIdempotencia,
        string? ip = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(claveIdempotencia)) throw new ArgumentException("Falta la clave de idempotencia.", nameof(claveIdempotencia));
        var orden = await OrdenAsync(c, numeroOrden, ct) ?? throw new ReglaNegocioException(0, "La orden de cobro no existe.");
        if (orden.Documento != c.Cedula) throw new ReglaNegocioException(0, "Esta orden no corresponde a su cuenta.");
        if (orden.Estado != "Pendiente") throw new ReglaNegocioException(0, $"La orden está {orden.Estado}.");

        return await snd.PostAsync<PagoEnLinea>($"{Base}/pagos/en-linea", new { numeroOrden = orden.NumeroOrden, tokenTarjeta },
            Ctx(c, ip), claveIdempotencia, ct);
    }

    internal static void ValidarSolicitud(SolicitudPortal s)
    {
        if (s.Tipo is not ("Certificacion" or "Renovacion" or "Duplicado")) throw new ReglaNegocioException(0, "Tipo de solicitud no válido.");
        if (string.IsNullOrWhiteSpace(s.Cedula) && string.IsNullOrWhiteSpace(s.ActaNacimiento))
            throw new ReglaNegocioException(0, "Indique la cédula o, si es un menor sin cédula, los datos del acta de nacimiento.");
        if (!string.IsNullOrWhiteSpace(s.Cedula) && !ValidadorCedula.EsValida(s.Cedula))
            throw new ReglaNegocioException(0, "La cédula no es válida. Revise los 11 dígitos.");
        if (!string.IsNullOrWhiteSpace(s.CedulaTutor) && !ValidadorCedula.EsValida(s.CedulaTutor))
            throw new ReglaNegocioException(0, "La cédula del tutor no es válida.");
        if (!string.IsNullOrWhiteSpace(s.Correo) && !ValidacionesPortal.CorreoValido(s.Correo))
            throw new ReglaNegocioException(0, "El correo electrónico no es válido.");
        if (s.Tipo == "Duplicado" && string.IsNullOrWhiteSpace(s.MotivoDuplicado))
            throw new ReglaNegocioException(0, "Indique el motivo del duplicado.");
        if (!s.ConsentimientoDatos)
            throw new ReglaNegocioException(0, "Debe autorizar el tratamiento de sus datos de salud (Ley 172-13) para continuar.");
    }

    private static ContextoPortal Ctx(CuentaSesion c, string? ip) => new(c.Cedula, ip);
}

/// <summary>Borradores del formulario de solicitud por pasos (continuar más tarde sin perder lo escrito).</summary>
public sealed class ServicioBorradores(EjecutorSp db)
{
    private static readonly string[] Tipos =
        [TiposBorrador.Certificacion, TiposBorrador.Renovacion, TiposBorrador.Duplicado, TiposBorrador.AyudaTecnica, TiposBorrador.Accesibilidad];

    public async Task<Borrador> GuardarAsync(int cuentaId, string tipo, int paso, object datos, CancellationToken ct = default)
    {
        if (!Tipos.Contains(tipo)) throw new ReglaNegocioException(0, "Tipo de borrador no válido.");
        var json = datos as string ?? JsonSerializer.Serialize(datos, ClienteSnd.Json);
        var tabla = await db.ConsultarAsync("web.ppBorrador_Guardar",
            [Parametro.Entero("@CuentaId", cuentaId), Parametro.Texto("@Tipo", tipo, 30), Parametro.Entero("@Paso", paso), Parametro.Texto("@Datos", json)], ct);
        return Mapear(tabla.Rows[0]);
    }

    public Task<Borrador?> ObtenerAsync(int cuentaId, string tipo, CancellationToken ct = default)
        => db.ConsultarUnoAsync("web.ppBorrador_Obtener", Mapear, [Parametro.Entero("@CuentaId", cuentaId), Parametro.Texto("@Tipo", tipo, 30)], ct);

    /// <summary>Lee los datos del borrador como el tipo indicado (ej.: SolicitudPortal).</summary>
    public static T? Leer<T>(Borrador b) => JsonSerializer.Deserialize<T>(b.Datos, ClienteSnd.Json);

    public Task EliminarAsync(int cuentaId, string tipo, CancellationToken ct = default)
        => db.EjecutarAsync("web.ppBorrador_Eliminar", [Parametro.Entero("@CuentaId", cuentaId), Parametro.Texto("@Tipo", tipo, 30)], ct);

    private static Borrador Mapear(System.Data.DataRow f)
        => new(f.Entero("Paso"), f.Texto("Datos"), f.Texto("ClaveIdempotencia"), f.FechaHora("FechaActualizacion"));
}
