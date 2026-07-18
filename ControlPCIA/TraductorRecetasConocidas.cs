using System.Text;
using System.Text.RegularExpressions;

namespace ControlPCIA;

internal sealed record ResultadoTraduccionControl(
    string Estado,
    PlanTareasControl Plan,
    IReadOnlyList<string> Conocimientos,
    string RespuestaModelo,
    string? Comando,
    bool Permitido,
    string Motivo,
    bool Ejecutado = false);

/// <summary>
/// Convierte una petición natural en el siguiente comando de una receta ya
/// conocida. Esta clase no ejecuta comandos: tanto el modo diagnóstico como el
/// controlador real consumen exactamente la misma propuesta.
/// </summary>
internal static class TraductorRecetasConocidas
{
    private sealed record VentanaObservada(
        string Proceso,
        string Titulo);

    internal const string VentanasEstado = "ventanas.estado";
    internal const string AplicacionesAbrir = "aplicaciones.abrir";
    internal const string AplicacionesInventario =
        "aplicaciones.inventario";
    internal const string ArchivosBuscar = "archivos.buscar";
    internal const string ArchivosAbrir = "archivos.abrir";

    private static readonly string[] ConocimientosDisponibles =
    [
        VentanasEstado,
        AplicacionesAbrir,
        AplicacionesInventario,
        ArchivosBuscar,
        ArchivosAbrir
    ];

    private static readonly HashSet<string> PalabrasOperacionVentana =
        new(StringComparer.Ordinal)
        {
            "activa", "activar", "activada", "ventana", "ventanas",
            "primer", "plano", "trae", "traer", "frente", "delante",
            "maximiza", "maximizar", "minimiza", "minimizar",
            "restaura", "restaurar", "mueve", "mover", "redimensiona",
            "redimensionar", "pantalla", "grande", "pon", "poner",
            "aplicacion", "programa"
        };

    private const string ComandoInventarioVentanas =
        "Get-Process | Where-Object { -not [string]::IsNullOrWhiteSpace($_.MainWindowTitle) } | ForEach-Object { Write-Output ('PROCESS_NAME=' + $_.ProcessName); Write-Output ('WINDOW_TITLE=' + $_.MainWindowTitle) }";

    private const string ComandoInventarioAplicaciones =
        "Get-StartApps | Where-Object { $_.Name -notmatch '^(Desinstalar|Uninstall|Remove)' } | ForEach-Object { Write-Output ('APP_NAME=' + $_.Name); Write-Output ('APP_ID=' + $_.AppID) }";

    internal static IReadOnlyList<string> ObtenerConocimientosAplicables(
        PlanTareasControl plan)
    {
        var resultado = new List<string>();

        foreach (string conocimiento in plan.ConocimientosSeleccionados)
        {
            if (ConocimientosDisponibles.Contains(
                    conocimiento,
                    StringComparer.Ordinal)
                && !resultado.Contains(
                    conocimiento,
                    StringComparer.Ordinal))
            {
                resultado.Add(conocimiento);
            }
        }

        if (ControlWindows.PlanSolicitaSoloEstadoDeVentanas(plan)
            && !resultado.Contains(
                VentanasEstado,
                StringComparer.Ordinal))
        {
            resultado.Add(VentanasEstado);
        }

        return resultado;
    }

    internal static bool PuedeResolverPlan(PlanTareasControl plan)
    {
        IReadOnlyList<string> conocimientos =
            ObtenerConocimientosAplicables(plan);

        if (conocimientos.Count == 0 || plan.Tareas.Count == 0)
        {
            return false;
        }

        return plan.Tareas.All(tarea =>
            conocimientos.Any(conocimiento =>
                TareaEsCompatibleConConocimiento(
                    tarea,
                    conocimiento)));
    }

    internal static bool TareaEsCompatibleConConocimiento(
        string tarea,
        string conocimiento)
    {
        string normalizada = InventarioTexto.Normalizar(tarea);

        return conocimiento switch
        {
            VentanasEstado => Regex.IsMatch(
                normalizada,
                @"\b(?:activ|primer plano|trae.*frente|delante|maximiz|minimiz|restaur|recoloc|redimension|cambia.*taman|mueve.*ventana)\w*",
                RegexOptions.CultureInvariant),
            AplicacionesAbrir =>
                Regex.IsMatch(
                    normalizada,
                    @"\b(?:abre|abrir|inicia|iniciar|lanza|lanzar|ejecuta|ejecutar)\b",
                    RegexOptions.CultureInvariant)
                && !MencionaArchivo(normalizada, tarea),
            AplicacionesInventario =>
                Regex.IsMatch(
                    normalizada,
                    @"\b(?:aplicacion|aplicaciones|programa|programas|ventana|ventanas|cosas)\w*\b",
                    RegexOptions.CultureInvariant)
                && Regex.IsMatch(
                    normalizada,
                    @"\b(?:abiert|ejecut|activo|activas|tengo|consult|lista|listar|muestra|dime|cual|cuales|que)\w*\b",
                    RegexOptions.CultureInvariant),
            ArchivosBuscar =>
                Regex.IsMatch(
                    normalizada,
                    @"\b(?:busca|buscar|encuentra|encontrar|localiza|localizar|donde|lista|listar)\w*\b",
                    RegexOptions.CultureInvariant)
                && MencionaArchivo(normalizada, tarea),
            ArchivosAbrir =>
                Regex.IsMatch(
                    normalizada,
                    @"\b(?:abre|abrir|inicia|iniciar|lanza|lanzar|ejecuta|ejecutar)\b",
                    RegexOptions.CultureInvariant)
                && MencionaArchivo(normalizada, tarea),
            _ => false
        };
    }

    internal static List<MensajeOllama> CrearMensajes(
        string instruccion,
        PlanTareasControl plan,
        IReadOnlyList<RecetaReferencia> recetas)
    {
        IReadOnlyList<string> conocimientos =
            ObtenerConocimientosAplicables(plan);
        string biblioteca = CrearBiblioteca(conocimientos);
        string memoria = CrearMemoria(recetas);

        return
        [
            new MensajeOllama(
                "system",
                """
                Eres un traductor rápido de lenguaje natural al siguiente
                comando literal de PowerShell. ControlPCIA ya eligió recetas
                conocidas por el significado de la petición.

                REGLAS:
                - No busques en Internet y no inventes otra estrategia.
                - Devuelve UN comando PowerShell, sin Markdown ni explicación.
                - Si necesitas un dato de Windows, devuelve primero la consulta
                  indicada por la receta. En el turno siguiente recibirás stdout,
                  stderr y el código de salida reales.
                - Usa únicamente datos literales que aparezcan en la petición,
                  en una receta aprendida o en stdout real.
                - No actúes sobre otra aplicación, archivo o ventana.
                - No uses SendKeys, UI Automation, OCR, ratón ni teclado.
                - Cuando todas las tareas estén demostradas por la salida real,
                  responde FIN.
                - Si falta una elección personal imprescindible, responde
                  PREGUNTAR: y una única pregunta breve.
                - Tú no ejecutas nada. El programa validará y, fuera del modo
                  diagnóstico, decidirá si ejecuta la propuesta.
                """),
            new MensajeOllama(
                "user",
                $"""
                PETICIÓN:
                {instruccion.Trim()}

                RESULTADOS SOLICITADOS:
                {plan.Formatear()}

                RECETAS SELECCIONADAS:
                {biblioteca}

                RECETAS APRENDIDAS RELACIONADAS:
                {memoria}

                Propón solamente el siguiente comando.
                """)
        ];
    }

    internal static bool TryCrearPrimerComandoDeterminista(
        PlanTareasControl plan,
        IReadOnlyList<RecetaReferencia> recetas,
        out string comando)
    {
        IReadOnlyList<string> conocimientos =
            ObtenerConocimientosAplicables(plan);

        if (conocimientos.Contains(
                AplicacionesAbrir,
                StringComparer.Ordinal))
        {
            string? aprendido = recetas
                .Where(receta => receta.Similitud >= 0.75)
                .SelectMany(receta => receta.Comandos)
                .FirstOrDefault(candidato => Regex.IsMatch(
                    candidato,
                    """
                    ^\s*explorer(?:\.exe)?\s+
                    (?:"shell:AppsFolder\\[^"]+"|'shell:AppsFolder\\[^']+')
                    \s*$
                    """,
                    RegexOptions.IgnoreCase
                    | RegexOptions.CultureInvariant
                    | RegexOptions.IgnorePatternWhitespace));

            comando = aprendido ?? ComandoInventarioAplicaciones;
            return true;
        }

        if (conocimientos.Contains(
                VentanasEstado,
                StringComparer.Ordinal)
            || conocimientos.Contains(
                AplicacionesInventario,
                StringComparer.Ordinal))
        {
            comando = ComandoInventarioVentanas;
            return true;
        }

        if ((conocimientos.Contains(
                 ArchivosBuscar,
                 StringComparer.Ordinal)
             || conocimientos.Contains(
                 ArchivosAbrir,
                 StringComparer.Ordinal))
            && TryCrearConsultaArchivoDeterminista(
                plan,
                out comando))
        {
            return true;
        }

        comando = string.Empty;
        return false;
    }

    internal static async Task<string?> CrearComandoEstadoVentanaAsync(
        PlanTareasControl plan,
        IReadOnlyList<ResultadoPasoControl> pasos,
        CancellationToken cancellationToken)
    {
        if (!ObtenerConocimientosAplicables(plan).Contains(
                VentanasEstado,
                StringComparer.Ordinal)
            || !PlanAdmitePlantillaFijaVentana(plan)
            || !pasos.Any(paso =>
                Regex.IsMatch(
                    paso.Salida,
                    @"(?:^|\r?\n)PROCESS_NAME=.+(?:\r?\n|$)",
                    RegexOptions.IgnoreCase
                    | RegexOptions.CultureInvariant)))
        {
            return null;
        }

        string? proceso = TrySeleccionarProcesoVentana(
            plan,
            pasos);
        proceso ??= await SeleccionarProcesoVentanaConLlamaAsync(
            plan,
            pasos,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(proceso))
        {
            return null;
        }

        string comando = CrearComandoEstadoVentana(
            plan,
            proceso);

        return pasos.Any(paso =>
            paso.Comando.Equals(
                comando,
                StringComparison.OrdinalIgnoreCase))
            ? null
            : comando;
    }

    internal static string? TrySeleccionarProcesoVentana(
        PlanTareasControl plan,
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        IReadOnlyList<VentanaObservada> ventanas =
            ObtenerVentanasObservadas(pasos);

        if (ventanas.Count == 0)
        {
            return null;
        }

        string peticion = InventarioTexto.Normalizar(
            string.Join(" ", plan.Tareas));
        HashSet<string> tokens = Regex.Matches(
                peticion,
                @"[\p{L}\p{N}]{3,}",
                RegexOptions.CultureInvariant)
            .Select(coincidencia => coincidencia.Value)
            .Where(token => !PalabrasOperacionVentana.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
        var puntuadas = ventanas
            .Select(ventana =>
            {
                string texto = InventarioTexto.Normalizar(
                    ventana.Proceso + " " + ventana.Titulo);
                int puntuacion = tokens.Count(token =>
                    texto.Contains(
                        token,
                        StringComparison.Ordinal));
                return (ventana, puntuacion);
            })
            .OrderByDescending(elemento => elemento.puntuacion)
            .ThenBy(elemento => elemento.ventana.Proceso)
            .ToArray();

        if (puntuadas.Length == 1)
        {
            return puntuadas[0].ventana.Proceso;
        }

        return puntuadas[0].puntuacion > 0
               && puntuadas[0].puntuacion
               > puntuadas[1].puntuacion
            ? puntuadas[0].ventana.Proceso
            : null;
    }

    internal static string CrearComandoEstadoVentana(
        PlanTareasControl plan,
        string proceso)
    {
        string tareas = InventarioTexto.Normalizar(
            string.Join(" ", plan.Tareas));
        bool activar = Regex.IsMatch(
            tareas,
            @"\b(?:activ|primer plano|trae.*frente|delante)\w*",
            RegexOptions.CultureInvariant);
        bool maximizar = Regex.IsMatch(
            tareas,
            @"\bmaximiz\w*",
            RegexOptions.CultureInvariant);
        bool minimizar = Regex.IsMatch(
            tareas,
            @"\bminimiz\w*",
            RegexOptions.CultureInvariant);
        bool restaurar = Regex.IsMatch(
            tareas,
            @"\brestaur\w*",
            RegexOptions.CultureInvariant);
        string procesoEscapado = EscaparPowerShell(proceso);
        var comando = new StringBuilder(
            $$"""
            $p = Get-Process -Name '{{procesoEscapado}}' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
            if ($null -eq $p) { throw 'No hay una ventana abierta para el proceso seleccionado.' }
            Add-Type -TypeDefinition @'
            using System;
            using System.Runtime.InteropServices;
            public static class VentanaRecetaControlPCIA {
                [DllImport("user32.dll")]
                public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
                [DllImport("user32.dll")]
                public static extern bool IsZoomed(IntPtr hWnd);
                [DllImport("user32.dll")]
                public static extern bool IsIconic(IntPtr hWnd);
                [DllImport("user32.dll")]
                public static extern bool SetForegroundWindow(IntPtr hWnd);
            }
            '@
            $correcto = $true
            Write-Output ('PROCESS_NAME=' + $p.ProcessName)
            """);

        if (maximizar)
        {
            comando.AppendLine();
            comando.Append(
                """
                [VentanaRecetaControlPCIA]::ShowWindowAsync($p.MainWindowHandle, 3) | Out-Null
                Start-Sleep -Milliseconds 200
                $p.Refresh()
                $maximizada = [VentanaRecetaControlPCIA]::IsZoomed($p.MainWindowHandle)
                Write-Output ('MAXIMIZED=' + $maximizada)
                $correcto = $correcto -and $maximizada
                """);
        }

        if (minimizar)
        {
            comando.AppendLine();
            comando.Append(
                """
                [VentanaRecetaControlPCIA]::ShowWindowAsync($p.MainWindowHandle, 6) | Out-Null
                Start-Sleep -Milliseconds 200
                $p.Refresh()
                $minimizada = [VentanaRecetaControlPCIA]::IsIconic($p.MainWindowHandle)
                Write-Output ('MINIMIZED=' + $minimizada)
                $correcto = $correcto -and $minimizada
                """);
        }

        if (restaurar)
        {
            comando.AppendLine();
            comando.Append(
                """
                [VentanaRecetaControlPCIA]::ShowWindowAsync($p.MainWindowHandle, 9) | Out-Null
                Start-Sleep -Milliseconds 200
                $p.Refresh()
                $restaurada = -not [VentanaRecetaControlPCIA]::IsIconic($p.MainWindowHandle)
                Write-Output ('RESTORED=' + $restaurada)
                $correcto = $correcto -and $restaurada
                """);
        }

        if (activar)
        {
            comando.AppendLine();
            comando.Append(
                """
                $primerPlano = [VentanaRecetaControlPCIA]::SetForegroundWindow($p.MainWindowHandle)
                $appActivate = (New-Object -ComObject WScript.Shell).AppActivate($p.Id)
                $activada = $primerPlano -or $appActivate
                Write-Output ('ACTIVATED=' + $activada)
                $correcto = $correcto -and $activada
                """);
        }

        comando.AppendLine();
        comando.Append("if (-not $correcto) { exit 1 }");
        return comando.ToString();
    }

    internal static ResultadoValidacionPowerShell ValidarAlineacion(
        PlanTareasControl plan,
        string comando,
        IReadOnlyList<ResultadoPasoControl> pasos,
        IReadOnlyList<RecetaReferencia>? recetas = null)
    {
        IReadOnlyList<string> conocimientos =
            ObtenerConocimientosAplicables(plan);

        if (conocimientos.Count == 0)
        {
            return new(
                false,
                "El plan no seleccionó ninguna receta conocida.");
        }

        if (!ControlWindows.EsComandoCompatibleConModoConsola(comando))
        {
            return new(
                false,
                "La propuesta intenta automatizar la interfaz, el ratón o el teclado.");
        }

        if (conocimientos.Contains(VentanasEstado, StringComparer.Ordinal)
            && PlanSolicitaUnicamenteEstadoVentana(plan)
            && ControlWindows.EsEstrategiaInvalidaParaEstadoVentana(
                comando))
        {
            return new(
                false,
                "La propuesta no corresponde a una operación de estado de ventana.");
        }

        bool coincide = conocimientos.Any(conocimiento =>
            ComandoPerteneceAReceta(
                plan,
                conocimiento,
                comando,
                pasos,
                recetas ?? []));

        return coincide
            ? new(
                true,
                "El comando pertenece a una de las recetas seleccionadas.")
            : new(
                false,
                "El comando no pertenece a ninguna receta seleccionada para esta petición.");
    }

    internal static string CrearCorreccion(
        PlanTareasControl plan,
        string motivo)
    {
        return $"""
            PROPUESTA RECHAZADA POR DESALINEACIÓN:
            {motivo}

            No cambies de objetivo. Usa exclusivamente estas recetas:
            {CrearBiblioteca(ObtenerConocimientosAplicables(plan))}
            """;
    }

    internal static string CrearResultadoPaso(
        ResultadoPasoControl paso)
    {
        return $"""
            RESULTADO REAL DEL ÚLTIMO COMANDO:
            COMANDO={paso.Comando}
            EJECUTADO={paso.Ejecutado}
            CODIGO={paso.CodigoSalida}
            STDOUT:
            {Limitar(paso.Salida, 5000)}
            STDERR:
            {Limitar(paso.Error, 2500)}

            Propón el siguiente comando de las mismas recetas o FIN si toda la
            petición está demostrada.
            """;
    }

    private static string CrearBiblioteca(
        IReadOnlyList<string> conocimientos)
    {
        var resultado = new StringBuilder();

        foreach (string conocimiento in conocimientos)
        {
            if (resultado.Length > 0)
            {
                resultado.AppendLine();
            }

            resultado.AppendLine($"[{conocimiento}]");
            resultado.AppendLine(
                conocimiento switch
                {
                    VentanasEstado => CrearRecetaVentanas(),
                    AplicacionesAbrir => CrearRecetaAbrirAplicacion(),
                    AplicacionesInventario =>
                        CrearRecetaInventarioAplicaciones(),
                    ArchivosBuscar => CrearRecetaBuscarArchivos(),
                    ArchivosAbrir => CrearRecetaAbrirArchivo(),
                    _ => string.Empty
                });
        }

        return resultado.ToString().Trim();
    }

    private static bool PlanAdmitePlantillaFijaVentana(
        PlanTareasControl plan)
    {
        string[] tareasVentana = plan.Tareas
            .Where(tarea =>
                TareaEsCompatibleConConocimiento(
                    tarea,
                    VentanasEstado))
            .ToArray();

        return tareasVentana.Length > 0
               && tareasVentana.All(tarea =>
            {
                string normalizada =
                    InventarioTexto.Normalizar(tarea);
                return Regex.IsMatch(
                           normalizada,
                           @"\b(?:activ|primer plano|trae.*frente|delante|maximiz|minimiz|restaur)\w*",
                           RegexOptions.CultureInvariant)
                       && !Regex.IsMatch(
                           normalizada,
                           @"\b(?:recoloc|redimension|cambia.*taman|mueve.*ventana)\w*",
                           RegexOptions.CultureInvariant);
            });
    }

    private static async Task<string?>
        SeleccionarProcesoVentanaConLlamaAsync(
            PlanTareasControl plan,
            IReadOnlyList<ResultadoPasoControl> pasos,
            CancellationToken cancellationToken)
    {
        IReadOnlyList<VentanaObservada> ventanas =
            ObtenerVentanasObservadas(pasos);

        if (ventanas.Count == 0)
        {
            return null;
        }

        string inventario = string.Join(
            Environment.NewLine,
            ventanas.Select(ventana =>
                $"PROCESS_NAME={ventana.Proceso}"
                + Environment.NewLine
                + $"WINDOW_TITLE={ventana.Titulo}"));
        var mensajes = new List<MensajeOllama>
        {
            new(
                "system",
                """
                Relaciona una petición de ventana con el inventario real de
                procesos. Devuelve exactamente `PROCESS_NAME=valor` usando un
                valor literal del inventario. No propongas ni ejecutes comandos.
                Si no hay una correspondencia clara, responde `SIN_PROCESO`.
                """),
            new(
                "user",
                $"""
                PETICIÓN:
                {plan.Formatear()}

                INVENTARIO REAL:
                {inventario}
                """)
        };
        string respuesta = (
            await ClienteOllama.ConversarAsync(
                mensajes,
                cancellationToken)).Trim();
        Match seleccion = Regex.Match(
            respuesta,
            @"^PROCESS_NAME=(?<proceso>[^\r\n]+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!seleccion.Success)
        {
            return null;
        }

        string proceso = seleccion.Groups["proceso"].Value.Trim();
        return ventanas.Any(ventana =>
            ventana.Proceso.Equals(
                proceso,
                StringComparison.OrdinalIgnoreCase))
            ? proceso
            : null;
    }

    private static IReadOnlyList<VentanaObservada>
        ObtenerVentanasObservadas(
            IReadOnlyList<ResultadoPasoControl> pasos)
    {
        var resultado = new List<VentanaObservada>();

        foreach (ResultadoPasoControl paso in pasos.Where(paso =>
                     paso.Ejecutado
                     && paso.CodigoSalida == 0))
        {
            string? proceso = null;

            foreach (string original in paso.Salida.Split(
                         ["\r\n", "\n"],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                string linea = original.Trim();

                if (linea.StartsWith(
                        "PROCESS_NAME=",
                        StringComparison.OrdinalIgnoreCase))
                {
                    proceso = linea["PROCESS_NAME=".Length..].Trim();
                    continue;
                }

                if (proceso is null
                    || !linea.StartsWith(
                        "WINDOW_TITLE=",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string titulo =
                    linea["WINDOW_TITLE=".Length..].Trim();

                if (proceso.Length > 0 && titulo.Length > 0)
                {
                    resultado.Add(
                        new VentanaObservada(
                            proceso,
                            titulo));
                }

                proceso = null;
            }
        }

        return resultado
            .DistinctBy(ventana => ventana.Proceso)
            .ToArray();
    }

    private static string CrearRecetaVentanas()
    {
        return """
            Selecciona con Get-Process un único proceso real cuyo
            MainWindowHandle sea distinto de cero. Para activar usa
            (New-Object -ComObject WScript.Shell).AppActivate($p.Id): recibe el
            Id del proceso, no el handle. Para cambiar estado declara mediante
            Add-Type las API Win32 ShowWindowAsync, IsZoomed, IsIconic,
            SetForegroundWindow o SetWindowPos. Los valores son 3 maximizar,
            6 minimizar y 9 restaurar. No uses Start-Process, Stop-Process,
            taskkill, SendKeys, registro ni otra aplicación. Publica
            PROCESS_NAME y cada evidencia solicitada: ACTIVATED, MAXIMIZED,
            MINIMIZED, RESTORED, MOVED o RESIZED. Devuelve código distinto de
            cero si la comprobación es falsa.
            """;
    }

    private static string CrearRecetaAbrirAplicacion()
    {
        return """
            Si una receta aprendida contiene un AppID literal verificado, usa
            directamente explorer.exe 'shell:AppsFolder\APPID'. Si no, consulta
            Get-StartApps filtrando semánticamente el nombre solicitado,
            excluye Desinstalar/Uninstall y publica APP_NAME y APP_ID. Usa
            después exactamente un APP_ID aparecido en stdout con explorer.exe
            'shell:AppsFolder\APPID'. No inventes ejecutables ni abras utilidades
            distintas. Comprueba la apertura con Get-Process -Name y publica
            PROCESS_NAME y WINDOW_TITLE. Si Get-StartApps no ofrece resultado,
            consulta las claves App Paths o Uninstall y publica un
            EXECUTABLE_PATH real antes de usar Start-Process -FilePath con esa
            misma ruta literal.
            """;
    }

    private static string CrearRecetaInventarioAplicaciones()
    {
        return """
            Ejecuta Get-Process, conserva sólo procesos con MainWindowTitle y
            publica para cada uno PROCESS_NAME y WINDOW_TITLE. No abras, cierres,
            actives ni cambies ninguna aplicación.
            """;
    }

    private static string CrearRecetaBuscarArchivos()
    {
        string raices = string.Join(
            ", ",
            ValidadorPowerShell
                .ObtenerRaicesBusquedaPermitidas()
                .Select(ruta => "'" + ruta.Replace(
                    "'",
                    "''",
                    StringComparison.Ordinal) + "'"));

        return $"""
            Busca mediante Get-ChildItem en las unidades locales literales
            disponibles ({raices}). Adapta -File o -Directory y -Filter al
            nombre solicitado, usa -Recurse -ErrorAction SilentlyContinue,
            limita resultados y publica FULL_NAME, LENGTH y LAST_WRITE_TIME.
            Una salida vacía es un resultado válido. No abras ni modifiques
            ningún resultado.
            """;
    }

    private static string CrearRecetaAbrirArchivo()
    {
        return """
            Si la petición contiene una ruta literal, compruébala primero con
            Get-Item -LiteralPath y publica FULL_NAME. Si no contiene una ruta,
            localiza el archivo con la receta archivos.buscar. Después usa
            Start-Process -FilePath con un FULL_NAME literal aparecido en stdout
            real. Abrir archivos locales está permitido. No inventes rutas ni
            abras un resultado diferente.
            """;
    }

    private static string CrearMemoria(
        IReadOnlyList<RecetaReferencia> recetas)
    {
        if (recetas.Count == 0)
        {
            return "- Ninguna. Usa la receta de sistema seleccionada.";
        }

        return string.Join(
            Environment.NewLine,
            recetas.Take(5).Select((receta, indice) =>
                $"""
                RECETA {indice + 1} (similitud {receta.Similitud:F2}, éxitos {receta.Exitos})
                Intención: {receta.Intencion}
                Comandos verificados:
                {string.Join(
                    Environment.NewLine,
                    receta.Comandos.Select(comando => "  " + comando))}
                """));
    }

    private static bool ComandoPerteneceAReceta(
        PlanTareasControl plan,
        string conocimiento,
        string comando,
        IReadOnlyList<ResultadoPasoControl> pasos,
        IReadOnlyList<RecetaReferencia> recetas)
    {
        return conocimiento switch
        {
            VentanasEstado =>
                EsInventarioVentanas(comando)
                || EsComandoEstadoVentana(
                    plan,
                    comando,
                    pasos),
            AplicacionesAbrir =>
                ContieneInicio(comando)
                    ? EsAperturaAplicacionConProcedencia(
                        comando,
                        pasos,
                        recetas)
                    : EsConsultaAplicaciones(comando)
                      || EsComprobacionAplicacion(comando),
            AplicacionesInventario =>
                EsInventarioVentanas(comando),
            ArchivosBuscar =>
                EsConsultaArchivosEstructurada(comando)
                && !ContieneInicio(comando),
            ArchivosAbrir =>
                ContieneInicio(comando)
                    ? EsAperturaArchivoConProcedencia(
                        comando,
                        pasos,
                        recetas)
                    : EsConsultaArchivoParaApertura(comando),
            _ => false
        };
    }

    private static bool EsInventarioVentanas(string comando)
    {
        return Regex.IsMatch(
                   comando,
                   @"\bGet-Process\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               && comando.Contains(
                   "MainWindowTitle",
                   StringComparison.OrdinalIgnoreCase)
               && comando.Contains(
                   "PROCESS_NAME=",
                   StringComparison.OrdinalIgnoreCase)
               && comando.Contains(
                   "WINDOW_TITLE=",
                   StringComparison.OrdinalIgnoreCase)
               && !Regex.IsMatch(
                   comando,
                   @"\b(?:AppActivate|ShowWindowAsync|SetForegroundWindow|SetWindowPos|Start-Process|Stop-Process|taskkill)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool EsComandoEstadoVentana(
        PlanTareasControl plan,
        string comando,
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        if (ControlWindows.EsEstrategiaInvalidaParaEstadoVentana(
                comando)
            || !Regex.IsMatch(
                comando,
                @"\bGet-Process\s+-Name\s+(?:""(?<doble>[^""]+)""|'(?<simple>[^']+)'|(?<libre>[A-Za-z0-9_.-]+))",
                RegexOptions.IgnoreCase
                | RegexOptions.CultureInvariant))
        {
            return false;
        }

        Match proceso = Regex.Match(
            comando,
            @"\bGet-Process\s+-Name\s+(?:""(?<doble>[^""]+)""|'(?<simple>[^']+)'|(?<libre>[A-Za-z0-9_.-]+))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        string nombre = proceso.Groups["doble"].Success
            ? proceso.Groups["doble"].Value
            : proceso.Groups["simple"].Success
                ? proceso.Groups["simple"].Value
                : proceso.Groups["libre"].Value;

        if (!ApareceEnEvidencia(
                nombre,
                pasos,
                "PROCESS_NAME=")
            || comando.Contains(
                "GetHINSTANCE",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string tarea in plan.Tareas)
        {
            string normalizada = InventarioTexto.Normalizar(tarea);

            if (Regex.IsMatch(
                    normalizada,
                    @"\b(?:activ|primer plano|trae.*frente|delante)\w*",
                    RegexOptions.CultureInvariant)
                && (!comando.Contains(
                        "AppActivate",
                        StringComparison.OrdinalIgnoreCase)
                    || !comando.Contains(
                        "ACTIVATED=",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (Regex.IsMatch(
                    normalizada,
                    @"\bmaximiz\w*",
                    RegexOptions.CultureInvariant)
                && (!Regex.IsMatch(
                        comando,
                        @"\[[A-Za-z_][\w.]*\]::ShowWindowAsync\s*\(",
                        RegexOptions.IgnoreCase
                        | RegexOptions.CultureInvariant)
                    || !comando.Contains(
                        "IsZoomed",
                        StringComparison.OrdinalIgnoreCase)
                    || !comando.Contains(
                        "MAXIMIZED=",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (Regex.IsMatch(
                    normalizada,
                    @"\bminimiz\w*",
                    RegexOptions.CultureInvariant)
                && (!comando.Contains(
                        "IsIconic",
                        StringComparison.OrdinalIgnoreCase)
                    || !comando.Contains(
                        "MINIMIZED=",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (Regex.IsMatch(
                    normalizada,
                    @"\brestaur\w*",
                    RegexOptions.CultureInvariant)
                && (!Regex.IsMatch(
                        comando,
                        @"\[[A-Za-z_][\w.]*\]::ShowWindowAsync\s*\(",
                        RegexOptions.IgnoreCase
                        | RegexOptions.CultureInvariant)
                    || !comando.Contains(
                        "RESTORED=",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (Regex.IsMatch(
                    normalizada,
                    @"\b(?:recoloc|mueve.*ventana)\w*",
                    RegexOptions.CultureInvariant)
                && (!comando.Contains(
                        "SetWindowPos",
                        StringComparison.OrdinalIgnoreCase)
                    || !comando.Contains(
                        "MOVED=",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (Regex.IsMatch(
                    normalizada,
                    @"\b(?:redimension|cambia.*taman)\w*",
                    RegexOptions.CultureInvariant)
                && (!comando.Contains(
                        "SetWindowPos",
                        StringComparison.OrdinalIgnoreCase)
                    || !comando.Contains(
                        "RESIZED=",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return comando.Contains(
            "PROCESS_NAME=",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool EsConsultaArchivosEstructurada(string comando)
    {
        return Regex.IsMatch(
                   comando,
                   @"\b(?:Get-ChildItem|gci|dir|ls)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               && comando.Contains(
                   "FULL_NAME=",
                   StringComparison.OrdinalIgnoreCase)
               && comando.Contains(
                   "LAST_WRITE_TIME=",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool EsConsultaArchivoParaApertura(string comando)
    {
        bool consulta = Regex.IsMatch(
            comando,
            @"\b(?:Get-Item|Test-Path|Get-ChildItem|gci|dir|ls)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return consulta
               && comando.Contains(
                   "FULL_NAME=",
                   StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TryCrearConsultaArchivoDeterminista(
        PlanTareasControl plan,
        out string comando)
    {
        foreach (string tarea in plan.Tareas)
        {
            Match ruta = Regex.Match(
                tarea,
                """
                (?:
                    ["'](?<entrecomillada>[A-Za-z]:[\\/][^"'\r\n]+)["']
                    |
                    (?<final>[A-Za-z]:[\\/][^"'<>|\r\n]+?)\s*$
                )
                """,
                RegexOptions.CultureInvariant
                | RegexOptions.IgnorePatternWhitespace);

            if (ruta.Success)
            {
                string literal = ruta.Groups["entrecomillada"].Success
                    ? ruta.Groups["entrecomillada"].Value
                    : ruta.Groups["final"].Value.Trim();

                if (EsLiteralArchivoSeguro(literal))
                {
                    string escapada = EscaparPowerShell(literal);
                    comando =
                        $"Get-Item -LiteralPath '{escapada}' -ErrorAction SilentlyContinue"
                        + " | ForEach-Object { Write-Output ('FULL_NAME=' + $_.FullName);"
                        + " Write-Output ('LENGTH=' + $_.Length);"
                        + " Write-Output ('LAST_WRITE_TIME=' + $_.LastWriteTime) }";
                    return true;
                }
            }

            Match nombre = Regex.Match(
                tarea,
                """
                \b(?:busca|buscar|encuentra|encontrar|localiza|localizar|abre|abrir)\w*
                \s+(?:(?:el|la|un|una|archivo|fichero|documento)\s+)*
                ["“«']?(?<nombre>[^"”»'\r\n;|<>]+?\.[\p{L}\p{N}]{1,16})["”»']?
                \s*$
                """,
                RegexOptions.IgnoreCase
                | RegexOptions.CultureInvariant
                | RegexOptions.IgnorePatternWhitespace);

            if (!nombre.Success)
            {
                continue;
            }

            string patron = nombre.Groups["nombre"].Value.Trim();

            if (!EsLiteralArchivoSeguro(patron)
                || patron.Contains('\\')
                || patron.Contains('/'))
            {
                continue;
            }

            string raices = string.Join(
                ",",
                ValidadorPowerShell
                    .ObtenerRaicesBusquedaPermitidas()
                    .Select(rutaLocal =>
                        "'" + EscaparPowerShell(rutaLocal) + "'"));
            comando =
                $"Get-ChildItem -LiteralPath {raices} -File -Filter '{EscaparPowerShell(patron)}'"
                + " -Recurse -ErrorAction SilentlyContinue | Select-Object -First 20"
                + " | ForEach-Object { Write-Output ('FULL_NAME=' + $_.FullName);"
                + " Write-Output ('LENGTH=' + $_.Length);"
                + " Write-Output ('LAST_WRITE_TIME=' + $_.LastWriteTime) }";
            return true;
        }

        comando = string.Empty;
        return false;
    }

    private static bool EsLiteralArchivoSeguro(string literal)
    {
        return literal.Length is >= 1 and <= 500
               && !literal.Any(char.IsControl)
               && !literal.Contains("..", StringComparison.Ordinal)
               && literal.IndexOfAny([';', '|', '<', '>']) < 0;
    }

    private static string EscaparPowerShell(string literal)
    {
        return literal.Replace(
            "'",
            "''",
            StringComparison.Ordinal);
    }

    private static bool ContieneInicio(string comando)
    {
        return Regex.IsMatch(
            comando,
            @"\b(?:Start-Process|start|saps)\b|^\s*explorer(?:\.exe)?\s+['""]shell:AppsFolder\\",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool EsConsultaAplicaciones(string comando)
    {
        return Regex.IsMatch(
                   comando,
                   @"(?:^|[;|]\s*)Get-StartApps\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               || Regex.IsMatch(
                   comando,
                   @"(?:^|[;|]\s*)Get-ItemProperty\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               && Regex.IsMatch(
                   comando,
                   @"\\(?:App Paths|Uninstall)\\",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool EsComprobacionAplicacion(string comando)
    {
        return Regex.IsMatch(
            comando,
            @"(?:^|[;|]\s*)Get-Process\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool EsAperturaAplicacionConProcedencia(
        string comando,
        IReadOnlyList<ResultadoPasoControl> pasos,
        IReadOnlyList<RecetaReferencia> recetas)
    {
        Match appId = Regex.Match(
            comando,
            """
            ^\s*explorer(?:\.exe)?\s+
            (?:"shell:AppsFolder\\(?<doble>[^"]+)"|'shell:AppsFolder\\(?<simple>[^']+)')
            \s*$
            """,
            RegexOptions.IgnoreCase
            | RegexOptions.CultureInvariant
            | RegexOptions.IgnorePatternWhitespace);

        if (appId.Success)
        {
            string literal = appId.Groups["doble"].Success
                ? appId.Groups["doble"].Value
                : appId.Groups["simple"].Value;
            return ApareceEnEvidencia(
                literal,
                pasos,
                "APP_ID=")
                || EsComandoAprendido(comando, recetas);
        }

        Match ruta = Regex.Match(
            comando,
            """
            (?:^|[;|]\s*)Start-Process\b[^\r\n;|]*?
            -FilePath(?:\s+|:)(?:"(?<doble>[^"]+)"|'(?<simple>[^']+)')
            """,
            RegexOptions.IgnoreCase
            | RegexOptions.CultureInvariant
            | RegexOptions.IgnorePatternWhitespace);

        if (!ruta.Success)
        {
            return false;
        }

        string literalRuta = ruta.Groups["doble"].Success
            ? ruta.Groups["doble"].Value
            : ruta.Groups["simple"].Value;
        return ApareceEnEvidencia(
            literalRuta,
            pasos,
            "EXECUTABLE_PATH=")
            || EsComandoAprendido(comando, recetas);
    }

    private static bool EsAperturaArchivoConProcedencia(
        string comando,
        IReadOnlyList<ResultadoPasoControl> pasos,
        IReadOnlyList<RecetaReferencia> recetas)
    {
        Match ruta = Regex.Match(
            comando,
            """
            (?:^|[;|]\s*)Start-Process\b[^\r\n;|]*?
            -FilePath(?:\s+|:)(?:"(?<doble>[^"]+)"|'(?<simple>[^']+)')
            """,
            RegexOptions.IgnoreCase
            | RegexOptions.CultureInvariant
            | RegexOptions.IgnorePatternWhitespace);

        if (!ruta.Success)
        {
            return false;
        }

        string literal = ruta.Groups["doble"].Success
            ? ruta.Groups["doble"].Value
            : ruta.Groups["simple"].Value;
        return ApareceEnEvidencia(
            literal,
            pasos,
            "FULL_NAME=")
            || EsComandoAprendido(comando, recetas);
    }

    private static bool ApareceEnEvidencia(
        string literal,
        IReadOnlyList<ResultadoPasoControl> pasos,
        string marcador)
    {
        return pasos.Any(paso =>
            paso.Ejecutado
            && paso.CodigoSalida == 0
            && paso.Salida
                .Split(
                    ["\r\n", "\n"],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(linea =>
                    linea.Trim().Equals(
                        marcador + literal,
                        StringComparison.OrdinalIgnoreCase)));
    }

    private static bool EsComandoAprendido(
        string comando,
        IReadOnlyList<RecetaReferencia> recetas)
    {
        string normalizado = comando.Trim();
        return recetas.Any(receta =>
            receta.Comandos.Any(aprendido =>
                aprendido.Trim().Equals(
                    normalizado,
                    StringComparison.Ordinal)));
    }

    private static bool PlanSolicitaUnicamenteEstadoVentana(
        PlanTareasControl plan)
    {
        return plan.Tareas.Count > 0
               && plan.Tareas.All(tarea =>
                   TareaEsCompatibleConConocimiento(
                       tarea,
                       VentanasEstado));
    }

    private static bool MencionaArchivo(
        string normalizada,
        string original)
    {
        return Regex.IsMatch(
                   normalizada,
                   @"\b(?:archivo|fichero|carpeta|documento|pdf|proyecto|imagen|foto|audio|video)\w*\b",
                   RegexOptions.CultureInvariant)
               || Regex.IsMatch(
                   original,
                   @"(?:[A-Za-z]:[\\/]|[\p{L}\p{N}_() -]+\.[\p{L}\p{N}_()-]+)",
                   RegexOptions.CultureInvariant);
    }

    private static string Limitar(string texto, int maximo)
    {
        string limpio = texto.Trim();
        return limpio.Length <= maximo
            ? limpio
            : limpio[..maximo] + "…";
    }
}
