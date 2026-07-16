using Xunit;

namespace ControlPCIA.Tests;

public sealed class EjecutorPowerShellTests
{
    [Fact]
    public async Task Ejecuta_una_consulta_permitida()
    {
        ResultadoEjecucionPowerShell resultado =
            await EjecutorPowerShell.EjecutarAsync(
                "Write-Output controlpcia-ok",
                TestContext.Current.CancellationToken);

        Assert.True(resultado.Ejecutado, resultado.Error);
        Assert.Equal(0, resultado.CodigoSalida);
        Assert.Equal("controlpcia-ok", resultado.Salida);
    }

    [Fact]
    public async Task No_inicia_PowerShell_para_un_comando_restringido()
    {
        ResultadoEjecucionPowerShell resultado =
            await EjecutorPowerShell.EjecutarAsync(
                "Set-Content prueba.txt bloqueado",
                TestContext.Current.CancellationToken);

        Assert.False(resultado.Ejecutado);
        Assert.Equal(-1, resultado.CodigoSalida);
        Assert.StartsWith("BLOQUEADO:", resultado.Error);
    }

    [Fact]
    public void La_automatizacion_local_devuelve_un_error_controlado()
    {
        string tituloInexistente =
            "controlpcia-ventana-inexistente-" + Guid.NewGuid().ToString("N");
        ResultadoAutomatizacionAplicacion resultado =
            AutomatizadorAplicaciones.Ejecutar(
                ["ui", "inspect", tituloInexistente]);

        Assert.Equal(4, resultado.CodigoSalida);
        Assert.Contains(
            "No se encontró una ventana visible",
            resultado.Error,
            StringComparison.Ordinal);
    }
}
