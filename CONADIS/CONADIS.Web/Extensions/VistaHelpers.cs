namespace CONADIS.Web.Extensions;

/// <summary>Ayudas de presentación usadas por varias vistas.</summary>
public static class VistaHelpers
{
    public static string EstadoTexto(string? estado) => estado switch
    {
        "Solicitado" => "Solicitado",
        "EnEvaluacion" => "En evaluación",
        "Aprobado" => "Aprobado",
        "Rechazado" => "No aprobado",
        "EnApelacion" => "En apelación",
        "Carnetizado" => "Carnetizado",
        "Vencido" => "Vencido",
        "Anulado" => "Anulado",
        null or "" => "—",
        _ => estado
    };

    public static string EstadoClase(string? estado) => estado switch
    {
        "Carnetizado" or "Aprobado" => "bg-success",
        "Rechazado" or "Anulado" or "Vencido" => "bg-danger",
        "EnEvaluacion" or "EnApelacion" => "bg-primary",
        _ => "bg-secondary"
    };

    public static string FormatoCedula(string? c)
        => c is { Length: 11 } ? $"{c[..3]}-{c[3..10]}-{c[10]}" : c ?? "";
}