using System.Text;
using ControlPCIA.Mobile.Modelos;
using Microsoft.Maui.Graphics;

namespace ControlPCIA.Mobile.Controles;

public sealed class VentanaLienzo
{
    public required string Titulo { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Ancho { get; set; }
    public float Alto { get; set; }

    public override string ToString()
    {
        return Titulo;
    }
}

public sealed class DistribucionDrawable : IDrawable
{
    private readonly List<PantallaLienzo> _pantallas = [];
    private readonly List<VentanaLienzo> _ventanas = [];
    private RectF _area;
    private float _inicioX;
    private float _inicioY;

    public IReadOnlyList<VentanaLienzo> Ventanas => _ventanas;
    public int Seleccionada { get; private set; } = -1;

    public VentanaLienzo? VentanaSeleccionada =>
        Seleccionada >= 0 && Seleccionada < _ventanas.Count
            ? _ventanas[Seleccionada]
            : null;

    public void Cargar(EscenaPc escena)
    {
        _pantallas.Clear();
        _ventanas.Clear();

        IReadOnlyList<PantallaPc> pantallas =
            escena.Pantallas is { Count: > 0 }
                ? escena.Pantallas
                : [new PantallaPc(0, 0, 1920, 1080, true)];

        int minimoX = pantallas.Min(pantalla => pantalla.X);
        int minimoY = pantallas.Min(pantalla => pantalla.Y);
        int maximoX = pantallas.Max(
            pantalla => pantalla.X + pantalla.Ancho);
        int maximoY = pantallas.Max(
            pantalla => pantalla.Y + pantalla.Alto);
        float anchoTotal = Math.Max(1, maximoX - minimoX);
        float altoTotal = Math.Max(1, maximoY - minimoY);

        foreach (PantallaPc pantalla in pantallas)
        {
            _pantallas.Add(
                new PantallaLienzo(
                    (pantalla.X - minimoX) / anchoTotal,
                    (pantalla.Y - minimoY) / altoTotal,
                    pantalla.Ancho / anchoTotal,
                    pantalla.Alto / altoTotal,
                    pantalla.Principal));
        }

        int indice = 0;

        foreach (VentanaPc ventana in
                 (escena.Ventanas ?? [])
                 .Where(ventana =>
                     !string.IsNullOrWhiteSpace(ventana.Titulo))
                 .Take(10))
        {
            float ancho = Limitar(
                ventana.Ancho / anchoTotal,
                0.18f,
                0.92f);
            float alto = Limitar(
                ventana.Alto / altoTotal,
                0.16f,
                0.92f);
            float x;
            float y;

            if (ventana.Minimizada)
            {
                x = 0.06f + indice % 4 * 0.07f;
                y = 0.08f + indice % 4 * 0.06f;
                ancho = 0.42f;
                alto = 0.42f;
            }
            else
            {
                x = (ventana.X - minimoX) / anchoTotal;
                y = (ventana.Y - minimoY) / altoTotal;
            }

            _ventanas.Add(
                new VentanaLienzo
                {
                    Titulo = ventana.Titulo.Trim(),
                    X = Limitar(x, 0, 1 - ancho),
                    Y = Limitar(y, 0, 1 - alto),
                    Ancho = ancho,
                    Alto = alto
                });

            indice++;
        }

        Seleccionada = _ventanas.Count > 0 ? 0 : -1;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        _area = new RectF(
            dirtyRect.X + 12,
            dirtyRect.Y + 12,
            Math.Max(1, dirtyRect.Width - 24),
            Math.Max(1, dirtyRect.Height - 24));

        canvas.FillColor = Color.FromArgb("#07111F");
        canvas.FillRectangle(dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height);

        canvas.StrokeColor = Color.FromArgb("#15304D");
        canvas.StrokeSize = 1;

        for (int indice = 1; indice < 8; indice++)
        {
            float x = _area.X + _area.Width * indice / 8;
            float y = _area.Y + _area.Height * indice / 8;
            canvas.DrawLine(x, _area.Y, x, _area.Bottom);
            canvas.DrawLine(_area.X, y, _area.Right, y);
        }

        foreach (PantallaLienzo pantalla in _pantallas)
        {
            RectF rectangulo = ObtenerRectangulo(pantalla);
            canvas.FillColor = Color.FromArgb(
                pantalla.Principal ? "#102945" : "#0D2239");
            canvas.FillRectangle(
                rectangulo.X,
                rectangulo.Y,
                rectangulo.Width,
                rectangulo.Height);
            canvas.StrokeColor = Color.FromArgb(
                pantalla.Principal ? "#38BDF8" : "#386387");
            canvas.StrokeSize = pantalla.Principal ? 2 : 1;
            canvas.DrawRectangle(
                rectangulo.X,
                rectangulo.Y,
                rectangulo.Width,
                rectangulo.Height);
        }

        for (int indice = 0; indice < _ventanas.Count; indice++)
        {
            VentanaLienzo ventana = _ventanas[indice];
            RectF rectangulo = ObtenerRectangulo(ventana);
            bool seleccionada = indice == Seleccionada;

            canvas.FillColor = Color.FromArgb(
                seleccionada ? "#2563EB" : "#334155");
            canvas.FillRectangle(
                rectangulo.X,
                rectangulo.Y,
                rectangulo.Width,
                rectangulo.Height);
            canvas.StrokeColor = Color.FromArgb(
                seleccionada ? "#BAE6FD" : "#64748B");
            canvas.StrokeSize = seleccionada ? 3 : 1;
            canvas.DrawRectangle(
                rectangulo.X,
                rectangulo.Y,
                rectangulo.Width,
                rectangulo.Height);

            canvas.FontColor = Colors.White;
            canvas.FontSize = seleccionada ? 13 : 11;
            canvas.DrawString(
                RecortarTitulo(ventana.Titulo, rectangulo.Width),
                rectangulo.X + 8,
                rectangulo.Y + 20,
                HorizontalAlignment.Left);
        }
    }

    public int Seleccionar(float x, float y)
    {
        for (int indice = _ventanas.Count - 1; indice >= 0; indice--)
        {
            if (ObtenerRectangulo(_ventanas[indice]).Contains(x, y))
            {
                Seleccionada = indice;
                return indice;
            }
        }

        return Seleccionada;
    }

    public void Seleccionar(int indice)
    {
        if (indice >= 0 && indice < _ventanas.Count)
        {
            Seleccionada = indice;
        }
    }

    public void IniciarArrastre()
    {
        if (VentanaSeleccionada is not { } ventana)
        {
            return;
        }

        _inicioX = ventana.X;
        _inicioY = ventana.Y;
    }

    public void Arrastrar(double desplazamientoX, double desplazamientoY)
    {
        if (VentanaSeleccionada is not { } ventana
            ||
            _area.Width <= 0
            ||
            _area.Height <= 0)
        {
            return;
        }

        ventana.X = Limitar(
            _inicioX + (float)desplazamientoX / _area.Width,
            0,
            1 - ventana.Ancho);
        ventana.Y = Limitar(
            _inicioY + (float)desplazamientoY / _area.Height,
            0,
            1 - ventana.Alto);
    }

    public void AjustarTamano(float ancho, float alto)
    {
        if (VentanaSeleccionada is not { } ventana)
        {
            return;
        }

        ventana.Ancho = Limitar(ancho, 0.18f, 1);
        ventana.Alto = Limitar(alto, 0.16f, 1);
        ventana.X = Limitar(ventana.X, 0, 1 - ventana.Ancho);
        ventana.Y = Limitar(ventana.Y, 0, 1 - ventana.Alto);
    }

    public string CrearOrden()
    {
        var texto = new StringBuilder(
            "Organiza las ventanas abiertas segun este dibujo del usuario. " +
            "Interpreta las zonas y usa comandos de consola e interfaz seguros.\n");

        foreach (VentanaLienzo ventana in _ventanas)
        {
            float centroX = ventana.X + ventana.Ancho / 2;
            float centroY = ventana.Y + ventana.Alto / 2;
            string horizontal = centroX switch
            {
                < 0.34f => "izquierda",
                > 0.66f => "derecha",
                _ => "centro"
            };
            string vertical = centroY switch
            {
                < 0.34f => "arriba",
                > 0.66f => "abajo",
                _ => "centro vertical"
            };

            texto.AppendLine(
                $"- \"{ventana.Titulo}\": {vertical}, {horizontal}; " +
                $"ancho aproximado {Math.Round(ventana.Ancho * 100)}%, " +
                $"alto {Math.Round(ventana.Alto * 100)}%.");

            if (texto.Length > 900)
            {
                break;
            }
        }

        return texto.ToString().Trim();
    }

    private RectF ObtenerRectangulo(VentanaLienzo ventana)
    {
        return new RectF(
            _area.X + ventana.X * _area.Width,
            _area.Y + ventana.Y * _area.Height,
            ventana.Ancho * _area.Width,
            ventana.Alto * _area.Height);
    }

    private RectF ObtenerRectangulo(PantallaLienzo pantalla)
    {
        return new RectF(
            _area.X + pantalla.X * _area.Width,
            _area.Y + pantalla.Y * _area.Height,
            pantalla.Ancho * _area.Width,
            pantalla.Alto * _area.Height);
    }

    private static string RecortarTitulo(string titulo, float ancho)
    {
        int limite = Math.Max(5, (int)(ancho / 8));

        return titulo.Length <= limite
            ? titulo
            : titulo[..Math.Max(2, limite - 1)] + "…";
    }

    private static float Limitar(float valor, float minimo, float maximo)
    {
        return Math.Clamp(valor, minimo, Math.Max(minimo, maximo));
    }

    private sealed record PantallaLienzo(
        float X,
        float Y,
        float Ancho,
        float Alto,
        bool Principal);
}
