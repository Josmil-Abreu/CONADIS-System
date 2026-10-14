using System.ComponentModel;
using CONADIS.Caja.Negocio.Modelos;
using CONADIS.Caja.Negocio.Servicios;

namespace CONADIS.Caja.App.Formularios;

/// <summary>
/// Cobro de servicios (CU-02). Flujo rápido con teclado:
///  F2 buscar orden · F3 agregar servicio · Supr quitar línea · F12 cobrar · Esc salir.
/// Con una orden del Core el cobro es: número de orden → F2 → monto recibido → F12.
/// Si Integración no responde se cobra igual (el pago se sincroniza después).
/// </summary>
public sealed class FormCobro : Form
{
    private sealed class LineaVista
    {
        public string Codigo { get; init; } = "";
        public string Servicio { get; init; } = "";
        public int Cantidad { get; set; }
        public decimal Precio { get; init; }
        public decimal Importe => Precio * Cantidad;
    }

    private readonly ServicioCobro _cobro;
    private readonly ServicioReportesCaja _reportes;
    private readonly ContextoCaja _contexto;
    private Dictionary<string, TasaLocal> _tasas = new();
    private readonly BindingList<LineaVista> _lineas = new();

    private readonly TextBox _orden = new() { Width = 160, Font = Ui.Normal };
    private readonly Button _buscarOrden = Ui.Boton("Buscar orden (F2)");
    private readonly TextBox _documento = new() { Width = 160, Font = Ui.Normal };
    private readonly TextBox _nombre = new() { Width = 300, Font = Ui.Normal };
    private readonly TextBox _correo = new() { Width = 240, Font = Ui.Normal };
    private readonly CheckBox _creditoFiscal = new() { Text = "Crédito fiscal (E31, RNC)", AutoSize = true, Font = Ui.Normal };
    private readonly CheckBox _gubernamental = new() { Text = "Gubernamental (E45)", AutoSize = true, Font = Ui.Normal };
    private readonly ComboBox _servicio = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380, Font = Ui.Normal };
    private readonly NumericUpDown _cantidad = new() { Minimum = 1, Maximum = 99, Value = 1, Width = 70, Font = Ui.Normal };
    private readonly Button _agregar = Ui.Boton("Agregar (F3)", 130);
    private readonly Button _quitar = Ui.Boton("Quitar (Supr)", 130);
    private readonly ComboBox _medio = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Font = Ui.Normal };
    private readonly NumericUpDown _recibido = new() { DecimalPlaces = 2, Maximum = 10_000_000, Increment = 100, Width = 140, Font = Ui.Normal };
    private readonly TextBox _referencia = new() { Width = 200, Font = Ui.Normal };
    private readonly Label _etiquetaReferencia = Ui.Etiqueta("Referencia / autorización");
    private readonly Label _total = new() { AutoSize = true, Font = Ui.Titulo, Text = "Total: RD$0.00" };
    private readonly Label _cambio = new() { AutoSize = true, Font = Ui.Normal };
    private readonly Button _cobrar = Ui.Boton("COBRAR (F12)", 200);

    private readonly DataGridView _grilla = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersVisible = false, Font = Ui.Normal
    };

    /// <summary>Comprobante emitido (null si se canceló).</summary>
    public ComprobanteEmitido? Emitido { get; private set; }

    public FormCobro(ServicioCobro cobro, ServicioReportesCaja reportes, ContextoCaja contexto)
    {
        _cobro = cobro;
        _reportes = reportes;
        _contexto = contexto;

        Text = "Cobrar servicio";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 640);
        Font = Ui.Normal;
        KeyPreview = true;

        var orden = Fila(Ui.Etiqueta("N.º de orden (opcional)"), _orden, _buscarOrden);
        var cliente = Fila(Ui.Etiqueta("Cédula / RNC"), _documento, Ui.Etiqueta("Nombre"), _nombre);
        var cliente2 = Fila(Ui.Etiqueta("Correo"), _correo, _creditoFiscal, _gubernamental);
        var servicio = Fila(Ui.Etiqueta("Servicio"), _servicio, Ui.Etiqueta("Cant."), _cantidad, _agregar, _quitar);
        var arriba = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
        arriba.Controls.AddRange(new Control[] { orden, cliente, cliente2, servicio });

        var pago = Fila(Ui.Etiqueta("Medio de pago"), _medio, Ui.Etiqueta("Recibido"), _recibido, _etiquetaReferencia, _referencia);
        var totales = Fila(_total, _cambio, _cobrar);
        var abajo = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
        abajo.Controls.AddRange(new Control[] { pago, totales });

        var centro = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
        centro.Controls.Add(_grilla);
        Controls.Add(centro);
        Controls.Add(arriba);
        Controls.Add(abajo);

        _medio.DataSource = new[] { MediosPagoCaja.Efectivo, MediosPagoCaja.Tarjeta, MediosPagoCaja.Transferencia };
        _grilla.DataSource = _lineas;
        AcceptButton = null;

        _buscarOrden.Click += async (_, _) => await Ui.EjecutarAsync(_buscarOrden, BuscarOrdenAsync);
        _agregar.Click += (_, _) => Agregar();
        _quitar.Click += (_, _) => Quitar();
        _cobrar.Click += async (_, _) => await Ui.EjecutarAsync(_cobrar, CobrarAsync);
        _medio.SelectedIndexChanged += (_, _) => Recalcular();
        _recibido.ValueChanged += (_, _) => Recalcular();
        KeyDown += async (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.F2: e.Handled = true; await Ui.EjecutarAsync(_buscarOrden, BuscarOrdenAsync); break;
                case Keys.F3: e.Handled = true; Agregar(); break;
                case Keys.F12: e.Handled = true; await Ui.EjecutarAsync(_cobrar, CobrarAsync); break;
                case Keys.Delete when _grilla.Focused: e.Handled = true; Quitar(); break;
                case Keys.Escape: e.Handled = true; Close(); break;
            }
        };
        Load += async (_, _) => await Ui.EjecutarAsync(_cobrar, CargarAsync);
    }

    private static FlowLayoutPanel Fila(params Control[] controles)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        f.Controls.AddRange(controles);
        return f;
    }

    private async Task CargarAsync()
    {
        var tasas = await _cobro.TasasAsync();
        _tasas = tasas.ToDictionary(t => t.Codigo, StringComparer.OrdinalIgnoreCase);
        _servicio.DataSource = tasas;
        _servicio.DisplayMember = nameof(TasaLocal.Descripcion);
        if (tasas.Count == 0)
            MessageBox.Show(this, "No hay tasas locales. Pulse \"Sincronizar ahora\" cuando haya conexión con Integración.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Recalcular();
        _orden.Focus();
    }

    private async Task BuscarOrdenAsync()
    {
        if (string.IsNullOrWhiteSpace(_orden.Text)) { _orden.Focus(); return; }
        var o = await _cobro.BuscarOrdenAsync(_orden.Text.Trim());
        if (o is null)
        {
            MessageBox.Show(this, "No se encontró la orden o no hay conexión con Integración. Puede cobrar seleccionando el servicio manualmente.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            _documento.Focus();
            return;
        }
        _documento.Text = o.Documento;
        _nombre.Text = o.NombrePagador;
        _lineas.Clear();
        var precio = _tasas.TryGetValue(o.TasaCodigo, out var t) ? t.Monto : o.Total / Math.Max(1, o.Cantidad);
        _lineas.Add(new LineaVista { Codigo = o.TasaCodigo, Servicio = o.Concepto, Cantidad = o.Cantidad, Precio = precio });
        Recalcular();
        _recibido.Focus();
    }

    private void Agregar()
    {
        if (_servicio.SelectedItem is not TasaLocal t) return;
        var cantidad = (int)_cantidad.Value;
        var existente = _lineas.FirstOrDefault(l => l.Codigo == t.Codigo);
        if (existente is not null) { existente.Cantidad += cantidad; _lineas.ResetBindings(); }
        else _lineas.Add(new LineaVista { Codigo = t.Codigo, Servicio = t.Descripcion, Cantidad = cantidad, Precio = t.Monto });
        _cantidad.Value = 1;
        Recalcular();
    }

    private void Quitar()
    {
        if (_grilla.CurrentRow?.DataBoundItem is LineaVista l) { _lineas.Remove(l); Recalcular(); }
    }

    private decimal TotalActual()
    {
        if (_lineas.Count == 0) return 0;
        try { return ReglasCobro.Calcular(_lineas.Select(l => new LineaCobro(l.Codigo, l.Cantidad)), _tasas).Total; }
        catch { return _lineas.Sum(l => l.Importe); }
    }

    private void Recalcular()
    {
        var total = TotalActual();
        _total.Text = $"Total: RD${total:N2}";
        var efectivo = (_medio.SelectedItem as string) == MediosPagoCaja.Efectivo;
        _recibido.Enabled = efectivo;
        _referencia.Enabled = !efectivo;
        _etiquetaReferencia.Text = (_medio.SelectedItem as string) == MediosPagoCaja.Tarjeta ? "Autorización POS" : "Referencia";
        _cambio.Text = efectivo && _recibido.Value >= total && total > 0 ? $"Cambio: RD${_recibido.Value - total:N2}" : "";
    }

    private async Task CobrarAsync()
    {
        if (_lineas.Count == 0)
        {
            MessageBox.Show(this, "Agregue al menos un servicio o busque la orden.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var medio = (string)_medio.SelectedItem!;
        var solicitud = new SolicitudCobro
        {
            NumeroOrden = string.IsNullOrWhiteSpace(_orden.Text) ? null : _orden.Text.Trim(),
            DocumentoCliente = _documento.Text,
            NombreCliente = _nombre.Text,
            CorreoCliente = string.IsNullOrWhiteSpace(_correo.Text) ? null : _correo.Text.Trim(),
            QuiereCreditoFiscal = _creditoFiscal.Checked,
            EsGubernamental = _gubernamental.Checked,
            Lineas = _lineas.Select(l => new LineaCobro(l.Codigo, l.Cantidad)).ToList(),
            MedioPago = medio,
            MontoRecibido = medio == MediosPagoCaja.Efectivo ? _recibido.Value : null,
            Referencia = medio == MediosPagoCaja.Transferencia ? _referencia.Text : null,
            Autorizacion = medio == MediosPagoCaja.Tarjeta ? _referencia.Text : null
        };

        var e = await _cobro.CobrarAsync(_contexto.SesionActual, _contexto.UsuarioActual, solicitud);
        Emitido = e;
        var cambio = e.Cambio is > 0 ? $"\nCambio a devolver: RD${e.Cambio:N2}" : "";
        var dgii = e.EstadoDgii == EstadosDgii.Contingencia ? "\nDGII sin respuesta: el e-CF queda en contingencia y se reenvía solo." : "";
        MessageBox.Show(this, $"Cobro registrado.\ne-NCF: {e.Encf}\nTotal: RD${e.Total:N2}{cambio}{dgii}", Text,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        Ui.Abrir(await _reportes.FacturaAsync(_contexto.UsuarioActual, e.FacturaId));
        DialogResult = DialogResult.OK;
        Close();
    }
}
