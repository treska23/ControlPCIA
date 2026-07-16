using Xunit;

namespace ControlPCIA.Tests;

public sealed class ControlWindowsTests
{
    [Fact]
    public void Iniciar_una_aplicacion_sin_enfocarla_deja_la_orden_pendiente()
    {
        ResultadoPasoControl[] pasos =
        [
            Exito("Start-Process calc.exe")
        ];

        Assert.True(ControlWindows.RequiereEnfoqueTrasInicio(pasos));
    }

    [Fact]
    public void Enfocar_despues_de_iniciar_completa_la_apertura()
    {
        ResultadoPasoControl[] pasos =
        [
            Exito("Start-Process calc.exe"),
            Exito("ControlPCIA.exe ui focus \"Calculadora\"")
        ];

        Assert.False(ControlWindows.RequiereEnfoqueTrasInicio(pasos));
    }

    [Fact]
    public void Una_accion_anterior_no_compensa_un_inicio_posterior()
    {
        ResultadoPasoControl[] pasos =
        [
            Exito("ControlPCIA.exe ui focus \"Calculadora\""),
            Exito("Start-Process calc.exe")
        ];

        Assert.True(ControlWindows.RequiereEnfoqueTrasInicio(pasos));
    }

    [Theory]
    [InlineData(
        "CONFIRMAR: ¿Quieres cambiar ahora la escala de todas las pantallas?",
        "¿Quieres cambiar ahora la escala de todas las pantallas?")]
    [InlineData(
        "confirmar:",
        "Esta acción necesita confirmación. ¿Quieres que continúe?")]
    public void Reconoce_una_pregunta_de_confirmacion(
        string respuesta,
        string esperada)
    {
        Assert.True(
            ControlWindows.TryObtenerPreguntaConfirmacion(
                respuesta,
                out string pregunta));
        Assert.Equal(esperada, pregunta);
    }

    [Theory]
    [InlineData("ControlPCIA.exe ui windows")]
    [InlineData("ControlPCIA.exe ui focus \"Calculator\"")]
    public void Solo_permite_consultar_o_enfocar_durante_una_apertura(
        string comando)
    {
        Assert.True(ControlWindows.EsComandoPermitidoMientrasEnfoca(comando));
    }

    [Theory]
    [InlineData("ControlPCIA.exe ui shortcut \"Calculator\" \"ALT+TAB\"")]
    [InlineData("Get-Process CalculatorApp")]
    [InlineData("Start-Process calc.exe")]
    public void Bloquea_otras_acciones_mientras_una_apertura_espera_enfoque(
        string comando)
    {
        Assert.False(ControlWindows.EsComandoPermitidoMientrasEnfoca(comando));
    }

    private static ResultadoPasoControl Exito(string comando)
    {
        return new ResultadoPasoControl(1, comando, true, 0, string.Empty, string.Empty);
    }
}
