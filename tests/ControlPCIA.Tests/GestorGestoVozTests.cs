using ControlPCIA.Mobile.Servicios;
using Xunit;

namespace ControlPCIA.Tests;

public sealed class GestorGestoVozTests
{
    [Fact]
    public void Mantener_y_soltar_inicia_y_envia()
    {
        var gestor = new GestorGestoVoz(84);

        Assert.Equal(
            AccionGestoVoz.IniciarEscucha,
            gestor.Pulsar());
        Assert.Equal(
            AccionGestoVoz.Enviar,
            gestor.Soltar());
        Assert.Equal(EstadoGestoVoz.Reposo, gestor.Estado);
    }

    [Fact]
    public void Arrastrar_hasta_el_umbral_ancla_y_soltar_no_envia()
    {
        var gestor = new GestorGestoVoz(84);

        gestor.Pulsar();

        Assert.Equal(
            AccionGestoVoz.Ninguna,
            gestor.Mover(83.9));
        Assert.Equal(
            AccionGestoVoz.Anclar,
            gestor.Mover(84));
        Assert.Equal(
            AccionGestoVoz.Ninguna,
            gestor.Soltar());
        Assert.Equal(EstadoGestoVoz.Anclado, gestor.Estado);
    }

    [Fact]
    public void Volver_a_pulsar_un_microfono_anclado_envia()
    {
        var gestor = new GestorGestoVoz(84);

        gestor.Pulsar();
        gestor.Mover(100);
        gestor.Soltar();

        Assert.Equal(
            AccionGestoVoz.Enviar,
            gestor.Pulsar());
        Assert.Equal(EstadoGestoVoz.Reposo, gestor.Estado);
    }

    [Fact]
    public void Cancelar_una_pulsacion_no_envia()
    {
        var gestor = new GestorGestoVoz(84);

        gestor.Pulsar();

        Assert.Equal(
            AccionGestoVoz.Cancelar,
            gestor.Cancelar());
        Assert.Equal(EstadoGestoVoz.Reposo, gestor.Estado);
        Assert.Equal(
            AccionGestoVoz.Ninguna,
            gestor.Soltar());
    }

    [Theory]
    [InlineData(-20, 0)]
    [InlineData(0, 0)]
    [InlineData(42, 0.5)]
    [InlineData(84, 1)]
    [InlineData(200, 1)]
    public void El_progreso_del_carril_esta_acotado(
        double distancia,
        double esperado)
    {
        var gestor = new GestorGestoVoz(84);

        Assert.Equal(
            esperado,
            gestor.CalcularProgreso(distancia),
            precision: 3);
    }
}
