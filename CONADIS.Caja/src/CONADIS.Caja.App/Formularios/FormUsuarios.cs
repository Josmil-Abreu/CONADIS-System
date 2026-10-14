using CONADIS.Caja.Negocio.Modelos;
using CONADIS.Caja.Negocio.Servicios;

namespace CONADIS.Caja.App.Formularios;

/// <summary>Administración de usuarios de la caja (solo rol Administrador): crear, editar, desactivar, restablecer contraseña y desbloquear.</summary>
public sealed class FormUsuarios : Form
{
    private readonly ServicioUsuariosCaja _usuarios;
    private readonly ContextoCaja _contexto;
    private int? _seleccionado;

    private readonly DataGridView _grilla = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersVisible = false, Font = Ui.Normal
    };
    private readonly TextBox _username = new() { Width = 260, Font = Ui.Normal };
    private readonly TextBox _nombre = new() { Width = 260, Font = Ui.Normal };
    private readonly ComboBox _rol = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Font = Ui.Normal };
    private readonly CheckBox _activo = new() { Text = "Activo", AutoSize = true, Font = Ui.Normal, Checked = true };
    private readonly TextBox _contrasena = new() { Width = 260, Font = Ui.Normal, UseSystemPasswordChar = true };
    private readonly Label _ayuda = new() { AutoSize = true, Font = Ui.Normal, MaximumSize = new Size(260, 0) };
    private readonly Button _nuevo = Ui.Boton("Nuevo", 120);
    private readonly Button _guardar = Ui.Boton("Guardar", 120);
    private readonly Button _desbloquear = Ui.Boton("Desbloquear", 120);

    public FormUsuarios(ServicioUsuariosCaja usuarios, ContextoCaja contexto)
    {
        _usuarios = usuarios;
        _contexto = contexto;

        Text = "Usuarios de la caja";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1000, 520);
        Font = Ui.Normal;

        var editor = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 300, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
        var botones = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
        botones.Controls.AddRange(new Control[] { _nuevo, _guardar, _desbloquear });
        editor.Controls.AddRange(new Control[]
        {
            Ui.Etiqueta("Usuario"), _username, Ui.Etiqueta("Nombre completo"), _nombre, Ui.Etiqueta("Rol"), _rol, _activo,
            Ui.Etiqueta("Contraseña"), _contrasena, _ayuda, botones
        });
        var lista = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        lista.Controls.Add(_grilla);
        Controls.Add(lista);
        Controls.Add(editor);

        _rol.DataSource = ServicioUsuariosCaja.Roles;
        _grilla.SelectionChanged += (_, _) => Seleccionar();
        _nuevo.Click += (_, _) => Limpiar();
        _guardar.Click += async (_, _) => await Ui.EjecutarAsync(_guardar, GuardarAsync);
        _desbloquear.Click += async (_, _) => await Ui.EjecutarAsync(_desbloquear, DesbloquearAsync);
        Load += async (_, _) => { Limpiar(); await Ui.EjecutarAsync(_guardar, CargarAsync); };
    }

    private async Task CargarAsync() => _grilla.DataSource = await _usuarios.ListarAsync(_contexto.UsuarioActual);

    private void Limpiar()
    {
        _seleccionado = null;
        _username.Text = _nombre.Text = "";
        _contrasena.Clear();
        _rol.SelectedItem = RolesCaja.Cajero;
        _activo.Checked = true;
        _desbloquear.Enabled = false;
        _ayuda.Text = "Nuevo usuario: la contraseña es obligatoria (8+ caracteres, letras y números).";
        _username.Focus();
    }

    private void Seleccionar()
    {
        if (_grilla.CurrentRow?.DataBoundItem is not UsuarioCajaDetalle u) return;
        _seleccionado = u.UsuarioId;
        _username.Text = u.Username;
        _nombre.Text = u.NombreCompleto;
        _rol.SelectedItem = u.Rol;
        _activo.Checked = u.Activo;
        _contrasena.Clear();
        _desbloquear.Enabled = u.Bloqueado;
        _ayuda.Text = "Deje la contraseña vacía para no cambiarla; escriba una para restablecerla.";
    }

    private async Task GuardarAsync()
    {
        await _usuarios.GuardarAsync(_contexto.UsuarioActual, new DatosUsuarioCaja(_seleccionado, _username.Text, _nombre.Text,
            (string)_rol.SelectedItem!, _activo.Checked, string.IsNullOrEmpty(_contrasena.Text) ? null : _contrasena.Text));
        _contrasena.Clear();
        MessageBox.Show(this, "Usuario guardado.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        await CargarAsync();
    }

    private async Task DesbloquearAsync()
    {
        if (_seleccionado is not { } id) return;
        await _usuarios.DesbloquearAsync(_contexto.UsuarioActual, id);
        await CargarAsync();
    }
}
