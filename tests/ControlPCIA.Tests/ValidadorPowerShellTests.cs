using Xunit;

namespace ControlPCIA.Tests;

public sealed class ValidadorPowerShellTests
{
    public static TheoryData<string> ComandosPermitidos => new()
    {
        "Get-Process",
        "Get-Process -Name Spotify | Select-Object Id, ProcessName, MainWindowTitle",
        "Get-StartApps | Where-Object Name -Like '*Spotify*' | Select-Object Name, AppID",
        "Get-CimInstance -ClassName Win32_OperatingSystem | Select-Object Caption, Version",
        "Get-NetIPConfiguration | Format-List InterfaceAlias, IPv4Address",
        "Get-Process | ConvertTo-Json -Depth 2",
        "Start-Process notepad",
        "Start-Process 'ms-settings:display'",
        "Start-Process 'https://www.youtube.com/results?search_query=botella+de+candor'",
        "explorer.exe 'https://www.youtube.com/results?search_query=botella+de+candor'",
        "Get-Process -Name Spotify | Stop-Process",
        "Get-Process -Name notepad | ForEach-Object { $_.CloseMainWindow() }",
        "$shell = New-Object -ComObject WScript.Shell; $shell.SendKeys([char]175)",
        "$shell = New-Object -ComObject WScript.Shell; $shell.SendKeys('{F11}')",
        "$ventana = Get-Process -Name Spotify; $ventana | Select-Object MainWindowTitle",
        "DisplaySwitch.exe /extend"
    };

    public static TheoryData<string> OperacionesRestringidas => new()
    {
        "Remove-Item C:\\prueba.txt",
        "rm C:\\prueba.txt",
        "Set-Content C:\\prueba.txt hola",
        "Get-Process > C:\\procesos.txt",
        "cmd /c del C:\\prueba.txt",
        "python -c \"import os; os.remove('C:/prueba.txt')\"",
        "Start-Process powershell -ArgumentList '-Command Remove-Item C:\\prueba.txt'",
        "[System.IO.File]::Delete('C:\\prueba.txt')",
        "[IO.Directory]::CreateDirectory('C:\\prueba')",
        "& ('Remove-' + 'Item') C:\\prueba.txt",
        ". { Remove-Item C:\\prueba.txt }",
        "$comando = 'Remove-Item'; & $comando C:\\prueba.txt",
        "Get-Process; Remove-Item C:\\prueba.txt",
        "Get-Process | ForEach-Object { Remove-Item C:\\prueba.txt }",
        "Get-CimInstance Win32_Process | Invoke-CimMethod -MethodName Terminate",
        "New-Object System.IO.FileInfo C:\\prueba.txt",
        "Add-Type -TypeDefinition 'public class X {}'",
        "Invoke-WebRequest https://example.com -OutFile C:\\prueba.exe",
        "Set-ExecutionPolicy Bypass",
        "Stop-Computer",
        "Format-Volume -DriveLetter C"
        ,"$ruta = 'C:\\prueba.txt'; [System.IO.File]::Delete($ruta)"
        ,"[Environment]::SetEnvironmentVariable('X','Y','Machine')"
        ,"[Diagnostics.Process]::Start('cmd.exe')"
        ,"New-Object IO.FileInfo C:\\prueba.txt"
        ,"New-Object -ComObject Scripting.FileSystemObject"
        ,"$shell = New-Object -ComObject WScript.Shell; $shell.Run('cmd.exe')"
        ,"$shell = New-Object -ComObject Shell.Application; $shell.ShellExecute('cmd.exe')"
        ,"$destino = 'notepad'; Start-Process $destino"
        ,"Start-Process notepad -ArgumentList 'C:\\secreto.txt'"
        ,"Stop-Process -Name lsass"
        ,"Get-Process | Stop-Process"
        ,"Get-Process -Name * | Stop-Process"
        ,"Get-Content C:\\secreto.txt"
        ,"Get-ChildItem C:\\"
        ,"Invoke-RestMethod https://example.com"
        ,"wsl rm /mnt/c/prueba.txt"
        ,"dotnet script peligro.csx"
        ,"sal x Remove-Item; x C:\\prueba.txt"
        ,"nal x Remove-Item; x C:\\prueba.txt"
        ,"Invoke-History 1"
        ,"notepad.exe C:\\secreto.txt"
        ,"$ruta = 'C:\\secreto.txt'; notepad.exe $ruta"
        ,"$ruta = 'C:' + '\\secreto.txt'; notepad.exe $ruta"
        ,"New-Object -ComObject Shell.Application"
        ,"Start-Job -FilePath C:\\peligro.ps1"
        ,"Out-Printer 'contenido'"
        ,"Send-MailMessage -To usuario@example.com -Body secreto"
        ,"Clear-RecycleBin -Force"
        ,"compact /c"
        ,"Get-History"
        ,"Get-Clipboard"
        ,"$shell = New-Object -ComObject WScript.Shell; $shell.SendKeys('Remove-Item C:\\secreto.txt{ENTER}')"
        ,"$shell = New-Object -ComObject WScript.Shell; $shell.SendKeys('^s')"
        ,"Start-Process 'http://127.0.0.1:8080/admin'"
        ,"Start-Process 'http://192.168.1.1/'"
        ,"Start-Process 'https://example.com/programa.exe'"
        ,"Start-Process 'https://example.com/archivo.zip'"
        ,"Start-Process 'file:///C:/secreto.txt'"
    };

    [Theory]
    [MemberData(nameof(ComandosPermitidos))]
    public void Permite_comandos_normales_fuera_del_ambito_restringido(
        string comando)
    {
        ResultadoValidacionPowerShell resultado =
            ValidadorPowerShell.Validar(comando);

        Assert.True(resultado.Permitido, resultado.Motivo);
    }

    [Theory]
    [MemberData(nameof(OperacionesRestringidas))]
    public void Bloquea_operaciones_y_evasiones_conocidas(
        string comando)
    {
        ResultadoValidacionPowerShell resultado =
            ValidadorPowerShell.Validar(comando);

        Assert.False(resultado.Permitido);
        Assert.NotEmpty(resultado.Motivo);
    }

    [Fact]
    public void Bloquea_comandos_vacios()
    {
        ResultadoValidacionPowerShell resultado =
            ValidadorPowerShell.Validar("   ");

        Assert.False(resultado.Permitido);
    }

    [Fact]
    public void Bloquea_sintaxis_invalida()
    {
        ResultadoValidacionPowerShell resultado =
            ValidadorPowerShell.Validar("Get-Process | {");

        Assert.False(resultado.Permitido);
        Assert.Contains("sintaxis", resultado.Motivo, StringComparison.OrdinalIgnoreCase);
    }
}
