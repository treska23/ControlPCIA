using System.Text;
using System.Text.Json;
using System.Net.Http;

namespace ControlPCIA;

internal sealed record PlanTareasControl(
    IReadOnlyList<string> Tareas)
{
    public string Formatear()
    {
        return string.Join(
            Environment.NewLine,
            Tareas.Select((tarea, indice) =>
                $"[ ] {indice + 1}. {tarea}"));
    }
}

internal sealed record RevisionTareasControl(
    bool Completa,
    IReadOnlyList<int> Pendientes,
    string Motivo);

/// <summary>
/// Llama descompone la petición y audita su propia ejecución. El programa no
/// contiene un catálogo de acciones ni sabe qué significa cada tarea.
/// </summary>
internal static class PlanificadorTareasIA
{
    public static async Task<PlanTareasControl> CrearAsync(
        string instruccion,
        IReadOnlyList<MensajeConversacionControl> contexto,
        CancellationToken cancellationToken)
    {
        try
        {
            var mensajes = new List<MensajeOllama>
            {
                new(
                    "system",
                    """
                    Descompón una petición para controlar Windows en todos los
                    resultados que el usuario espera. Una orden puede contener
                    una sola tarea o varias.

                    No propongas comandos ni métodos. Conserva los nombres de
                    aplicaciones y los datos pronunciados por el usuario. No
                    conviertas los pasos técnicos necesarios en tareas: sólo
                    enumera resultados observables solicitados.

                    Separa siempre resultados independientes unidos por "y",
                    "luego", "después" o equivalentes. Por ejemplo, "abre X y
                    realiza Y dentro de X" son dos tareas: "abrir X" y
                    "realizar Y dentro de X". No mantengas ambas dentro de una
                    sola cadena.

                    Responde exclusivamente con JSON válido:
                    {"tareas":["primera tarea","segunda tarea"]}

                    Usa entre 1 y 12 tareas. Si sólo se pide una cosa, devuelve
                    una sola. No des por supuesta ninguna tarea que el usuario
                    no haya pedido.
                    """)
            };

            mensajes.AddRange(
                contexto
                    .TakeLast(6)
                    .Select(mensaje =>
                        new MensajeOllama(
                            mensaje.Rol,
                            mensaje.Texto)));
            mensajes.Add(new MensajeOllama("user", instruccion.Trim()));

            string respuesta =
                await ClienteOllama.ConversarAsync(
                    mensajes,
                    cancellationToken);
            IReadOnlyList<string> tareas =
                ExtraerTareas(respuesta);

            return tareas.Count == 0
                ? CrearPlanMinimo(instruccion)
                : new PlanTareasControl(tareas);
        }
        catch (Exception ex) when (
            !cancellationToken.IsCancellationRequested
            && ex is HttpRequestException
                or JsonException
                or InvalidOperationException
                or TaskCanceledException)
        {
            return CrearPlanMinimo(instruccion);
        }
    }

    public static async Task<RevisionTareasControl> RevisarAsync(
        PlanTareasControl plan,
        IReadOnlyList<ResultadoPasoControl> pasos,
        CancellationToken cancellationToken,
        string? respuestaCandidata = null)
    {
        if (pasos.Count == 0)
        {
            return new RevisionTareasControl(
                false,
                Enumerable.Range(1, plan.Tareas.Count).ToArray(),
                "No se ha ejecutado ningún comando que demuestre las tareas.");
        }

        try
        {
            string tareas = string.Join(
                Environment.NewLine,
                plan.Tareas.Select((tarea, indice) =>
                    $"{indice + 1}. {tarea}"));
            string evidencias = FormatearEvidencias(pasos);
            string candidata = string.IsNullOrWhiteSpace(respuestaCandidata)
                ? "(no hay respuesta candidata; se está auditando FIN)"
                : respuestaCandidata.Trim();
            var mensajes = new List<MensajeOllama>
            {
                new(
                    "system",
                    """
                    Audita si una ejecución por consola completó TODAS las
                    tareas solicitadas. Basa la decisión sólo en los comandos,
                    códigos de salida, stdout y stderr reales proporcionados.

                    Un comando correcto puede completar una o varias tareas.
                    Un código distinto de cero o un comando bloqueado no
                    completa nada. Abrir una aplicación no demuestra una
                    segunda acción pedida dentro de ella. No aceptes una
                    afirmación sin un comando correspondiente.
                    Las acciones mediante SendKeys, AppActivate, atajos,
                    automatización de interfaz, ratón u OCR están prohibidas y
                    nunca cuentan como evidencia. Una tarea interna de una
                    aplicación sólo se completa mediante su CLI, cmdlets, API o
                    protocolo invocable íntegramente por consola y con una
                    salida verificable. Una repetición idéntica no aporta
                    evidencia.
                    Una lista de programas con ventana abierta sólo queda
                    demostrada por una consulta Get-Process que filtre
                    MainWindowTitle y por su salida real; enumerar todos los
                    procesos no demuestra esa tarea. Localizar un archivo exige
                    una búsqueda Get-ChildItem cuyo stdout contenga su FullName
                    o una salida vacía que demuestre que no se encontró. No
                    aceptes explicaciones del modelo como evidencia.
                    Si hay una RESPUESTA CANDIDATA, además de existir evidencia,
                    el texto debe contestar todas las tareas informativas. Si
                    omite una lista, una ruta o un resultado solicitado, marca
                    esa tarea pendiente aunque el comando correcto se ejecutara.

                    Responde exclusivamente con JSON válido:
                    {"completa":true,"pendientes":[],"motivo":"..."}
                    o:
                    {"completa":false,"pendientes":[2],"motivo":"..."}

                    Los números de pendientes se refieren a la lista de tareas.
                    """),
                new(
                    "user",
                    $"""
                    TAREAS:
                    {tareas}

                    EVIDENCIAS DE CONSOLA:
                    {evidencias}

                    RESPUESTA CANDIDATA:
                    {candidata}
                    """)
            };

            string respuesta =
                await ClienteOllama.ConversarAsync(
                    mensajes,
                    cancellationToken);

            return ExtraerRevision(
                    respuesta,
                    plan.Tareas.Count)
                ?? CrearRevisionConservadora(plan, pasos);
        }
        catch (Exception ex) when (
            !cancellationToken.IsCancellationRequested
            && ex is HttpRequestException
                or JsonException
                or InvalidOperationException
                or TaskCanceledException)
        {
            return CrearRevisionConservadora(plan, pasos);
        }
    }

    internal static IReadOnlyList<string> ExtraerTareas(string respuesta)
    {
        using JsonDocument? documento = ExtraerJson(respuesta);

        if (documento is null
            || !documento.RootElement.TryGetProperty(
                "tareas",
                out JsonElement tareas)
            || tareas.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return tareas
            .EnumerateArray()
            .Where(elemento => elemento.ValueKind == JsonValueKind.String)
            .Select(elemento => elemento.GetString()?.Trim() ?? string.Empty)
            .Where(tarea => tarea.Length is >= 1 and <= 240)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    internal static RevisionTareasControl? ExtraerRevision(
        string respuesta,
        int cantidadTareas)
    {
        using JsonDocument? documento = ExtraerJson(respuesta);

        if (documento is null
            || !documento.RootElement.TryGetProperty(
                "completa",
                out JsonElement completa)
            || completa.ValueKind is not (
                JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }

        var pendientes = new List<int>();

        if (documento.RootElement.TryGetProperty(
                "pendientes",
                out JsonElement lista)
            && lista.ValueKind == JsonValueKind.Array)
        {
            pendientes.AddRange(
                lista
                    .EnumerateArray()
                    .Where(elemento =>
                        elemento.TryGetInt32(out int numero)
                        && numero >= 1
                        && numero <= cantidadTareas)
                    .Select(elemento => elemento.GetInt32())
                    .Distinct()
                    .Order());
        }

        string motivo =
            documento.RootElement.TryGetProperty(
                "motivo",
                out JsonElement texto)
            && texto.ValueKind == JsonValueKind.String
                ? texto.GetString()?.Trim() ?? string.Empty
                : string.Empty;
        bool finalizada = completa.GetBoolean();

        if (!finalizada && pendientes.Count == 0)
        {
            pendientes.AddRange(
                Enumerable.Range(1, cantidadTareas));
        }

        return new RevisionTareasControl(
            finalizada && pendientes.Count == 0,
            pendientes,
            motivo);
    }

    internal static bool EsPeticionInformativa(string instruccion)
    {
        string normalizada =
            InventarioTexto.Normalizar(instruccion);
        string[] comienzos =
        [
            "que ", "cual ", "cuales ", "como ", "cuando ", "donde ",
            "por que ", "dime ", "explica ", "explicame ", "informame ",
            "lista ",
            "what ", "which ", "how ", "when ", "where ", "why "
        ];

        return instruccion.Contains('?')
               || comienzos.Any(comienzo =>
                   normalizada.StartsWith(
                       comienzo,
                       StringComparison.Ordinal));
    }

    internal static bool PareceAclaracionPendiente(string respuesta)
    {
        string normalizada =
            InventarioTexto.Normalizar(respuesta);

        return respuesta.Contains('?')
               || normalizada.Contains(
                   "no se encontro",
                   StringComparison.Ordinal)
               || normalizada.Contains(
                   "no he podido",
                   StringComparison.Ordinal)
               || normalizada.Contains(
                   "necesito que",
                   StringComparison.Ordinal)
               || normalizada.Contains(
                   "puedes confirmar",
                   StringComparison.Ordinal)
               || normalizada.Contains(
                   "proporciona",
                   StringComparison.Ordinal)
               || normalizada.Contains(
                   "indica cual",
                   StringComparison.Ordinal);
    }

    private static PlanTareasControl CrearPlanMinimo(string instruccion)
    {
        return new PlanTareasControl([instruccion.Trim()]);
    }

    private static RevisionTareasControl CrearRevisionConservadora(
        PlanTareasControl plan,
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        int exitos = pasos.Count(paso =>
            paso.Ejecutado && paso.CodigoSalida == 0);
        bool hayFalloPosterior =
            pasos.LastOrDefault() is { } ultimo
            && (!ultimo.Ejecutado || ultimo.CodigoSalida != 0);
        bool completa =
            !hayFalloPosterior
            && exitos >= plan.Tareas.Count;

        return new RevisionTareasControl(
            completa,
            completa
                ? []
                : Enumerable.Range(
                    Math.Min(exitos + 1, plan.Tareas.Count),
                    Math.Max(1, plan.Tareas.Count - exitos))
                  .Where(numero => numero <= plan.Tareas.Count)
                  .ToArray(),
            completa
                ? "Hay al menos una ejecución correcta por tarea."
                : "No hay evidencia suficiente para todas las tareas.");
    }

    private static string FormatearEvidencias(
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        var resultado = new StringBuilder();

        foreach (ResultadoPasoControl paso in pasos)
        {
            resultado.AppendLine($"PASO {paso.Numero}");
            resultado.AppendLine("COMANDO: " + Limitar(paso.Comando, 1200));
            resultado.AppendLine(
                "EJECUTADO: " + paso.Ejecutado.ToString().ToLowerInvariant());
            resultado.AppendLine("CÓDIGO: " + paso.CodigoSalida);
            resultado.AppendLine("SALIDA: " + Limitar(paso.Salida, 2000));
            resultado.AppendLine("ERROR: " + Limitar(paso.Error, 1200));
        }

        return resultado.ToString();
    }

    private static JsonDocument? ExtraerJson(string respuesta)
    {
        int inicio = respuesta.IndexOf('{');
        int fin = respuesta.LastIndexOf('}');

        if (inicio < 0 || fin <= inicio)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(
                respuesta[inicio..(fin + 1)]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Limitar(string texto, int maximo)
    {
        string limpia = texto.Trim();
        return limpia.Length <= maximo
            ? limpia
            : limpia[..maximo] + "…";
    }
}

internal static class InventarioTexto
{
    public static string Normalizar(string texto)
    {
        string descompuesto = texto
            .Trim()
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);
        var resultado = new StringBuilder(descompuesto.Length);

        foreach (char caracter in descompuesto)
        {
            if (System.Globalization.CharUnicodeInfo
                    .GetUnicodeCategory(caracter)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                resultado.Append(caracter);
            }
        }

        return resultado
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }
}
