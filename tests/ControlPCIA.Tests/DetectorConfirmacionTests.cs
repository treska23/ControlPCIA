using ControlPCIA.Mobile.Servicios;
using Xunit;

namespace ControlPCIA.Tests;

public sealed class DetectorConfirmacionTests
{
    [Theory]
    [InlineData("Sí")]
    [InlineData("confirmo")]
    [InlineData("adelante")]
    [InlineData("hazlo")]
    public void Reconoce_respuestas_afirmativas(string texto)
    {
        Assert.True(DetectorConfirmacion.EsAfirmativa(texto));
        Assert.False(DetectorConfirmacion.EsNegativa(texto));
    }

    [Theory]
    [InlineData("no")]
    [InlineData("cancélalo")]
    [InlineData("déjalo")]
    public void Reconoce_respuestas_negativas(string texto)
    {
        Assert.True(DetectorConfirmacion.EsNegativa(texto));
        Assert.False(DetectorConfirmacion.EsAfirmativa(texto));
    }

    [Fact]
    public void Una_orden_nueva_no_se_confunde_con_una_confirmacion()
    {
        Assert.False(DetectorConfirmacion.EsAfirmativa("abre la calculadora"));
        Assert.False(DetectorConfirmacion.EsNegativa("abre la calculadora"));
    }
}
