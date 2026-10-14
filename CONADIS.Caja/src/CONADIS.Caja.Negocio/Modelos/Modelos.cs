namespace CONADIS.Caja.Negocio.Modelos;

// ------------------------------------------------------------------ usuarios y cajas

public static class RolesCaja
{
    public const string Cajero = "Cajero";
    public const string Supervisor = "SupervisorCaja";
    public const string Administrador = "Administrador";
}

public sealed record UsuarioCaja(int UsuarioId, string Username, string NombreCompleto, string Rol)
{
    public bool EsSupervisor => Rol is RolesCaja.Supervisor or RolesCaja.Administrador;
}

public sealed record ResultadoLoginCaja(bool Exito, string? Mensaje, UsuarioCaja? Usuario)
{
    public static ResultadoLoginCaja Correcto(UsuarioCaja u) => new(true, null, u);
    public static ResultadoLoginCaja Fallido(string m) => new(false, m, null);
}

public sealed record CajaFisica(int CajaId, string Codigo, string Sucursal);

public sealed record SesionCaja(int SesionId, int CajaId, string CajaCodigo, string Sucursal, decimal FondoInicial, string Estado,
    DateTimeOffset FechaApertura);

public static class EstadosSesion
{
    public const string Abierta = "Abierta";
    public const string EnArqueo = "EnArqueo";
    public const string Cerrada = "Cerrada";
}

// ------------------------------------------------------------------ cobro

public sealed record TasaLocal(string Codigo, string Descripcion, decimal Monto, decimal ItbisPorcentaje, bool Activa, DateTimeOffset FechaSincronizacion);

public sealed record LineaCobro(string TasaCodigo, int Cantidad);

public static class MediosPagoCaja
{
    public const string Efectivo = "Efectivo";
    public const string Tarjeta = "Tarjeta";
    public const string Transferencia = "Transferencia";
}

/// <summary>Tipos de e-CF (RF-CAJ-02).</summary>
public static class TiposEcf
{
    public const string CreditoFiscal = "31";
    public const string Consumo = "32";
    public const string NotaCredito = "34";
    public const string Gubernamental = "45";
}

/// <summary>Lo que el cajero captura en la pantalla de cobro (CU-02).</summary>
public sealed record SolicitudCobro
{
    public string? NumeroOrden { get; init; }
    public required string DocumentoCliente { get; init; }     // cédula (11) o RNC (9)
    public required string NombreCliente { get; init; }
    public string? CorreoCliente { get; init; }
    public bool QuiereCreditoFiscal { get; init; }               // E31 (requiere RNC)
    public bool EsGubernamental { get; init; }                   // E45
    public required IReadOnlyList<LineaCobro> Lineas { get; init; }
    public required string MedioPago { get; init; }
    public decimal? MontoRecibido { get; init; }                 // efectivo
    public string? Referencia { get; init; }                     // transferencia
    public string? Autorizacion { get; init; }                   // tarjeta (POS)
}

public sealed record ComprobanteEmitido(long FacturaId, string Tipo, string Encf, string CodigoSeguridad, decimal Subtotal, decimal Itbis,
    decimal Total, decimal? Cambio, DateTimeOffset Fecha, string EstadoDgii);

public sealed record LineaComprobante(int Linea, string TasaCodigo, string Descripcion, int Cantidad, decimal PrecioUnitario,
    decimal ItbisPorcentaje, decimal Itbis, decimal Total, Guid TransaccionId, string EstadoSincronizacion);

public sealed record PagoComprobante(string MedioPago, decimal Monto, decimal? MontoRecibido, decimal? Cambio, string? Referencia, string? Autorizacion);

/// <summary>Comprobante completo (para la representación impresa / reimpresión).</summary>
public sealed record ComprobanteDetalle(long FacturaId, int SesionId, string Tipo, string Encf, string? NumeroOrden, string DocumentoCliente,
    string NombreCliente, string? CorreoCliente, decimal Subtotal, decimal Itbis, decimal Total, string CodigoSeguridad, string EstadoDgii,
    string? TrackId, string? MensajeDgii, bool Anulada, DateTimeOffset Fecha, string? EncfReferencia, string CajaCodigo, string Cajero,
    IReadOnlyList<LineaComprobante> Lineas, IReadOnlyList<PagoComprobante> Pagos);

public sealed record ComprobanteResumen(long FacturaId, string Tipo, string Encf, string NombreCliente, decimal Total, string EstadoDgii,
    bool Anulada, DateTimeOffset Fecha, string? MedioPago);

public static class EstadosDgii
{
    public const string Pendiente = "Pendiente";
    public const string Aceptado = "Aceptado";
    public const string AceptadoCondicional = "AceptadoCondicional";
    public const string Rechazado = "Rechazado";
    public const string Contingencia = "Contingencia";
}

// ------------------------------------------------------------------ arqueo y cierre

public sealed record ResultadoArqueo(decimal EfectivoDeclarado, decimal TarjetaDeclarado, decimal TransferenciaDeclarado,
    decimal EfectivoEsperado, decimal TarjetaEsperado, decimal TransferenciaEsperado,
    decimal DiferenciaEfectivo, decimal DiferenciaTarjeta, decimal DiferenciaTransferencia)
{
    public bool HayDiferencias => DiferenciaEfectivo != 0 || DiferenciaTarjeta != 0 || DiferenciaTransferencia != 0;
}

public sealed record ResultadoCierre(decimal DiferenciaTotal, int EcfEnContingencia);

public sealed record SesionSinCerrar(int SesionId, string CajaCodigo, string Cajero, string Estado, DateTimeOffset FechaApertura);

public sealed record EstadoSecuencia(string Tipo, long Siguiente, long Hasta, long Disponibles, DateOnly VenceEn, bool RequiereAtencion);

// ------------------------------------------------------------------ sincronización con Integración

public sealed record ResumenSincronizacion(int PagosPendientes, int PagosPorConfirmar, int PagosRechazados, int EcfEnContingencia,
    DateTimeOffset? UltimaActualizacionTasas);

public sealed record ResultadoSincronizacion(bool Conectado, int TasasActualizadas, int PagosEnviados, int PagosConfirmados,
    int EcfEnviados, IReadOnlyList<string> Avisos);

/// <summary>Orden de cobro del Core (consultada a través de Integración).</summary>
public sealed record OrdenParaCobro(string NumeroOrden, string TasaCodigo, string Concepto, string Documento, string NombrePagador,
    int Cantidad, decimal Total, string Estado);

public sealed record RespuestaRnc(bool Existe, bool Activo, string? RazonSocial);
