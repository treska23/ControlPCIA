using System.Runtime.InteropServices;

namespace ControlPCIA;

internal static class EntradaTecladoSegura
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const uint Unicode = 0x0004;

    private static readonly Dictionary<string, ushort> Teclas =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["BACKSPACE"] = 0x08,
            ["TAB"] = 0x09,
            ["ENTER"] = 0x0D,
            ["SHIFT"] = 0x10,
            ["CTRL"] = 0x11,
            ["ALT"] = 0x12,
            ["ESC"] = 0x1B,
            ["SPACE"] = 0x20,
            ["PGUP"] = 0x21,
            ["PGDN"] = 0x22,
            ["END"] = 0x23,
            ["HOME"] = 0x24,
            ["LEFT"] = 0x25,
            ["UP"] = 0x26,
            ["RIGHT"] = 0x27,
            ["DOWN"] = 0x28,
            ["DELETE"] = 0x2E
        };

    public static void EnviarAtajo(string atajo)
    {
        if (!ValidadorAutomatizacionAplicaciones.EsAtajoSeguro(atajo))
        {
            throw new InvalidOperationException(
                "El atajo no ha superado la validación de seguridad.");
        }

        string[] partes = atajo
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant()
            .Split('+', StringSplitOptions.RemoveEmptyEntries);
        ushort[] modificadores = partes[..^1]
            .Select(ObtenerCodigo)
            .ToArray();
        ushort tecla = ObtenerCodigo(partes[^1]);
        var entradas = new List<Entrada>();

        foreach (ushort modificador in modificadores)
        {
            entradas.Add(CrearEntrada(modificador, 0));
        }

        entradas.Add(CrearEntrada(tecla, 0));
        entradas.Add(CrearEntrada(tecla, KeyUp));

        foreach (ushort modificador in modificadores.Reverse())
        {
            entradas.Add(CrearEntrada(modificador, KeyUp));
        }

        Enviar(entradas);
    }

    public static void ReemplazarTexto(string texto)
    {
        EnviarAtajo("CTRL+A");
        var entradas = new List<Entrada>(texto.Length * 2);

        foreach (char caracter in texto)
        {
            entradas.Add(CrearEntradaUnicode(caracter, 0));
            entradas.Add(CrearEntradaUnicode(caracter, KeyUp));
        }

        Enviar(entradas);
    }

    private static ushort ObtenerCodigo(string tecla)
    {
        if (Teclas.TryGetValue(tecla, out ushort codigo))
        {
            return codigo;
        }

        if (tecla.Length == 1
            && char.IsLetterOrDigit(tecla[0]))
        {
            return char.ToUpperInvariant(tecla[0]);
        }

        if (tecla.Length is 2 or 3
            && tecla[0] == 'F'
            && int.TryParse(tecla[1..], out int funcion)
            && funcion is >= 1 and <= 24)
        {
            return (ushort)(0x70 + funcion - 1);
        }

        throw new InvalidOperationException(
            $"La tecla '{tecla}' no tiene un código seguro conocido.");
    }

    private static Entrada CrearEntrada(ushort tecla, uint indicadores)
    {
        return new Entrada
        {
            Tipo = InputKeyboard,
            Union = new UnionEntrada
            {
                Teclado = new EntradaTeclado
                {
                    TeclaVirtual = tecla,
                    Indicadores = indicadores
                }
            }
        };
    }

    private static Entrada CrearEntradaUnicode(
        char caracter,
        uint indicadores)
    {
        return new Entrada
        {
            Tipo = InputKeyboard,
            Union = new UnionEntrada
            {
                Teclado = new EntradaTeclado
                {
                    Codigo = caracter,
                    Indicadores = Unicode | indicadores
                }
            }
        };
    }

    private static void Enviar(IReadOnlyCollection<Entrada> entradas)
    {
        if (entradas.Count == 0)
        {
            return;
        }

        Entrada[] datos = entradas.ToArray();
        uint enviados = SendInput(
            (uint)datos.Length,
            datos,
            Marshal.SizeOf<Entrada>());

        if (enviados != datos.Length)
        {
            throw new InvalidOperationException(
                "Windows no aceptó todas las pulsaciones solicitadas.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Entrada
    {
        public uint Tipo;
        public UnionEntrada Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct UnionEntrada
    {
        [FieldOffset(0)]
        public EntradaTeclado Teclado;

        [FieldOffset(0)]
        public EntradaRaton Raton;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EntradaRaton
    {
        public int X;
        public int Y;
        public uint Datos;
        public uint Indicadores;
        public uint Tiempo;
        public UIntPtr InformacionExtra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EntradaTeclado
    {
        public ushort TeclaVirtual;
        public ushort Codigo;
        public uint Indicadores;
        public uint Tiempo;
        public UIntPtr InformacionExtra;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint numeroEntradas,
        Entrada[] entradas,
        int tamanoEntrada);
}
