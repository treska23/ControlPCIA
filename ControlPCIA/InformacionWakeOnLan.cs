using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ControlPCIA;

internal sealed record DestinoWakeOnLan(
    string Nombre,
    string Mac,
    int Puerto,
    IReadOnlyList<string> DireccionesBroadcast);

internal static class InformacionWakeOnLan
{
    internal const int PuertoPredeterminado = 9;

    public static IReadOnlyList<DestinoWakeOnLan> ObtenerDestinos()
    {
        var destinos = new List<DestinoWakeOnLan>();

        try
        {
            foreach (NetworkInterface adaptador in
                     NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adaptador.OperationalStatus != OperationalStatus.Up
                    ||
                    !EsAdaptadorFisico(adaptador.NetworkInterfaceType))
                {
                    continue;
                }

                byte[] mac = adaptador
                    .GetPhysicalAddress()
                    .GetAddressBytes();

                if (mac.Length != 6)
                {
                    continue;
                }

                IPInterfaceProperties propiedades =
                    adaptador.GetIPProperties();

                bool tienePuertaEnlace = propiedades.GatewayAddresses.Any(
                    puerta =>
                        puerta.Address.AddressFamily ==
                        AddressFamily.InterNetwork
                        &&
                        !puerta.Address.Equals(IPAddress.Any));

                if (!tienePuertaEnlace)
                {
                    continue;
                }

                string[] broadcasts = propiedades.UnicastAddresses
                    .Where(direccion =>
                        direccion.Address.AddressFamily ==
                        AddressFamily.InterNetwork
                        &&
                        direccion.IPv4Mask is not null
                        &&
                        ServidorMovil.EsDireccionPermitida(
                            direccion.Address))
                    .Select(direccion =>
                        CalcularBroadcast(
                            direccion.Address,
                            direccion.IPv4Mask))
                    .Where(direccion => direccion is not null)
                    .Select(direccion => direccion!.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (broadcasts.Length == 0)
                {
                    continue;
                }

                destinos.Add(
                    new DestinoWakeOnLan(
                        adaptador.Name,
                        Convert.ToHexString(mac),
                        PuertoPredeterminado,
                        broadcasts));
            }
        }
        catch (NetworkInformationException)
        {
            return [];
        }

        return destinos
            .OrderBy(destino => destino.Nombre)
            .ToArray();
    }

    internal static IPAddress? CalcularBroadcast(
        IPAddress direccion,
        IPAddress mascara)
    {
        byte[] ip = direccion.GetAddressBytes();
        byte[] mask = mascara.GetAddressBytes();

        if (ip.Length != 4 || mask.Length != 4)
        {
            return null;
        }

        var broadcast = new byte[4];

        for (int indice = 0; indice < broadcast.Length; indice++)
        {
            broadcast[indice] =
                (byte)(ip[indice] | ~mask[indice]);
        }

        return new IPAddress(broadcast);
    }

    private static bool EsAdaptadorFisico(NetworkInterfaceType tipo)
    {
        return tipo is NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.Wireless80211;
    }
}
