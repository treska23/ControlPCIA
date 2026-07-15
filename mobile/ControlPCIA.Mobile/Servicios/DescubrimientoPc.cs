using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ControlPCIA.Mobile.Modelos;

namespace ControlPCIA.Mobile.Servicios;

public sealed class DescubrimientoPc
{
    private const int Puerto = 5188;
    private const string Peticion = "CONTROLPCIA_DESCUBRIR_V1";
    private const string Protocolo = "controlpcia/1";

    private static readonly JsonSerializerOptions OpcionesJson =
        new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PcDescubierto>> BuscarAsync(
        CancellationToken cancellationToken = default)
    {
        using var limite =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        limite.CancelAfter(TimeSpan.FromSeconds(3));

        using var udp = new UdpClient(AddressFamily.InterNetwork)
        {
            EnableBroadcast = true
        };

        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        byte[] peticion = Encoding.ASCII.GetBytes(Peticion);

        await udp.SendAsync(
            peticion,
            new IPEndPoint(IPAddress.Broadcast, Puerto),
            limite.Token);

        var encontrados = new Dictionary<string, PcDescubierto>(
            StringComparer.OrdinalIgnoreCase);

        while (!limite.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult recibido =
                    await udp.ReceiveAsync(limite.Token);

                RespuestaDescubrimiento? respuesta =
                    JsonSerializer.Deserialize<RespuestaDescubrimiento>(
                        recibido.Buffer,
                        OpcionesJson);

                if (respuesta?.Protocolo != Protocolo)
                {
                    continue;
                }

                foreach (string direccion in respuesta.Direcciones ?? [])
                {
                    if (!TryNormalizarDireccion(
                            direccion,
                            permitirBucleLocal: false,
                            out string normalizada))
                    {
                        continue;
                    }

                    encontrados[normalizada] =
                        new PcDescubierto(
                            string.IsNullOrWhiteSpace(respuesta.Nombre)
                                ? "PC con ControlPCIA"
                                : respuesta.Nombre.Trim(),
                            normalizada);
                }
            }
            catch (OperationCanceledException)
                when (limite.IsCancellationRequested)
            {
                break;
            }
            catch (JsonException)
            {
                // Se ignoran respuestas UDP que no sean de ControlPCIA.
            }
        }

        return encontrados.Values
            .OrderBy(pc => pc.Nombre)
            .ThenBy(pc => pc.Direccion)
            .ToArray();
    }

    public static bool TryNormalizarDireccion(
        string? valor,
        bool permitirBucleLocal,
        out string direccion)
    {
        direccion = string.Empty;
        string candidato = valor?.Trim().TrimEnd('/') ?? string.Empty;

        if (!candidato.Contains("://", StringComparison.Ordinal))
        {
            candidato = "http://" + candidato;
        }

        if (!Uri.TryCreate(candidato, UriKind.Absolute, out Uri? uri)
            ||
            uri.Scheme is not ("http" or "https")
            ||
            !IPAddress.TryParse(uri.Host, out IPAddress? ip)
            ||
            !EsDireccionLocal(ip, permitirBucleLocal)
            ||
            uri.Port is < 1024 or > 65535)
        {
            return false;
        }

        var limpia = new UriBuilder(uri.Scheme, uri.Host, uri.Port)
        {
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        direccion = limpia.Uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    private static bool EsDireccionLocal(
        IPAddress direccion,
        bool permitirBucleLocal)
    {
        if (IPAddress.IsLoopback(direccion))
        {
            return permitirBucleLocal;
        }

        if (direccion.IsIPv4MappedToIPv6)
        {
            direccion = direccion.MapToIPv4();
        }

        byte[] bytes = direccion.GetAddressBytes();

        return direccion.AddressFamily == AddressFamily.InterNetwork
               &&
               (bytes[0] == 10
                || bytes[0] == 192 && bytes[1] == 168
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 169 && bytes[1] == 254);
    }

    private sealed record RespuestaDescubrimiento(
        string? Protocolo,
        string? Nombre,
        IReadOnlyList<string>? Direcciones);
}
