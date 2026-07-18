using System.IO;
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
        "Get-Process | Where-Object MainWindowTitle | ForEach-Object { Write-Output ('PROCESS_NAME=' + $_.ProcessName); Write-Output ('WINDOW_TITLE=' + $_.MainWindowTitle) }",
        $"Get-ChildItem -LiteralPath '{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}' -Directory | Select-Object Name",
        $"Get-ChildItem -LiteralPath '{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}' -Filter '*proyecto*' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 20 FullName, Length, LastWriteTime",
        $"Get-ChildItem -LiteralPath '{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}','{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}' -Filter 'README.md' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 20 FullName, Length, LastWriteTime",
        $"Get-ChildItem -LiteralPath '{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}' -Filter 'README.md' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 20 | ForEach-Object {{ Write-Output ('FULL_NAME=' + $_.FullName); Write-Output ('LENGTH=' + $_.Length); Write-Output ('LAST_WRITE_TIME=' + $_.LastWriteTime) }}",
        "Start-Process notepad",
        "Start-Process 'ms-settings:display'",
        "Start-Process 'https://www.youtube.com/results?search_query=botella+de+candor'",
        "explorer.exe 'https://www.youtube.com/results?search_query=botella+de+candor'",
        "explorer.exe 'shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App'",
        "explorer.exe 'shell:AppsFolder\\{6D809377-6AF0-444B-8957-A3773F02200E}\\Steinberg\\Cubase 15\\Cubase15.exe'",
        "Get-Process -Name ControlPCIANoWindowProcess | Stop-Process",
        "Get-Process -Name notepad | ForEach-Object { $_.CloseMainWindow() }",
        "winget search PowerToys --source winget",
        "$ventana = Get-Process -Name Spotify; $ventana | Select-Object MainWindowTitle",
        "DisplaySwitch.exe /extend"
    };

    public static TheoryData<string> OperacionesRestringidas => new()
    {
        "Remove-Item C:\\prueba.txt",
        "New-Item -Path 'C:\\ControlPCIA-Prueba-Nueva' -ItemType Directory",
        "Copy-Item -LiteralPath 'C:\\origen.txt' -Destination 'C:\\destino.txt'",
        "Start-Process 'C:\\Proyectos\\Cancion.cpr'",
        "explorer.exe 'C:\\Proyectos'",
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
        ,"Select-String -Path C:\\secreto.txt -Pattern clave"
        ,"Get-FileHash C:\\secreto.txt"
        ,"Get-ChildItem -LiteralPath 'C:\\' -Filter '*.txt' -File -Recurse -ErrorAction SilentlyContinue"
        ,$"Get-ChildItem -LiteralPath '{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}' -File -Recurse -ErrorAction SilentlyContinue"
        ,"Get-ChildItem -LiteralPath 'Env:'"
        ,"Invoke-RestMethod https://example.com"
        ,"wsl rm /mnt/c/prueba.txt"
        ,"dotnet script peligro.csx"
        ,"sal x Remove-Item; x C:\\prueba.txt"
        ,"nal x Remove-Item; x C:\\prueba.txt"
        ,"Invoke-History 1"
        ,"notepad.exe C:\\secreto.txt"
        ,"$ruta = 'C:\\secreto.txt'; notepad.exe $ruta"
        ,"$ruta = 'C:' + '\\secreto.txt'; notepad.exe $ruta"
        ,"$destino = 'shell:AppsFolder\\Aplicacion_123!App'; explorer.exe $destino"
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
        ,"explorer.exe 'shell:AppsFolder\\..\\WindowsPowerShell\\powershell.exe'"
        ,"explorer.exe 'shell:AppsFolder\\\\servidor\\aplicacion.exe'"
        ,"$shell = New-Object -ComObject WScript.Shell; $shell.SendKeys('Remove-Item{ENTER}')"
        ,"$shell = New-Object -ComObject WScript.Shell; $shell.SendKeys('2+5')"
        ,"$shell = New-Object -ComObject WScript.Shell; $shell.SendKeys('2+5=')"
        ,"$shell = New-Object -ComObject WScript.Shell; $shell.SendKeys([char]175)"
        ,"$shell = New-Object -ComObject WScript.Shell; $shell.SendKeys('{F11}')"
        ,"$shell = New-Object -ComObject WScript.Shell; if ($shell.AppActivate('Calculator')) { $shell.SendKeys('2{+}5=') } else { Write-Host 'No se activó' }"
        ,"$shell = New-Object -ComObject WScript.Shell; if ($shell.AppActivate('Calculator')) { $shell.SendKeys('2{+}5=') } else { Write-Error 'No se encontró la ventana'; exit 1 }"
        ,"$shell = New-Object -ComObject WScript.Shell; $activada = $false; for ($i = 0; $i -lt 10 -and -not $activada; $i++) { $activada = $shell.AppActivate('Calculator'); if (-not $activada) { Start-Sleep -Milliseconds 300 } }; if ($activada) { $shell.SendKeys('2{+}5=') } else { Write-Error 'No se encontró la ventana'; exit 1 }"
        ,"$shell = New-Object -ComObject WScript.Shell; $shell.AppActivate('Calculator'); $shell.SendKeys('2{+}5=')"
        ,"$shell = New-Object -ComObject WScript.Shell; $shell.AppActivate('Calculator'); $shell.SendKeys('2{+}5={ENTER}')"
        ,"ControlPCIA.exe ui windows"
        ,"ControlPCIA.exe ui inspect 'Cubase' 6"
        ,"ControlPCIA.exe ui status 'Cubase'"
        ,"ControlPCIA.exe ui close 'Cubase'"
        ,"ControlPCIA.exe ui invoke 'Cubase' 'Add Track' 'MenuItem'"
        ,"ControlPCIA.exe ui invoke 'Word' 'Save As' 'MenuItem'"
        ,"ControlPCIA.exe ui invoke 'Cubase' 'Install plugin' 'Button'"
        ,"ControlPCIA.exe ui text 'Cubase' 'Search' 'Kontakt 7'"
        ,"ControlPCIA.exe ui text 'Abrir' 'File name' 'C:\\Proyectos\\Cancion.cpr'"
        ,"ControlPCIA.exe ui shortcut 'Word' 'CTRL+S'"
        ,"ControlPCIA.exe ui shortcut 'Explorador de archivos' 'CTRL+C'"
        ,"ControlPCIA.exe ui shortcut 'Explorador de archivos' 'CTRL+V'"
        ,"ControlPCIA.exe ui shortcut 'Cubase' 'CTRL+T'"
        ,"ControlPCIA.exe --servidor"
        ,"ControlPCIA.exe --activar-inicio"
        ,"ControlPCIA.exe ui inspect 'PowerShell'"
        ,"ControlPCIA.exe ui invoke 'Word' 'Don''t Save' 'Button'"
        ,"Stop-Process -Name notepad -Force"
        ,"winget uninstall --id Microsoft.PowerToys"
        ,"winget install --id Microsoft.PowerToys --exact --source winget --scope user --accept-package-agreements --accept-source-agreements"
        ,"winget install --manifest C:\\paquete.yaml"
        ,"$env:CONTROLPCIA_PERMITIR_DESCARTE = '1'; ControlPCIA.exe ui invoke 'Word' 'Don''t Save' 'Button'"
        ,"$accion = 'inspect'; ControlPCIA.exe ui $accion 'Cubase'"
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

    [Fact]
    public void Bloquea_crear_y_copiar_aunque_el_destino_sea_nuevo()
    {
        Assert.False(
            ValidadorPowerShell.Validar(
                "New-Item -LiteralPath 'C:\\nuevo.txt' -ItemType File")
                .Permitido);
        Assert.False(
            ValidadorPowerShell.Validar(
                "Copy-Item -LiteralPath 'C:\\origen.txt' -Destination 'C:\\nuevo.txt'")
                .Permitido);
    }

    [Fact]
    public void La_autorizacion_no_reactiva_la_interfaz_deshabilitada()
    {
        const string comando =
            "ControlPCIA.exe ui invoke 'Word' 'Don''t Save' 'Button'";

        Assert.False(
            ValidadorPowerShell.Validar(comando).Permitido);
        Assert.False(
            ValidadorPowerShell.Validar(
                comando,
                permitirDescarte: true).Permitido);
    }
}
