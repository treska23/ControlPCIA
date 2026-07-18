using System.Net;
using ControlPCIA.Mobile.Servicios;
using Xunit;
using DestinoMovilWakeOnLan =
    ControlPCIA.Mobile.Modelos.DestinoWakeOnLan;

namespace ControlPCIA.Tests;

public sealed class EmisorWakeOnLanTests
{
    [Fact]
    public void Crea_un_paquete_magico_estandar()
    {
        byte[] mac = [0x10, 0x20, 0x30, 0x40, 0x50, 0x60];

        byte[] paquete = EmisorWakeOnLan.CrearPaquete(mac);

        Assert.Equal(102, paquete.Length);
        Assert.All(
            paquete.Take(6),
            valor => Assert.Equal(0xFF, valor));

        for (int repeticion = 0; repeticion < 16; repeticion++)
        {
            Assert.Equal(
                mac,
                paquete
                    .Skip(6 + repeticion * mac.Length)
                    .Take(mac.Length));
        }
    }

    [Theory]
    [InlineData("10:20:30:40:50:60")]
    [InlineData("10-20-30-40-50-60")]
    [InlineData("102030405060")]
    public void Acepta_formatos_normales_de_mac(string texto)
    {
        Assert.True(
            EmisorWakeOnLan.TryObtenerMac(
                texto,
                out byte[] mac));
        Assert.Equal(
            [0x10, 0x20, 0x30, 0x40, 0x50, 0x60],
            mac);
    }

    [Theory]
    [InlineData("")]
    [InlineData("10:20:30")]
    [InlineData("GG:20:30:40:50:60")]
    public void Rechaza_mac_invalida(string texto)
    {
        Assert.False(
            EmisorWakeOnLan.TryObtenerMac(
                texto,
                out _));
    }

    [Fact]
    public async Task Envia_tres_paquetes_por_destino_sin_duplicados()
    {
        var destinos = new[]
        {
            new DestinoMovilWakeOnLan(
                "Ethernet",
                "10:20:30:40:50:60",
                9,
                ["192.168.1.255", "192.168.1.255"])
        };
        var observados = new List<(byte[] Paquete, IPEndPoint Extremo)>();

        int unicos = await EmisorWakeOnLan.EnviarAsync(
            destinos,
            (paquete, extremo, _) =>
            {
                observados.Add((paquete, extremo));
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, unicos);
        Assert.Equal(6, observados.Count);
        Assert.Equal(
            3,
            observados.Count(elemento =>
                elemento.Extremo.Address.Equals(
                    IPAddress.Parse("192.168.1.255"))));
        Assert.Equal(
            3,
            observados.Count(elemento =>
                elemento.Extremo.Address.Equals(
                    IPAddress.Broadcast)));
        Assert.All(
            observados,
            elemento =>
            {
                Assert.Equal(9, elemento.Extremo.Port);
                Assert.Equal(102, elemento.Paquete.Length);
            });
    }

    [Fact]
    public async Task Rechaza_una_configuracion_sin_destinos_validos()
    {
        DestinoMovilWakeOnLan[] destinos =
        [
            new(
                "Ethernet",
                "invalida",
                9,
                ["no-es-una-ip"])
        ];

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => EmisorWakeOnLan.EnviarAsync(
                    destinos,
                    (_, _, _) => Task.CompletedTask,
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "ya no es valida",
            error.Message,
            StringComparison.OrdinalIgnoreCase);
    }
}
