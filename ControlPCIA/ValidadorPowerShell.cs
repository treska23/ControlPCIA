using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace ControlPCIA;

internal sealed record ResultadoValidacionPowerShell(
    bool Permitido,
    string Motivo);

/// <summary>
/// Analiza el AST completo de PowerShell y deniega capacidades peligrosas.
/// No decide qué acciones puede realizar la IA: impide borrado o movimiento
/// destructivo de archivos, daños de disco, credenciales y mecanismos de evasión.
/// </summary>
internal static class ValidadorPowerShell
{
    private const int LongitudMaxima = 20_000;

    private static readonly HashSet<string> ComandosBloqueados =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly List<string> PrefijosBloqueados = new();
    private static readonly List<ReglaArgumento> ArgumentosBloqueados = new();
    private static readonly HashSet<string> DestinosBloqueados =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> TextosBloqueados = new();

    private static readonly HashSet<string> ComandosAdicionalesBloqueados =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Lectura de contenido de archivos. La enumeración de nombres y
            // rutas sí está permitida para que la IA pueda localizar proyectos.
            "Get-Content", "gc", "cat", "type",
            "Select-String", "sls", "Get-FileHash",
            "Invoke-Item", "ii", "Import-Csv", "Import-Clixml",
            "Import-PowerShellDataFile", "Import-LocalizedData",
            "Import-Module",

            // Red y descarga: evitan exfiltración y escrituras indirectas.
            "Invoke-WebRequest", "iwr", "curl", "wget",
            "Invoke-RestMethod", "irm", "Start-BitsTransfer", "bitsadmin",
            "Send-MailMessage", "Get-Credential", "Get-History",
            "Invoke-History", "ihy", "r", "Get-Clipboard",

            // Procesos en segundo plano, persistencia y escritura indirecta.
            "Start-Job", "Receive-Job", "Debug-Job", "Wait-Job",
            "Register-ObjectEvent", "Register-EngineEvent", "Register-WmiEvent",
            "Out-Printer", "Clear-RecycleBin", "New-PSDrive", "Remove-PSDrive",
            "New-EventLog", "Write-EventLog", "Clear-EventLog",
            "Remove-EventLog", "Limit-EventLog", "Checkpoint-Computer",
            "Restore-Computer", "Enable-ComputerRestore", "Disable-ComputerRestore",
            "Set-ProcessMitigation", "Set-PSRepository", "Register-PSRepository",
            "Unregister-PSRepository", "Register-PackageSource",
            "Unregister-PackageSource", "Update-Module", "Update-Script",

            // Servicios, tareas y otros cambios sensibles no cubiertos antes.
            "Start-Service", "Stop-Service", "Restart-Service", "Suspend-Service",
            "Start-ScheduledTask", "Stop-ScheduledTask",
            "powercfg", "dism", "sfc", "pnputil", "devcon",
            "vssadmin", "diskshadow", "wbadmin", "wevtutil", "eventcreate",
            "taskkill", "bcdboot",

            // Intérpretes, compiladores y gestores que permitirían escapar.
            "python", "python.exe", "python3", "py", "py.exe",
            "node", "node.exe", "bun", "deno", "ruby", "perl",
            "java", "java.exe", "jshell", "wsl", "wsl.exe",
            "bash", "sh", "git", "hg", "svn", "dotnet", "msbuild",
            "csc", "cl", "gcc", "msiexec", "msiexec.exe",
            "choco", "scoop", "tar", "7z", "certutil",
            "forfiles", "findstr",

            // Utilidades nativas que leen, transforman o modifican archivos.
            "more", "more.com", "find", "find.exe", "fc", "fc.exe",
            "comp", "comp.exe", "compact", "compact.exe", "expand",
            "expand.exe", "makecab", "makecab.exe", "replace", "replace.exe",
            "where", "where.exe", "sort", "sort.exe", "print", "print.exe",
            "esentutl", "esentutl.exe", "lodctr", "unlodctr", "regini",
            "regedt32", "diskraid", "defrag", "label", "subst",

            // Clientes de red capaces de enviar o descargar contenido.
            "ftp", "ftp.exe", "tftp", "tftp.exe", "ssh", "ssh.exe",
            "scp", "scp.exe", "sftp", "sftp.exe", "telnet", "telnet.exe",
            "nc", "ncat"
        };

    private static readonly HashSet<string> ExtensionesWebBloqueadas =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".msi", ".msix", ".appx", ".appxbundle",
            ".zip", ".rar", ".7z", ".tar", ".gz", ".iso", ".img",
            ".ps1", ".bat", ".cmd", ".vbs", ".js", ".reg", ".dll",
            ".scr", ".com", ".jar", ".apk", ".dmg", ".pkg",
            ".deb", ".rpm", ".torrent"
        };

    private static readonly HashSet<string> ExtensionesEjecutablesBloqueadas =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".com", ".scr", ".cpl", ".dll",
            ".ps1", ".psm1", ".psd1", ".bat", ".cmd",
            ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
            ".hta", ".msi", ".msp", ".msix", ".appx", ".appxbundle",
            ".reg", ".lnk", ".url", ".scf"
        };

    private static readonly string[] TiposBloqueados =
    [
        "system.io", "io.", "microsoft.win32.registry",
        "system.environment", "environment",
        "system.diagnostics", "diagnostics.",
        "system.reflection", "reflection.",
        "system.runtime.interopservices", "runtime.interopservices",
        "system.management.automation", "management.automation",
        "system.net", "net.", "microsoft.visualbasic.fileio",
        "windows.storage", "system.activator", "activator",
        "system.security", "system.directoryservices",
        "system.data.sqlclient", "microsoft.data.sqlclient",
        "system.configuration", "system.messaging"
    ];

    private static readonly HashSet<string> MetodosBloqueados =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Delete", "DeleteFile", "DeleteFolder", "Create", "CreateDirectory",
            "CreateTextFile", "Open", "OpenRead", "OpenWrite", "OpenText",
            "Write", "WriteAllText", "WriteAllBytes", "WriteLines",
            "AppendAllText", "AppendAllLines", "Save", "SaveAs",
            "Copy", "CopyTo", "CopyFile", "CopyFolder", "CopyHere",
            "Move", "MoveTo", "MoveFile", "MoveFolder", "MoveHere",
            "Rename", "Replace", "SetAccessControl", "SetAttributes",
            "DownloadFile", "DownloadString", "UploadFile", "UploadString",
            "ExtractToDirectory", "RegWrite", "RegDelete", "CreateShortcut",
            "Run", "Exec", "ShellExecute", "Start", "Kill",
            "Invoke", "InvokeScript", "CreateDelegate", "Compile",
            "Load", "LoadFile", "LoadFrom", "LoadWithPartialName",
            "GetType", "MakeGenericType"
        };

    private static readonly string[] PrefijosMetodosBloqueados =
    [
        "Append", "Commit", "Compile", "Connect", "Copy", "Create",
        "Delete", "Download", "Exec", "Export", "Extract", "FromFile",
        "GetResponse", "Import", "Install", "Invoke", "Kill", "Load",
        "Move", "Open", "ParseName", "Put", "Read", "Reg", "Remove",
        "Rename", "Replace", "Run", "Save", "SetAccess", "SetAttributes",
        "Shell", "Start", "Upload", "Write"
    ];

    private static readonly HashSet<string> ProcesosCriticos =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "registry", "smss", "csrss", "wininit", "services",
            "lsass", "winlogon", "svchost", "fontdrvhost", "dwm"
        };

    private static readonly Dictionary<string, string> AliasEspeciales =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["start"] = "Start-Process",
            ["saps"] = "Start-Process",
            ["kill"] = "Stop-Process",
            ["spps"] = "Stop-Process",
            ["gps"] = "Get-Process",
            ["gci"] = "Get-ChildItem",
            ["dir"] = "Get-ChildItem",
            ["ls"] = "Get-ChildItem",
            ["ni"] = "New-Item",
            ["md"] = "New-Item",
            ["mkdir"] = "New-Item",
            ["cp"] = "Copy-Item",
            ["copy"] = "Copy-Item",
            ["cpi"] = "Copy-Item",
            ["nal"] = "New-Alias",
            ["sal"] = "Set-Alias",
            ["ral"] = "Remove-Alias",
            ["ipal"] = "Import-Alias",
            ["epal"] = "Export-Alias",
            ["ihy"] = "Invoke-History",
            ["r"] = "Invoke-History"
        };

    static ValidadorPowerShell()
    {
        CargarRestricciones();
    }

    public static ResultadoValidacionPowerShell Validar(
        string comando,
        bool permitirDescarte = false)
    {
        if (string.IsNullOrWhiteSpace(comando))
        {
            return Bloquear("El comando está vacío.");
        }

        comando = comando.Trim();

        if (comando.Length > LongitudMaxima)
        {
            return Bloquear("El comando supera la longitud máxima permitida.");
        }

        ScriptBlockAst ast = Parser.ParseInput(
            comando,
            out Token[] tokens,
            out ParseError[] errores);

        if (errores.Length > 0 || tokens.Any(token => token.Kind == TokenKind.Unknown))
        {
            string detalle = errores.FirstOrDefault()?.Message
                ?? "se encontró un elemento no reconocido";
            return Bloquear($"La sintaxis de PowerShell no es válida: {detalle}");
        }

        FileRedirectionAst? redireccion = ast
            .FindAll(nodo => nodo is FileRedirectionAst, searchNestedScriptBlocks: true)
            .OfType<FileRedirectionAst>()
            .FirstOrDefault();

        if (redireccion is not null)
        {
            return Bloquear("No se permite redirigir la salida a un archivo.");
        }

        if (ast.Find(nodo => nodo is UsingStatementAst, true) is not null)
        {
            return Bloquear("No se permite cargar módulos o ensamblados mediante 'using'.");
        }

        foreach (TypeExpressionAst tipo in ast
                     .FindAll(nodo => nodo is TypeExpressionAst, true)
                     .Cast<TypeExpressionAst>())
        {
            if (EsTipoRestringido(tipo.TypeName.FullName))
            {
                return Bloquear($"El tipo .NET '{tipo.TypeName.FullName}' está restringido.");
            }
        }

        foreach (InvokeMemberExpressionAst invocacion in ast
                     .FindAll(nodo => nodo is InvokeMemberExpressionAst, true)
                     .Cast<InvokeMemberExpressionAst>())
        {
            string? miembro = ObtenerNombreMiembro(invocacion.Member);

            if (miembro is null || EsMetodoRestringido(miembro))
            {
                return Bloquear(
                    miembro is null
                        ? "No se permiten nombres de método dinámicos."
                        : $"El método '{miembro}' está restringido.");
            }

            ResultadoValidacionPowerShell? bloqueoInvocacion =
                ValidarInvocacionEspecial(invocacion, miembro);

            if (bloqueoInvocacion is not null)
            {
                return bloqueoInvocacion;
            }
        }

        IReadOnlyList<CommandAst> comandos = ast
            .FindAll(nodo => nodo is CommandAst, searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .ToArray();

        if (comandos.Count == 0)
        {
            return Bloquear("No se encontró ningún comando ejecutable.");
        }

        foreach (CommandAst comandoAst in comandos)
        {
            if (comandoAst.InvocationOperator != TokenKind.Unknown)
            {
                return Bloquear("Los operadores de invocación y dot-sourcing están restringidos.");
            }

            string? nombreOriginal = comandoAst.GetCommandName();

            if (string.IsNullOrWhiteSpace(nombreOriginal))
            {
                return Bloquear("No se permiten nombres de comando dinámicos.");
            }

            string nombre = NormalizarAlias(nombreOriginal);

            ResultadoValidacionPowerShell? bloqueo =
                ValidarNombreComando(nombre);

            if (bloqueo is not null)
            {
                return bloqueo;
            }

            bloqueo = ValidarArgumentosConfigurados(nombre, comandoAst);

            if (bloqueo is not null)
            {
                return bloqueo;
            }

            if (nombre.Equals("Start-Process", StringComparison.OrdinalIgnoreCase))
            {
                bloqueo = ValidarStartProcess(comandoAst);
            }
            else if (nombre.Equals("Stop-Process", StringComparison.OrdinalIgnoreCase))
            {
                bloqueo = ValidarStopProcess(comandoAst, comandos);
            }
            else if (nombre.Equals(
                         "Get-ChildItem",
                         StringComparison.OrdinalIgnoreCase))
            {
                bloqueo = ValidarGetChildItem(comandoAst);
            }
            else if (nombre.Equals("New-Object", StringComparison.OrdinalIgnoreCase))
            {
                bloqueo = ValidarNewObject(comandoAst);
            }
            else if (nombre.Equals("explorer", StringComparison.OrdinalIgnoreCase)
                     || nombre.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            {
                bloqueo = ValidarExplorer(comandoAst);
            }
            else if (nombre.Equals("winget", StringComparison.OrdinalIgnoreCase)
                     || nombre.Equals("winget.exe", StringComparison.OrdinalIgnoreCase))
            {
                bloqueo = ValidarWinget(comandoAst);
            }
            else if (EsComandoControlPcia(nombre))
            {
                IReadOnlyList<string>? argumentos =
                    ObtenerArgumentosLiterales(comandoAst);

                bloqueo = Bloquear(
                    "ControlPCIA.exe no puede invocarse recursivamente desde una orden. Usa sólo comandos, API o protocolos externos invocables íntegramente desde consola.");
            }

            if (bloqueo is null
                &&
                EsComandoNativo(nombre)
                && !EsComandoControlPcia(nombre)
                && !EsWinget(nombre)
                && !EsExplorer(nombre))
            {
                bloqueo = ValidarArgumentosNativos(comandoAst);
            }

            if (bloqueo is not null)
            {
                return bloqueo;
            }
        }

        foreach (string texto in TextosBloqueados)
        {
            if (comando.Contains(texto, StringComparison.OrdinalIgnoreCase))
            {
                return Bloquear($"Se detectó un mecanismo restringido: '{texto}'.");
            }
        }

        if (comando.Contains(
                ".SendKeys(",
                StringComparison.OrdinalIgnoreCase)
            || comando.Contains(
                ".AppActivate(",
                StringComparison.OrdinalIgnoreCase))
        {
            return Bloquear(
                "La simulación de teclado y la activación automatizada de ventanas están restringidas. Usa sólo comandos, protocolos o API de consola propios de Windows o de la aplicación.");
        }

        return new(
            true,
            "El comando no contiene operaciones restringidas por la política.");
    }

    private static ResultadoValidacionPowerShell? ValidarNombreComando(string nombre)
    {
        if (ComandosBloqueados.Contains(nombre)
            || ComandosAdicionalesBloqueados.Contains(nombre))
        {
            return Bloquear($"El comando '{nombre}' está restringido.");
        }

        foreach (string prefijo in PrefijosBloqueados)
        {
            if (nombre.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
            {
                return Bloquear(
                    $"El comando '{nombre}' pertenece a la familia restringida '{prefijo}'.");
            }
        }

        if (nombre.Contains('\\') || nombre.Contains('/'))
        {
            return Bloquear("No se permite ejecutar un programa mediante una ruta de archivo.");
        }

        return null;
    }

    private static ResultadoValidacionPowerShell? ValidarArgumentosConfigurados(
        string nombre,
        CommandAst comando)
    {
        foreach (ReglaArgumento regla in ArgumentosBloqueados)
        {
            if (nombre.Equals(regla.Comando, StringComparison.OrdinalIgnoreCase)
                && TieneParametro(comando, regla.Argumento.TrimStart('-')))
            {
                return Bloquear(
                    $"El argumento '{regla.Argumento}' no está permitido con '{nombre}'.");
            }
        }

        return null;
    }

    private static ResultadoValidacionPowerShell? ValidarStartProcess(CommandAst comando)
    {
        string[] parametrosRestringidos =
        [
            "ArgumentList", "Verb", "WorkingDirectory", "Credential",
            "RedirectStandardInput", "RedirectStandardOutput",
            "RedirectStandardError", "Environment", "UseNewEnvironment"
        ];

        foreach (string parametro in parametrosRestringidos)
        {
            if (TieneParametro(comando, parametro))
            {
                return Bloquear($"Start-Process no permite el parámetro '-{parametro}'.");
            }
        }

        IReadOnlyList<string>? destinos = ObtenerLiterales(
            comando,
            ["FilePath"],
            posicion: 0);

        if (destinos is null || destinos.Count != 1)
        {
            return Bloquear("Start-Process requiere un destino literal y verificable.");
        }

        string destino = destinos[0].Trim();
        string nombre = Path.GetFileName(destino);

        if ((nombre.Equals(
                 "explorer",
                 StringComparison.OrdinalIgnoreCase)
             || nombre.Equals(
                 "explorer.exe",
                 StringComparison.OrdinalIgnoreCase))
            && comando.Extent.Text.Contains(
                "shell:AppsFolder",
                StringComparison.OrdinalIgnoreCase))
        {
            return Bloquear(
                "Para abrir un AppID usa explorer.exe con un destino shell:AppsFolder literal, no Start-Process ni variables.");
        }

        if (EsUrlWebNavegableSegura(destino))
        {
            return null;
        }

        if (destino.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return Bloquear("Start-Process no admite direcciones file:.");
        }

        if (PareceRutaLocal(destino))
        {
            return Bloquear(
                "ControlPCIA no abre archivos, documentos ni proyectos locales; sólo puede consultar sus nombres, rutas y metadatos.");
        }

        if (destino.Contains('\\') || destino.Contains('/'))
        {
            return Bloquear(
                "Start-Process no puede abrir rutas relativas ni ubicaciones de red.");
        }

        if (DestinosBloqueados.Contains(nombre)
            || ComandosAdicionalesBloqueados.Contains(nombre))
        {
            return Bloquear($"No está permitido iniciar '{destino}'.");
        }

        return null;
    }

    private static ResultadoValidacionPowerShell? ValidarStopProcess(
        CommandAst stopProcess,
        IReadOnlyList<CommandAst> comandos)
    {
        if (TieneParametro(stopProcess, "Force")
            || TieneParametro(stopProcess, "Id")
            || TieneParametro(stopProcess, "InputObject"))
        {
            return Bloquear(
                "Stop-Process no admite cierre forzado ni identificadores; usa el cierre nativo verificable de ControlPCIA.");
        }

        IReadOnlyList<string>? nombres = ObtenerLiterales(
            stopProcess,
            ["Name"],
            posicion: 0);

        if (nombres is null || nombres.Count == 0)
        {
            CommandAst? getProcess = comandos
                .LastOrDefault(c =>
                    NormalizarAlias(c.GetCommandName() ?? "")
                        .Equals("Get-Process", StringComparison.OrdinalIgnoreCase));

            if (getProcess is not null)
            {
                nombres = ObtenerLiterales(getProcess, ["Name"], posicion: 0);
            }
        }

        if (nombres is null || nombres.Count == 0)
        {
            return Bloquear(
                "Stop-Process requiere uno o varios nombres literales; no puede actuar sobre todos los procesos.");
        }

        foreach (string nombre in nombres)
        {
            string limpio = Path.GetFileNameWithoutExtension(nombre.Trim());

            if (limpio.Contains('*') || limpio.Contains('?')
                || ProcesosCriticos.Contains(limpio))
            {
                return Bloquear($"No está permitido detener el proceso '{nombre}'.");
            }

            try
            {
                if (Process.GetProcessesByName(limpio)
                    .Any(proceso => proceso.MainWindowHandle != IntPtr.Zero))
                {
                    return Bloquear(
                        $"'{nombre}' tiene una ventana abierta. Usa CloseMainWindow desde PowerShell para solicitar un cierre normal que permita a la aplicación avisar de trabajo sin guardar.");
                }
            }
            catch (InvalidOperationException)
            {
                return Bloquear(
                    $"No se pudo comprobar si '{nombre}' puede cerrarse sin perder trabajo.");
            }
        }

        return null;
    }

    private static ResultadoValidacionPowerShell? ValidarGetChildItem(
        CommandAst comando)
    {
        HashSet<string> parametros = ObtenerNombresParametros(comando);
        string[] permitidos =
        [
            "Path", "LiteralPath", "Filter", "File", "Directory",
            "Recurse", "Depth", "Name", "ErrorAction"
        ];

        if (parametros.Any(parametro =>
                !permitidos.Contains(
                    parametro,
                    StringComparer.OrdinalIgnoreCase)))
        {
            return Bloquear(
                "Get-ChildItem sólo puede buscar nombres y metadatos dentro de las carpetas personales autorizadas.");
        }

        IReadOnlyList<string>? rutas = ObtenerLiterales(
            comando,
            ["Path", "LiteralPath"],
            posicion: 0);

        if (rutas is null
            || rutas.Count is < 1 or > 8
            || rutas.Any(ruta => !EsRutaDeBusquedaPermitida(ruta)))
        {
            return Bloquear(
                "Get-ChildItem requiere entre una y ocho rutas literales dentro de las carpetas personales autorizadas.");
        }

        if (TieneParametro(comando, "Filter"))
        {
            IReadOnlyList<string>? filtros = ObtenerLiterales(
                comando,
                ["Filter"],
                posicion: 0);

            if (filtros is not { Count: 1 }
                || !EsFiltroDeNombreSeguro(filtros[0]))
            {
                return Bloquear(
                    "El filtro de búsqueda debe ser un nombre literal breve, sin rutas.");
            }
        }
        else if (TieneParametro(comando, "Recurse"))
        {
            return Bloquear(
                "Una búsqueda recursiva requiere -Filter para no enumerar indiscriminadamente todos los archivos personales.");
        }

        if (TieneParametro(comando, "Depth"))
        {
            IReadOnlyList<string>? profundidad = ObtenerLiterales(
                comando,
                ["Depth"],
                posicion: 0);

            if (profundidad is not { Count: 1 }
                || !int.TryParse(profundidad[0], out int valor)
                || valor is < 0 or > 20)
            {
                return Bloquear(
                    "La profundidad de búsqueda debe ser un literal entre 0 y 20.");
            }
        }

        if (TieneParametro(comando, "ErrorAction"))
        {
            IReadOnlyList<string>? accionError = ObtenerLiterales(
                comando,
                ["ErrorAction"],
                posicion: 0);

            if (accionError is not { Count: 1 }
                || !accionError[0].Equals(
                    "SilentlyContinue",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Bloquear(
                    "La búsqueda sólo admite -ErrorAction SilentlyContinue.");
            }
        }

        return null;
    }

    private static ResultadoValidacionPowerShell? ValidarWinget(
        CommandAst comando)
    {
        IReadOnlyList<string>? argumentos =
            ObtenerArgumentosLiterales(comando);

        if (argumentos is null || argumentos.Count < 1)
        {
            return Bloquear(
                "winget requiere una acción literal.");
        }

        string accion = argumentos[0].ToLowerInvariant();
        string[] resto = argumentos.Skip(1).ToArray();

        return accion switch
        {
            "install" => Bloquear(
                "ControlPCIA no instala programas porque esa operación modifica el disco."),
            "search" or "show" or "list" =>
                ValidarWingetConsulta(resto),
            _ => Bloquear(
                "winget sólo puede consultar paquetes; instalar, actualizar y desinstalar no está permitido.")
        };
    }

    private static ResultadoValidacionPowerShell? ValidarWingetConsulta(
        IReadOnlyList<string> argumentos)
    {
        string[] indicadoresSinValor =
        [
            "--exact", "-e", "--disable-interactivity",
            "--accept-source-agreements"
        ];
        string[] indicadoresConValor =
        [
            "--id", "--name", "--query", "-q", "--source", "--count"
        ];
        int posicionales = 0;

        for (int indice = 0; indice < argumentos.Count; indice++)
        {
            string argumento = argumentos[indice];

            if (indicadoresSinValor.Contains(
                    argumento,
                    StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (indicadoresConValor.Contains(
                    argumento,
                    StringComparer.OrdinalIgnoreCase))
            {
                if (++indice >= argumentos.Count)
                {
                    return Bloquear(
                        $"Falta el valor literal de '{argumento}'.");
                }

                string valor = argumentos[indice];

                if (argumento.Equals(
                        "--source",
                        StringComparison.OrdinalIgnoreCase)
                    && !valor.Equals(
                        "winget",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Bloquear(
                        "Las consultas de paquetes sólo pueden usar el catálogo winget.");
                }

                if (argumento.Equals(
                        "--count",
                        StringComparison.OrdinalIgnoreCase)
                    && (!int.TryParse(valor, out int cantidad)
                        || cantidad is < 1 or > 50))
                {
                    return Bloquear(
                        "El número de resultados de winget debe estar entre 1 y 50.");
                }

                if (valor.Length is < 1 or > 160
                    || valor.Any(char.IsControl))
                {
                    return Bloquear(
                        "Los valores de consulta de winget deben ser literales breves.");
                }

                continue;
            }

            if (argumento.StartsWith('-')
                || ++posicionales > 1
                || argumento.Length > 160
                || argumento.Any(char.IsControl))
            {
                return Bloquear(
                    $"El argumento '{argumento}' no está permitido en una consulta winget.");
            }
        }

        return null;
    }

    private static ResultadoValidacionPowerShell? ValidarNewObject(CommandAst comando)
    {
        IReadOnlyList<string>? tipo = ObtenerLiterales(
            comando,
            ["TypeName", "ComObject"],
            posicion: 0);

        if (tipo is null || tipo.Count != 1)
        {
            return Bloquear("New-Object requiere un tipo literal y verificable.");
        }

        string nombre = tipo[0];

        if (TieneParametro(comando, "ComObject"))
        {
            return Bloquear(
                $"La automatización COM mediante '{nombre}' está restringida.");
        }

        if (EsTipoRestringido(nombre)
            || nombre.Contains("FileSystemObject", StringComparison.OrdinalIgnoreCase)
            || nombre.Contains("ADODB.Stream", StringComparison.OrdinalIgnoreCase))
        {
            return Bloquear($"New-Object no puede crear el tipo '{nombre}'.");
        }

        return null;
    }

    private static ResultadoValidacionPowerShell? ValidarExplorer(CommandAst comando)
    {
        IReadOnlyList<string>? argumentos = ObtenerLiterales(
            comando,
            Array.Empty<string>(),
            posicion: 0);

        if (argumentos is null)
        {
            return Bloquear(
                "Explorer sólo admite un destino literal verificable.");
        }

        if (argumentos.Count == 0)
        {
            return null;
        }

        string destino = argumentos[0];

        if (EsUrlWebNavegableSegura(destino))
        {
            return null;
        }

        if (EsDestinoAppsFolderSeguro(destino))
        {
            return null;
        }

        if (destino.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || destino.Contains('\\') || destino.Contains('/'))
        {
            return Bloquear(
                "ControlPCIA no abre archivos ni carpetas locales con Explorer; sólo puede consultar sus nombres, rutas y metadatos.");
        }

        return null;
    }

    private static ResultadoValidacionPowerShell? ValidarArgumentosNativos(
        CommandAst comando)
    {
        for (int indice = 1; indice < comando.CommandElements.Count; indice++)
        {
            CommandElementAst elemento = comando.CommandElements[indice];
            IReadOnlyList<string>? valores = ExtraerLiterales(elemento);

            if (valores is null)
            {
                return Bloquear(
                    $"El programa nativo '{comando.GetCommandName()}' sólo admite argumentos literales verificables.");
            }

            foreach (string valor in valores)
            {
                if (!EsArgumentoNativoSeguro(valor))
                {
                    return Bloquear(
                        $"El argumento '{valor}' no es seguro para un programa nativo.");
                }
            }
        }

        return null;
    }

    private static ResultadoValidacionPowerShell? ValidarInvocacionEspecial(
        InvokeMemberExpressionAst invocacion,
        string miembro)
    {
        if (miembro.Equals("AppActivate", StringComparison.OrdinalIgnoreCase)
            || miembro.Equals("SendKeys", StringComparison.OrdinalIgnoreCase))
        {
            return Bloquear(
                $"La automatización de interfaz mediante '{miembro}' está restringida.");
        }

        return null;
    }

    private static bool EsDestinoAppsFolderSeguro(string valor)
    {
        const string prefijo = @"shell:AppsFolder\";

        if (!valor.StartsWith(
                prefijo,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string identificador = valor[prefijo.Length..];

        return identificador.Length is >= 3 and <= 500
               && !identificador.Contains("..", StringComparison.Ordinal)
               && !identificador.StartsWith('\\')
               && !identificador.EndsWith('\\')
               && identificador.All(caracter =>
                   char.IsLetterOrDigit(caracter)
                   || caracter is '.'
                       or '_'
                       or '-'
                       or '+'
                       or '!'
                       or '{'
                       or '}'
                       or '\\'
                       or ' '
                       or '('
                       or ')');
    }

    private static bool EsArgumentoNativoSeguro(string valor)
    {
        valor = valor.Trim();

        if (valor.Length == 0)
        {
            return true;
        }

        if (EsUrlWebNavegableSegura(valor))
        {
            return true;
        }

        if (valor.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
            ||
            valor.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        bool interruptor =
            (valor[0] == '-' || valor[0] == '/')
            &&
            !valor.Contains('\\')
            &&
            valor.Count(caracter => caracter == '/') <= 1
            &&
            !valor.Contains(':')
            &&
            !valor.Contains("..", StringComparison.Ordinal);

        if (interruptor)
        {
            return true;
        }

        return decimal.TryParse(
            valor.TrimEnd('%'),
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
               out _);
    }

    private static HashSet<string> ObtenerNombresParametros(
        CommandAst comando)
    {
        return comando.CommandElements
            .OfType<CommandParameterAst>()
            .Select(parametro => parametro.ParameterName.TrimStart('-'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool PareceRutaLocal(string valor)
    {
        return valor.StartsWith(@"\\", StringComparison.Ordinal)
               || Regex.IsMatch(
                   valor,
                   "^[A-Za-z]:[\\\\/]",
                   RegexOptions.CultureInvariant);
    }

    internal static IReadOnlyList<string> ObtenerRaicesBusquedaPermitidas()
    {
        string perfil = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        string[] candidatas =
        [
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory),
            string.IsNullOrWhiteSpace(perfil)
                ? string.Empty
                : Path.Combine(perfil, "Downloads"),
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyVideos),
            Environment.GetEnvironmentVariable("OneDrive") ?? string.Empty,
            Environment.GetEnvironmentVariable("OneDriveConsumer")
                ?? string.Empty,
            perfil
        ];

        return candidatas
            .Where(ruta =>
                !string.IsNullOrWhiteSpace(ruta)
                && Directory.Exists(ruta))
            .Select(Path.GetFullPath)
            .Select(ruta => ruta.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool EsRutaDeBusquedaPermitida(string valor)
    {
        if (!EsRutaLocalAbsoluta(valor))
        {
            return false;
        }

        string completa = Path.GetFullPath(valor).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return ObtenerRaicesBusquedaPermitidas().Any(raiz =>
            completa.Equals(
                raiz,
                StringComparison.OrdinalIgnoreCase)
            || completa.StartsWith(
                raiz + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool EsFiltroDeNombreSeguro(string filtro)
    {
        return filtro.Length is >= 1 and <= 260
               && filtro is not "." and not ".."
               && !filtro.Contains("..", StringComparison.Ordinal)
               && !filtro.Contains('\\')
               && !filtro.Contains('/')
               && !filtro.Contains(':')
               && filtro.All(caracter => !char.IsControl(caracter));
    }

    private static bool EsRutaLocalAbsoluta(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor)
            || valor.Length > 1024
            || valor.StartsWith(@"\\", StringComparison.Ordinal)
            || valor.Contains('*')
            || valor.Contains('?')
            || valor.Contains('[', StringComparison.Ordinal)
            || valor.Contains(']', StringComparison.Ordinal)
            || valor.Any(char.IsControl)
            || !Regex.IsMatch(
                valor,
                "^[A-Za-z]:[\\\\/]",
                RegexOptions.CultureInvariant)
            || valor[2..].Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            string completa = Path.GetFullPath(valor);
            string raiz = Path.GetPathRoot(completa) ?? string.Empty;

            if (raiz.Length == 0
                || completa.Split(
                        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                        StringSplitOptions.RemoveEmptyEntries)
                    .Any(parte => parte == ".."))
            {
                return false;
            }

            return completa.StartsWith(
                raiz,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (
            ex is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    private static bool EsUrlWebNavegableSegura(string valor)
    {
        if (valor.Length > 2048
            ||
            !Uri.TryCreate(valor, UriKind.Absolute, out Uri? uri)
            ||
            uri.Scheme is not ("http" or "https")
            ||
            string.IsNullOrWhiteSpace(uri.Host)
            ||
            !string.IsNullOrEmpty(uri.UserInfo)
            ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            ||
            uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out IPAddress? direccion)
            &&
            EsDireccionPrivada(direccion))
        {
            return false;
        }

        string extension = Path.GetExtension(
            Uri.UnescapeDataString(uri.AbsolutePath));

        return !ExtensionesWebBloqueadas.Contains(extension);
    }

    private static bool EsDireccionPrivada(IPAddress direccion)
    {
        if (IPAddress.IsLoopback(direccion))
        {
            return true;
        }

        if (direccion.IsIPv4MappedToIPv6)
        {
            direccion = direccion.MapToIPv4();
        }

        byte[] bytes = direccion.GetAddressBytes();

        if (direccion.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10
                   || bytes[0] == 127
                   || bytes[0] == 192 && bytes[1] == 168
                   || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                   || bytes[0] == 169 && bytes[1] == 254;
        }

        return direccion.AddressFamily == AddressFamily.InterNetworkV6
               &&
               (direccion.IsIPv6LinkLocal
                || (bytes[0] & 0xFE) == 0xFC);
    }

    private static bool EsComandoNativo(string nombre)
    {
        return !nombre.Contains('-');
    }

    private static bool EsComandoControlPcia(string nombre)
    {
        return nombre.Equals(
            "ControlPCIA.exe",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool EsWinget(string nombre)
    {
        return nombre.Equals("winget", StringComparison.OrdinalIgnoreCase)
               || nombre.Equals(
                   "winget.exe",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool EsExplorer(string nombre)
    {
        return nombre.Equals(
                   "explorer",
                   StringComparison.OrdinalIgnoreCase)
               || nombre.Equals(
                   "explorer.exe",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string>? ObtenerArgumentosLiterales(
        CommandAst comando)
    {
        var argumentos = new List<string>();

        for (int indice = 1; indice < comando.CommandElements.Count; indice++)
        {
            if (comando.CommandElements[indice] is CommandParameterAst)
            {
                return null;
            }

            IReadOnlyList<string>? valores =
                ExtraerLiterales(comando.CommandElements[indice]);

            if (valores is null)
            {
                return null;
            }

            argumentos.AddRange(valores);
        }

        return argumentos;
    }

    private static IReadOnlyList<string>? ObtenerLiterales(
        CommandAst comando,
        IReadOnlyCollection<string> nombresParametro,
        int posicion)
    {
        for (int indice = 1; indice < comando.CommandElements.Count; indice++)
        {
            if (comando.CommandElements[indice] is not CommandParameterAst parametro
                || !nombresParametro.Contains(
                    parametro.ParameterName,
                    StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (parametro.Argument is not null)
            {
                return ExtraerLiterales(parametro.Argument);
            }

            if (indice + 1 >= comando.CommandElements.Count
                || comando.CommandElements[indice + 1] is CommandParameterAst)
            {
                return null;
            }

            return ExtraerLiterales(comando.CommandElements[indice + 1]);
        }

        int actual = 0;

        for (int indice = 1; indice < comando.CommandElements.Count; indice++)
        {
            CommandElementAst elemento = comando.CommandElements[indice];

            if (elemento is CommandParameterAst)
            {
                if (indice + 1 < comando.CommandElements.Count
                    && comando.CommandElements[indice + 1] is not CommandParameterAst)
                {
                    indice++;
                }

                continue;
            }

            if (actual++ == posicion)
            {
                return ExtraerLiterales(elemento);
            }
        }

        return null;
    }

    private static IReadOnlyList<string>? ExtraerLiterales(Ast ast)
    {
        if (ast is StringConstantExpressionAst cadena)
        {
            return [cadena.Value];
        }

        if (ast is ExpandableStringExpressionAst expandible
            && expandible.NestedExpressions.Count == 0)
        {
            return [expandible.Value];
        }

        if (ast is ConstantExpressionAst constante && constante.Value is not null)
        {
            return [constante.Value.ToString() ?? ""];
        }

        if (ast is ArrayLiteralAst array)
        {
            var resultado = new List<string>();

            foreach (ExpressionAst elemento in array.Elements)
            {
                IReadOnlyList<string>? valores = ExtraerLiterales(elemento);

                if (valores is null)
                {
                    return null;
                }

                resultado.AddRange(valores);
            }

            return resultado;
        }

        return null;
    }

    private static bool TieneParametro(CommandAst comando, string nombre) =>
        comando.CommandElements
            .OfType<CommandParameterAst>()
            .Any(parametro =>
                parametro.ParameterName.Equals(
                    nombre.TrimStart('-'),
                    StringComparison.OrdinalIgnoreCase));

    private static string? ObtenerNombreMiembro(Ast miembro) => miembro switch
    {
        StringConstantExpressionAst cadena => cadena.Value,
        ConstantExpressionAst constante => constante.Value?.ToString(),
        _ => null
    };

    private static bool EsTipoRestringido(string nombre)
    {
        string normalizado = nombre.Trim().Trim('[', ']').ToLowerInvariant();
        return TiposBloqueados.Any(tipo => normalizado.StartsWith(tipo));
    }

    private static bool EsMetodoRestringido(string miembro)
    {
        return MetodosBloqueados.Contains(miembro)
               ||
               PrefijosMetodosBloqueados.Any(prefijo =>
                   miembro.StartsWith(
                       prefijo,
                       StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizarAlias(string nombre) =>
        AliasEspeciales.TryGetValue(nombre, out string? expandido)
            ? expandido
            : nombre;

    private static void CargarRestricciones()
    {
        foreach (string lineaOriginal in BaseRestriccionesPowerShell.Datos.Split('\n'))
        {
            string linea = lineaOriginal.Trim();

            if (string.IsNullOrWhiteSpace(linea) || linea.StartsWith('#'))
            {
                continue;
            }

            string[] partes = linea.Split('|');

            if (partes.Length < 2)
            {
                continue;
            }

            string tipo = partes[0].Trim();
            string valor = partes[1].Trim();

            switch (tipo.ToUpperInvariant())
            {
                case "CMD":
                    ComandosBloqueados.Add(valor);
                    break;
                case "PREFIX":
                    PrefijosBloqueados.Add(valor);
                    break;
                case "TARGET":
                    DestinosBloqueados.Add(Path.GetFileName(valor));
                    break;
                case "TEXT":
                    TextosBloqueados.Add(valor);
                    break;
                case "ARG" when partes.Length >= 3:
                    ArgumentosBloqueados.Add(
                        new(partes[1].Trim(), string.Join('|', partes.Skip(2)).Trim()));
                    break;
            }
        }
    }

    private static ResultadoValidacionPowerShell Bloquear(string motivo) =>
        new(false, motivo);

    private sealed record ReglaArgumento(string Comando, string Argumento);
}
