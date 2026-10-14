namespace CONADIS.Inicio;

/// <summary>
/// Pantalla del login único. Después de abrir el módulo queda lista para que entre otra persona
/// (útil en la demo: ciudadano, cajero y personal desde el mismo lugar).
/// </summary>
public sealed class FormInicio : Form
{
    private readonly LoginUnico _login;

    private readonly TextBox _usuario = new() { Width = 300, Font = new Font("Segoe UI", 11F) };
    private readonly TextBox _contrasena = new() { Width = 300, Font = new Font("Segoe UI", 11F), UseSystemPasswordChar = true };
    private readonly Button _entrar = new() { Text = "Entrar", Width = 300, Height = 40, Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
    private readonly Label _mensaje = new() { AutoSize = true, MaximumSize = new Size(300, 0), Font = new Font("Segoe UI", 10F) };

    public FormInicio(LoginUnico login)
    {
        _login = login;

        Text = "CONADIS - Inicio";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(28);

        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        panel.Controls.Add(new Label { Text = "CONADIS", AutoSize = true, Font = new Font("Segoe UI", 20F, FontStyle.Bold), ForeColor = Color.FromArgb(13, 71, 161) });
        panel.Controls.Add(new Label
        {
            Text = "Un solo acceso para ciudadanos, cajeros y personal.\nCiudadanos: escriban su cédula o correo.",
            AutoSize = true, MaximumSize = new Size(300, 0), Font = new Font("Segoe UI", 9.5F), ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 12)
        });
        panel.Controls.Add(new Label { Text = "Usuario, cédula o correo", AutoSize = true, Font = new Font("Segoe UI", 10F) });
        panel.Controls.Add(_usuario);
        panel.Controls.Add(new Label { Text = "Contraseña", AutoSize = true, Font = new Font("Segoe UI", 10F), Margin = new Padding(0, 10, 0, 0) });
        panel.Controls.Add(_contrasena);
        _entrar.Margin = new Padding(0, 16, 0, 8);
        panel.Controls.Add(_entrar);
        panel.Controls.Add(_mensaje);
        Controls.Add(panel);

        AcceptButton = _entrar;
        _entrar.Click += async (_, _) => await EntrarAsync();
    }

    private async Task EntrarAsync()
    {
        _entrar.Enabled = false;
        _mensaje.ForeColor = Color.DimGray;
        _mensaje.Text = "Verificando…";
        try
        {
            var r = await _login.EntrarAsync(_usuario.Text, _contrasena.Text);
            _contrasena.Clear();
            if (!r.Exito || r.Acceso is null)
            {
                _mensaje.ForeColor = Color.Firebrick;
                _mensaje.Text = r.Mensaje ?? "No se pudo iniciar sesión.";
                _contrasena.Focus();
                return;
            }

            _login.Abrir(r.Acceso);
            _mensaje.ForeColor = Color.DarkGreen;
            _mensaje.Text = r.Acceso.Modulo switch
            {
                Modulo.Caja => $"Abriendo la Caja para {r.Acceso.Nombre}…",
                Modulo.Core => $"Abriendo el sistema interno para {r.Acceso.Nombre} en el navegador…",
                _ => $"Abriendo el Portal Ciudadano para {r.Acceso.Nombre} en el navegador…"
            };
            _usuario.Clear();
            _usuario.Focus();
        }
        catch (Exception ex)
        {
            _mensaje.ForeColor = Color.Firebrick;
            _mensaje.Text = ex.Message;
        }
        finally
        {
            _entrar.Enabled = true;
        }
    }
}
