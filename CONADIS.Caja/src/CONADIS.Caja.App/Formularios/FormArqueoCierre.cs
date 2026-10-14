using CONADIS.Caja.Negocio.Modelos;
using CONADIS.Caja.Negocio.Servicios;

namespace CONADIS.Caja.App.Formularios;

/// <summary>
/// Arqueo ciego y cierre de caja (RF-CAJ-03).
///  1. El cajero cuenta el efectivo por denominación y declara tarjeta y transferencia, SIN ver lo esperado.
///  2. Se registra el arqueo y recién entonces se muestran las diferencias.
///  3. Si hay diferencias, se exige justificación. Un supervisor aprueba con su usuario y contraseña.
///  4. Se imprime el reporte de cierre.
/// </summary>
public sealed class FormArqueoCierre : Form
{
    private static readonly int[] Denominaciones = [2000, 1000, 500, 200, 100, 50, 25, 10, 5, 1];

    private readonly ServicioSesiones _sesiones;
    private readonly ServicioAutenticacionCaja _autenticacion;
    private readonly ServicioReportesCaja _reportes;
    private readonly ContextoCaja _contexto;

    private readonly Dictionary<int, NumericUpDown> _conteo = new();
    private readonly Label _efectivo = new() { AutoSize = true, Font = Ui.Titulo, Text = "Efectivo contado: RD$0.00" };
    private readonly NumericUpDown _tarjeta = new() { DecimalPlaces = 2, Maximum = 100_000_000, Width = 160, Font = Ui.Normal };
    private readonly NumericUpDown _transferencia = new() { DecimalPlaces = 2, Maximum = 100_000_000, Width = 160, Font = Ui.Normal };
    private readonly Button _arquear = Ui.Boton("Registrar arqueo", 180);

    private readonly Label _resultado = new() { AutoSize = true, Font = Ui.Normal };
    private readonly TextBox _justificacion = new() { Width = 520, Font = Ui.Normal };
    private readonly TextBox _observaciones = new() { Width = 520, Font = Ui.Normal };
    private readonly TextBox _supervisor = new() { Width = 200, Font = Ui.Normal };
    private readonly TextBox _clave = new() { Width = 200, Font = Ui.Normal, UseSystemPasswordChar = true };
    private readonly Button _cerrar = Ui.Boton("Aprobar cierre", 180);
    private readonly FlowLayoutPanel _panelCierre;

    private ResultadoArqueo? _arqueo;

    public FormArqueoCierre(ServicioSesiones sesiones, ServicioAutenticacionCaja autenticacion, ServicioReportesCaja reportes, ContextoCaja contexto)
    {
        _sesiones = sesiones;
        _autenticacion = autenticacion;
        _reportes = reportes;
        _contexto = contexto;

        Text = "Arqueo y cierre de caja";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 720);
        Font = Ui.Normal;

        var raiz = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
        raiz.Controls.Add(new Label { Text = "1. Conteo (arqueo ciego)", Font = Ui.Titulo, AutoSize = true });
        raiz.Controls.Add(Ui.Etiqueta("Cantidad de billetes y monedas por denominación:"));
        var tabla = new TableLayoutPanel { ColumnCount = 4, AutoSize = true };
        foreach (var d in Denominaciones)
        {
            var n = new NumericUpDown { Maximum = 100_000, Width = 90, Font = Ui.Normal };
            n.ValueChanged += (_, _) => SumarEfectivo();
            _conteo[d] = n;
            tabla.Controls.Add(new Label { Text = $"RD${d:N0}", AutoSize = true, Font = Ui.Normal, Margin = new Padding(0, 8, 4, 0) });
            tabla.Controls.Add(n);
        }
        raiz.Controls.Add(tabla);
        raiz.Controls.Add(_efectivo);
        raiz.Controls.Add(Ui.Etiqueta("Total vouchers de tarjeta (RD$)"));
        raiz.Controls.Add(_tarjeta);
        raiz.Controls.Add(Ui.Etiqueta("Total transferencias (RD$)"));
        raiz.Controls.Add(_transferencia);
        raiz.Controls.Add(_arquear);

        _panelCierre = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Visible = false };
        _panelCierre.Controls.AddRange(new Control[]
        {
            new Label { Text = "2. Resultado y cierre", Font = Ui.Titulo, AutoSize = true, Margin = new Padding(0, 16, 0, 0) },
            _resultado,
            Ui.Etiqueta("Justificación de diferencias (obligatoria si las hay)"), _justificacion,
            Ui.Etiqueta("Observaciones"), _observaciones,
            Ui.Etiqueta("Supervisor que aprueba"), _supervisor,
            Ui.Etiqueta("Contraseña del supervisor"), _clave,
            _cerrar
        });
        raiz.Controls.Add(_panelCierre);
        Controls.Add(raiz);

        _arquear.Click += async (_, _) => await Ui.EjecutarAsync(_arquear, ArquearAsync);
        _cerrar.Click += async (_, _) => await Ui.EjecutarAsync(_cerrar, CerrarAsync);
    }

    private decimal EfectivoContado() => _conteo.Sum(p => p.Key * p.Value.Value);

    private void SumarEfectivo() => _efectivo.Text = $"Efectivo contado: RD${EfectivoContado():N2}";

    private async Task ArquearAsync()
    {
        var sesion = _contexto.SesionActual;
        if (MessageBox.Show(this, "Una vez registrado el arqueo no se puede volver a contar. ¿Continuar?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        _arqueo = await _sesiones.ArquearAsync(sesion.SesionId, _contexto.UsuarioActual, EfectivoContado(), _tarjeta.Value, _transferencia.Value);
        var a = _arqueo;
        _resultado.Text =
            $"Efectivo:       declarado RD${a.EfectivoDeclarado:N2} · esperado RD${a.EfectivoEsperado:N2} · diferencia RD${a.DiferenciaEfectivo:N2}\n" +
            $"Tarjeta:        declarado RD${a.TarjetaDeclarado:N2} · esperado RD${a.TarjetaEsperado:N2} · diferencia RD${a.DiferenciaTarjeta:N2}\n" +
            $"Transferencia:  declarado RD${a.TransferenciaDeclarado:N2} · esperado RD${a.TransferenciaEsperado:N2} · diferencia RD${a.DiferenciaTransferencia:N2}\n" +
            (a.HayDiferencias ? "HAY DIFERENCIAS: escriba la justificación." : "Cuadre correcto.");
        _resultado.ForeColor = a.HayDiferencias ? Color.DarkRed : Color.DarkGreen;
        foreach (var n in _conteo.Values) n.Enabled = false;
        _tarjeta.Enabled = _transferencia.Enabled = _arquear.Visible = false;
        _panelCierre.Visible = true;
        _contexto.EstablecerSesion(await _sesiones.ObtenerAbiertaAsync(_contexto.UsuarioActual.UsuarioId) ?? sesion);
        (_arqueo.HayDiferencias ? _justificacion : _supervisor).Focus();
    }

    private async Task CerrarAsync()
    {
        if (_arqueo is null) return;
        if (_arqueo.HayDiferencias && string.IsNullOrWhiteSpace(_justificacion.Text))
        {
            MessageBox.Show(this, "Hay diferencias: escriba la justificación antes de cerrar.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _justificacion.Focus();
            return;
        }
        var supervisor = await _autenticacion.AutorizarSupervisorAsync(_supervisor.Text, _clave.Text);
        _clave.Clear();
        var sesionId = _contexto.SesionActual.SesionId;
        var r = await _sesiones.CerrarAsync(sesionId, supervisor,
            string.IsNullOrWhiteSpace(_justificacion.Text) ? null : _justificacion.Text.Trim(),
            string.IsNullOrWhiteSpace(_observaciones.Text) ? null : _observaciones.Text.Trim());
        _contexto.EstablecerSesion(null);
        var aviso = r.EcfEnContingencia > 0 ? $"\n{r.EcfEnContingencia} e-CF siguen en contingencia: se reenviarán automáticamente." : "";
        MessageBox.Show(this, $"Caja cerrada. Diferencia total: RD${r.DiferenciaTotal:N2}{aviso}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        Ui.Abrir(await _reportes.CierreAsync(supervisor, sesionId));
        DialogResult = DialogResult.OK;
        Close();
    }
}
