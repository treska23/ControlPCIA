using Xunit;

namespace ControlPCIA.Tests;

public sealed class EntradaRemotaTests
{
    [Fact]
    public void Crea_movimiento_relativo_del_raton()
    {
        bool correcto =
            EntradaRemota.TryCrearEventosRaton(
                new SolicitudRatonRemoto(
                    "move",
                    32,
                    -18),
                out IReadOnlyList<EventoEntradaRemota> eventos,
                out string error);

        Assert.True(correcto, error);
        EventoEntradaRemota evento =
            Assert.Single(eventos);
        Assert.Equal(
            TipoEventoEntradaRemota.Movimiento,
            evento.Tipo);
        Assert.Equal(32, evento.X);
        Assert.Equal(-18, evento.Y);
    }

    [Theory]
    [InlineData("left-click", 1)]
    [InlineData("right-click", 2)]
    [InlineData("middle-click", 3)]
    public void Un_clic_envia_pulsacion_y_liberacion(
        string accion,
        int boton)
    {
        Assert.True(
            EntradaRemota.TryCrearEventosRaton(
                new SolicitudRatonRemoto(accion),
                out IReadOnlyList<EventoEntradaRemota> eventos,
                out string error),
            error);
        Assert.Equal(2, eventos.Count);
        Assert.Equal(
            TipoEventoEntradaRemota.BotonRatonAbajo,
            eventos[0].Tipo);
        Assert.Equal(
            TipoEventoEntradaRemota.BotonRatonArriba,
            eventos[1].Tipo);
        Assert.All(
            eventos,
            evento => Assert.Equal(
                boton,
                evento.Codigo));
    }

    [Fact]
    public void Un_atajo_pulsa_y_libera_modificadores_en_orden()
    {
        bool correcto =
            EntradaRemota.TryCrearEventosTeclado(
                new SolicitudTecladoRemoto(
                    null,
                    "s",
                    ["ctrl", "shift"]),
                out IReadOnlyList<EventoEntradaRemota> eventos,
                out string error);

        Assert.True(correcto, error);
        Assert.Equal(6, eventos.Count);
        Assert.Equal(
            [
                TipoEventoEntradaRemota.TeclaAbajo,
                TipoEventoEntradaRemota.TeclaAbajo,
                TipoEventoEntradaRemota.TeclaAbajo,
                TipoEventoEntradaRemota.TeclaArriba,
                TipoEventoEntradaRemota.TeclaArriba,
                TipoEventoEntradaRemota.TeclaArriba
            ],
            eventos.Select(evento => evento.Tipo));
        Assert.Equal(0x11, eventos[0].Codigo);
        Assert.Equal(0x10, eventos[1].Codigo);
        Assert.Equal('S', eventos[2].Codigo);
        Assert.Equal(0x10, eventos[4].Codigo);
        Assert.Equal(0x11, eventos[5].Codigo);
    }

    [Fact]
    public void El_texto_se_convierte_en_unicode_sin_interpretarlo()
    {
        Assert.True(
            EntradaRemota.TryCrearEventosTeclado(
                new SolicitudTecladoRemoto(
                    "Hola ñ",
                    null,
                    null),
                out IReadOnlyList<EventoEntradaRemota> eventos,
                out string error),
            error);
        Assert.Equal(12, eventos.Count);
        Assert.All(
            eventos.Where(
                (_, indice) => indice % 2 == 0),
            evento => Assert.Equal(
                TipoEventoEntradaRemota.UnicodeAbajo,
                evento.Tipo));
        Assert.Equal('ñ', eventos[^2].Codigo);
    }

    [Fact]
    public void Los_saltos_de_linea_se_envian_como_intro()
    {
        Assert.True(
            EntradaRemota.TryCrearEventosTeclado(
                new SolicitudTecladoRemoto(
                    "uno\r\ndos",
                    null,
                    null),
                out IReadOnlyList<EventoEntradaRemota> eventos,
                out string error),
            error);
        Assert.Contains(
            eventos,
            evento =>
                evento.Tipo
                == TipoEventoEntradaRemota.TeclaAbajo
                && evento.Codigo == 0x0D);
        Assert.Equal(14, eventos.Count);
    }

    [Theory]
    [InlineData("move", 6000, 0, 0)]
    [InlineData("wheel", 0, 0, 0)]
    [InlineData("wheel", 0, 0, 3000)]
    [InlineData("inventado", 0, 0, 0)]
    public void Rechaza_eventos_de_raton_invalidos(
        string accion,
        int x,
        int y,
        int rueda)
    {
        Assert.False(
            EntradaRemota.TryCrearEventosRaton(
                new SolicitudRatonRemoto(
                    accion,
                    x,
                    y,
                    rueda),
                out _,
                out string error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void No_envia_un_evento_si_la_peticion_es_invalida()
    {
        int envios = 0;
        ResultadoEntradaRemota resultado =
            EntradaRemota.ProcesarTeclado(
                new SolicitudTecladoRemoto(
                    null,
                    "tecla inventada",
                    null),
                _ =>
                {
                    envios++;
                    return true;
                });

        Assert.False(resultado.Correcto);
        Assert.Equal(0, envios);
    }
}
