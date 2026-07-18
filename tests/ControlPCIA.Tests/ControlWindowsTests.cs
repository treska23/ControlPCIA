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

    [Fact]
    public void Una_accion_de_interfaz_exige_observar_despues()
    {
        ResultadoPasoControl[] pasos =
        [
            Exito("ControlPCIA.exe ui inspect \"Calculator\" 6"),
            Exito("ControlPCIA.exe ui invoke \"Calculator\" \"id:num2Button\" \"Button\"")
        ];

        Assert.True(
            ControlWindows.RequiereVerificacionTrasCambio(pasos));
    }

    [Fact]
    public void Una_inspeccion_posterior_verifica_el_cambio()
    {
        ResultadoPasoControl[] pasos =
        [
            Exito("ControlPCIA.exe ui invoke \"Calculator\" \"id:equalButton\" \"Button\""),
            Exito("ControlPCIA.exe ui inspect \"Calculator\" 6")
        ];

        Assert.False(
            ControlWindows.RequiereVerificacionTrasCambio(pasos));
    }

    [Fact]
    public void Un_cierre_exige_enumerar_despues()
    {
        ResultadoPasoControl[] pasos =
        [
            Exito("ControlPCIA.exe ui status \"Visual Studio\""),
            Exito("ControlPCIA.exe ui close \"Visual Studio\"")
        ];

        Assert.True(
            ControlWindows.RequiereVerificacionTrasCambio(pasos));

        pasos =
        [
            .. pasos,
            Exito("ControlPCIA.exe ui windows")
        ];

        Assert.False(
            ControlWindows.RequiereVerificacionTrasCambio(pasos));
    }

    [Fact]
    public void Reconoce_una_respuesta_natural()
    {
        Assert.True(
            ControlWindows.TryObtenerRespuestaNatural(
                "RESPONDER: Tienes abiertas Calculadora y Word.",
                out string respuesta));
        Assert.Equal(
            "Tienes abiertas Calculadora y Word.",
            respuesta);
    }

    [Fact]
    public void Solo_autoriza_descartar_con_frase_directa_o_confirmacion_contextual()
    {
        Assert.True(
            ControlWindows.EsDescarteConfirmado(
                "cierra Visual Studio sin guardar",
                []));
        Assert.False(
            ControlWindows.EsDescarteConfirmado(
                "no",
                [
                    new MensajeConversacionControl(
                        "assistant",
                        "¿Quieres que guarde el trabajo?")
                ]));
        Assert.True(
            ControlWindows.EsDescarteConfirmado(
                "sí",
                [
                    new MensajeConversacionControl(
                        "assistant",
                        "¿Quieres cerrar sin guardar?")
                ]));
    }

    private static ResultadoPasoControl Exito(string comando)
    {
        return new ResultadoPasoControl(1, comando, true, 0, string.Empty, string.Empty);
    }
}
