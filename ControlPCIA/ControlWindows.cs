using System.Management.Automation.Language;
using System.Text;
using System.Text.RegularExpressions;

namespace ControlPCIA;

internal sealed record DependenciasControlWindows(
    Func<
        IReadOnlyList<MensajeOllama>,
        CancellationToken,
        Task<string>> ConversarAsync,
    Func<
        string,
        CancellationToken,
        Task<ResultadoEjecucionPowerShell>> EjecutarAsync,
    Func<
        string,
        CancellationToken,
        Task<IReadOnlyList<RecetaReferencia>>> BuscarAprendidoAsync,
    Func<
        string,
        IReadOnlyList<string>,
        CancellationToken,
        Task<bool>> AprenderAsync);

internal static class ControlWindows
{
    private const int MaximoPasos = 8;
    private const int MaximoCaracteresContexto = 8_000;
    private const int MaximoMensajesContexto = 12;

    internal const string InstruccionSistema =
        """
        Traduce lo que diga el usuario a comandos de consola PowerShell para
        Windows. Devuelve únicamente el comando.

        Si no sabes qué comando corresponde, búscalo primero en la lista de
        comandos aprendidos que recibas, investígalo mediante la propia consola
        PowerShell o búscalo en Internet mediante comandos de consola.

        Sólo hay tres restricciones:
        1. No eliminar elementos ni contenido.
        2. No mover ni cortar elementos.
        3. No formatear, limpiar ni reinicializar discos o unidades.
        """;

    public static Task<ResultadoControl> ControlarAsync(
        string instruccion,
        Action<EventoControl>? informar = null,
        IReadOnlyList<MensajeConversacionControl>? contextoConversacion = null,
        CancellationToken cancellationToken = default,
        bool soloTraducir = false)
    {
        var dependencias = new DependenciasControlWindows(
            ClienteOllama.ConversarAsync,
            static (comando, cancelacion) =>
                EjecutorPowerShell.EjecutarAsync(
                    comando,
                    cancelacion),
            static (peticion, cancelacion) =>
                MemoriaRecetas.Predeterminada.BuscarAsync(
                    peticion,
                    cancellationToken: cancelacion),
            static (peticion, comandos, cancelacion) =>
                MemoriaRecetas.Predeterminada.AprenderAsync(
                    peticion,
                    comandos,
                    cancelacion));

        return ControlarConDependenciasAsync(
            instruccion,
            informar,
            contextoConversacion,
            cancellationToken,
            soloTraducir,
            dependencias);
    }

    internal static async Task<ResultadoControl>
        ControlarConDependenciasAsync(
            string instruccion,
            Action<EventoControl>? informar,
            IReadOnlyList<MensajeConversacionControl>? contextoConversacion,
            CancellationToken cancellationToken,
            bool soloTraducir,
            DependenciasControlWindows dependencias)
    {
        if (string.IsNullOrWhiteSpace(instruccion))
        {
            return Finalizar(
                false,
                "orden_vacia",
                "No se ha recibido ninguna orden.",
                [],
                false,
                informar);
        }

        string peticion = instruccion.Trim();

        if (peticion.Length > 1_000)
        {
            return Finalizar(
                false,
                "orden_demasiado_larga",
                "La orden supera los 1000 caracteres.",
                [],
                false,
                informar);
        }

        Informar(
            informar,
            new EventoControl(
                "pensando",
                "Llama está traduciendo la petición a PowerShell."));

        IReadOnlyList<RecetaReferencia> aprendidos =
            await BuscarAprendidosAsync(
                peticion,
                dependencias,
                informar,
                cancellationToken);
        var mensajes = new List<MensajeOllama>
        {
            new("system", InstruccionSistema)
        };

        mensajes.AddRange(
            NormalizarContexto(contextoConversacion)
                .Select(mensaje =>
                    new MensajeOllama(
                        mensaje.Rol,
                        mensaje.Texto)));
        mensajes.Add(
            new MensajeOllama(
                "user",
                CrearMensajePeticion(
                    peticion,
                    aprendidos)));

        var pasos = new List<ResultadoPasoControl>();
        var propuestas = new HashSet<string>(
            StringComparer.Ordinal);
        bool pideAccion = EsPeticionDeAccion(peticion);
        bool accionEjecutada = false;

        for (int intento = 1; intento <= MaximoPasos; intento++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Informar(
                informar,
                new EventoControl(
                    "pensando",
                    intento == 1
                        ? "Llama está preparando el comando."
                        : "Llama está usando el resultado real de PowerShell."));

            string respuesta;

            try
            {
                respuesta = LimpiarRespuestaModelo(
                    await dependencias.ConversarAsync(
                        mensajes,
                        cancellationToken));
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Finalizar(
                    false,
                    "error_ia",
                    "No se pudo obtener el comando de Llama: " + ex.Message,
                    pasos,
                    false,
                    informar);
            }

            if (TryExtraerPregunta(respuesta, out string pregunta))
            {
                return Finalizar(
                    false,
                    "requiere_aclaracion",
                    pregunta,
                    pasos,
                    false,
                    informar);
            }

            if (string.IsNullOrWhiteSpace(respuesta))
            {
                return Finalizar(
                    false,
                    "respuesta_invalida",
                    "Llama no devolvió ningún comando.",
                    pasos,
                    false,
                    informar);
            }

            if (!propuestas.Add(respuesta))
            {
                return Finalizar(
                    false,
                    "sin_progreso",
                    "Llama repitió el mismo comando sin resolver la petición.",
                    pasos,
                    false,
                    informar);
            }

            ResultadoValidacionPowerShell validacion =
                ValidadorPowerShell.Validar(respuesta);

            if (!validacion.Permitido)
            {
                var pasoBloqueado = new ResultadoPasoControl(
                    pasos.Count + 1,
                    respuesta,
                    false,
                    -1,
                    string.Empty,
                    "BLOQUEADO: " + validacion.Motivo);
                pasos.Add(pasoBloqueado);
                Informar(
                    informar,
                    new EventoControl(
                        "bloqueado",
                        pasoBloqueado.Error,
                        respuesta,
                        pasoBloqueado));

                return Finalizar(
                    false,
                    "comando_rechazado",
                    pasoBloqueado.Error,
                    pasos,
                    false,
                    informar);
            }

            if (soloTraducir)
            {
                var pasoPropuesto = new ResultadoPasoControl(
                    1,
                    respuesta,
                    false,
                    0,
                    string.Empty,
                    string.Empty);

                return Finalizar(
                    false,
                    "prueba_sin_ejecucion",
                    "Modo de prueba seguro: el comando ha sido validado, pero no se ha ejecutado.",
                    [pasoPropuesto],
                    false,
                    informar);
            }

            Informar(
                informar,
                new EventoControl(
                    "comando",
                    "El programa va a ejecutar el comando validado.",
                    respuesta));

            ResultadoEjecucionPowerShell ejecucion;

            try
            {
                ejecucion =
                    await dependencias.EjecutarAsync(
                        respuesta,
                        cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ejecucion = new ResultadoEjecucionPowerShell(
                    false,
                    -1,
                    string.Empty,
                    ex.Message);
            }

            var paso = new ResultadoPasoControl(
                pasos.Count + 1,
                respuesta,
                ejecucion.Ejecutado,
                ejecucion.CodigoSalida,
                ejecucion.Salida,
                ejecucion.Error);
            pasos.Add(paso);
            Informar(
                informar,
                new EventoControl(
                    ejecucion.Ejecutado
                    && ejecucion.CodigoSalida == 0
                    && string.IsNullOrWhiteSpace(ejecucion.Error)
                        ? "resultado"
                        : "error",
                    CrearMensajeResultadoBreve(ejecucion),
                    respuesta,
                    paso));

            if (TryExtraerPregunta(ejecucion.Salida, out pregunta))
            {
                return Finalizar(
                    false,
                    "requiere_aclaracion",
                    pregunta,
                    pasos,
                    false,
                    informar);
            }

            bool correcto =
                ejecucion.Ejecutado
                && ejecucion.CodigoSalida == 0
                && string.IsNullOrWhiteSpace(ejecucion.Error);
            bool consulta = EsComandoDeConsulta(respuesta);
            accionEjecutada |= correcto && !consulta;
            bool necesitaOtroPaso =
                !correcto
                || consulta && pideAccion && !accionEjecutada
                || consulta
                   && pideAccion
                   && accionEjecutada
                   && RequiereVerificacionTrasCambio(pasos)
                || !consulta
                   && string.IsNullOrWhiteSpace(ejecucion.Salida);

            if (!necesitaOtroPaso)
            {
                bool aprendido =
                    await AprenderAsync(
                        peticion,
                        pasos,
                        dependencias,
                        informar,
                        cancellationToken);
                string mensaje = string.IsNullOrWhiteSpace(ejecucion.Salida)
                    ? "La consulta no devolvió resultados."
                    : ejecucion.Salida;

                return Finalizar(
                    true,
                    consulta ? "respuesta" : "completado",
                    mensaje,
                    pasos,
                    aprendido,
                    informar);
            }

            if (intento == MaximoPasos)
            {
                string mensajeFinal = correcto
                    ? "PowerShell terminó sin error, pero no devolvió evidencia suficiente del resultado."
                    : CrearMensajeErrorFinal(ejecucion);

                return Finalizar(
                    false,
                    correcto ? "sin_evidencia" : "error_powershell",
                    mensajeFinal,
                    pasos,
                    false,
                    informar);
            }

            mensajes.Add(new MensajeOllama("assistant", respuesta));
            mensajes.Add(
                new MensajeOllama(
                    "user",
                    CrearMensajeResultadoParaLlama(
                        peticion,
                        ejecucion)));
        }

        return Finalizar(
            false,
            "limite_pasos",
            "No se pudo resolver la petición dentro del límite de pasos.",
            pasos,
            false,
            informar);
    }

    internal static bool EsComandoDeConsulta(string comando)
    {
        ScriptBlockAst ast = Parser.ParseInput(
            comando,
            out _,
            out ParseError[] errores);

        if (errores.Length > 0
            || ast.FindAll(
                    nodo => nodo is InvokeMemberExpressionAst,
                    searchNestedScriptBlocks: true)
                .Any())
        {
            return false;
        }

        CommandAst[] comandos = ast
            .FindAll(
                nodo => nodo is CommandAst,
                searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .ToArray();

        if (comandos.Length == 0)
        {
            return !ast
                .FindAll(
                    nodo => nodo is AssignmentStatementAst,
                    searchNestedScriptBlocks: true)
                .Any();
        }

        return comandos.All(comandoAst =>
            EsNombreComandoDeConsulta(
                comandoAst.GetCommandName(),
                comandoAst.Extent.Text));
    }

    internal static bool EsPeticionDeAccion(string peticion)
    {
        string normalizada = MemoriaRecetas.Normalizar(peticion);

        return Regex.IsMatch(
            normalizada,
            @"\b(?:abre(?:lo|la|los|las)?|abrir|cierra|cerrar|inicia|iniciar|lanza|lanzar|ejecuta|ejecutar|crea|crear|cambia|cambiar|configura|configurar|maximiza|maximizar|minimiza|minimizar|mueve|mover|coloca|colocar|reproduce|reproducir|pon|poner|instala|instalar|descarga|descargar|guarda|guardar|activa|activar|desactiva|desactivar|apaga|apagar|reinicia|reiniciar|enciende|encender|escribe|escribir|haz|hacer|controla|controlar)\b",
            RegexOptions.CultureInvariant);
    }

    internal static string LimpiarRespuestaModelo(string respuesta)
    {
        string limpia = (respuesta ?? string.Empty).Trim();
        Match bloque = Regex.Match(
            limpia,
            @"```(?:powershell|pwsh|ps1)?\s*(?<comando>[\s\S]*?)```",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (bloque.Success)
        {
            limpia = bloque.Groups["comando"].Value.Trim();
        }

        if (limpia.StartsWith(
                "powershell:",
                StringComparison.OrdinalIgnoreCase))
        {
            limpia = limpia["powershell:".Length..].Trim();
        }

        return limpia;
    }

    internal static bool TryExtraerPregunta(
        string texto,
        out string pregunta)
    {
        pregunta = (texto ?? string.Empty).Trim();

        foreach (string prefijo in
                 new[]
                 {
                     "PREGUNTAR:",
                     "CONFIRMAR:",
                     "RESPONDER:"
                 })
        {
            if (pregunta.StartsWith(
                    prefijo,
                    StringComparison.OrdinalIgnoreCase))
            {
                pregunta = pregunta[prefijo.Length..].Trim();
                break;
            }
        }

        if (!pregunta.EndsWith(
                "?",
                StringComparison.Ordinal))
        {
            pregunta = string.Empty;
            return false;
        }

        string normalizada = MemoriaRecetas.Normalizar(pregunta);
        bool parecePregunta =
            pregunta.StartsWith(
                "¿",
                StringComparison.Ordinal)
            || Regex.IsMatch(
                normalizada,
                @"\b(?:que|cual|cuales|donde|cuando|como|quieres|puedes|necesito|indica|dime)\b",
                RegexOptions.CultureInvariant);

        if (!parecePregunta)
        {
            pregunta = string.Empty;
        }

        return parecePregunta;
    }

    internal static bool EsComandoCompatibleConModoConsola(
        string comando)
    {
        return !Regex.IsMatch(
            comando,
            @"\bControlPCIA(?:\.exe)?\s+(?:ui|window\s+keys)\b|\.SendKeys\s*\(|\bSystem\.Windows\.Forms\.SendKeys\b|\.SendWait\s*\(|\bUIAutomation(?:Client)?\b",
            RegexOptions.IgnoreCase
            | RegexOptions.CultureInvariant);
    }

    internal static bool RequiereVerificacionTrasCambio(
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        int ultimoCambio = -1;
        int ultimaObservacion = -1;

        for (int indice = 0; indice < pasos.Count; indice++)
        {
            ResultadoPasoControl paso = pasos[indice];

            if (!paso.Ejecutado
                || paso.CodigoSalida != 0
                || !string.IsNullOrWhiteSpace(paso.Error))
            {
                continue;
            }

            if (EsComandoQueCambiaEstado(paso.Comando))
            {
                ultimoCambio = indice;
            }

            if (ultimoCambio >= 0
                && indice > ultimoCambio
                && EsComandoDeConsulta(paso.Comando)
                && (!EsInicioAplicacion(
                        pasos[ultimoCambio].Comando)
                    || !string.IsNullOrWhiteSpace(paso.Salida)))
            {
                ultimaObservacion = indice;
            }
        }

        return ultimoCambio >= 0
               && ultimaObservacion < ultimoCambio;
    }

    private static bool EsComandoQueCambiaEstado(string comando)
    {
        return !EsComandoDeConsulta(comando);
    }

    private static bool EsInicioAplicacion(string comando)
    {
        return Regex.IsMatch(
                   comando,
                   @"(?:^|[;|\r\n]\s*)(?:Start-Process|start|saps)\b",
                   RegexOptions.IgnoreCase
                   | RegexOptions.CultureInvariant)
               || Regex.IsMatch(
                   comando,
                   @"^\s*explorer(?:\.exe)?\s+['""]shell:AppsFolder\\",
                   RegexOptions.IgnoreCase
                   | RegexOptions.CultureInvariant);
    }

    private static bool EsNombreComandoDeConsulta(
        string? nombreOriginal,
        string textoComando)
    {
        if (string.IsNullOrWhiteSpace(nombreOriginal))
        {
            return false;
        }

        string nombre = nombreOriginal.Contains('\\')
            ? nombreOriginal[(nombreOriginal.LastIndexOf('\\') + 1)..]
            : nombreOriginal;
        string nombreSinExe = nombre.EndsWith(
                ".exe",
                StringComparison.OrdinalIgnoreCase)
            ? nombre[..^4]
            : nombre;

        if (nombreSinExe.Equals(
                "winget",
                StringComparison.OrdinalIgnoreCase))
        {
            return Regex.IsMatch(
                textoComando,
                @"^\s*winget(?:\.exe)?\s+(?:search|show|list|source\s+list)\b",
                RegexOptions.IgnoreCase
                | RegexOptions.CultureInvariant);
        }

        if (nombreSinExe.Equals(
                "reg",
                StringComparison.OrdinalIgnoreCase))
        {
            return Regex.IsMatch(
                textoComando,
                @"^\s*reg(?:\.exe)?\s+query\b",
                RegexOptions.IgnoreCase
                | RegexOptions.CultureInvariant);
        }

        var externos = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "curl",
            "wget",
            "where",
            "tasklist",
            "systeminfo",
            "ipconfig",
            "netstat",
            "whoami",
            "hostname",
            "nslookup",
            "ping"
        };

        if (externos.Contains(nombreSinExe))
        {
            return true;
        }

        var nombres = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "Invoke-WebRequest",
            "Invoke-RestMethod",
            "Write-Output",
            "Write-Host",
            "Where-Object",
            "Select-Object",
            "Sort-Object",
            "Group-Object",
            "Measure-Object",
            "Compare-Object",
            "ForEach-Object",
            "Format-List",
            "Format-Table",
            "Out-String"
        };

        if (nombres.Contains(nombre))
        {
            if (nombre.Equals(
                    "Invoke-WebRequest",
                    StringComparison.OrdinalIgnoreCase)
                && Regex.IsMatch(
                    textoComando,
                    @"\s-OutFile\b",
                    RegexOptions.IgnoreCase
                    | RegexOptions.CultureInvariant))
            {
                return false;
            }

            return true;
        }

        int separador = nombre.IndexOf('-');
        string verbo = separador > 0
            ? nombre[..separador]
            : string.Empty;
        var verbosConsulta = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "Get",
            "Find",
            "Search",
            "Test",
            "Measure",
            "Select",
            "Where",
            "Sort",
            "Group",
            "Compare",
            "Resolve",
            "Read",
            "Format",
            "ConvertFrom"
        };

        return verbosConsulta.Contains(verbo);
    }

    private static async Task<IReadOnlyList<RecetaReferencia>>
        BuscarAprendidosAsync(
            string peticion,
            DependenciasControlWindows dependencias,
            Action<EventoControl>? informar,
            CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<RecetaReferencia> recetas =
                await dependencias.BuscarAprendidoAsync(
                    peticion,
                    cancellationToken);
            RecetaReferencia[] validas = recetas
                .Where(receta =>
                    receta.Comandos.Count > 0
                    && receta.Comandos.All(comando =>
                        ValidadorPowerShell.Validar(comando).Permitido
                        && EsComandoCompatibleConModoConsola(comando)))
                .Take(5)
                .ToArray();

            if (validas.Length > 0)
            {
                Informar(
                    informar,
                    new EventoControl(
                        "memoria",
                        $"Se encontraron {validas.Length} secuencias aprendidas relacionadas."));
            }

            return validas;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            Informar(
                informar,
                new EventoControl(
                    "memoria_error",
                    "La memoria local no está disponible; la petición continuará sin ella."));
            return [];
        }
    }

    private static async Task<bool> AprenderAsync(
        string peticion,
        IReadOnlyList<ResultadoPasoControl> pasos,
        DependenciasControlWindows dependencias,
        Action<EventoControl>? informar,
        CancellationToken cancellationToken)
    {
        string[] comandos = pasos
            .Where(paso =>
                paso.Ejecutado
                && paso.CodigoSalida == 0
                && string.IsNullOrWhiteSpace(paso.Error))
            .Select(paso => paso.Comando)
            .ToArray();

        if (comandos.Length == 0)
        {
            return false;
        }

        try
        {
            bool aprendido =
                await dependencias.AprenderAsync(
                    peticion,
                    comandos,
                    cancellationToken);

            if (aprendido)
            {
                Informar(
                    informar,
                    new EventoControl(
                        "aprendido",
                        "Los comandos que funcionaron quedaron guardados en la memoria local."));
            }

            return aprendido;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            Informar(
                informar,
                new EventoControl(
                    "memoria_error",
                    "La orden terminó, pero no se pudo actualizar la memoria local."));
            return false;
        }
    }

    private static IReadOnlyList<MensajeConversacionControl>
        NormalizarContexto(
            IReadOnlyList<MensajeConversacionControl>? contexto)
    {
        if (contexto is null || contexto.Count == 0)
        {
            return [];
        }

        var resultado = new List<MensajeConversacionControl>();
        int caracteres = 0;

        foreach (MensajeConversacionControl mensaje in contexto
                     .TakeLast(MaximoMensajesContexto))
        {
            string rol = mensaje.Rol.Equals(
                    "assistant",
                    StringComparison.OrdinalIgnoreCase)
                ? "assistant"
                : "user";
            string texto = (mensaje.Texto ?? string.Empty).Trim();

            if (texto.Length == 0
                || caracteres + texto.Length > MaximoCaracteresContexto)
            {
                continue;
            }

            resultado.Add(
                new MensajeConversacionControl(
                    rol,
                    texto));
            caracteres += texto.Length;
        }

        return resultado;
    }

    private static string CrearMensajePeticion(
        string peticion,
        IReadOnlyList<RecetaReferencia> aprendidos)
    {
        var mensaje = new StringBuilder();
        mensaje.AppendLine("PETICIÓN DEL USUARIO:");
        mensaje.AppendLine(peticion);
        mensaje.AppendLine();
        mensaje.AppendLine("COMANDOS APRENDIDOS RELACIONADOS:");

        if (aprendidos.Count == 0)
        {
            mensaje.Append("Ninguno.");
            return mensaje.ToString();
        }

        int numero = 1;

        foreach (RecetaReferencia receta in aprendidos)
        {
            foreach (string comando in receta.Comandos)
            {
                mensaje.AppendLine($"{numero}. {comando}");
                numero++;
            }
        }

        return mensaje.ToString().TrimEnd();
    }

    private static string CrearMensajeResultadoParaLlama(
        string peticion,
        ResultadoEjecucionPowerShell resultado)
    {
        return $"""
            PETICIÓN ORIGINAL:
            {peticion}

            RESULTADO REAL DEL COMANDO ANTERIOR:
            EJECUTADO={resultado.Ejecutado}
            CODIGO_SALIDA={resultado.CodigoSalida}
            STDOUT:
            {(string.IsNullOrWhiteSpace(resultado.Salida) ? "(vacío)" : resultado.Salida)}
            STDERR:
            {(string.IsNullOrWhiteSpace(resultado.Error) ? "(vacío)" : resultado.Error)}
            """;
    }

    private static string CrearMensajeResultadoBreve(
        ResultadoEjecucionPowerShell resultado)
    {
        if (!resultado.Ejecutado)
        {
            return string.IsNullOrWhiteSpace(resultado.Error)
                ? "PowerShell no pudo ejecutar el comando."
                : resultado.Error;
        }

        if (resultado.CodigoSalida != 0
            || !string.IsNullOrWhiteSpace(resultado.Error))
        {
            return CrearMensajeErrorFinal(resultado);
        }

        return string.IsNullOrWhiteSpace(resultado.Salida)
            ? "PowerShell terminó correctamente y no devolvió salida."
            : resultado.Salida;
    }

    private static string CrearMensajeErrorFinal(
        ResultadoEjecucionPowerShell resultado)
    {
        string error = string.IsNullOrWhiteSpace(resultado.Error)
            ? "PowerShell terminó con un error sin texto adicional."
            : resultado.Error;

        return $"Código de salida: {resultado.CodigoSalida}. {error}";
    }

    private static ResultadoControl Finalizar(
        bool completado,
        string estado,
        string mensaje,
        IReadOnlyList<ResultadoPasoControl> pasos,
        bool aprendido,
        Action<EventoControl>? informar)
    {
        Informar(
            informar,
            new EventoControl(
                "final",
                mensaje));

        return new ResultadoControl(
            completado,
            estado,
            mensaje,
            pasos,
            aprendido);
    }

    private static void Informar(
        Action<EventoControl>? informar,
        EventoControl evento)
    {
        informar?.Invoke(evento);
    }
}
