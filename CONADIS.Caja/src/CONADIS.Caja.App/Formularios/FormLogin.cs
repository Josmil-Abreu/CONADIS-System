using CONADIS.Caja.Negocio.Servicios;

namespace CONADIS.Caja.App.Formularios;

/// <summary>
/// Inicio de sesión del cajero o supervisor (usuarios de seg.Usuario en CONADIS_CajaDB).
/// El bloqueo tras varios intentos fallidos lo aplica ServicioAutenticacionCaja.
/// Esqueleto funcional: el diseño final se puede cambiar sin tocar la lógica.
/// </summary>
public sealed class FormLogin : Form
{
    private readonly ServicioAutenticacionCaja _autenticacion;
    private readonly ContextoCaja _contexto;

    private readonly TextBox _usuario = new() { Width = 260, Font = Ui.Normal };
    private readonly TextBox _contrasena = new() { Width = 260, Font = Ui.Normal, UseSystemPasswordChar = true };
    private readonly Button _entrar = Ui.Boton("Entrar", 260);
    private readonly Label _mensaje = new() { AutoSize = true, ForeColor = Color.Firebrick, Font = Ui.Normal, MaximumSize = new Size(260, 0) };

    public FormLogin(ServicioAutenticacionCaja autenticacion, ContextoCaja contexto)
    {
        _autenticacion = autenticacion;
        _contexto = contexto;

        Text = "CONADIS - Caja";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(24);

        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        panel.Controls.Add(new Label { Text = "Iniciar sesión en la caja", Font = Ui.Titulo, AutoSize = true });
        panel.Controls.Add(Ui.Etiqueta("Usuario"));
        panel.Controls.Add(_usuario);
        panel.Controls.Add(Ui.Etiqueta("Contraseña"));
        panel.Controls.Add(_contrasena);
        panel.Controls.Add(_entrar);
        panel.Controls.Add(_mensaje);
        Controls.Add(panel);

        AcceptButton = _entrar;
        _entrar.Click += async (_, _) => await Ui.EjecutarAsync(_entrar, EntrarAsync);
    }

    private async Task EntrarAsync()
    {
        _mensaje.Text = "";
        var r = await _autenticacion.IniciarSesionAsync(_usuario.Text, _contrasena.Text);
        if (!r.Exito || r.Usuario is null)
        {
            _mensaje.Text = r.Mensaje ?? "No se pudo iniciar sesión.";
            _contrasena.Clear();
            _contrasena.Focus();
            return;
        }

        _contexto.IniciarSesion(r.Usuario);
        DialogResult = DialogResult.OK;
        Close();
    }
}
