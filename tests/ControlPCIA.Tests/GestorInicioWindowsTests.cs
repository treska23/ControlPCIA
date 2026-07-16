using Xunit;

namespace ControlPCIA.Tests;

public sealed class GestorInicioWindowsTests
{
    [Fact]
    public void Crea_comando_oculto_para_un_ejecutable_publicado()
    {
        string comando = GestorInicioWindows.CrearComandoInicio(
            @"C:\Program Files\ControlPCIA\ControlPCIA.exe",
            @"C:\Program Files\ControlPCIA\ControlPCIA.dll");

        Assert.Equal(
            "\"C:\\Program Files\\ControlPCIA\\ControlPCIA.exe\" --servidor --oculto",
            comando);
    }

    [Fact]
    public void Crea_comando_oculto_para_dotnet_en_desarrollo()
    {
        string comando = GestorInicioWindows.CrearComandoInicio(
            @"C:\Program Files\dotnet\dotnet.exe",
            @"D:\ControlPCIA\ControlPCIA.dll");

        Assert.Equal(
            "\"C:\\Program Files\\dotnet\\dotnet.exe\" " +
            "\"D:\\ControlPCIA\\ControlPCIA.dll\" --servidor --oculto",
            comando);
    }

    [Fact]
    public void Rechaza_proceso_sin_ruta()
    {
        Assert.Throws<InvalidOperationException>(() =>
            GestorInicioWindows.CrearComandoInicio(null, null));
    }
}
