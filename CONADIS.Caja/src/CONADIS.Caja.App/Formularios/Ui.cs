using CONADIS.Caja.Negocio.Datos;
using CONADIS.Caja.Negocio.Externos;
using CONADIS.Caja.Negocio.Reportes;
using System.Diagnostics;

namespace CONADIS.Caja.App.Formularios;

/// <summary>Ayudas comunes de las pantallas: mensajes de error y controles con el mismo estilo.</summary>
internal static class Ui
{
    public static readonly Font Titulo = new("Segoe UI", 14F, FontStyle.Bold);
    public static readonly Font Normal = new("Segoe UI", 10F);

    /// <summary>
    /// Muestra un error de forma comprensible para el cajero:
    ///  - ReglaNegocioException → advertencia con el mensaje del SP / del servicio.
    ///  - SinConexionException → aviso: se puede seguir cobrando, se sincroniza después.
    ///  - BaseDatosNoDisponibleException → error: la base local no responde.
    /// </summary>
    public static void MostrarError(IWin32Window? dueno, Exception ex)
    {
        var (texto, icono) = ex switch
        {
            ReglaNegocioException r => (r.Message, MessageBoxIcon.Warning),
            SinConexionException => ("No hay conexión con Integración. Puede seguir cobrando: se sincroniza cuando vuelva la conexión.",
                MessageBoxIcon.Information),
            BaseDatosNoDisponibleException => ("La base de datos de la caja no responde. Avise a soporte.", MessageBoxIcon.Error),
            _ => ($"Ocurrió un error inesperado: {ex.Message}", MessageBoxIcon.Error)
        };
        MessageBox.Show(dueno, texto, "CONADIS - Caja", MessageBoxButtons.OK, icono);
    }

    /// <summary>Ejecuta una acción asíncrona de un botón: lo deshabilita mientras trabaja y muestra los errores.</summary>
    public static async Task EjecutarAsync(Control boton, Func<Task> accion)
    {
        boton.Enabled = false;
        var cursor = Cursor.Current;
        Cursor.Current = Cursors.WaitCursor;
        try
        {
            await accion();
        }
        catch (Exception ex)
        {
            MostrarError(boton.FindForm(), ex);
        }
        finally
        {
            Cursor.Current = cursor;
            boton.Enabled = true;
        }
    }

    /// <summary>Guarda el reporte en la carpeta temporal y lo abre con el programa predeterminado (visor de PDF o Excel).</summary>
    public static void Abrir(ArchivoReporte archivo)
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "CONADIS-Caja");
        Directory.CreateDirectory(carpeta);
        var ruta = Path.Combine(carpeta, archivo.NombreArchivo);
        File.WriteAllBytes(ruta, archivo.Contenido);
        Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
    }

    public static Label Etiqueta(string texto) => new() { Text = texto, AutoSize = true, Font = Normal, Margin = new Padding(0, 8, 0, 2) };

    public static Button Boton(string texto, int ancho = 160) => new()
    {
        Text = texto, Font = Normal, Width = ancho, Height = 36, Margin = new Padding(0, 6, 8, 0)
    };
}
