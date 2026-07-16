using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace ControlPCIA;

internal sealed class AgenteBandeja : IDisposable
{
    private const int Ocultar = 0;
    private const int Mostrar = 5;

    private readonly CancellationTokenSource _cancelacion;
    private readonly ManualResetEventSlim _preparado = new(false);
    private readonly Thread _hilo;
    private Forms.NotifyIcon? _icono;
    private Forms.ToolStripMenuItem? _alternarConsola;
    private Forms.ToolStripMenuItem? _inicioWindows;
    private bool _consolaVisible;

    public AgenteBandeja(
        CancellationTokenSource cancelacion,
        bool ocultarConsola)
    {
        _cancelacion = cancelacion;
        _consolaVisible = !ocultarConsola;

        if (ocultarConsola)
        {
            CambiarVisibilidadConsola(visible: false);
        }

        _hilo = new Thread(EjecutarBandeja)
        {
            IsBackground = true,
            Name = "ControlPCIA.Bandeja"
        };
        _hilo.SetApartmentState(ApartmentState.STA);
        _hilo.Start();
        _preparado.Wait(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (_hilo.IsAlive)
        {
            try
            {
                Forms.ContextMenuStrip? menu =
                    _icono?.ContextMenuStrip;
                menu?.BeginInvoke(
                    (Action)(() => Forms.Application.ExitThread()));
                _hilo.Join(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }
        }

        _preparado.Dispose();
    }

    private void EjecutarBandeja()
    {
        try
        {
            _icono = new Forms.NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "ControlPCIA · activo",
                Visible = true,
                ContextMenuStrip = CrearMenu()
            };
            _icono.DoubleClick += (_, _) =>
                AbrirPaginaLocal();
            _preparado.Set();
            Forms.Application.Run();
        }
        finally
        {
            if (_icono is not null)
            {
                _icono.Visible = false;
                _icono.Dispose();
            }

            _preparado.Set();
        }
    }

    private Forms.ContextMenuStrip CrearMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        var estado = new Forms.ToolStripMenuItem(
            $"ControlPCIA activo · puerto {ServidorMovil.ObtenerPuerto()}")
        {
            Enabled = false
        };
        var abrir = new Forms.ToolStripMenuItem(
            "Abrir página de ControlPCIA");
        abrir.Click += (_, _) => AbrirPaginaLocal();

        _alternarConsola = new Forms.ToolStripMenuItem(
            _consolaVisible ? "Ocultar consola" : "Mostrar consola");
        _alternarConsola.Click += (_, _) => AlternarConsola();

        _inicioWindows = new Forms.ToolStripMenuItem(
            "Iniciar con Windows")
        {
            Checked = GestorInicioWindows.EstaActivado(),
            CheckOnClick = false
        };
        _inicioWindows.Click += (_, _) => AlternarInicioWindows();

        var salir = new Forms.ToolStripMenuItem("Salir de ControlPCIA");
        salir.Click += (_, _) =>
        {
            _cancelacion.Cancel();
            Forms.Application.ExitThread();
        };

        menu.Items.Add(estado);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(abrir);
        menu.Items.Add(_alternarConsola);
        menu.Items.Add(_inicioWindows);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(salir);
        return menu;
    }

    private void AlternarConsola()
    {
        _consolaVisible = !_consolaVisible;
        CambiarVisibilidadConsola(_consolaVisible);

        if (_alternarConsola is not null)
        {
            _alternarConsola.Text = _consolaVisible
                ? "Ocultar consola"
                : "Mostrar consola";
        }
    }

    private void AlternarInicioWindows()
    {
        try
        {
            if (GestorInicioWindows.EstaActivado())
            {
                GestorInicioWindows.Desactivar();
            }
            else
            {
                GestorInicioWindows.Activar();
            }

            if (_inicioWindows is not null)
            {
                _inicioWindows.Checked =
                    GestorInicioWindows.EstaActivado();
            }
        }
        catch (Exception ex)
        {
            Forms.MessageBox.Show(
                "No se pudo cambiar el inicio con Windows.\n\n" + ex.Message,
                "ControlPCIA",
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Error);
        }
    }

    private static void AbrirPaginaLocal()
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        $"http://127.0.0.1:{ServidorMovil.ObtenerPuerto()}",
                    UseShellExecute = true
                });
        }
        catch
        {
        }
    }

    private static void CambiarVisibilidadConsola(bool visible)
    {
        IntPtr consola = GetConsoleWindow();

        if (consola != IntPtr.Zero)
        {
            ShowWindow(consola, visible ? Mostrar : Ocultar);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr ventana, int comando);
}
