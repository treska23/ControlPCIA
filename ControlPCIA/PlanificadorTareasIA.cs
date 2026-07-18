using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace ControlPCIA;

internal sealed record PlanTareasControl(
    IReadOnlyList<string> Tareas,
    IReadOnlyList<string>? Conocimientos = null,
    string? Pregunta = null)
{
    public IReadOnlyList<string> ConocimientosSeleccionados =>
        Conocimientos ?? [];

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

internal sealed record PreparacionSolicitudControl(
    bool Lista,
    string? Pregunta);

/// <summary>
/// Llama descompone la petición y audita su propia ejecución. El programa no
/// contiene un catálogo de acciones ni sabe qué significa cada tarea.
/// </summary>
internal static class PlanificadorTareasIA
{
    public static async Task<PreparacionSolicitudControl> PrepararAsync(
        string instruccion,
        IReadOnlyList<MensajeConversacionControl> contexto,
        CancellationToken cancellationToken)
    {
        if (EsPeticionInformativa(instruccion))
        {
            return new PreparacionSolicitudControl(true, null);
        }

        try
        {
            var mensajes = new List<MensajeOllama>
            {
                new(
                    "system",
                    """
                    Antes de controlar un PC, decide si falta un dato que sólo
                    puede elegir el usuario. Tú haces esta decisión: el programa
                    no contiene un catálogo de aplicaciones ni acciones.

                    Investigar comandos, aplicaciones instaladas, plantillas,
                    rutas predeterminadas y estado de Windows corresponde a la
                    IA y NO justifica preguntar al usuario. Usa un valor normal,
                    reversible y predecible cuando exista; por ejemplo, una
                    carpeta de documentos para contenido nuevo.

                    Si Windows proporciona una carpeta Documentos existente,
                    úsala como ubicación predeterminada para proyectos o
                    documentos nuevos cuando el usuario no indique otra. No
                    preguntes dónde guardar en ese caso. La aplicación puede
                    crear dentro una subcarpeta con el nombre del contenido.

                    Pregunta únicamente por una decisión personal imprescindible
                    que no aparezca ni en la petición ni en la conversación. Por
                    ejemplo, si se pide crear un proyecto nuevo sin darle nombre,
                    pregunta qué nombre quiere. Si ya dijo «llamado Demo», no
                    preguntes otra vez. No abras una aplicación antes de obtener
                    el dato imprescindible.

                    Si el mensaje actual responde a una pregunta anterior, usa
                    esa respuesta junto con la petición previa y marca la
                    solicitud como lista.

                    Responde exclusivamente con uno de estos JSON:
                    {"lista":true,"pregunta":null}
                    {"lista":false,"pregunta":"Una sola pregunta breve"}
                    """)
            };

            mensajes.AddRange(
                contexto
                    .TakeLast(8)
                    .Select(mensaje =>
                        new MensajeOllama(
                            mensaje.Rol,
                            mensaje.Texto)));
            string documentos = Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments);
            string escritorio = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);
            mensajes.Add(
                new MensajeOllama(
                    "user",
                    $"""
                    PETICIÓN ACTUAL:
                    {instruccion.Trim()}

                    CARPETAS PREDETERMINADAS REALES DE WINDOWS:
                    Documentos: {documentos}
                    Escritorio: {escritorio}
                    """));

            string respuesta =
                await ClienteOllama.ConversarAsync(
                    mensajes,
                    cancellationToken);

            return ExtraerPreparacion(respuesta)
                   ?? new PreparacionSolicitudControl(true, null);
        }
        catch (Exception ex) when (
            !cancellationToken.IsCancellationRequested
            && ex is HttpRequestException
                or JsonException
                or InvalidOperationException
                or TaskCanceledException)
        {
            return new PreparacionSolicitudControl(true, null);
        }
    }

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

                    Si el mensaje actual responde a una pregunta anterior,
                    reconstruye la petición original usando la conversación e
                    incorpora la respuesta. No planifiques sólo la palabra o
                    frase de la respuesta actual.

                    «Crea un proyecto nuevo en Cubase» es UNA tarea observable,
                    no cinco tareas para abrir, elegir, nombrar, ubicar y
                    confirmar. «Crea un proyecto nuevo en Cubase llamado Demo»
                    también es una sola tarea y debe conservar el nombre Demo.

                    Separa siempre resultados independientes unidos por "y",
                    "luego", "después" o equivalentes. Por ejemplo, "abre X y
                    realiza Y dentro de X" son dos tareas: "abrir X" y
                    "realizar Y dentro de X". No mantengas ambas dentro de una
                    sola cadena.

                    Además, selecciona las recetas de conocimiento que puedan
                    resolver la petición sin investigar. Las recetas disponibles
                    son:
                    - ventanas.estado: activar, traer al frente, maximizar,
                      minimizar, restaurar, mover o redimensionar ventanas.
                    - aplicaciones.abrir: encontrar e iniciar aplicaciones.
                    - aplicaciones.inventario: consultar programas con ventana.
                    - archivos.buscar: localizar archivos o carpetas.
                    - archivos.abrir: abrir un archivo con su aplicación.

                    Relaciona la intención por significado, no por coincidencia
                    literal. Por ejemplo, «pon Edge delante», «tráeme el
                    navegador» y «activa la ventana» seleccionan
                    `ventanas.estado`.

                    En la misma respuesta decide si falta una elección personal
                    imprescindible que no figure en la petición ni en el
                    contexto. Investigar Windows o adaptar una receta nunca
                    justifica preguntar. Si se pide crear un proyecto sin
                    nombre, pregunta el nombre; si ya lo dio, no preguntes.

                    Responde exclusivamente con JSON válido:
                    {"tareas":["primera tarea","segunda tarea"],"conocimientos":["ventanas.estado"],"pregunta":null}
                    o, si falta una decisión personal:
                    {"tareas":["crear un proyecto"],"conocimientos":[],"pregunta":"¿Qué nombre quieres ponerle?"}

                    Usa entre 1 y 24 tareas. Si sólo se pide una cosa, devuelve
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
            IReadOnlyList<string> conocimientos =
                ExtraerConocimientos(respuesta);
            string? pregunta =
                ExtraerPreguntaPlan(respuesta);

            return tareas.Count == 0
                ? CrearPlanMinimo(instruccion)
                : new PlanTareasControl(
                    ConservarNombreLiteralExacto(
                        instruccion,
                        tareas),
                    conocimientos,
                    pregunta);
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

    internal static IReadOnlyList<string>
        ConservarNombreLiteralExacto(
            string instruccion,
            IReadOnlyList<string> tareas)
    {
        string? nombre = ExtraerNombreLiteralSolicitado(
            instruccion);

        if (string.IsNullOrWhiteSpace(nombre)
            || tareas.Any(tarea =>
                tarea.Contains(
                    nombre,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return tareas;
        }

        string[] resultado = tareas.ToArray();
        int indice = Array.FindIndex(
            resultado,
            tarea => Regex.IsMatch(
                InventarioTexto.Normalizar(tarea),
                @"\b(?:crea|crear|genera|generar|nuevo|nueva)\b",
                RegexOptions.CultureInvariant));

        if (indice < 0)
        {
            indice = 0;
        }

        resultado[indice] =
            resultado[indice].TrimEnd()
            + $" con el nombre literal exacto «{nombre}»";

        return resultado;
    }

    internal static string? ExtraerNombreLiteralSolicitado(
        string instruccion)
    {
        if (string.IsNullOrWhiteSpace(instruccion))
        {
            return null;
        }

        Match coincidencia = Regex.Match(
            instruccion,
            """
            \b(?:llamad[oa]|(?:con\s+el\s+)?nombre(?:\s+de)?|n[oó]mbral[oa]?\s+como|como)\s+
            ["“«']?(?<nombre>.+?)["”»']?
            (?=\s+(?:y\s+)?(?:luego|despu[eé]s)\b|[,.!?;]|$)
            """,
            RegexOptions.IgnoreCase
            | RegexOptions.CultureInvariant
            | RegexOptions.IgnorePatternWhitespace);

        if (!coincidencia.Success)
        {
            return null;
        }

        string nombre = coincidencia.Groups["nombre"].Value
            .Trim()
            .Trim('"', '\'', '“', '”', '«', '»');

        return nombre.Length is >= 1 and <= 180
            ? nombre
            : null;
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
                    Una apertura sí queda demostrada cuando un comando de inicio
                    correcto va seguido por `Get-Process -Name` y stdout contiene
                    el `PROCESS_NAME` concreto; no exijas que la aplicación haya
                    terminado toda su pantalla de carga.
                    Las acciones mediante SendKeys, atajos, automatización de
                    controles internos, ratón u OCR están prohibidas y nunca
                    cuentan como evidencia. Activar, traer al frente, maximizar,
                    restaurar, minimizar, mover o redimensionar una ventana
                    superior mediante AppActivate o APIs Win32 invocables desde
                    consola sí está permitido y cuenta como evidencia cuando la
                    salida comprueba el estado o la ventana objetivo. Una tarea interna de una
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
            .Take(24)
            .ToArray();
    }

    internal static IReadOnlyList<string> ExtraerConocimientos(
        string respuesta)
    {
        using JsonDocument? documento = ExtraerJson(respuesta);

        if (documento is null
            || !documento.RootElement.TryGetProperty(
                "conocimientos",
                out JsonElement conocimientos)
            || conocimientos.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        string[] disponibles =
        [
            "ventanas.estado",
            "aplicaciones.abrir",
            "aplicaciones.inventario",
            "archivos.buscar",
            "archivos.abrir"
        ];

        return conocimientos
            .EnumerateArray()
            .Where(elemento =>
                elemento.ValueKind == JsonValueKind.String)
            .Select(elemento =>
                elemento.GetString()?.Trim().ToLowerInvariant()
                ?? string.Empty)
            .Where(conocimiento =>
                disponibles.Contains(
                    conocimiento,
                    StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    internal static string? ExtraerPreguntaPlan(string respuesta)
    {
        using JsonDocument? documento = ExtraerJson(respuesta);

        if (documento is null
            || !documento.RootElement.TryGetProperty(
                "pregunta",
                out JsonElement pregunta)
            || pregunta.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string texto = pregunta.GetString()?.Trim()
            ?? string.Empty;

        return texto.Length switch
        {
            0 => null,
            <= 300 => texto,
            _ => texto[..300].Trim()
        };
    }

    internal static PreparacionSolicitudControl? ExtraerPreparacion(
        string respuesta)
    {
        using JsonDocument? documento = ExtraerJson(respuesta);

        if (documento is null
            || !documento.RootElement.TryGetProperty(
                "lista",
                out JsonElement lista)
            || lista.ValueKind is not (
                JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }

        if (lista.GetBoolean())
        {
            return new PreparacionSolicitudControl(true, null);
        }

        string pregunta =
            documento.RootElement.TryGetProperty(
                "pregunta",
                out JsonElement texto)
            && texto.ValueKind == JsonValueKind.String
                ? texto.GetString()?.Trim() ?? string.Empty
                : string.Empty;

        if (pregunta.Length == 0)
        {
            return null;
        }

        return new PreparacionSolicitudControl(
            false,
            pregunta.Length <= 300
                ? pregunta
                : pregunta[..300].Trim());
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
                       StringComparison.Ordinal))
               || Regex.IsMatch(
                   normalizada,
                   @"\b(?:dime|informame|explicame)\s+(?:que|cual|cuales|como|cuando|donde|por que|si)\b",
                   RegexOptions.CultureInvariant);
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
