using CONADIS.Caja.Negocio.Modelos;
using CONADIS.Caja.Negocio.Servicios;

namespace CONADIS.Caja.App.Formularios;

/// <summary>Apertura de caja (RF-CAJ-03): elegir la caja física y declarar el fondo inicial.</summary>
public sealed class FormAbrirCaja : Form
{
    private readonly ServicioSesiones _sesiones;
    private readonly ContextoCaja _contexto;

    private readonly ComboBox _cajas = new() { Width = 260, Font = Ui.Normal, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _fondo = new() { Width = 260, Font = Ui.Normal, DecimalPlaces = 2, Maximum = 1_000_000, Increment = 100 };
    private readonly Button _abrir = Ui.Boton("Abrir caja", 260);

    public FormAbrirCaja(ServicioSesiones sesiones, ContextoCaja contexto)
    {
        _sesiones = sesiones;
        _contexto = contexto;

        Text = "Abrir caja";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(24);

        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        panel.Controls.Add(new Label { Text = "Abrir caja", Font = Ui.Titulo, AutoSize = true });
        panel.Controls.Add(Ui.Etiqueta("Caja"));
        panel.Controls.Add(_cajas);
        panel.Controls.Add(Ui.Etiqueta("Fondo inicial (RD$)"));
        panel.Controls.Add(_fondo);
        panel.Controls.Add(_abrir);
        Controls.Add(panel);

        AcceptButton = _abrir;
        _cajas.DisplayMember = nameof(CajaFisica.Codigo);
        Load += async (_, _) => await Ui.EjecutarAsync(_abrir, CargarCajasAsync);
        _abrir.Click += async (_, _) => await Ui.EjecutarAsync(_abrir, AbrirAsync);
    }

    private async Task CargarCajasAsync()
    {
        _cajas.DataSource = await _sesiones.CajasAsync();
    }

    private async Task AbrirAsync()
    {
        if (_cajas.SelectedItem is not CajaFisica caja)
        {
            MessageBox.Show(this, "Elija la caja.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // El SP impide abrir dos sesiones en la misma caja o dos cajas para el mismo cajero.
        var sesion = await _sesiones.AbrirAsync(caja.CajaId, _contexto.UsuarioActual, _fondo.Value);
        _contexto.EstablecerSesion(sesion);
        DialogResult = DialogResult.OK;
        Close();
    }
}
