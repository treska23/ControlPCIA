using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ControlPCIA;

internal enum TipoPeticionBasica
{
    NoCompatible,
    AbrirAplicacion,
    ConsultarAplicacionesAbiertas
}

internal sealed record PeticionBasica(
    TipoPeticionBasica Tipo,
    string Objetivo = "",
    string Motivo = "");

internal sealed record DependenciasControlBasico(
    Func<
        CancellationToken,
        Task<IReadOnlyList<AplicacionInstalada>>>
        ObtenerAplicacionesAsync,
    Func<
        string,
        CancellationToken,
        Task<ResultadoEjecucionPowerShell>>
        EjecutarAsync);

/// <summary>
/// Primer núcleo estable de ControlPCIA. No consulta a un modelo y sólo
/// admite una acción por petición: abrir una aplicación instalada o enumerar
/// las aplicaciones que tienen una ventana abierta.
/// </summary>
internal static class ControlBasico
{
    private const string MensajeCapacidades =
        "Por ahora sólo puedo abrir una aplicación instalada o decirte qué aplicaciones están abiertas.";

    private static readonly IReadOnlyDictionary<string, string>
        AliasAplicaciones =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["calculadora"] = "Calculator",
                ["la calculadora"] = "Calculator",
                ["bloc de notas"] = "Notepad",
                ["el bloc de notas"] = "Notepad",
                ["explorador"] = "Explorador de archivos",
                ["explorador de windows"] = "Explorador de archivos",
                ["explorador de archivos"] = "Explorador de archivos",
                ["explorador de ficheros"] = "Explorador de archivos",
                ["administrador de archivos"] = "Explorador de archivos"
            };

    private static readonly Regex VerboAbrir = new(
        @"\b(?:abre(?:me)?|abrir|abras|inicia|iniciar|arranca|arrancar|ejecuta|ejecutar|lanza|lanzar)\b",
        RegexOptions.IgnoreCase
        | RegexOptions.CultureInvariant
        | RegexOptions.Compiled);

    private static readonly Regex PrefijoPermitido = new(
        @"^(?:por favor[\s,]*|quiero\s+|quiero\s+que\s+|puedes\s+|podrias\s+|podrías\s+|necesito\s+|me\s+)*$",
        RegexOptions.IgnoreCase
        | RegexOptions.CultureInvariant
        | RegexOptions.Compiled);

    public static EstadoControlBasico Estado { get; } =
        new(
            true,
            "control-basico",
            "Control básico preparado: abrir una aplicación o consultar las aplicaciones abiertas.");

    public static Task<ResultadoControl> ControlarAsync(
        string instruccion,
        CancellationToken cancellationToken = default,
        bool soloTraducir = false)
    {
        var dependencias = new DependenciasControlBasico(
            InventarioAplicaciones.ObtenerAplicacionesAsync,
            static (comando, cancelacion) =>
                EjecutorPowerShell.EjecutarAsync(
                    comando,
                    cancelacion));

        return ControlarConDependenciasAsync(
            instruccion,
            soloTraducir,
            dependencias,
            cancellationToken);
    }

    internal static async Task<ResultadoControl>
        ControlarConDependenciasAsync(
            string instruccion,
            bool soloTraducir,
            DependenciasControlBasico dependencias,
            CancellationToken cancellationToken = default)
    {
        PeticionBasica peticion =
            Interpretar(instruccion);

        return peticion.Tipo switch
        {
            TipoPeticionBasica.AbrirAplicacion =>
                await AbrirAplicacionAsync(
                    peticion.Objetivo,
                    soloTraducir,
                    dependencias,
                    cancellationToken),

            TipoPeticionBasica.ConsultarAplicacionesAbiertas =>
                await ConsultarAplicacionesAbiertasAsync(
                    soloTraducir,
                    dependencias,
                    cancellationToken),

            _ =>
                NoCompatible(
                    peticion.Motivo)
        };
    }

    internal static PeticionBasica Interpretar(
        string instruccion)
    {
        string texto = (instruccion ?? string.Empty).Trim();
        texto = texto.TrimStart('¿', '¡').TrimStart();

        if (texto.Length == 0)
        {
            return new PeticionBasica(
                TipoPeticionBasica.NoCompatible,
                Motivo: "No he recibido ninguna orden. "
                        + MensajeCapacidades);
        }

        string normalizada = Normalizar(texto);

        if (EsConsultaAplicacionesAbiertas(normalizada))
        {
            return new PeticionBasica(
                TipoPeticionBasica
                    .ConsultarAplicacionesAbiertas);
        }

        Match verbo = VerboAbrir.Match(texto);

        if (!verbo.Success
            || !PrefijoPermitido.IsMatch(
                texto[..verbo.Index]))
        {
            return new PeticionBasica(
                TipoPeticionBasica.NoCompatible,
                Motivo: MensajeCapacidades);
        }

        string objetivo = texto[(verbo.Index + verbo.Length)..]
            .Trim();
        objetivo = Regex.Replace(
                objetivo,
                @"^(?:(?:la|el|una|un)\s+)?(?:(?:aplicacion|aplicación|programa)\s+)?",
                string.Empty,
                RegexOptions.IgnoreCase
                | RegexOptions.CultureInvariant)
            .Trim();
        objetivo = Regex.Replace(
                objetivo,
                @"[\s,.]*(?:por favor)?[.!?]*$",
                string.Empty,
                RegexOptions.IgnoreCase
                | RegexOptions.CultureInvariant)
            .Trim();

        if (objetivo.Length == 0)
        {
            return new PeticionBasica(
                TipoPeticionBasica.NoCompatible,
                Motivo:
                    "Dime qué aplicación quieres abrir.");
        }

        string objetivoNormalizado =
            Normalizar(objetivo);

        if (Regex.IsMatch(
                objetivoNormalizado,
                @"(?:[,;]|\s+y\s+|\s+despues\s+|\s+luego\s+)",
                RegexOptions.CultureInvariant)
            || VerboAbrir.IsMatch(objetivo))
        {
            return new PeticionBasica(
                TipoPeticionBasica.NoCompatible,
                Motivo:
                    "En esta primera versión sólo puedo abrir una aplicación cada vez.");
        }

        return new PeticionBasica(
            TipoPeticionBasica.AbrirAplicacion,
            objetivo);
    }

    private static bool EsConsultaAplicacionesAbiertas(
        string texto)
    {
        bool mencionaApertura =
            Regex.IsMatch(
                texto,
                @"\b(?:abierto|abierta|abiertos|abiertas|ejecutando|en ejecucion)\b",
                RegexOptions.CultureInvariant);
        bool mencionaObjetos =
            Regex.IsMatch(
                texto,
                @"\b(?:aplicacion|aplicaciones|programa|programas|ventana|ventanas|cosas|procesos)\b",
                RegexOptions.CultureInvariant);
        bool preguntaPorLoPropio =
            Regex.IsMatch(
                texto,
                @"\b(?:que|cuales|dime|lista|muestra|tengo|hay)\b",
                RegexOptions.CultureInvariant);

        return mencionaApertura
               && (mencionaObjetos
                   || preguntaPorLoPropio)
               && preguntaPorLoPropio;
    }

    private static async Task<ResultadoControl>
        AbrirAplicacionAsync(
            string objetivo,
            bool soloTraducir,
            DependenciasControlBasico dependencias,
            CancellationToken cancellationToken)
    {
        IReadOnlyList<AplicacionInstalada> aplicaciones;

        try
        {
            aplicaciones =
                await dependencias.ObtenerAplicacionesAsync(
                    cancellationToken);
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException)
        {
            return Error(
                "inventario_no_disponible",
                "No he podido consultar las aplicaciones instaladas: "
                + ex.Message);
        }

        string busqueda =
            ResolverAlias(objetivo);
        AplicacionInstalada? aplicacion =
            InventarioAplicaciones
                .SeleccionarCandidatas(
                    busqueda,
                    aplicaciones)
                .FirstOrDefault();

        if (aplicacion is null)
        {
            return Error(
                "aplicacion_no_encontrada",
                $"No encuentro «{objetivo}» entre las aplicaciones instaladas.");
        }

        string comando =
            "Start-Process -FilePath 'explorer.exe' -ArgumentList "
            + EscaparLiteralPowerShell(
                "shell:AppsFolder\\"
                + aplicacion.AppId);

        if (soloTraducir)
        {
            return new ResultadoControl(
                false,
                "prueba_sin_ejecucion",
                $"Abriría {aplicacion.Nombre}, pero el modo de prueba no ejecuta comandos.",
                [
                    new ResultadoPasoControl(
                        1,
                        comando,
                        false,
                        0,
                        string.Empty,
                        string.Empty)
                ],
                false);
        }

        ResultadoEjecucionPowerShell lanzamiento =
            await dependencias.EjecutarAsync(
                comando,
                cancellationToken);
        var pasos = new List<ResultadoPasoControl>
        {
            CrearPaso(
                1,
                comando,
                lanzamiento)
        };

        if (!EsCorrecto(lanzamiento))
        {
            return Error(
                "error_al_abrir",
                $"Windows no ha podido abrir {aplicacion.Nombre}: "
                + ObtenerError(lanzamiento),
                pasos);
        }

        string mensaje = string.IsNullOrWhiteSpace(
            lanzamiento.Salida)
            ? $"He enviado a Windows la orden para abrir {aplicacion.Nombre}."
            : $"Windows ha respondido al intentar abrir {aplicacion.Nombre}:\n"
              + lanzamiento.Salida.Trim();

        return new ResultadoControl(
            true,
            "completado",
            mensaje,
            pasos,
            false);
    }

    private static async Task<ResultadoControl>
        ConsultarAplicacionesAbiertasAsync(
            bool soloTraducir,
            DependenciasControlBasico dependencias,
            CancellationToken cancellationToken)
    {
        const string comando =
            "Get-Process | "
            + "Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle } | "
            + "Sort-Object ProcessName,Id | "
            + "Select-Object -First 50 "
            + "@{Name='Proceso';Expression={$_.ProcessName}},"
            + "@{Name='Titulo';Expression={$_.MainWindowTitle}},Id | "
            + "ConvertTo-Json -Compress";

        if (soloTraducir)
        {
            return new ResultadoControl(
                false,
                "prueba_sin_ejecucion",
                "Consultaría las aplicaciones abiertas, pero el modo de prueba no ejecuta comandos.",
                [
                    new ResultadoPasoControl(
                        1,
                        comando,
                        false,
                        0,
                        string.Empty,
                        string.Empty)
                ],
                false);
        }

        ResultadoEjecucionPowerShell ejecucion =
            await dependencias.EjecutarAsync(
                comando,
                cancellationToken);
        ResultadoPasoControl paso =
            CrearPaso(
                1,
                comando,
                ejecucion);

        if (!EsCorrecto(ejecucion))
        {
            return Error(
                "consulta_fallida",
                "No he podido consultar las aplicaciones abiertas: "
                + ObtenerError(ejecucion),
                [paso]);
        }

        IReadOnlyList<ProcesoVentana> procesos =
            DeserializarProcesos(
                ejecucion.Salida);

        if (procesos.Count == 0)
        {
            return new ResultadoControl(
                true,
                "respuesta",
                "No hay ninguna aplicación con una ventana abierta.",
                [paso],
                false);
        }

        var mensaje = new StringBuilder();
        mensaje.AppendLine(
            procesos.Count == 1
                ? "Tienes una aplicación abierta:"
                : $"Tienes {procesos.Count} aplicaciones abiertas:");

        foreach (ProcesoVentana proceso in procesos)
        {
            mensaje.Append("- ");
            mensaje.Append(proceso.Proceso);

            if (!string.IsNullOrWhiteSpace(proceso.Titulo)
                && !proceso.Titulo.Equals(
                    proceso.Proceso,
                    StringComparison.OrdinalIgnoreCase))
            {
                mensaje.Append(" — ");
                mensaje.Append(proceso.Titulo);
            }

            mensaje.AppendLine();
        }

        return new ResultadoControl(
            true,
            "respuesta",
            mensaje.ToString().TrimEnd(),
            [paso],
            false);
    }

    private static string ResolverAlias(
        string objetivo)
    {
        string normalizado = Normalizar(objetivo);
        return AliasAplicaciones.TryGetValue(
            normalizado,
            out string? alias)
                ? alias
                : objetivo;
    }

    private static string EscaparLiteralPowerShell(
        string valor)
    {
        return "'"
               + valor.Replace(
                   "'",
                   "''",
                   StringComparison.Ordinal)
               + "'";
    }

    private static ResultadoPasoControl CrearPaso(
        int numero,
        string comando,
        ResultadoEjecucionPowerShell ejecucion)
    {
        return new ResultadoPasoControl(
            numero,
            comando,
            ejecucion.Ejecutado,
            ejecucion.CodigoSalida,
            ejecucion.Salida,
            ejecucion.Error);
    }

    private static bool EsCorrecto(
        ResultadoEjecucionPowerShell resultado)
    {
        return resultado.Ejecutado
               && resultado.CodigoSalida == 0
               && string.IsNullOrWhiteSpace(
                   resultado.Error);
    }

    private static string ObtenerError(
        ResultadoEjecucionPowerShell resultado)
    {
        return string.IsNullOrWhiteSpace(resultado.Error)
            ? $"código de salida {resultado.CodigoSalida}"
            : resultado.Error;
    }

    private static ResultadoControl NoCompatible(
        string motivo)
    {
        return Error(
            "no_disponible",
            string.IsNullOrWhiteSpace(motivo)
                ? MensajeCapacidades
                : motivo);
    }

    private static ResultadoControl Error(
        string estado,
        string mensaje,
        IReadOnlyList<ResultadoPasoControl>? pasos = null)
    {
        return new ResultadoControl(
            false,
            estado,
            mensaje,
            pasos ?? [],
            false);
    }

    private static IReadOnlyList<ProcesoVentana>
        DeserializarProcesos(
            string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using JsonDocument documento =
                JsonDocument.Parse(json);
            IEnumerable<JsonElement> elementos =
                documento.RootElement.ValueKind
                == JsonValueKind.Array
                    ? documento.RootElement
                        .EnumerateArray()
                    : [documento.RootElement];

            return elementos
                .Select(elemento =>
                    new ProcesoVentana(
                        ObtenerTexto(
                            elemento,
                            "Proceso"),
                        ObtenerTexto(
                            elemento,
                            "Titulo"),
                        elemento.TryGetProperty(
                            "Id",
                            out JsonElement id)
                            && id.TryGetInt32(
                                out int numero)
                                ? numero
                                : 0))
                .Where(proceso =>
                    proceso.Proceso.Length > 0)
                .DistinctBy(proceso =>
                    proceso.Proceso
                    + "\n"
                    + proceso.Titulo,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ObtenerTexto(
        JsonElement elemento,
        string propiedad)
    {
        return elemento.TryGetProperty(
                   propiedad,
                   out JsonElement valor)
               && valor.ValueKind
               == JsonValueKind.String
            ? (valor.GetString() ?? string.Empty)
                .Trim()
            : string.Empty;
    }

    private static string Normalizar(
        string texto)
    {
        string descompuesto = (texto ?? string.Empty)
            .Normalize(NormalizationForm.FormD);
        var resultado = new StringBuilder(
            descompuesto.Length);

        foreach (char caracter in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(
                    caracter)
                != UnicodeCategory.NonSpacingMark)
            {
                resultado.Append(
                    char.ToLowerInvariant(caracter));
            }
        }

        return resultado
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }

    private sealed record ProcesoVentana(
        string Proceso,
        string Titulo,
        int Id);
}
