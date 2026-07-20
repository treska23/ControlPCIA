using ControlPCIA.Mobile.Servicios;
using Xunit;

namespace ControlPCIA.Tests;

public sealed class GestorTrackpadRemotoTests
{
    [Fact]
    public void Un_toque_con_un_dedo_hace_clic_izquierdo()
    {
        var gestor =
            new GestorTrackpadRemoto();

        gestor.Procesar(
            FaseGestoTrackpad.Pulsado,
            1,
            20,
            30,
            1_000);
        ResultadoGestoTrackpad resultado =
            gestor.Procesar(
                FaseGestoTrackpad.Soltado,
                1,
                20,
                30,
                1_250);

        Assert.Equal(
            AccionGestoTrackpad.ClicIzquierdo,
            resultado.Accion);
    }

    [Fact]
    public void Un_toque_con_dos_dedos_hace_clic_derecho()
    {
        var gestor =
            new GestorTrackpadRemoto();

        gestor.Procesar(
            FaseGestoTrackpad.Pulsado,
            1,
            20,
            30,
            1_000);
        gestor.Procesar(
            FaseGestoTrackpad.Pulsado,
            2,
            35,
            30,
            1_080);
        ResultadoGestoTrackpad resultado =
            gestor.Procesar(
                FaseGestoTrackpad.Soltado,
                2,
                35,
                30,
                1_400);

        Assert.Equal(
            AccionGestoTrackpad.ClicDerecho,
            resultado.Accion);
    }

    [Fact]
    public void Un_dedo_desplaza_el_puntero()
    {
        var gestor =
            new GestorTrackpadRemoto();

        gestor.Procesar(
            FaseGestoTrackpad.Pulsado,
            1,
            10,
            10,
            1_000);
        ResultadoGestoTrackpad resultado =
            gestor.Procesar(
                FaseGestoTrackpad.Movido,
                1,
                30,
                0,
                1_100);

        Assert.True(resultado.DeltaX > 40);
        Assert.True(resultado.DeltaY < -20);
        Assert.Equal(0, resultado.Rueda);
        Assert.Equal(
            AccionGestoTrackpad.Ninguna,
            resultado.Accion);
    }

    [Fact]
    public void Un_movimiento_rapido_recorre_mas_que_un_movimiento_lento()
    {
        var gestorLento =
            new GestorTrackpadRemoto();
        var gestorRapido =
            new GestorTrackpadRemoto();

        gestorLento.Procesar(
            FaseGestoTrackpad.Pulsado,
            1,
            0,
            0,
            1_000);
        ResultadoGestoTrackpad lento =
            gestorLento.Procesar(
                FaseGestoTrackpad.Movido,
                1,
                20,
                0,
                1_200);

        gestorRapido.Procesar(
            FaseGestoTrackpad.Pulsado,
            1,
            0,
            0,
            1_000);
        ResultadoGestoTrackpad rapido =
            gestorRapido.Procesar(
                FaseGestoTrackpad.Movido,
                1,
                20,
                0,
                1_020);

        Assert.True(
            rapido.DeltaX
            > lento.DeltaX * 3);
    }

    [Fact]
    public void Arrastrar_dos_dedos_activa_la_rueda_inmediatamente()
    {
        var gestor =
            new GestorTrackpadRemoto();

        gestor.Procesar(
            FaseGestoTrackpad.Pulsado,
            2,
            50,
            100,
            1_000);
        ResultadoGestoTrackpad resultado =
            gestor.Procesar(
                FaseGestoTrackpad.Movido,
                2,
                50,
                70,
                1_100);

        Assert.Equal(120, resultado.Rueda);
        Assert.Equal(0, resultado.DeltaX);
        Assert.Equal(0, resultado.DeltaY);
    }

    [Fact]
    public void Un_temblo_minimo_de_dos_dedos_sigue_siendo_un_tap()
    {
        var gestor =
            new GestorTrackpadRemoto();

        gestor.Procesar(
            FaseGestoTrackpad.Pulsado,
            2,
            50,
            100,
            1_000);
        ResultadoGestoTrackpad movimiento =
            gestor.Procesar(
                FaseGestoTrackpad.Movido,
                2,
                50,
                99,
                1_300);
        ResultadoGestoTrackpad soltado =
            gestor.Procesar(
                FaseGestoTrackpad.Soltado,
                2,
                50,
                99,
                1_400);

        Assert.Equal(0, movimiento.Rueda);
        Assert.Equal(
            AccionGestoTrackpad.ClicDerecho,
            soltado.Accion);
    }

    [Fact]
    public void Cancelar_un_gesto_impide_el_clic()
    {
        var gestor =
            new GestorTrackpadRemoto();

        gestor.Procesar(
            FaseGestoTrackpad.Pulsado,
            1,
            0,
            0,
            1_000);
        gestor.Procesar(
            FaseGestoTrackpad.Cancelado,
            1,
            0,
            0,
            1_100);
        ResultadoGestoTrackpad resultado =
            gestor.Procesar(
                FaseGestoTrackpad.Soltado,
                1,
                0,
                0,
                1_200);

        Assert.Equal(
            AccionGestoTrackpad.Ninguna,
            resultado.Accion);
    }
}
