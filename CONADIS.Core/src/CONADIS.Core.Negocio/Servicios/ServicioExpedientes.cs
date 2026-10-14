using System.Data;
using System.Net.Mail;
using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Seguridad;
using log4net;

namespace CONADIS.Core.Negocio.Servicios;

/// <summary>
/// Expediente Único y solicitudes (RF-CORE-01): registro, consultas internas, consultas del ciudadano
/// y resultados de las validaciones de identidad (JCE) y diagnóstico (MSP).
/// </summary>
public sealed class ServicioExpedientes(EjecutorSp db)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioExpedientes));

    private static readonly string[] Tipos = ["Certificacion", "Renovacion", "Duplicado"];
    private static readonly string[] Canales = ["Web", "Presencial"];
    private static readonly string[] EstadosIdentidad = ["Validada", "Pendiente"];
    private static readonly string[] EstadosDiagnostico = ["Validado", "NoCoincide", "NoEncontrado", "ValidacionManual"];

    // ------------------------------------------------------------------ registro

    /// <summary>
    /// Registra una solicitud y crea (o reutiliza) el Expediente Único de la persona.
    /// Las validaciones de formato se hacen aquí; las de negocio (solicitud activa duplicada,
    /// renovación, duplicado...) las hace el SP y llegan como <see cref="ReglaNegocioException"/>.
    /// </summary>
    public async Task<SolicitudRegistrada> RegistrarSolicitudAsync(NuevaSolicitud s, ContextoAccion contexto,
        CancellationToken ct = default)
    {
        var cedula = string.IsNullOrWhiteSpace(s.Cedula) ? null : ValidadorCedula.Normalizar(s.Cedula);
        var cedulaCuenta = string.IsNullOrWhiteSpace(s.CedulaCuenta) ? null : ValidadorCedula.Normalizar(s.CedulaCuenta);
        var cedulaTutor = string.IsNullOrWhiteSpace(s.CedulaTutor) ? null : ValidadorCedula.Normalizar(s.CedulaTutor);

        ValidarSolicitud(s, cedula, cedulaCuenta, cedulaTutor);

        var tabla = await db.ConsultarAsync("core.ppSolicitud_Registrar",
        [
            Parametro.Texto("@Canal", s.Canal, 20),
            Parametro.Texto("@UsuarioRegistro", contexto.Usuario, 100),
            Parametro.TextoAnsi("@CedulaCuenta", cedulaCuenta, 11),
            Parametro.Texto("@Tipo", s.Tipo, 20),
            Parametro.TextoAnsi("@Cedula", cedula, 11),
            Parametro.Texto("@ActaNacimiento", Limpiar(s.ActaNacimiento), 100),
            Parametro.Texto("@Nombres", s.Nombres.Trim(), 100),
            Parametro.Texto("@Apellidos", s.Apellidos.Trim(), 100),
            Parametro.Fecha("@FechaNacimiento", s.FechaNacimiento),
            Parametro.Entero("@Sexo", s.Sexo),
            Parametro.Texto("@EstadoIdentidad", s.EstadoIdentidad, 20),
            Parametro.Texto("@Telefono", Limpiar(s.Telefono), 20),
            Parametro.Texto("@Correo", Limpiar(s.Correo), 150),
            Parametro.Texto("@CanalNotificacion", s.CanalNotificacion, 10),
            Parametro.Texto("@Provincia", Limpiar(s.Provincia), 60),
            Parametro.Texto("@Municipio", Limpiar(s.Municipio), 60),
            Parametro.Texto("@Sector", Limpiar(s.Sector), 100),
            Parametro.Texto("@Direccion", Limpiar(s.Direccion), 250),
            Parametro.TextoAnsi("@CedulaTutor", cedulaTutor, 11),
            Parametro.Texto("@VinculoTutor", Limpiar(s.VinculoTutor), 50),
            Parametro.Texto("@Establecimiento", Limpiar(s.Establecimiento), 200),
            Parametro.Texto("@NumeroCertificadoMedico", Limpiar(s.NumeroCertificadoMedico), 50),
            Parametro.Fecha("@FechaCertificadoMedico", s.FechaCertificadoMedico),
            Parametro.TextoAnsi("@CodigoCie10", Limpiar(s.CodigoCie10)?.ToUpperInvariant(), 10),
            Parametro.Texto("@EstadoDiagnostico", s.EstadoDiagnostico, 30),
            Parametro.Entero("@CentroPreferidoId", s.CentroPreferidoId),
            Parametro.FechaHora("@FechaCitaPreferida", s.FechaCitaPreferida),
            Parametro.Texto("@Apoyos", Limpiar(s.Apoyos), 300),
            Parametro.Booleano("@VisitaDomiciliaria", s.VisitaDomiciliaria),
            Parametro.Texto("@MotivoDuplicado", Limpiar(s.MotivoDuplicado), 50),
            Parametro.Booleano("@ConsentimientoDatos", s.ConsentimientoDatos)
        ], ct);

        var f = tabla.Rows[0];
        var resultado = new SolicitudRegistrada(f.Texto("NumeroTramite"), f.Texto("NumeroExpediente"),
            f.FechaHora("FechaRegistro"), f.TextoONulo("NumeroOrden"));

        Log.Info($"Solicitud {resultado.NumeroTramite} ({s.Tipo}, {s.Canal}) registrada en {resultado.NumeroExpediente} por {contexto.Usuario}.");
        return resultado;
    }

    private static void ValidarSolicitud(NuevaSolicitud s, string? cedula, string? cedulaCuenta, string? cedulaTutor)
    {
        if (!Canales.Contains(s.Canal)) throw new ReglaNegocioException(0, "Canal no válido (Web o Presencial).");
        if (!Tipos.Contains(s.Tipo)) throw new ReglaNegocioException(0, "Tipo de solicitud no válido.");
        if (!EstadosIdentidad.Contains(s.EstadoIdentidad))
            throw new ReglaNegocioException(0, "El estado de identidad debe ser Validada o Pendiente.");
        if (!EstadosDiagnostico.Contains(s.EstadoDiagnostico))
            throw new ReglaNegocioException(0, "Estado de validación del diagnóstico no válido.");

        if (cedula is null && string.IsNullOrWhiteSpace(s.ActaNacimiento))
            throw new ReglaNegocioException(0, "Escribe la cédula o, si es un menor sin cédula, los datos del acta de nacimiento.");
        if (cedula is not null && !ValidadorCedula.EsValida(cedula))
            throw new ReglaNegocioException(0, "La cédula no es válida. Revisa los 11 dígitos.");
        if (cedulaCuenta is not null && !ValidadorCedula.EsValida(cedulaCuenta))
            throw new ReglaNegocioException(0, "La cédula de quien presenta la solicitud no es válida.");
        if (cedulaTutor is not null && !ValidadorCedula.EsValida(cedulaTutor))
            throw new ReglaNegocioException(0, "La cédula del tutor no es válida.");

        if (string.IsNullOrWhiteSpace(s.Nombres) || string.IsNullOrWhiteSpace(s.Apellidos))
            throw new ReglaNegocioException(0, "Escribe los nombres y apellidos.");
        if (!string.IsNullOrWhiteSpace(s.Correo) && !MailAddress.TryCreate(s.Correo.Trim(), out _))
            throw new ReglaNegocioException(0, "El correo no es válido.");
        if (s.FechaNacimiento is { } nacimiento && nacimiento > DateOnly.FromDateTime(DateTime.Today))
            throw new ReglaNegocioException(0, "La fecha de nacimiento no puede ser futura.");
        if (s.FechaCitaPreferida is { } cita && cita <= DateTimeOffset.Now)
            throw new ReglaNegocioException(0, "La fecha de cita preferida debe ser futura.");
        if (!s.ConsentimientoDatos)
            throw new ReglaNegocioException(0, "Debes autorizar el tratamiento de tus datos de salud (Ley 172-13) para continuar.");
    }

    private static string? Limpiar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    // ------------------------------------------------------------------ consultas internas

    /// <summary>Busca por número de expediente o trámite, cédula o nombre. Los evaluadores solo ven su centro.</summary>
    public Task<List<ExpedienteResumen>> BuscarAsync(UsuarioSesion usuario, string? texto, string? estado = null,
        CancellationToken ct = default)
    {
        var centro = usuario.Rol == Roles.Evaluador ? usuario.CentroId ?? -1 : (int?)null;
        return db.ConsultarListaAsync("core.ppExpediente_Buscar", f => new ExpedienteResumen(
                f.Entero("ExpedienteId"), f.Texto("NumeroExpediente"), f.TextoONulo("Cedula"), f.Texto("NombreCompleto"),
                f.Texto("Estado"), f.TextoONulo("TipoDiscapacidad"), f.TextoONulo("GradoDiscapacidad"), f.TextoONulo("Centro"),
                f.Texto("EstadoIdentidad"), f.FechaHora("FechaActualizacion")),
            [
                Parametro.Texto("@Texto", Limpiar(texto), 100),
                Parametro.Texto("@Estado", Limpiar(estado), 20),
                Parametro.Entero("@CentroId", centro)
            ], ct);
    }

    /// <summary>Expediente completo (8 conjuntos de resultados). Null si no existe.</summary>
    public async Task<ExpedienteCompleto?> ObtenerAsync(int expedienteId, CancellationToken ct = default)
    {
        var ds = await db.ConsultarVariosAsync("core.ppExpediente_Obtener", [Parametro.Entero("@ExpedienteId", expedienteId)], ct);
        if (ds.Tables.Count < 8 || ds.Tables[0].Rows.Count == 0) return null;

        var c = ds.Tables[0].Rows[0];
        var cabecera = new ExpedienteCabecera(c.Entero("ExpedienteId"), c.Texto("NumeroExpediente"), c.Texto("Estado"),
            c.TextoONulo("TipoDiscapacidad"), c.TextoONulo("GradoDiscapacidad"), c.FechaONula("FechaVencimiento"),
            c.EnteroONulo("CentroId"), c.TextoONulo("Centro"), c.FechaHora("FechaCreacion"), c.FechaHora("FechaActualizacion"),
            c.FechaHoraONula("FechaRechazo"), c.Entero("PersonaId"), c.TextoONulo("Cedula"), c.TextoONulo("ActaNacimiento"),
            c.Texto("Nombres"), c.Texto("Apellidos"), c.FechaONula("FechaNacimiento"), c.EnteroONulo("Sexo"),
            c.TextoONulo("Telefono"), c.TextoONulo("Correo"), c.Texto("CanalNotificacion"), c.TextoONulo("Provincia"),
            c.TextoONulo("Municipio"), c.TextoONulo("Sector"), c.TextoONulo("Direccion"), c.Texto("EstadoIdentidad"),
            c.FechaHoraONula("FechaValidacionJce"));

        return new ExpedienteCompleto(
            cabecera,
            Mapear(ds.Tables[1], f => new SolicitudExpediente(f.Entero("SolicitudId"), f.Texto("NumeroTramite"), f.Texto("Tipo"),
                f.Texto("Canal"), f.Texto("Estado"), f.TextoONulo("Establecimiento"), f.TextoONulo("NumeroCertificadoMedico"),
                f.TextoONulo("CodigoCie10"), f.Texto("EstadoDiagnostico"), f.TextoONulo("Apoyos"), f.Booleano("VisitaDomiciliaria"),
                f.TextoONulo("MotivoDuplicado"), f.FechaHoraONula("FechaCitaPreferida"), f.TextoONulo("CentroPreferido"),
                f.FechaHora("FechaRegistro"), f.Texto("UsuarioRegistro"))),
            Mapear(ds.Tables[2], f => new CambioEstado(f.TextoONulo("EstadoAnterior"), f.Texto("EstadoNuevo"), f.Texto("Unidad"),
                f.Texto("Usuario"), f.TextoONulo("Comentario"), f.FechaHora("Fecha"))),
            Mapear(ds.Tables[3], f => new CitaExpediente(f.Entero("CitaId"), f.Texto("Centro"), f.FechaHora("FechaHora"),
                f.Booleano("Domiciliaria"), f.Texto("Estado"))),
            Mapear(ds.Tables[4], f => new EvaluacionResumen(f.Entero("EvaluacionId"), f.FechaHora("FechaEvaluacion"), f.Texto("Estado"),
                f.DecimalONulo("PuntajeWhodas"), f.TextoONulo("GradoPropuesto"), f.TextoONulo("TipoPropuesto"),
                f.TextoONulo("VersionBaremo"), f.TextoONulo("Dictamen"))),
            Mapear(ds.Tables[5], f => new CarnetExpediente(f.Entero("CarnetId"), f.Texto("NumeroCarnet"), f.Texto("NumeroCertificado"),
                f.FechaONula("FechaEmision"), f.FechaONula("FechaVencimiento"), f.Texto("Estado"), f.TextoONulo("MotivoAnulacion"))),
            Mapear(ds.Tables[6], f => new ApelacionExpediente(f.Entero("ApelacionId"), f.Texto("Motivo"), f.Texto("Estado"),
                f.TextoONulo("Resolucion"), f.FechaHora("FechaRegistro"), f.FechaHoraONula("FechaResolucion"))),
            Mapear(ds.Tables[7], f => new OrdenExpediente(f.Texto("NumeroOrden"), f.Texto("Concepto"), f.Decimal("Total"),
                f.Texto("Estado"), f.FechaHora("FechaCreacion"), f.FechaHoraONula("FechaPago"), f.TextoONulo("NumeroComprobante"))));
    }

    // ------------------------------------------------------------------ consultas del ciudadano

    /// <summary>
    /// Un trámite visto por el ciudadano. Devuelve null tanto si no existe como si no le pertenece,
    /// para no revelar qué números existen (RF-WEB-02).
    /// </summary>
    public async Task<TramiteCiudadano?> ObtenerTramiteCiudadanoAsync(string numero, string cedula, CancellationToken ct = default)
    {
        var cedulaNormal = ValidadorCedula.Normalizar(cedula);
        if (string.IsNullOrWhiteSpace(numero) || cedulaNormal.Length != 11) return null;

        var ds = await db.ConsultarVariosAsync("core.ppTramite_ObtenerCiudadano",
            [Parametro.TextoAnsi("@Numero", numero.Trim().ToUpperInvariant(), 20), Parametro.TextoAnsi("@Cedula", cedulaNormal, 11)], ct);

        if (ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return null;

        var c = ds.Tables[0].Rows[0];
        var historial = ds.Tables.Count > 1
            ? Mapear(ds.Tables[1], f => new HitoTramite(f.Texto("Estado"), f.FechaHora("Fecha"), f.TextoONulo("Comentario")))
            : [];

        return new TramiteCiudadano(c.Texto("Numero"), c.TextoONulo("NumeroExpediente"), c.Texto("Cedula"),
            c.Texto("NombreCompleto"), c.Texto("Estado"), c.FechaHora("FechaRegistro"), c.FechaHora("FechaActualizacion"),
            c.TextoONulo("TipoDiscapacidad"), c.TextoONulo("GradoDiscapacidad"), c.TextoONulo("NumeroCarnet"),
            c.TextoONulo("CentroValoracion"), c.FechaHoraONula("FechaCita"), c.TextoONulo("Observaciones"), historial);
    }

    /// <summary>Panel "Mis trámites": los de la cédula y los de las personas que representa.</summary>
    public Task<List<TramiteResumen>> ListarTramitesPorCedulaAsync(string cedula, CancellationToken ct = default)
        => db.ConsultarListaAsync("core.ppTramite_ListarPorCedula",
            f => new TramiteResumen(f.Texto("Numero"), f.Texto("Tipo"), f.Texto("Estado"), f.FechaHora("Actualizado"), f.Texto("Persona")),
            [Parametro.TextoAnsi("@Cedula", ValidadorCedula.Normalizar(cedula), 11)], ct);

    // ------------------------------------------------------------------ identidad y diagnóstico

    /// <summary>Resultado de una revalidación con la JCE hecha en segundo plano por Integración (RF-INT-01).</summary>
    public Task RegistrarValidacionJceAsync(string cedula, bool esValida, string? nombres, string? apellidos,
        DateOnly? fechaNacimiento, int? sexo, ContextoAccion contexto, CancellationToken ct = default)
        => db.EjecutarAsync("core.ppPersona_RegistrarValidacionJce",
        [
            Parametro.TextoAnsi("@Cedula", ValidadorCedula.Normalizar(cedula), 11),
            Parametro.Texto("@Resultado", esValida ? "Validada" : "Invalida", 20),
            Parametro.Texto("@Nombres", nombres, 100),
            Parametro.Texto("@Apellidos", apellidos, 100),
            Parametro.Fecha("@FechaNacimiento", fechaNacimiento),
            Parametro.Entero("@Sexo", sexo),
            Parametro.Texto("@Usuario", contexto.Usuario, 100)
        ], ct);

    /// <summary>Personas con identidad pendiente: Integración las reintenta; tras 24 h pasan al analista.</summary>
    public Task<List<PersonaIdentidadPendiente>> ListarIdentidadPendienteAsync(int horasMaximas = 24, CancellationToken ct = default)
        => db.ConsultarListaAsync("core.ppPersona_ListarIdentidadPendiente", f => new PersonaIdentidadPendiente(
                f.Entero("PersonaId"), f.Texto("Cedula"), f.Texto("Nombres"), f.Texto("Apellidos"),
                f.TextoONulo("NumeroExpediente"), f.FechaHora("FechaCreacion"), f.Entero("RequiereAnalista") == 1),
            [Parametro.Entero("@HorasMaximas", horasMaximas)], ct);

    /// <summary>El analista verificó el documento físico (validación manual de identidad).</summary>
    public Task AprobarIdentidadManualAsync(UsuarioSesion usuario, int personaId, string comentario, CancellationToken ct = default)
    {
        Autorizacion.Exigir(usuario, Roles.AnalistaRegistro, Roles.Administrador);
        if (string.IsNullOrWhiteSpace(comentario) || comentario.Trim().Length < 10)
            throw new ReglaNegocioException(0, "Describe la evidencia revisada (mínimo 10 caracteres).");

        return db.EjecutarAsync("core.ppPersona_AprobarIdentidadManual",
        [
            Parametro.Entero("@PersonaId", personaId),
            Parametro.Texto("@Usuario", usuario.Username, 100),
            Parametro.Texto("@Comentario", comentario.Trim(), 500)
        ], ct);
    }

    /// <summary>Resultado de la validación del diagnóstico con el MSP (RF-INT-02).</summary>
    public Task RegistrarValidacionMspAsync(string numeroTramite, string estado, ContextoAccion contexto, CancellationToken ct = default)
    {
        if (!EstadosDiagnostico.Contains(estado))
            throw new ReglaNegocioException(0, "Estado de validación del diagnóstico no válido.");

        return db.EjecutarAsync("core.ppSolicitud_RegistrarValidacionMsp",
        [
            Parametro.TextoAnsi("@NumeroTramite", numeroTramite.Trim(), 20),
            Parametro.Texto("@Estado", estado, 30),
            Parametro.Texto("@Usuario", contexto.Usuario, 100)
        ], ct);
    }

    internal static List<T> Mapear<T>(DataTable tabla, Func<DataRow, T> mapear)
    {
        var lista = new List<T>(tabla.Rows.Count);
        foreach (DataRow fila in tabla.Rows) lista.Add(mapear(fila));
        return lista;
    }
}