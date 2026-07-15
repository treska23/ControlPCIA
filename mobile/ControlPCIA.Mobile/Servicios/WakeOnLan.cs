using System.Net;
using System.Net.Sockets;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ControlPCIA.Mobile.Modelos;
using Microsoft.Maui.Storage;

namespace ControlPCIA.Mobile.Servicios;

public sealed class WakeOnLan
{
    private const string ClaveDestinos = "controlpcia_wake_on_lan";
    private static readonly JsonSerializerOptions OpcionesJson =
        new(JsonSerializerDefaults.Web);

    private IReadOnlyList<DestinoWakeOnLan> _destinos = [];

    public bool EstaConfigurado => _destinos.Count > 0;
    public int Puerto =>
        _destinos.FirstOrDefault()?.Puerto is > 0 and <= 65535
            ? _destinos[0].Puerto
            : 9;

    public void Cargar()
    {
        string json = Preferences.Default.Get(ClaveDestinos, string.Empty);

        if (string.IsNullOrWhiteSpace(json))
        {
            _destinos = [];
            return;
        }

        try
        {
            _destinos =
                JsonSerializer.Deserialize<DestinoWakeOnLan[]>(
                    json,
                    OpcionesJson)
                ?? [];
        }
        catch (JsonException)
        {
            _destinos = [];
        }
    }

    public void Guardar(IReadOnlyList<DestinoWakeOnLan>? destinos)
    {
        DestinoWakeOnLan[] validos = (destinos ?? [])
            .Where(destino =>
                TryObtenerMac(destino.Mac, out _)
                &&
                destino.Puerto is > 0 and <= 65535
                &&
                destino.DireccionesBroadcast is { Count: > 0 })
            .ToArray();

        if (validos.Length == 0)
        {
            return;
        }

        _destinos = validos;
        Preferences.Default.Set(
            ClaveDestinos,
            JsonSerializer.Serialize(validos, OpcionesJson));
    }

    public async Task<int> EncenderAsync(
        CancellationToken cancellationToken = default)
    {
        if (_destinos.Count == 0)
        {
            throw new InvalidOperationException(
                "Conecta la app con el PC una vez para que aprenda su tarjeta de red.");
        }

        using var udp = new UdpClient(AddressFamily.InterNetwork)
        {
            EnableBroadcast = true
        };

        var enviados = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (DestinoWakeOnLan destino in _destinos)
        {
            if (!TryObtenerMac(destino.Mac, out byte[] mac))
            {
                continue;
            }

            byte[] paquete = CrearPaquete(mac);
            int puerto = destino.Puerto is > 0 and <= 65535
                ? destino.Puerto
                : 9;
            IEnumerable<string> direcciones =
                (destino.DireccionesBroadcast ?? [])
                .Append("255.255.255.255")
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (string texto in direcciones)
            {
                if (!IPAddress.TryParse(texto, out IPAddress? direccion)
                    ||
                    direccion.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                string clave = $"{destino.Mac}:{direccion}:{puerto}";

                if (!enviados.Add(clave))
                {
                    continue;
                }

                var extremo = new IPEndPoint(direccion, puerto);

                for (int intento = 0; intento < 3; intento++)
                {
                    await udp.SendAsync(
                        paquete,
                        extremo,
                        cancellationToken);
                }
            }
        }

        if (enviados.Count == 0)
        {
            throw new InvalidOperationException(
                "La configuracion Wake-on-LAN guardada ya no es valida.");
        }

        return enviados.Count;
    }

    public static bool EsOrdenEncender(string? texto)
    {
        string normalizada = Normalizar(texto);

        if (string.IsNullOrWhiteSpace(normalizada)
            ||
            normalizada.StartsWith("no ", StringComparison.Ordinal))
        {
            return false;
        }

        string[] palabras = normalizada.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);
        bool accion = palabras.Any(palabra =>
            palabra.StartsWith("encend", StringComparison.Ordinal)
            || palabra.StartsWith("arranc", StringComparison.Ordinal)
            || palabra.StartsWith("despiert", StringComparison.Ordinal));
        bool dispositivo = palabras.Any(palabra =>
            palabra is "pc" or "ordenador" or "computador" or "computadora" or "equipo");

        return accion && dispositivo;
    }

    internal static byte[] CrearPaquete(byte[] mac)
    {
        if (mac.Length != 6)
        {
            throw new ArgumentException(
                "Una direccion MAC debe tener 6 bytes.",
                nameof(mac));
        }

        var paquete = new byte[6 + 16 * mac.Length];
        Array.Fill(paquete, (byte)0xFF, 0, 6);

        for (int repeticion = 0; repeticion < 16; repeticion++)
        {
            Buffer.BlockCopy(
                mac,
                0,
                paquete,
                6 + repeticion * mac.Length,
                mac.Length);
        }

        return paquete;
    }

    private static bool TryObtenerMac(
        string? texto,
        out byte[] mac)
    {
        mac = [];
        string limpia = (texto ?? string.Empty)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (limpia.Length != 12)
        {
            return false;
        }

        try
        {
            mac = Convert.FromHexString(limpia);
            return mac.Length == 6;
        }
        catch (FormatException)
        {
            mac = [];
            return false;
        }
    }

    private static string Normalizar(string? texto)
    {
        string descompuesto = (texto ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);
        var resultado = new StringBuilder(descompuesto.Length);

        foreach (char caracter in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caracter)
                != UnicodeCategory.NonSpacingMark)
            {
                resultado.Append(
                    char.IsLetterOrDigit(caracter) || char.IsWhiteSpace(caracter)
                        ? caracter
                        : ' ');
            }
        }

        return resultado
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }
}
