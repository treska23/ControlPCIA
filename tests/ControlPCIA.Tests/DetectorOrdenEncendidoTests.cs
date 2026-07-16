using ControlPCIA.Mobile.Servicios;
using Xunit;

namespace ControlPCIA.Tests;

public sealed class DetectorOrdenEncendidoTests
{
    [Theory]
    [InlineData("enciende el ordenador")]
    [InlineData("Encender el PC")]
    [InlineData("arranca la computadora")]
    [InlineData("despierta el equipo")]
    [InlineData("prende el computador")]
    public void Reconoce_ordenes_de_encendido(string texto)
    {
        Assert.True(
            DetectorOrdenEncendido.EsOrdenEncender(texto));
    }

    [Theory]
    [InlineData("no enciendas el ordenador")]
    [InlineData("apaga el ordenador")]
    [InlineData("enciende la luz")]
    [InlineData("abre el navegador")]
    [InlineData("")]
    public void Rechaza_frases_que_no_son_de_encendido(string texto)
    {
        Assert.False(
            DetectorOrdenEncendido.EsOrdenEncender(texto));
    }
}
