using CONADIS.Caja.Negocio.Modelos;
using CONADIS.Caja.Negocio.Servicios;
using Microsoft.Extensions.DependencyInjection;

namespace CONADIS.Caja.App.Formularios;

/// <summary>
/// Pantalla principal de la Caja. Esqueleto: barra de estado (usuario, caja, sincronización),
/// comprobantes de la sesión y botones para cada caso de uso. Las pantallas de cobro y de arqueo/cierre
/// están indicadas con el servicio que deben llamar (ver CobrarAsync y ArqueoAsync).
/// </summary>
public sealed class FormPrincipal : Form
{
    private readonly IServiceProvider _servicios;
    private readonly ContextoCaja _contexto;
    private readonly ServicioSesiones _sesiones;
    private readonly ServicioCobro _cobro;
    private readonly SincronizacionPeriodica _sincronizacion;
    private readonly ServicioReportesCaja _reportesCaja;

    private readonly Button _abrirCaja = Ui.Boton("Abrir caja");
    private readonly Button _cobrar = Ui.Boton("Cobrar");
    private readonly Button _arqueo = Ui.Boton("Arqueo y cierre");
    private readonly Button _sincronizar = Ui.Boton("Sincronizar ahora");
    private readonly Button _salir = Ui.Boton("Cerrar sesión");
    private readonly Button _imprimir = Ui.Boton("Imprimir comprobante");
    private readonly Button _reportes = Ui.Boton("Reportes y transacciones", 200);
    private readonly Button _miActividad = Ui.Boton("Mi actividad (PDF)");

    private readonly DataGridView _comprobantes = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersVisible = false, Font = Ui.Normal
    };

    private readonly ToolStripStatusLabel _estadoUsuario = new();
    private readonly ToolStripStatusLabel _estadoCaja = new();
    private readonly ToolStripStatusLabel _estadoSincronizacion = new() { Spring = true, TextAlign = ContentAlignment.MiddleRight };

    /// <summary>true si el usuario pulsó "Cerrar sesión" (Program vuelve a mostrar el login).</summary>
    public bool VolverAlLogin { get; private set; }

    public FormPrincipal(IServiceProvider servicios, ContextoCaja contexto, ServicioSesiones sesiones, ServicioCobro cobro,
        SincronizacionPeriodica sincronizacion, ServicioReportesCaja reportesCaja)
    {
        _reportesCaja = reportesCaja;
        _servicios = servicios;
        _contexto = contexto;
        _sesiones = sesiones;
        _cobro = cobro;
        _sincronizacion = sincronizacion;

        Text = "CONADIS - Caja";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 560);
        Font = Ui.Normal;

        var barraBotones = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12) };
        barraBotones.Controls.AddRange(new Control[] { _abrirCaja, _cobrar, _imprimir, _arqueo, _reportes, _miActividad, _sincronizar, _salir });

        var titulo = new Label
        {
            Text = "Comprobantes de esta sesión", Font = Ui.Titulo, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 4, 0, 4)
        };

        var estado = new StatusStrip();
        estado.Items.AddRange(new ToolStripItem[] { _estadoUsuario, _estadoCaja, _estadoSincronizacion });

        var contenido = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        contenido.Controls.Add(_comprobantes);

        // El orden de Add importa con Dock: primero lo que llena, luego los bordes.
        Controls.Add(contenido);
        Controls.Add(titulo);
        Controls.Add(barraBotones);
        Controls.Add(estado);

        _abrirCaja.Click += (_, _) => AbrirCaja();
        _cobrar.Click += async (_, _) => await Ui.EjecutarAsync(_cobrar, CobrarAsync);
        _arqueo.Click += async (_, _) => await Ui.EjecutarAsync(_arqueo, ArqueoAsync);
        // Reimprimir el e-CF del comprobante seleccionado (doble clic también lo imprime)
        _imprimir.Click += async (_, _) => await Ui.EjecutarAsync(_imprimir, ImprimirSeleccionadoAsync);
        _comprobantes.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await Ui.EjecutarAsync(_imprimir, ImprimirSeleccionadoAsync); };
        // Todo lo que hizo el usuario de la sesión en los últimos 30 días
        _miActividad.Click += async (_, _) => await Ui.EjecutarAsync(_miActividad, async () =>
            Ui.Abrir(await _reportesCaja.MiActividadAsync(_contexto.UsuarioActual,
                DateOnly.FromDateTime(DateTime.Today.AddDays(-30)), DateOnly.FromDateTime(DateTime.Today))));
        _reportes.Click += (_, _) =>
        {
            using var form = _servicios.GetRequiredService<FormReportes>();
            form.ShowDialog(this);
        };
        _sincronizar.Click += async (_, _) => await Ui.EjecutarAsync(_sincronizar, () => _sincronizacion.SincronizarAhoraAsync());
        _salir.Click += (_, _) =>
        {
            _contexto.CerrarSesion();
            VolverAlLogin = true;
            Close();
        };

        _contexto.Cambio += AlCambiarContexto;
        _sincronizacion.Resultado += AlSincronizar;
        Load += async (_, _) => await Ui.EjecutarAsync(_abrirCaja, CargarAsync);
        FormClosed += (_, _) =>
        {
            _contexto.Cambio -= AlCambiarContexto;
            _sincronizacion.Resultado -= AlSincronizar;
        };
    }

    private async Task CargarAsync()
    {
        // Si el cajero ya tenía una caja abierta (p. ej. reinició la aplicación), se retoma.
        _contexto.EstablecerSesion(await _sesiones.ObtenerAbiertaAsync(_contexto.UsuarioActual.UsuarioId));
        _sincronizacion.Iniciar();
        _estadoSincronizacion.Text = "Sincronizando…";
        await RefrescarComprobantesAsync();
    }

    private void AbrirCaja()
    {
        using var form = _servicios.GetRequiredService<FormAbrirCaja>();
        if (form.ShowDialog(this) == DialogResult.OK) _ = RefrescarComprobantesAsync();
    }

    /// <summary>
    /// PANTALLA PENDIENTE (CU-02): formulario de cobro. Debe:
    ///  1. Opcional: buscar la orden con _cobro.BuscarOrdenAsync(numero) para cobrar el monto exacto.
    ///  2. Mostrar las tasas de _cobro.TasasAsync() y calcular el total con ReglasCobro.Calcular(...).
    ///  3. Armar un SolicitudCobro (cliente, líneas, medio de pago, monto recibido / autorización / referencia).
    ///  4. Llamar _cobro.CobrarAsync(_contexto.SesionActual, _contexto.UsuarioActual, solicitud) y mostrar el e-CF devuelto.
    ///  5. Imprimir el comprobante: Ui.Abrir(await _reportesCaja.FacturaAsync(_contexto.UsuarioActual, emitido.FacturaId)).
    /// </summary>
    private async Task CobrarAsync()
    {
        _ = _contexto.SesionActual;   // avisa "No hay una caja abierta" si corresponde
        MessageBox.Show(this, "La pantalla de cobro está pendiente. Ver el comentario de FormPrincipal.CobrarAsync.",
            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        await RefrescarComprobantesAsync();
    }

    /// <summary>
    /// PANTALLA PENDIENTE (RF-CAJ-03): arqueo ciego y cierre. Debe:
    ///  1. Pedir lo contado por medio de pago (sin mostrar lo esperado) y llamar _sesiones.ArquearAsync(...).
    ///  2. Mostrar el ResultadoArqueo; si hay diferencias, pedir la justificación.
    ///  3. Pedir usuario y contraseña del supervisor (ServicioAutenticacionCaja.AutorizarSupervisorAsync)
    ///     y llamar _sesiones.CerrarAsync(...). Luego Ui.Abrir(await _reportesCaja.CierreAsync(usuario, sesionId)) imprime el cierre.
    /// </summary>
    private async Task ArqueoAsync()
    {
        _ = _contexto.SesionActual;
        MessageBox.Show(this, "La pantalla de arqueo y cierre está pendiente. Ver el comentario de FormPrincipal.ArqueoAsync.",
            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        await Task.CompletedTask;
    }

    /// <summary>Representación impresa del e-CF seleccionado (cobro o nota de crédito).</summary>
    private async Task ImprimirSeleccionadoAsync()
    {
        if (_comprobantes.CurrentRow?.DataBoundItem is not ComprobanteResumen c)
        {
            MessageBox.Show(this, "Seleccione un comprobante de la lista.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Ui.Abrir(await _reportesCaja.FacturaAsync(_contexto.UsuarioActual, c.FacturaId));
    }

    private async Task RefrescarComprobantesAsync()
    {
        _comprobantes.DataSource = _contexto.Sesion is { } s
            ? await _cobro.ListarSesionAsync(s.SesionId)
            : new List<ComprobanteResumen>();
    }

    private void AlCambiarContexto(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => AlCambiarContexto(sender, e)));
            return;
        }

        _estadoUsuario.Text = _contexto.Usuario is { } u ? $"{u.NombreCompleto} ({u.Rol})" : "Sin sesión";
        _estadoCaja.Text = _contexto.Sesion is { } s ? $"  |  {s.CajaCodigo} · {s.Sucursal} · {s.Estado}" : "  |  Caja cerrada";
        _abrirCaja.Enabled = _contexto.Sesion is null;
        _cobrar.Enabled = _contexto.PuedeCobrar;
        _imprimir.Enabled = _contexto.Sesion is not null;
        _arqueo.Enabled = _contexto.Sesion is not null;
    }

    /// <summary>Llega desde un hilo de fondo: se pasa al hilo de la pantalla con BeginInvoke.</summary>
    private void AlSincronizar(object? sender, ResultadoSincronizacion r)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => AlSincronizar(sender, r)));
            return;
        }

        _estadoSincronizacion.Text = r.Conectado
            ? $"En línea · {DateTime.Now:HH:mm} · enviados {r.PagosEnviados}, confirmados {r.PagosConfirmados}, e-CF {r.EcfEnviados}"
            : $"Sin conexión con Integración · {DateTime.Now:HH:mm} · se reintentará";
        _estadoSincronizacion.ForeColor = r.Conectado ? Color.DarkGreen : Color.DarkOrange;
    }
}
