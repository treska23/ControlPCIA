using System.Net;
using System.Net.Sockets;
using DestinoMovilWakeOnLan =
    ControlPCIA.Mobile.Modelos.DestinoWakeOnLan;

namespace ControlPCIA.Mobile.Servicios;

public static class EmisorWakeOnLan
{
    public static async Task<int> EnviarAsync(
        IReadOnlyList<DestinoMovilWakeOnLan> destinos,
        CancellationToken cancellationToken = default)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork)
        {
            EnableBroadcast = true
        };

        return await EnviarAsync(
            destinos,
            async (paquete, extremo, token) =>
            {
                await udp.SendAsync(
                    paquete,
                    extremo,
                    token);
            },
            cancellationToken);
    }

    internal static async Task<int> EnviarAsync(
        IReadOnlyList<DestinoMovilWakeOnLan> destinos,
        Func<byte[], IPEndPoint, CancellationToken, Task> enviar,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinos);
        ArgumentNullException.ThrowIfNull(enviar);

        var enviados = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (DestinoMovilWakeOnLan destino in destinos)
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
                if (!IPAddress.TryParse(
                        texto,
                        out IPAddress? direccion)
                    ||
                    direccion.AddressFamily
                        != AddressFamily.InterNetwork)
                {
                    continue;
                }

                string clave =
                    $"{Convert.ToHexString(mac)}:{direccion}:{puerto}";

                if (!enviados.Add(clave))
                {
                    continue;
                }

                var extremo = new IPEndPoint(direccion, puerto);

                for (int intento = 0; intento < 3; intento++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await enviar(
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

    internal static bool TryObtenerMac(
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
}
