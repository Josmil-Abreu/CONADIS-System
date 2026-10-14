namespace CONADIS.Core.Negocio.Modelos;

/// <summary>
/// Quién hace la operación y desde dónde. Se pasa a todos los métodos que modifican datos
/// para que la bitácora de auditoría registre usuario e IP (RNF-SEG-06).
/// </summary>
public sealed record ContextoAccion(string Usuario, string? Ip = null)
{
    public static ContextoAccion Sistema(string proceso) => new($"sistema:{proceso}");
}

/// <summary>Nombres de los roles tal como están en seg.Rol.</summary>
public static class Roles
{
    public const string Administrador = "Administrador";
    public const string AnalistaRegistro = "AnalistaRegistro";
    public const string SupervisorRegistro = "SupervisorRegistro";
    public const string Evaluador = "Evaluador";
    public const string Comite = "Comite";
    public const string Auditor = "Auditor";
}

/// <summary>Textos de estados y catálogos cerrados (los mismos valores que validan los CHECK de SQL).</summary>
public static class Valores
{
    public static readonly string[] EstadosExpediente =
        ["Solicitado", "EnEvaluacion", "Aprobado", "Rechazado", "EnApelacion", "Carnetizado", "Vencido", "Anulado"];

    public static readonly string[] TiposDiscapacidad =
        ["Fisica", "Visual", "Auditiva", "Intelectual", "Psicosocial", "Multiple"];

    public static readonly string[] Grados = ["Leve", "Moderado", "Severo", "Profundo"];

    public static readonly string[] Disciplinas = ["Medica", "Psicologica", "Social"];

    public static readonly string[] MediosPago = ["Efectivo", "Tarjeta", "Transferencia", "EnLinea"];
}