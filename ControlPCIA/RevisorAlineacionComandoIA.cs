using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ControlPCIA;

internal sealed record RevisionAlineacionComando(
    bool Alineado,
    string Motivo);

/// <summary>
/// Comprueba que una propuesta del agente general corresponda a una tarea
/// pendiente. No es una política de capacidades: evita ejecutar traducciones
/// equivocadas sobre objetivos que el usuario no ha mencionado.
/// </summary>
internal static class RevisorAlineacionComandoIA
{
    internal static async Task<RevisionAlineacionComando> RevisarAsync(
        PlanTareasControl plan,
        IReadOnlySet<int> tareasPendientes,
        string comando,
        IReadOnlyList<ResultadoPasoControl> pasos,
        CancellationToken cancellationToken)
    {
        RevisionAlineacionComando comprobacionLocal =
            ValidarContradiccionesEvidentes(
                plan,
                tareasPendientes,
                comando);

        if (!comprobacionLocal.Alineado)
        {
            return comprobacionLocal;
        }

        string tareas = string.Join(
            Environment.NewLine,
            tareasPendientes
                .Where(numero =>
                    numero >= 1
                    && numero <= plan.Tareas.Count)
                .Order()
                .Select(numero =>
                    $"{numero}. {plan.Tareas[numero - 1]}"));
        string evidencia = FormatearEvidencia(pasos);
        var mensajes = new List<MensajeOllama>
        {
            new(
                "system",
                """
                Audita la correspondencia entre una petición y UN comando
                PowerShell antes de ejecutarlo. No propongas comandos y no
                evalúes la política de seguridad.

                Marca alineado=true sólo si el efecto o la consulta del comando
                avanza directamente al menos una tarea pendiente y no actúa
                sobre otra aplicación, ventana, archivo, configuración o
                destino ajeno. Los pasos técnicos necesarios para investigar o
                comprobar una tarea sí están alineados. Si el comando cierra,
                abre, cambia o inicia algo que la tarea no pide ni necesita,
                marca false. Ante una relación dudosa, marca false.

                Responde exclusivamente:
                {"alineado":true,"motivo":"frase breve"}
                o
                {"alineado":false,"motivo":"desajuste concreto"}
                """),
            new(
                "user",
                $"""
                TAREAS PENDIENTES:
                {tareas}

                COMANDO PROPUESTO:
                {comando}

                EVIDENCIA PREVIA DE CONSOLA:
                {evidencia}
                """)
        };

        try
        {
            string respuesta =
                await ClienteOllama.ConversarAsync(
                    mensajes,
                    cancellationToken);

            return ExtraerRevision(respuesta)
                   ?? new RevisionAlineacionComando(
                       false,
                       "La IA revisora no devolvió una decisión estructurada.");
        }
        catch (Exception ex) when (
            !cancellationToken.IsCancellationRequested
            && ex is HttpRequestException
                or JsonException
                or InvalidOperationException
                or TaskCanceledException)
        {
            return new RevisionAlineacionComando(
                false,
                "No se pudo comprobar la correspondencia del comando: "
                + ex.Message);
        }
    }

    internal static RevisionAlineacionComando
        ValidarContradiccionesEvidentes(
            PlanTareasControl plan,
            IReadOnlySet<int> tareasPendientes,
            string comando)
    {
        string tareas = string.Join(
            " ",
            tareasPendientes
                .Where(numero =>
                    numero >= 1
                    && numero <= plan.Tareas.Count)
                .Select(numero =>
                    plan.Tareas[numero - 1]));
        string normalizadas = InventarioTexto.Normalizar(tareas);
        bool cierra = Regex.IsMatch(
            comando,
            @"\b(?:Stop-Process|taskkill|CloseMainWindow|Kill)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        bool pideCerrar = Regex.IsMatch(
            normalizadas,
            @"\b(?:cierra|cerrar|deten|detener|termina|terminar|mata|matar|salir|apaga|apagar)\w*\b",
            RegexOptions.CultureInvariant);

        if (cierra && !pideCerrar)
        {
            return new RevisionAlineacionComando(
                false,
                "El comando cerraría o detendría un proceso, pero ninguna tarea pendiente lo pide.");
        }

        bool inicia = Regex.IsMatch(
            comando,
            @"\b(?:Start-Process|explorer(?:\.exe)?\s+['""]shell:AppsFolder\\)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        bool pideIniciar = Regex.IsMatch(
            normalizadas,
            @"\b(?:abre|abrir|inicia|iniciar|lanza|lanzar|ejecuta|ejecutar|crea|crear|instala|instalar|descarga|descargar|navega|buscar.*web|reproduce|reproducir|pon.*video|muestra|mostrar)\w*\b",
            RegexOptions.CultureInvariant);

        if (inicia && !pideIniciar)
        {
            return new RevisionAlineacionComando(
                false,
                "El comando iniciaría o abriría algo, pero ninguna tarea pendiente lo pide o lo necesita.");
        }

        bool abreInformacionSistema = Regex.IsMatch(
            comando,
            @"\b(?:msinfo32|SystemProperties\w*|control(?:\.exe)?\s+sysdm\.cpl)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        bool pideInformacionSistema = Regex.IsMatch(
            normalizadas,
            @"\b(?:informacion.*sistema|propiedades.*(?:pc|equipo|sistema)|msinfo)\w*\b",
            RegexOptions.CultureInvariant);

        if (abreInformacionSistema && !pideInformacionSistema)
        {
            return new RevisionAlineacionComando(
                false,
                "El comando abriría información o propiedades del sistema sin que la petición lo solicite.");
        }

        return new RevisionAlineacionComando(
            true,
            "No se detectó una contradicción evidente.");
    }

    internal static RevisionAlineacionComando? ExtraerRevision(
        string respuesta)
    {
        int inicio = respuesta.IndexOf('{');
        int fin = respuesta.LastIndexOf('}');

        if (inicio < 0 || fin <= inicio)
        {
            return null;
        }

        try
        {
            using JsonDocument documento =
                JsonDocument.Parse(respuesta[inicio..(fin + 1)]);
            JsonElement raiz = documento.RootElement;

            if (!raiz.TryGetProperty(
                    "alineado",
                    out JsonElement alineado)
                || alineado.ValueKind is not (
                    JsonValueKind.True or JsonValueKind.False))
            {
                return null;
            }

            string motivo =
                raiz.TryGetProperty(
                    "motivo",
                    out JsonElement texto)
                && texto.ValueKind == JsonValueKind.String
                    ? texto.GetString()?.Trim() ?? string.Empty
                    : string.Empty;

            if (motivo.Length == 0)
            {
                motivo = alineado.GetBoolean()
                    ? "El comando corresponde a una tarea pendiente."
                    : "El comando no corresponde a una tarea pendiente.";
            }

            return new RevisionAlineacionComando(
                alineado.GetBoolean(),
                motivo.Length <= 500
                    ? motivo
                    : motivo[..500].Trim());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FormatearEvidencia(
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        if (pasos.Count == 0)
        {
            return "(ninguna)";
        }

        var resultado = new StringBuilder();

        foreach (ResultadoPasoControl paso in pasos.TakeLast(4))
        {
            resultado.AppendLine("COMANDO=" + Limitar(paso.Comando, 800));
            resultado.AppendLine("CODIGO=" + paso.CodigoSalida);
            resultado.AppendLine("STDOUT=" + Limitar(paso.Salida, 1200));
            resultado.AppendLine("STDERR=" + Limitar(paso.Error, 600));
        }

        return resultado.ToString().Trim();
    }

    private static string Limitar(string texto, int maximo)
    {
        string limpio = texto.Trim();
        return limpio.Length <= maximo
            ? limpio
            : limpio[..maximo] + "…";
    }
}
