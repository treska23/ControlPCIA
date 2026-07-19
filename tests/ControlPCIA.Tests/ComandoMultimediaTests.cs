using Xunit;

namespace ControlPCIA.Tests;

public sealed class ComandoMultimediaTests
{
    [Theory]
    [InlineData("list")]
    [InlineData("status")]
    [InlineData("status", "--app", "spotify")]
    [InlineData("play")]
    [InlineData("pause", "--app", "browser")]
    [InlineData("toggle")]
    [InlineData("stop")]
    [InlineData("next")]
    [InlineData("previous")]
    [InlineData("forward")]
    [InlineData("rewind")]
    [InlineData("seek", "30")]
    [InlineData("seek", "-15.5", "--app", "spotify")]
    [InlineData("shuffle", "on")]
    [InlineData("shuffle", "off")]
    [InlineData("repeat", "track")]
    [InlineData("repeat", "list")]
    [InlineData("repeat", "off")]
    [InlineData("rate", "1.5")]
    public void Analiza_comandos_multimedia_validos(
        params string[] argumentos)
    {
        bool correcto =
            ComandoMultimedia.TryAnalizar(
                argumentos,
                out OpcionesComandoMultimedia? opciones,
                out string error);

        Assert.True(correcto, error);
        Assert.NotNull(opciones);
    }

    [Theory]
    [InlineData()]
    [InlineData("fullscreen")]
    [InlineData("seek")]
    [InlineData("seek", "mañana")]
    [InlineData("shuffle", "quizas")]
    [InlineData("repeat", "forever")]
    [InlineData("rate", "0")]
    [InlineData("pause", "--app")]
    public void Rechaza_comandos_multimedia_invalidos(
        params string[] argumentos)
    {
        bool correcto =
            ComandoMultimedia.TryAnalizar(
                argumentos,
                out _,
                out string error);

        Assert.False(correcto);
        Assert.NotEmpty(error);
    }
}
