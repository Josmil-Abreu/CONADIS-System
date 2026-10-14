using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Seguridad;
using CONADIS.Core.Negocio.Servicios;

namespace CONADIS.Core.Pruebas;

/// <summary>
/// CU-01 completo desde C#, con los usuarios reales de cada rol:
/// solicitud -> cita -> valoración CIF de 3 disciplinas -> WHODAS -> consolidación ->
/// dictamen por quórum -> carnet firmado -> verificación pública -> consulta del ciudadano.
/// Crea datos reales con una cédula nueva en cada ejecución (quedan como historial de pruebas).
/// </summary>
public class FlujoCompletoPruebas
{
    private readonly EjecutorSp _db = BaseDatosPrueba.Ejecutor();

    [Fact]
    public async Task CU01_de_la_solicitud_al_carnet_verificado()
    {
        var analista = await DatosPrueba.SesionAsync("analista");
        var medico = await DatosPrueba.SesionAsync("medico");
        var psicologo = await DatosPrueba.SesionAsync("psicologo");
        var social = await DatosPrueba.SesionAsync("social");
        var comite1 = await DatosPrueba.SesionAsync("comite1");
        var comite2 = await DatosPrueba.SesionAsync("comite2");

        var centro = (await new ServicioCatalogos(_db).ListarCentrosAsync()).Single(c => c.Nombre == DatosPrueba.CentroSede);
        var expedientes = new ServicioExpedientes(_db);
        var cedula = DatosPrueba.CedulaNueva();

        // 1) Solicitud presencial
        var solicitud = new NuevaSolicitud
        {
            Canal = "Presencial",
            Tipo = "Certificacion",
            Cedula = cedula,
            Nombres = "Prueba",
            Apellidos = "Automatizada Flujo",
            FechaNacimiento = new DateOnly(1990, 5, 15),
            Sexo = 1,
            EstadoIdentidad = "Validada",
            Correo = "prueba@correo.do",
            Provincia = "Distrito Nacional",
            CentroPreferidoId = centro.CentroId,
            ConsentimientoDatos = true
        };
        var registrada = await expedientes.RegistrarSolicitudAsync(solicitud, new ContextoAccion(analista.Username));
        Assert.StartsWith("TRA-", registrada.NumeroTramite);
        Assert.StartsWith("EXP-", registrada.NumeroExpediente);

        // Una segunda solicitud activa para la misma persona se rechaza e informa el trámite existente (E2)
        var duplicada = await Assert.ThrowsAsync<ReglaNegocioException>(
            () => expedientes.RegistrarSolicitudAsync(solicitud, new ContextoAccion(analista.Username)));
        Assert.Contains(registrada.NumeroTramite, duplicada.Message);

        var expedienteId = (await expedientes.BuscarAsync(analista, cedula)).Single().ExpedienteId;

        // 2) Cita: Solicitado -> EnEvaluacion
        await new ServicioCitas(_db).AsignarAsync(analista, expedienteId, centro.CentroId, DatosPrueba.FechaCitaLaborable(), false);

        // 3) Valoración CIF: cada evaluador codifica con su propia disciplina
        var valoracion = new ServicioValoracion(_db);
        var evaluacionId = await valoracion.IniciarAsync(medico, expedienteId);
        await valoracion.GuardarCodificacionAsync(medico, evaluacionId, new NuevaCodificacion("b730", 3));
        await valoracion.GuardarCodificacionAsync(psicologo, evaluacionId, new NuevaCodificacion("b152", 2));
        await valoracion.GuardarCodificacionAsync(social, evaluacionId, new NuevaCodificacion("d450", 3, 2));
        await valoracion.GuardarCodificacionAsync(social, evaluacionId, new NuevaCodificacion("e120", 3, EsFacilitador: true));

        // 4) WHODAS: promedio 47.5 => Moderado según el baremo BAR-2026.1
        var leida = await valoracion.ObtenerAsync(evaluacionId);
        var whodas = new PuntajesWhodas(60, 55, 50, 45, 40, 35);
        var resultado = await valoracion.RegistrarWhodasAsync(medico, evaluacionId, leida!.RowVersion, whodas);
        Assert.Equal(47.5m, resultado.PuntajeWhodas);
        Assert.Equal("Moderado", resultado.GradoPropuesto);

        // Concurrencia optimista: guardar con la versión vieja se rechaza (no pisa el trabajo del otro)
        await Assert.ThrowsAsync<ReglaNegocioException>(
            () => valoracion.RegistrarWhodasAsync(psicologo, evaluacionId, leida.RowVersion, whodas));

        // 5) Firmas de las 3 disciplinas y consolidación
        await valoracion.FirmarAsync(medico, evaluacionId);
        await valoracion.FirmarAsync(psicologo, evaluacionId);
        await valoracion.FirmarAsync(social, evaluacionId);
        await valoracion.ConsolidarAsync(medico, evaluacionId, "Fisica");

        // 6) Dictamen: el primer voto espera quórum, el segundo lo emite
        var dictamen = new ServicioDictamen(_db);
        Assert.Equal("PendienteQuorum", (await dictamen.VotarAsync(comite1, new NuevoVoto(evaluacionId, "Aprobar"))).Resultado);
        Assert.Equal("Aprobado", (await dictamen.VotarAsync(comite2, new NuevoVoto(evaluacionId, "Aprobar"))).Resultado);

        // 7) Carnet firmado: Aprobado -> Carnetizado
        var carnets = new ServicioCarnets(_db, new FirmaFalsa());
        var carnet = await carnets.EmitirAsync(analista, expedienteId);
        Assert.StartsWith("PCD-", carnet.NumeroCarnet);
        Assert.StartsWith("CERT-", carnet.NumeroCertificado);

        // 8) Verificación pública por QR: sin datos sensibles (RINV-02)
        var verificacion = await carnets.VerificarPublicoAsync(carnet.CodigoVerificacion);
        Assert.Equal("Vigente", verificacion.Resultado);
        Assert.Equal("P*** A*** F***", verificacion.TitularParcial);

        // 9) El ciudadano ve su trámite carnetizado; otra cédula no lo ve (sin enumeración)
        var tramite = await expedientes.ObtenerTramiteCiudadanoAsync(registrada.NumeroTramite, cedula);
        Assert.Equal("Carnetizado", tramite!.Estado);
        Assert.NotEmpty(tramite.Historial);
        Assert.Null(await expedientes.ObtenerTramiteCiudadanoAsync(registrada.NumeroTramite, DatosPrueba.CedulaNueva()));

        // 10) El expediente completo refleja todo el recorrido
        var completo = await expedientes.ObtenerAsync(expedienteId);
        Assert.Equal("Carnetizado", completo!.Cabecera.Estado);
        Assert.Equal("Moderado", completo.Cabecera.GradoDiscapacidad);
        Assert.Contains(completo.Carnets, c => c.Estado == "Vigente");
    }

    [Fact]
    public async Task Si_la_firma_digital_falla_no_se_emite_el_carnet()
    {
        var analista = await DatosPrueba.SesionAsync("analista");
        var carnets = new ServicioCarnets(_db, new FirmaCaida());

        // La firma se pide ANTES de tocar la base: si falla, no se guarda nada (E3 del CU-01)
        await Assert.ThrowsAsync<FirmaNoDisponibleException>(() => carnets.EmitirAsync(analista, int.MaxValue));
    }

    [Fact]
    public async Task Un_evaluador_no_puede_votar_en_el_comite()
    {
        var medico = await DatosPrueba.SesionAsync("medico");
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => new ServicioDictamen(_db).VotarAsync(medico, new NuevoVoto(1, "Aprobar")));
    }

    [Fact]
    public async Task Una_cedula_con_digito_verificador_incorrecto_se_rechaza_antes_de_llegar_a_la_base()
    {
        var cedula = DatosPrueba.CedulaNueva();
        var mala = cedula[..10] + ((cedula[10] - '0' + 1) % 10);

        var error = await Assert.ThrowsAsync<ReglaNegocioException>(() => new ServicioExpedientes(_db).RegistrarSolicitudAsync(
            new NuevaSolicitud
            {
                Canal = "Web",
                Tipo = "Certificacion",
                Cedula = mala,
                Nombres = "X",
                Apellidos = "Y",
                EstadoIdentidad = "Pendiente",
                ConsentimientoDatos = true
            }, new ContextoAccion("prueba")));
        Assert.Contains("cédula", error.Message);
    }
}