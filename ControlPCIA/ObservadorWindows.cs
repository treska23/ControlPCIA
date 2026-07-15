using System.Runtime.InteropServices;
using System.Text;

namespace ControlPCIA;

internal sealed record VentanaObservada(
    string Titulo,
    int X,
    int Y,
    int Ancho,
    int Alto,
    bool Minimizada);

internal sealed record PantallaObservada(
    int X,
    int Y,
    int Ancho,
    int Alto,
    bool Principal);

internal sealed record EscenaWindows(
    IReadOnlyList<PantallaObservada> Pantallas,
    IReadOnlyList<VentanaObservada> Ventanas);

internal static class ObservadorWindows
{
    public static List<string> ObtenerVentanasAbiertas()
    {
        return ObtenerEscena()
            .Ventanas
            .Select(ventana => ventana.Titulo)
            .ToList();
    }

    public static EscenaWindows ObtenerEscena()
    {
        var pantallas = new List<PantallaObservada>();
        var ventanas = new List<VentanaObservada>();

        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (
                IntPtr monitor,
                IntPtr contexto,
                ref Rectangulo rectangulo,
                IntPtr parametro) =>
            {
                var informacion = new InformacionMonitor
                {
                    Tamano = Marshal.SizeOf<InformacionMonitor>()
                };

                if (GetMonitorInfo(monitor, ref informacion))
                {
                    Rectangulo area = informacion.AreaMonitor;

                    pantallas.Add(
                        new PantallaObservada(
                            area.Izquierda,
                            area.Arriba,
                            area.Derecha - area.Izquierda,
                            area.Abajo - area.Arriba,
                            (informacion.Indicadores & 1) != 0));
                }

                return true;
            },
            IntPtr.Zero);

        EnumWindows(
            (ventana, _) =>
            {
                if (!IsWindowVisible(ventana))
                {
                    return true;
                }

                string titulo = ObtenerTitulo(ventana);

                if (string.IsNullOrWhiteSpace(titulo)
                    ||
                    !GetWindowRect(ventana, out Rectangulo rectangulo))
                {
                    return true;
                }

                int ancho = rectangulo.Derecha - rectangulo.Izquierda;
                int alto = rectangulo.Abajo - rectangulo.Arriba;

                if (ancho <= 0 || alto <= 0)
                {
                    return true;
                }

                ventanas.Add(
                    new VentanaObservada(
                        titulo,
                        rectangulo.Izquierda,
                        rectangulo.Arriba,
                        ancho,
                        alto,
                        IsIconic(ventana)));

                return true;
            },
            IntPtr.Zero);

        return new EscenaWindows(
            pantallas
                .OrderByDescending(pantalla => pantalla.Principal)
                .ThenBy(pantalla => pantalla.X)
                .ToArray(),
            ventanas
                .OrderBy(ventana => ventana.Titulo)
                .ToArray());
    }

    public static string ObtenerVentanaActiva()
    {
        IntPtr ventana = GetForegroundWindow();

        return ventana == IntPtr.Zero
            ? string.Empty
            : ObtenerTitulo(ventana);
    }

    private static string ObtenerTitulo(IntPtr ventana)
    {
        int longitud = GetWindowTextLength(ventana);

        if (longitud == 0)
        {
            return string.Empty;
        }

        var texto = new StringBuilder(longitud + 1);
        GetWindowText(ventana, texto, texto.Capacity);

        return texto.ToString().Trim();
    }

    private delegate bool EnumerarVentanas(
        IntPtr ventana,
        IntPtr parametro);

    private delegate bool EnumerarMonitores(
        IntPtr monitor,
        IntPtr contexto,
        ref Rectangulo rectangulo,
        IntPtr parametro);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangulo
    {
        public int Izquierda;
        public int Arriba;
        public int Derecha;
        public int Abajo;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct InformacionMonitor
    {
        public int Tamano;
        public Rectangulo AreaMonitor;
        public Rectangulo AreaTrabajo;
        public int Indicadores;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(
        EnumerarVentanas funcion,
        IntPtr parametro);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr contexto,
        IntPtr region,
        EnumerarMonitores funcion,
        IntPtr parametro);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(
        IntPtr monitor,
        ref InformacionMonitor informacion);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr ventana);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr ventana);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(
        IntPtr ventana,
        out Rectangulo rectangulo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr ventana,
        StringBuilder texto,
        int longitudMaxima);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr ventana);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
