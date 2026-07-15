using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ControlPCIA;

internal static class ServidorDescubrimiento
{
    internal const int Puerto = 5188;
    internal const string Peticion = "CONTROLPCIA_DESCUBRIR_V1";
    internal const string Protocolo = "controlpcia/1";

    public static async Task EscucharAsync(
        int puertoHttp,
        CancellationToken cancellationToken)
    {
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);

            udp.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);

            udp.Client.Bind(
                new IPEndPoint(IPAddress.Any, Puerto));

            byte[] respuesta = CrearRespuesta(puertoHttp);

            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult recibido;

                try
                {
                    recibido = await udp.ReceiveAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (!ServidorMovil.EsDireccionPermitida(
                        recibido.RemoteEndPoint.Address)
                    ||
                    !recibido.Buffer.AsSpan().SequenceEqual(
                        Encoding.ASCII.GetBytes(Peticion)))
                {
                    continue;
                }

                await udp.SendAsync(
                    respuesta,
                    recibido.RemoteEndPoint,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Cierre normal junto con el servidor HTTP.
        }
        catch (SocketException ex)
        {
            Console.Error.WriteLine(
                "No se pudo activar la busqueda automatica del PC: " +
                ex.Message);
        }
    }

    internal static byte[] CrearRespuesta(int puertoHttp)
    {
        var respuesta = new RespuestaDescubrimiento(
            Protocolo,
            Environment.MachineName,
            ServidorMovil.ObtenerDireccionesLocales(puertoHttp));

        return JsonSerializer.SerializeToUtf8Bytes(
            respuesta,
            OpcionesJson);
    }

    private static readonly JsonSerializerOptions OpcionesJson =
        new(JsonSerializerDefaults.Web);

    private sealed record RespuestaDescubrimiento(
        string Protocolo,
        string Nombre,
        IReadOnlyList<string> Direcciones);
}
