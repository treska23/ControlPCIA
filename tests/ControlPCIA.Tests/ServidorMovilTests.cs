using System.Net;
using System.Text.Json;
using Xunit;

namespace ControlPCIA.Tests;

public sealed class ServidorMovilTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.2.3.4")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.25")]
    [InlineData("169.254.2.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1234")]
    public void Permite_direcciones_de_red_local(string texto)
    {
        Assert.True(
            ServidorMovil.EsDireccionPermitida(
                IPAddress.Parse(texto)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void Bloquea_direcciones_publicas(string texto)
    {
        Assert.False(
            ServidorMovil.EsDireccionPermitida(
                IPAddress.Parse(texto)));
    }

    [Fact]
    public void Bloquea_direccion_ausente()
    {
        Assert.False(
            ServidorMovil.EsDireccionPermitida(null));
    }

    [Fact]
    public void Publica_un_manifiesto_pwa_instalable()
    {
        using JsonDocument manifiesto =
            JsonDocument.Parse(ServidorMovil.ManifiestoPwa);

        JsonElement raiz = manifiesto.RootElement;

        Assert.Equal(
            "standalone",
            raiz.GetProperty("display").GetString());
        Assert.Equal(
            "/?source=pwa",
            raiz.GetProperty("start_url").GetString());
        Assert.Contains(
            raiz.GetProperty("icons").EnumerateArray(),
            icono =>
                icono.GetProperty("sizes").GetString() == "512x512"
                &&
                icono.GetProperty("purpose").GetString() == "maskable");
    }

    [Fact]
    public void La_pwa_no_guarda_respuestas_de_la_api()
    {
        Assert.Contains(
            "url.pathname.startsWith('/api/')",
            ServidorMovil.ServiceWorkerPwa,
            StringComparison.Ordinal);
        Assert.Contains(
            "rel=\"manifest\"",
            ServidorMovil.PaginaMovil,
            StringComparison.Ordinal);
        Assert.Contains(
            "serviceWorker.register('/sw.js'",
            ServidorMovil.PaginaMovil,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Publica_la_descarga_de_la_aplicacion_android()
    {
        Assert.Equal(
            Path.Combine(
                AppContext.BaseDirectory,
                ServidorMovil.NombreArchivoApkAndroid),
            ServidorMovil.ObtenerRutaApkAndroid());
        Assert.Contains(
            $"href=\"{ServidorMovil.RutaDescargaApkAndroid}\"",
            ServidorMovil.PaginaMovil,
            StringComparison.Ordinal);
        Assert.Contains(
            "Descargar app Android",
            ServidorMovil.PaginaMovil,
            StringComparison.Ordinal);
        Assert.Contains(
            "url.pathname === '/app-android.apk'",
            ServidorMovil.ServiceWorkerPwa,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Descubrimiento_publica_protocolo_y_direcciones_locales()
    {
        byte[] contenido =
            ServidorDescubrimiento.CrearRespuesta(5187);

        using JsonDocument respuesta =
            JsonDocument.Parse(contenido);

        Assert.Equal(
            ServidorDescubrimiento.Protocolo,
            respuesta.RootElement
                .GetProperty("protocolo")
                .GetString());
        Assert.False(
            string.IsNullOrWhiteSpace(
                respuesta.RootElement
                    .GetProperty("nombre")
                    .GetString()));
        Assert.Contains(
            respuesta.RootElement
                .GetProperty("direcciones")
                .EnumerateArray(),
            direccion =>
                direccion.GetString() == "http://127.0.0.1:5187");
    }

    [Theory]
    [InlineData("192.168.1.15", "255.255.255.0", "192.168.1.255")]
    [InlineData("10.20.35.7", "255.255.0.0", "10.20.255.255")]
    public void Calcula_el_broadcast_para_wake_on_lan(
        string direccion,
        string mascara,
        string esperado)
    {
        IPAddress? broadcast =
            InformacionWakeOnLan.CalcularBroadcast(
                IPAddress.Parse(direccion),
                IPAddress.Parse(mascara));

        Assert.Equal(esperado, broadcast?.ToString());
    }
}
