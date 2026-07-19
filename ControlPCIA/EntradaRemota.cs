using System.Runtime.InteropServices;

namespace ControlPCIA;

internal sealed record SolicitudRatonRemoto(
    string? Accion,
    int DeltaX = 0,
    int DeltaY = 0,
    int Rueda = 0);

internal sealed record SolicitudTecladoRemoto(
    string? Texto,
    string? Tecla,
    IReadOnlyList<string>? Modificadores);

internal sealed record ResultadoEntradaRemota(
    bool Correcto,
    string Detalle);

internal enum TipoEventoEntradaRemota
{
    Movimiento,
    Rueda,
    BotonRatonAbajo,
    BotonRatonArriba,
    TeclaAbajo,
    TeclaArriba,
    UnicodeAbajo,
    UnicodeArriba
}

internal sealed record EventoEntradaRemota(
    TipoEventoEntradaRemota Tipo,
    int Codigo = 0,
    int X = 0,
    int Y = 0,
    int Dato = 0);

/// <summary>
/// Entrada remota explícita para un móvil emparejado. No se expone al modelo
/// ni al traductor de comandos.
/// </summary>
internal static class EntradaRemota
{
    private const int MaximoDesplazamiento = 5_000;
    private const int MaximoRueda = 2_400;
    private const int MaximoTexto = 500;

    private const int BotonIzquierdo = 1;
    private const int BotonDerecho = 2;
    private const int BotonCentral = 3;

    private static readonly IReadOnlyDictionary<string, int>
        Teclas = CrearTeclas();

    private static readonly IReadOnlyDictionary<string, int>
        Modificadores = new Dictionary<string, int>(
            StringComparer.Ordinal)
        {
            ["ctrl"] = 0x11,
            ["control"] = 0x11,
            ["alt"] = 0x12,
            ["shift"] = 0x10,
            ["mayus"] = 0x10,
            ["win"] = 0x5B,
            ["windows"] = 0x5B
        };

    public static ResultadoEntradaRemota ProcesarRaton(
        SolicitudRatonRemoto solicitud)
    {
        return ProcesarRaton(
            solicitud,
            EnviarEventos);
    }

    internal static ResultadoEntradaRemota ProcesarRaton(
        SolicitudRatonRemoto solicitud,
        Func<IReadOnlyList<EventoEntradaRemota>, bool> enviar)
    {
        if (!TryCrearEventosRaton(
                solicitud,
                out IReadOnlyList<EventoEntradaRemota> eventos,
                out string error))
        {
            return new ResultadoEntradaRemota(
                false,
                error);
        }

        return enviar(eventos)
            ? new ResultadoEntradaRemota(
                true,
                "Entrada de ratón enviada.")
            : new ResultadoEntradaRemota(
                false,
                "Windows no aceptó la entrada de ratón.");
    }

    public static ResultadoEntradaRemota ProcesarTeclado(
        SolicitudTecladoRemoto solicitud)
    {
        return ProcesarTeclado(
            solicitud,
            EnviarEventos);
    }

    internal static ResultadoEntradaRemota ProcesarTeclado(
        SolicitudTecladoRemoto solicitud,
        Func<IReadOnlyList<EventoEntradaRemota>, bool> enviar)
    {
        if (!TryCrearEventosTeclado(
                solicitud,
                out IReadOnlyList<EventoEntradaRemota> eventos,
                out string error))
        {
            return new ResultadoEntradaRemota(
                false,
                error);
        }

        return enviar(eventos)
            ? new ResultadoEntradaRemota(
                true,
                "Entrada de teclado enviada.")
            : new ResultadoEntradaRemota(
                false,
                "Windows no aceptó la entrada de teclado.");
    }

    internal static bool TryCrearEventosRaton(
        SolicitudRatonRemoto solicitud,
        out IReadOnlyList<EventoEntradaRemota> eventos,
        out string error)
    {
        string accion =
            (solicitud.Accion ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        switch (accion)
        {
            case "move":
            case "mover":
                if (Math.Abs((long)solicitud.DeltaX)
                    > MaximoDesplazamiento
                    || Math.Abs((long)solicitud.DeltaY)
                    > MaximoDesplazamiento)
                {
                    return Fallar(
                        "El movimiento solicitado es demasiado grande.",
                        out eventos,
                        out error);
                }

                eventos =
                [
                    new EventoEntradaRemota(
                        TipoEventoEntradaRemota.Movimiento,
                        X: solicitud.DeltaX,
                        Y: solicitud.DeltaY)
                ];
                error = string.Empty;
                return true;

            case "wheel":
            case "rueda":
                if (solicitud.Rueda == 0
                    || Math.Abs((long)solicitud.Rueda)
                    > MaximoRueda)
                {
                    return Fallar(
                        "El desplazamiento de rueda no es válido.",
                        out eventos,
                        out error);
                }

                eventos =
                [
                    new EventoEntradaRemota(
                        TipoEventoEntradaRemota.Rueda,
                        Dato: solicitud.Rueda)
                ];
                error = string.Empty;
                return true;

            case "left-click":
                eventos = Click(BotonIzquierdo);
                error = string.Empty;
                return true;
            case "right-click":
                eventos = Click(BotonDerecho);
                error = string.Empty;
                return true;
            case "middle-click":
                eventos = Click(BotonCentral);
                error = string.Empty;
                return true;
            case "left-down":
                eventos =
                [
                    new EventoEntradaRemota(
                        TipoEventoEntradaRemota.BotonRatonAbajo,
                        BotonIzquierdo)
                ];
                error = string.Empty;
                return true;
            case "left-up":
                eventos =
                [
                    new EventoEntradaRemota(
                        TipoEventoEntradaRemota.BotonRatonArriba,
                        BotonIzquierdo)
                ];
                error = string.Empty;
                return true;
            case "right-down":
                eventos =
                [
                    new EventoEntradaRemota(
                        TipoEventoEntradaRemota.BotonRatonAbajo,
                        BotonDerecho)
                ];
                error = string.Empty;
                return true;
            case "right-up":
                eventos =
                [
                    new EventoEntradaRemota(
                        TipoEventoEntradaRemota.BotonRatonArriba,
                        BotonDerecho)
                ];
                error = string.Empty;
                return true;
            default:
                return Fallar(
                    "Acción de ratón no reconocida.",
                    out eventos,
                    out error);
        }
    }

    internal static bool TryCrearEventosTeclado(
        SolicitudTecladoRemoto solicitud,
        out IReadOnlyList<EventoEntradaRemota> eventos,
        out string error)
    {
        string texto =
            solicitud.Texto ?? string.Empty;
        string tecla =
            NormalizarTecla(
                solicitud.Tecla);

        if (texto.Length > 0 && tecla.Length > 0)
        {
            return Fallar(
                "Envía texto o una tecla, no ambos.",
                out eventos,
                out error);
        }

        if (texto.Length > 0)
        {
            if (texto.Length > MaximoTexto
                || texto.Any(caracter =>
                    char.IsControl(caracter)
                    && caracter is not '\r'
                    and not '\n'
                    and not '\t'))
            {
                return Fallar(
                    "El texto contiene caracteres no admitidos o es demasiado largo.",
                    out eventos,
                    out error);
            }

            var eventosTexto =
                new List<EventoEntradaRemota>();

            for (int indice = 0;
                 indice < texto.Length;
                 indice++)
            {
                char caracter =
                    texto[indice];

                if (caracter == '\r'
                    && indice + 1 < texto.Length
                    && texto[indice + 1] == '\n')
                {
                    continue;
                }

                if (caracter is '\r' or '\n' or '\t')
                {
                    int teclaEspecial =
                        caracter == '\t'
                            ? 0x09
                            : 0x0D;
                    eventosTexto.Add(
                        new EventoEntradaRemota(
                            TipoEventoEntradaRemota.TeclaAbajo,
                            teclaEspecial));
                    eventosTexto.Add(
                        new EventoEntradaRemota(
                            TipoEventoEntradaRemota.TeclaArriba,
                            teclaEspecial));
                    continue;
                }

                eventosTexto.Add(
                    new EventoEntradaRemota(
                        TipoEventoEntradaRemota.UnicodeAbajo,
                        caracter));
                eventosTexto.Add(
                    new EventoEntradaRemota(
                        TipoEventoEntradaRemota.UnicodeArriba,
                        caracter));
            }

            eventos = eventosTexto;
            error = string.Empty;
            return true;
        }

        if (!Teclas.TryGetValue(
                tecla,
                out int codigo))
        {
            return Fallar(
                "Tecla no reconocida.",
                out eventos,
                out error);
        }

        var modificadores =
            new List<int>();

        foreach (string valor in solicitud.Modificadores ?? [])
        {
            string normalizado =
                NormalizarTecla(valor);

            if (!Modificadores.TryGetValue(
                    normalizado,
                    out int modificador))
            {
                return Fallar(
                    $"Modificador no reconocido: {valor}.",
                    out eventos,
                    out error);
            }

            if (!modificadores.Contains(modificador))
            {
                modificadores.Add(modificador);
            }
        }

        var resultado =
            new List<EventoEntradaRemota>();
        resultado.AddRange(
            modificadores.Select(modificador =>
                new EventoEntradaRemota(
                    TipoEventoEntradaRemota.TeclaAbajo,
                    modificador)));
        resultado.Add(
            new EventoEntradaRemota(
                TipoEventoEntradaRemota.TeclaAbajo,
                codigo));
        resultado.Add(
            new EventoEntradaRemota(
                TipoEventoEntradaRemota.TeclaArriba,
                codigo));
        resultado.AddRange(
            modificadores
                .AsEnumerable()
                .Reverse()
                .Select(modificador =>
                    new EventoEntradaRemota(
                        TipoEventoEntradaRemota.TeclaArriba,
                        modificador)));
        eventos = resultado;
        error = string.Empty;
        return true;
    }

    private static IReadOnlyList<EventoEntradaRemota> Click(
        int boton)
    {
        return
        [
            new EventoEntradaRemota(
                TipoEventoEntradaRemota.BotonRatonAbajo,
                boton),
            new EventoEntradaRemota(
                TipoEventoEntradaRemota.BotonRatonArriba,
                boton)
        ];
    }

    private static bool Fallar(
        string mensaje,
        out IReadOnlyList<EventoEntradaRemota> eventos,
        out string error)
    {
        eventos = [];
        error = mensaje;
        return false;
    }

    private static string NormalizarTecla(
        string? tecla)
    {
        string normalizada =
            MemoriaRecetas.Normalizar(
                tecla ?? string.Empty);
        return normalizada switch
        {
            "intro" or "retorno" => "enter",
            "escape" => "esc",
            "retroceso" => "backspace",
            "suprimir" or "supr" => "delete",
            "espacio" => "space",
            "izquierda" => "left",
            "derecha" => "right",
            "arriba" => "up",
            "abajo" => "down",
            "inicio" => "home",
            "fin" => "end",
            "pagina arriba" => "pageup",
            "pagina abajo" => "pagedown",
            _ => normalizada
        };
    }

    private static IReadOnlyDictionary<string, int>
        CrearTeclas()
    {
        var teclas =
            new Dictionary<string, int>(
                StringComparer.Ordinal)
            {
                ["backspace"] = 0x08,
                ["tab"] = 0x09,
                ["enter"] = 0x0D,
                ["shift"] = 0x10,
                ["ctrl"] = 0x11,
                ["control"] = 0x11,
                ["alt"] = 0x12,
                ["pause"] = 0x13,
                ["capslock"] = 0x14,
                ["esc"] = 0x1B,
                ["space"] = 0x20,
                ["pageup"] = 0x21,
                ["pagedown"] = 0x22,
                ["end"] = 0x23,
                ["home"] = 0x24,
                ["left"] = 0x25,
                ["up"] = 0x26,
                ["right"] = 0x27,
                ["down"] = 0x28,
                ["printscreen"] = 0x2C,
                ["insert"] = 0x2D,
                ["delete"] = 0x2E,
                ["win"] = 0x5B,
                ["windows"] = 0x5B
            };

        for (int codigo = '0'; codigo <= '9'; codigo++)
        {
            teclas[((char)codigo).ToString()] = codigo;
        }

        for (int codigo = 'A'; codigo <= 'Z'; codigo++)
        {
            teclas[
                char.ToLowerInvariant(
                    (char)codigo)
                .ToString()] = codigo;
        }

        for (int numero = 1; numero <= 24; numero++)
        {
            teclas[$"f{numero}"] =
                0x70 + numero - 1;
        }

        return teclas;
    }

    private static bool EnviarEventos(
        IReadOnlyList<EventoEntradaRemota> eventos)
    {
        Input[] entradas = eventos
            .Select(CrearInput)
            .ToArray();

        if (entradas.Length == 0)
        {
            return false;
        }

        uint enviados =
            SendInput(
                (uint)entradas.Length,
                entradas,
                Marshal.SizeOf<Input>());
        return enviados == entradas.Length;
    }

    private static Input CrearInput(
        EventoEntradaRemota evento)
    {
        return evento.Tipo switch
        {
            TipoEventoEntradaRemota.Movimiento =>
                CrearRaton(
                    evento.X,
                    evento.Y,
                    0,
                    0x0001),
            TipoEventoEntradaRemota.Rueda =>
                CrearRaton(
                    0,
                    0,
                    unchecked((uint)evento.Dato),
                    0x0800),
            TipoEventoEntradaRemota.BotonRatonAbajo =>
                CrearRaton(
                    0,
                    0,
                    0,
                    evento.Codigo switch
                    {
                        BotonDerecho => 0x0008,
                        BotonCentral => 0x0020,
                        _ => 0x0002
                    }),
            TipoEventoEntradaRemota.BotonRatonArriba =>
                CrearRaton(
                    0,
                    0,
                    0,
                    evento.Codigo switch
                    {
                        BotonDerecho => 0x0010,
                        BotonCentral => 0x0040,
                        _ => 0x0004
                    }),
            TipoEventoEntradaRemota.UnicodeAbajo =>
                CrearTeclado(
                    0,
                    checked((ushort)evento.Codigo),
                    0x0004),
            TipoEventoEntradaRemota.UnicodeArriba =>
                CrearTeclado(
                    0,
                    checked((ushort)evento.Codigo),
                    0x0004 | 0x0002),
            TipoEventoEntradaRemota.TeclaArriba =>
                CrearTeclado(
                    checked((ushort)evento.Codigo),
                    0,
                    0x0002),
            _ =>
                CrearTeclado(
                    checked((ushort)evento.Codigo),
                    0,
                    0)
        };
    }

    private static Input CrearRaton(
        int x,
        int y,
        uint dato,
        uint banderas)
    {
        return new Input
        {
            Tipo = 0,
            Datos = new UnionEntrada
            {
                Raton = new EntradaRaton
                {
                    X = x,
                    Y = y,
                    Datos = dato,
                    Banderas = banderas
                }
            }
        };
    }

    private static Input CrearTeclado(
        ushort tecla,
        ushort scan,
        uint banderas)
    {
        return new Input
        {
            Tipo = 1,
            Datos = new UnionEntrada
            {
                Teclado = new EntradaTeclado
                {
                    Tecla = tecla,
                    Scan = scan,
                    Banderas = banderas
                }
            }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Tipo;
        public UnionEntrada Datos;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct UnionEntrada
    {
        [FieldOffset(0)]
        public EntradaRaton Raton;

        [FieldOffset(0)]
        public EntradaTeclado Teclado;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EntradaRaton
    {
        public int X;
        public int Y;
        public uint Datos;
        public uint Banderas;
        public uint Tiempo;
        public nuint InformacionExtra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EntradaTeclado
    {
        public ushort Tecla;
        public ushort Scan;
        public uint Banderas;
        public uint Tiempo;
        public nuint InformacionExtra;
    }

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern uint SendInput(
        uint cantidad,
        [In] Input[] entradas,
        int tamano);
}
