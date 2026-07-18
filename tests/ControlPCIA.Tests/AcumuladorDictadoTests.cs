using ControlPCIA.Mobile.Servicios;
using Xunit;

namespace ControlPCIA.Tests;

public sealed class AcumuladorDictadoTests
{
    [Fact]
    public void Une_segmentos_separados_hasta_la_detencion()
    {
        var acumulador = new AcumuladorDictado();

        acumulador.ActualizarParcial("abre YouTube");
        acumulador.ConfirmarSegmento("abre YouTube");
        acumulador.ActualizarParcial("y busca música");
        acumulador.ConfirmarSegmento("y busca música");

        Assert.Equal(
            "abre YouTube y busca música",
            acumulador.ObtenerTexto());
    }

    [Fact]
    public void Conserva_el_ultimo_parcial_si_android_termina_sin_resultado()
    {
        var acumulador = new AcumuladorDictado();

        acumulador.ActualizarParcial("enciende el ordenador");
        acumulador.ConfirmarParcial();

        Assert.Equal(
            "enciende el ordenador",
            acumulador.ObtenerTexto());
    }

    [Fact]
    public void No_duplica_un_segmento_repetido()
    {
        var acumulador = new AcumuladorDictado();

        acumulador.ConfirmarSegmento("abre la calculadora");
        acumulador.ConfirmarSegmento("abre la calculadora");

        Assert.Equal(
            "abre la calculadora",
            acumulador.ObtenerTexto());
    }
}
