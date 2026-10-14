using System.Data;
using CONADIS.Caja.Negocio.Reportes;
using CONADIS.Caja.Negocio.Servicios;

namespace CONADIS.Caja.App.Formularios;

/// <summary>
/// Reportes de la caja: transacciones del período (cada una con su comprobante), cierre de la sesión
/// e ingresos por servicio (supervisor). Todos se generan con los RDLC de CONADIS.Caja.Negocio.
/// </summary>
public sealed class FormReportes : Form
{
    private readonly ServicioReportesCaja _reportes;
    private readonly ContextoCaja _contexto;

    private readonly DateTimePicker _desde = new() { Format = DateTimePickerFormat.Short, Width = 140, Font = Ui.Normal };
    private readonly DateTimePicker _hasta = new() { Format = DateTimePickerFormat.Short, Width = 140, Font = Ui.Normal };
    private readonly Button _buscar = Ui.Boton("Ver transacciones");
    private readonly Button _comprobante = Ui.Boton("Comprobante (PDF)");
    private readonly Button _listadoPdf = Ui.Boton("Listado (PDF)");
    private readonly Button _listadoExcel = Ui.Boton("Listado (Excel)");
    private readonly Button _cierre = Ui.Boton("Cierre de la sesión", 170);
    private readonly Button _ingresos = Ui.Boton("Ingresos por servicio", 180);
    private readonly Button _miActividad = Ui.Boton("Mi actividad (PDF)");

    private readonly DataGridView _grilla = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersVisible = false, Font = Ui.Normal
    };

    public FormReportes(ServicioReportesCaja reportes, ContextoCaja contexto)
    {
        _reportes = reportes;
        _contexto = contexto;

        Text = "Reportes y transacciones";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(980, 560);
        Font = Ui.Normal;
        _desde.Value = DateTime.Today.AddDays(-7);
        _hasta.Value = DateTime.Today;

        var filtros = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12) };
        filtros.Controls.AddRange(new Control[] { Ui.Etiqueta("Desde"), _desde, Ui.Etiqueta("Hasta"), _hasta, _buscar });

        var acciones = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 0, 12, 8) };
        acciones.Controls.AddRange(new Control[] { _comprobante, _listadoPdf, _listadoExcel, _miActividad, _cierre, _ingresos });

        var contenido = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        contenido.Controls.Add(_grilla);
        Controls.Add(contenido);
        Controls.Add(acciones);
        Controls.Add(filtros);

        var u = _contexto.UsuarioActual;
        _ingresos.Visible = u.EsSupervisor;
        _cierre.Enabled = _contexto.Sesion is not null;

        _buscar.Click += async (_, _) => await Ui.EjecutarAsync(_buscar, BuscarAsync);
        _comprobante.Click += async (_, _) => await Ui.EjecutarAsync(_comprobante, ComprobanteAsync);
        _grilla.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await Ui.EjecutarAsync(_comprobante, ComprobanteAsync); };
        _listadoPdf.Click += async (_, _) => await Ui.EjecutarAsync(_listadoPdf, () => ListadoAsync(FormatoReporte.Pdf));
        _listadoExcel.Click += async (_, _) => await Ui.EjecutarAsync(_listadoExcel, () => ListadoAsync(FormatoReporte.Excel));
        _cierre.Click += async (_, _) => await Ui.EjecutarAsync(_cierre,
            async () => Ui.Abrir(await _reportes.CierreAsync(u, _contexto.SesionActual.SesionId)));
        _ingresos.Click += async (_, _) => await Ui.EjecutarAsync(_ingresos,
            async () => Ui.Abrir(await _reportes.IngresosArchivoAsync(u, Fecha(_desde), Fecha(_hasta))));
        _miActividad.Click += async (_, _) => await Ui.EjecutarAsync(_miActividad,
            async () => Ui.Abrir(await _reportes.MiActividadAsync(u, Fecha(_desde), Fecha(_hasta))));
        Load += async (_, _) => await Ui.EjecutarAsync(_buscar, BuscarAsync);
    }

    private static DateOnly Fecha(DateTimePicker p) => DateOnly.FromDateTime(p.Value.Date);

    private async Task BuscarAsync()
        => _grilla.DataSource = await _reportes.TransaccionesAsync(_contexto.UsuarioActual, Fecha(_desde), Fecha(_hasta));

    private async Task ComprobanteAsync()
    {
        if (_grilla.CurrentRow?.DataBoundItem is not DataRowView fila)
        {
            MessageBox.Show(this, "Seleccione una transacción de la lista.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Ui.Abrir(await _reportes.ComprobanteAsync(_contexto.UsuarioActual, Convert.ToInt64(fila["EventoId"])));
    }

    private async Task ListadoAsync(FormatoReporte formato)
        => Ui.Abrir(await _reportes.TransaccionesArchivoAsync(_contexto.UsuarioActual, Fecha(_desde), Fecha(_hasta), formato));
}
