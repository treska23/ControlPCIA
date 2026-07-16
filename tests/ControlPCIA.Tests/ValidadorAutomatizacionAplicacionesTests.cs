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
            datos.Add(["ui", "focus", "Cubase"]);
            datos.Add(["ui", "invoke", "Cubase", "Add Track", "MenuItem"]);
            datos.Add(["ui", "select", "Cubase", "Kontakt", "ListItem"]);
            datos.Add(["ui", "toggle", "Cubase", "Monitor", "CheckBox"]);
            datos.Add(["ui", "expand", "Cubase", "Inserts", "TreeItem"]);
            datos.Add(["ui", "text", "Cubase", "Search", "Kontakt 7"]);
            datos.Add(["ui", "shortcut", "Cubase", "CTRL+T"]);
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
            datos.Add(["ui", "invoke", "Cubase", "Save As", "MenuItem"]);
            datos.Add(["ui", "invoke", "Cubase", "Install plugin", "Button"]);
            datos.Add(["ui", "text", "Cubase", "Search", "C:\\secreto.txt"]);
            datos.Add(["ui", "shortcut", "Cubase", "CTRL+S"]);
            datos.Add(["ui", "shortcut", "Cubase", "ALT+F4"]);
            datos.Add(["ui", "shortcut", "Cubase", "WIN+R"]);
            datos.Add(["ui", "shortcut", "Cubase", "A"]);
            datos.Add(["ui", "shortcut", "Cubase", "CTRL+V"]);
            datos.Add(["ui", "shortcut", "Cubase", "DELETE"]);
            datos.Add(["ui", "invoke", "Cubase", "Eliminar pista", "Button"]);
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
    [InlineData("ALT+P")]
    [InlineData("SHIFT+F3")]
    [InlineData("ENTER")]
    public void Reconoce_atajos_seguros(string atajo)
    {
        Assert.True(
            ValidadorAutomatizacionAplicaciones.EsAtajoSeguro(atajo));
    }

    [Theory]
    [InlineData("CTRL+S")]
    [InlineData("CTRL+SHIFT+S")]
    [InlineData("ALT+F4")]
    [InlineData("WIN+R")]
    [InlineData("CTRL+V")]
    [InlineData("DELETE")]
    [InlineData("texto libre")]
    public void Rechaza_atajos_sensibles_o_texto_libre(string atajo)
    {
        Assert.False(
            ValidadorAutomatizacionAplicaciones.EsAtajoSeguro(atajo));
    }
}
