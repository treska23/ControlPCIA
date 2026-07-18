using Xunit;

namespace ControlPCIA.Tests;

public sealed class ValidadorAutomatizacionAplicacionesTests
{
    public static TheoryData<string[]> OrdenesPermitidas
    {
        get
        {
            var datos = new TheoryData<string[]>();
            datos.Add(["ui", "windows"]);
            datos.Add(["ui", "inspect", "Cubase", "4"]);
            datos.Add(["ui", "status", "Cubase"]);
            datos.Add(["ui", "focus", "Cubase"]);
            datos.Add(["ui", "close", "Cubase"]);
            datos.Add(["ui", "invoke", "Cubase", "Add Track", "MenuItem"]);
            datos.Add(["ui", "invoke", "Cubase", "Eliminar pista", "Button"]);
            datos.Add(["ui", "invoke", "Word", "Save As", "MenuItem"]);
            datos.Add(["ui", "invoke", "Cubase", "Install plugin", "Button"]);
            datos.Add(["ui", "select", "Cubase", "Kontakt", "ListItem"]);
            datos.Add(["ui", "toggle", "Cubase", "Monitor", "CheckBox"]);
            datos.Add(["ui", "expand", "Cubase", "Inserts", "TreeItem"]);
            datos.Add(["ui", "text", "Cubase", "Search", "Kontakt 7"]);
            datos.Add(["ui", "shortcut", "Cubase", "CTRL+T"]);
            datos.Add(["ui", "shortcut", "Word", "CTRL+S"]);
            datos.Add(["ui", "shortcut", "Word", "CTRL+N"]);
            datos.Add(["ui", "shortcut", "Word", "CTRL+V"]);
            datos.Add(["ui", "shortcut", "Word", "CTRL+X"]);
            datos.Add(["ui", "shortcut", "Cubase", "DELETE"]);
            datos.Add(["ui", "shortcut", "Cubase", "DOWN"]);
            return datos;
        }
    }

    public static TheoryData<string[]> OrdenesBloqueadas
    {
        get
        {
            var datos = new TheoryData<string[]>();
            datos.Add(["--servidor"]);
            datos.Add(["ui", "inspect", "PowerShell"]);
            datos.Add(["ui", "inspect", "Cubase", "20"]);
            datos.Add(["ui", "invoke", "Word", "Don't Save", "Button"]);
            datos.Add(["ui", "invoke", "Aplicación", "Uninstall", "Button"]);
            datos.Add(["ui", "invoke", "Configuración", "Format disk", "Button"]);
            datos.Add(["ui", "shortcut", "Cubase", "ALT+F4"]);
            datos.Add(["ui", "shortcut", "Cubase", "WIN+R"]);
            datos.Add(["ui", "shortcut", "Cubase", "A"]);
            datos.Add(["ui", "shortcut", "Cubase", "SHIFT+DELETE"]);
            return datos;
        }
    }

    [Theory]
    [MemberData(nameof(OrdenesPermitidas))]
    public void Permite_primitivas_genericas_seguras(string[] argumentos)
    {
        ResultadoValidacionPowerShell resultado =
            ValidadorAutomatizacionAplicaciones.Validar(argumentos);

        Assert.True(resultado.Permitido, resultado.Motivo);
    }

    [Theory]
    [MemberData(nameof(OrdenesBloqueadas))]
    public void Bloquea_superficies_y_acciones_sensibles(string[] argumentos)
    {
        ResultadoValidacionPowerShell resultado =
            ValidadorAutomatizacionAplicaciones.Validar(argumentos);

        Assert.False(resultado.Permitido);
    }

    [Theory]
    [InlineData("CTRL+T")]
    [InlineData("CTRL+S")]
    [InlineData("CTRL+V")]
    [InlineData("CTRL+X")]
    [InlineData("DELETE")]
    [InlineData("ALT+P")]
    [InlineData("SHIFT+F3")]
    [InlineData("ENTER")]
    public void Reconoce_atajos_seguros(string atajo)
    {
        Assert.True(
            ValidadorAutomatizacionAplicaciones.EsAtajoSeguro(atajo));
    }

    [Theory]
    [InlineData("ALT+F4")]
    [InlineData("WIN+R")]
    [InlineData("SHIFT+DELETE")]
    [InlineData("texto libre")]
    public void Rechaza_atajos_sensibles_o_texto_libre(string atajo)
    {
        Assert.False(
            ValidadorAutomatizacionAplicaciones.EsAtajoSeguro(atajo));
    }

    [Theory]
    [InlineData("CTRL+X")]
    [InlineData("DELETE")]
    public void Bloquea_cortar_o_eliminar_en_una_superficie_de_archivos(
        string atajo)
    {
        Assert.False(
            ValidadorAutomatizacionAplicaciones
                .EsAtajoPermitidoEnVentana(
                    atajo,
                    superficieArchivos: true));
    }

    [Theory]
    [InlineData("CTRL+C")]
    [InlineData("CTRL+V")]
    [InlineData("CTRL+N")]
    public void Permite_crear_y_copiar_en_una_superficie_de_archivos(
        string atajo)
    {
        Assert.True(
            ValidadorAutomatizacionAplicaciones
                .EsAtajoPermitidoEnVentana(
                    atajo,
                    superficieArchivos: true));
    }

    [Fact]
    public void Descartar_requiere_autorizacion_explicita()
    {
        string[] argumentos =
            ["ui", "invoke", "Word", "Don't Save", "Button"];

        Assert.False(
            ValidadorAutomatizacionAplicaciones.Validar(
                argumentos).Permitido);
        Assert.True(
            ValidadorAutomatizacionAplicaciones.Validar(
                argumentos,
                permitirDescarte: true).Permitido);
    }
}
