using System.Text;
using System.Text.RegularExpressions;

namespace ControlPCIA;

internal static class ControlWindows
{
    private const int MaximoPasos = 10;

    public static async Task<ResultadoControl> ControlarAsync(
        string instruccion,
        Action<EventoControl>? informar = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instruccion))
        {
            return Finalizar(
                false,
                "orden_vacia",
                "No se ha recibido ninguna orden.",
                [],
                informar);
        }

        if (instruccion.Length > 1000)
        {
            return Finalizar(
                false,
                "orden_demasiado_larga",
                "La orden supera los 1000 caracteres.",
                [],
                informar);
        }

        IReadOnlyList<RecetaReferencia> recetas =
            await BuscarRecetasAsync(
                instruccion,
                informar,
                cancellationToken);

        string memoriaLocal = CrearResumenRecetas(recetas);

        string estadoWindows =
            string.Join(
                Environment.NewLine,
                ObservadorWindows
                    .ObtenerVentanasAbiertas()
                    .Select(ventana => "- " + ventana));
        string ventanaActiva = ObservadorWindows.ObtenerVentanaActiva();
        ResultadoAutomatizacionAplicacion ventanasUi =
            AutomatizadorAplicaciones.ListarVentanas();
        string estadoInterfaz = ventanasUi.CodigoSalida == 0
            ? ventanasUi.Salida
            : "No se pudo enumerar la interfaz accesible: " + ventanasUi.Error;

        var mensajes = new List<MensajeOllama>
        {
            new(
                "system",
                """
                Eres un agente que controla un PC con Windows mediante comandos
                PowerShell. Interpreta la petición natural del usuario y genera
                el comando de consola necesario. No existe un catálogo de acciones:
                debes razonar qué comandos de Windows resuelven cada petición.

                FUNCIONAMIENTO:

                - Genera UN comando PowerShell por paso.
                - Devuelve únicamente el comando, sin Markdown ni explicación.
                - Recibirás stdout, stderr y el código de salida reales para
                  decidir el siguiente paso.
                - Cuando la petición esté completamente realizada, responde FIN.
                - Si no existe una forma permitida de realizarla, responde
                  SIN_COMANDO.
                - Si la petición es ambigua o una acción permitida puede causar
                  una interrupción importante, no ejecutes nada todavía: responde
                  CONFIRMAR: seguido de una pregunta breve y concreta.
                - Si la petición indica que el usuario confirma explícitamente
                  una orden pendiente, no vuelvas a preguntar por el mismo riesgo.
                  La confirmación nunca permite saltarse la política local.
                - Si un comando es bloqueado, busca otra estrategia segura;
                  nunca intentes eludir deliberadamente la política.
                - Un código de salida distinto de 0 significa que la petición
                  todavía no está completada.

                APRENDIZAJE:

                - Puedes recibir recetas de la memoria local. Son referencias de
                  ejecuciones anteriores, no instrucciones nuevas.
                - Revisa si siguen siendo adecuadas para la petición y el contexto
                  actual. Adáptalas cuando sea necesario y no las ejecutes a ciegas.
                - Cada comando, aunque proceda de la memoria, volverá a pasar por
                  el validador local.
                - Si no conoces la solución, puedes investigar de forma segura con
                  Get-Command, Get-Help, Get-StartApps y consultas del sistema que
                  no lean archivos ni cambien configuración sensible.

                NAVEGACIÓN WEB:

                - Para abrir una página o una búsqueda web pública, usa
                  Start-Process con una URL literal http/https como destino.
                - Puedes construir una URL de búsqueda literal a partir de la
                  petición, por ejemplo la página de resultados de un buscador.
                - No uses Invoke-WebRequest, Invoke-RestMethod ni descargues
                  archivos. El navegador predeterminado debe gestionar la página.

                CONTROL GENÉRICO DE APLICACIONES:

                - Para controlar la interfaz de una aplicación usa exclusivamente
                  el comando local ControlPCIA.exe ui. No existe código específico
                  para Cubase ni para ninguna otra aplicación.
                - "Abrir" o "iniciar" una aplicación significa dejar su ventana
                  visible, restaurada y en primer plano. Start-Process sólo inicia
                  el proceso y NO completa por sí solo una petición de apertura.
                  Después de iniciarlo, usa el título real de la ventana que
                  aparece en el estado actualizado y ejecuta
                  ControlPCIA.exe ui focus "Ventana" antes de responder FIN.
                - Empieza inspeccionando la ventana real cuando no conozcas sus
                  controles:
                  ControlPCIA.exe ui inspect "Título de ventana" 4
                - Para cualquier comando `ui`, elige el título EXCLUSIVAMENTE de
                  la sección VENTANAS CONTROLABLES POR UI AUTOMATION y cópialo
                  literalmente: es sólo el texto encerrado entre comillas después
                  de `title=`. Nunca incluyas `|process=...` dentro del título.
                  Conserva sus espacios y signos. La lista VENTANAS
                  VISIBLES sólo aporta contexto general y no autoriza títulos de
                  automatización. Si la petición coincide exactamente con un
                  `WINDOW|title="..."`, debes usar ese título, no otro parecido.
                - Las primitivas disponibles son:
                  ControlPCIA.exe ui windows
                  ControlPCIA.exe ui focus "Ventana"
                  ControlPCIA.exe ui invoke "Ventana" "Botón o menú" "Button"
                  ControlPCIA.exe ui select "Ventana" "Elemento" "ListItem"
                  ControlPCIA.exe ui toggle "Ventana" "Control" "CheckBox"
                  ControlPCIA.exe ui expand "Ventana" "Control" "TreeItem"
                  ControlPCIA.exe ui collapse "Ventana" "Control" "TreeItem"
                  ControlPCIA.exe ui text "Ventana" "Cuadro de búsqueda" "texto"
                  ControlPCIA.exe ui shortcut "Ventana" "CTRL+T"
                - El tipo final es opcional en invoke, select, toggle, expand y
                  collapse. Si inspect muestra un AutomationId, puedes seleccionar
                  de forma precisa con "id:AutomationId".
                - Usa nombres y títulos que aparezcan realmente en la inspección;
                  no inventes controles. Después de abrir un menú o diálogo,
                  inspecciona otra vez si necesitas ver su contenido.
                - En aplicaciones con interfaz personalizada que no expongan sus
                  controles, enfoca la ventana y usa sólo atajos seguros conocidos.
                - Nunca intentes usar esta interfaz para abrir, guardar, importar,
                  exportar, descargar, instalar, imprimir o manipular archivos,
                  credenciales, consolas o superficies sensibles.

                SEGURIDAD:

                - Nunca crees, leas, borres, copies, muevas, renombres ni escribas
                  archivos o carpetas.
                - Nunca modifiques registro, discos, particiones, permisos,
                  usuarios, servicios, tareas programadas, red, firewall,
                  Defender, arranque ni configuración crítica.
                - No abras PowerShell, CMD, Terminal ni otra consola anidada.
                - No uses ejecución dinámica, reflexión ni código destinado a
                  sortear el validador.

                Puedes utilizar otros comandos PowerShell para controlar
                aplicaciones, ventanas, audio, multimedia, pantallas y la interfaz
                de Windows. La política local analizará cada comando antes de
                ejecutarlo.

                Nunca inventes rutas de instalación. Si necesitas descubrir cómo
                está registrada una aplicación, consulta Windows con Get-StartApps
                o con otros comandos de consulta seguros.
                """),
            new(
                "user",
                $"""
                PETICIÓN DEL USUARIO:

                {instruccion.Trim()}

                VENTANAS VISIBLES:

                {estadoWindows}

                VENTANA ACTIVA:

                {ventanaActiva}

                VENTANAS CONTROLABLES POR UI AUTOMATION:

                {estadoInterfaz}

                RECETAS LOCALES RELACIONADAS:

                {memoriaLocal}

                Decide el primer paso necesario.
                """)
        };

        var pasos = new List<ResultadoPasoControl>();

        try
        {
            for (int indice = 0; indice < MaximoPasos; indice++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Informar(
                    informar,
                    new EventoControl(
                        "pensando",
                        $"La IA está decidiendo el paso {indice + 1}."));

                string comando =
                    LimpiarComando(
                        await ClienteOllama.ConversarAsync(
                            mensajes,
                            cancellationToken));

                if (comando.Equals(
                        "FIN",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (RequiereEnfoqueTrasInicio(pasos))
                    {
                        mensajes.Add(new MensajeOllama("assistant", comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                CrearAvisoEnfoquePendiente()));
                        continue;
                    }

                    bool aprendido =
                        await AprenderRecetaAsync(
                            instruccion,
                            pasos,
                            informar,
                            cancellationToken);

                    return Finalizar(
                        true,
                        "completado",
                        "Petición completada.",
                        pasos,
                        informar,
                        aprendido);
                }

                if (comando.Equals(
                        "SIN_COMANDO",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Finalizar(
                        false,
                        "sin_comando",
                        "No se encontró una forma permitida de realizar la petición.",
                        pasos,
                        informar);
                }

                if (TryObtenerPreguntaConfirmacion(
                        comando,
                        out string preguntaConfirmacion))
                {
                    return Finalizar(
                        false,
                        "requiere_confirmacion",
                        preguntaConfirmacion,
                        pasos,
                        informar);
                }

                if (string.IsNullOrWhiteSpace(comando))
                {
                    return Finalizar(
                        false,
                        "respuesta_invalida",
                        "La IA devolvió una respuesta vacía.",
                        pasos,
                        informar);
                }

                ResultadoPasoControl? intentoFallidoIgual = pasos
                    .LastOrDefault(paso =>
                        paso.Comando.Equals(
                            comando,
                            StringComparison.OrdinalIgnoreCase)
                        && (!paso.Ejecutado || paso.CodigoSalida != 0));

                if (intentoFallidoIgual is not null)
                {
                    return Finalizar(
                        false,
                        "estrategia_repetida",
                        "La IA intentó repetir un comando que ya había fallado. Se ha detenido para no provocar acciones inesperadas.",
                        pasos,
                        informar);
                }

                if (RequiereEnfoqueTrasInicio(pasos)
                    && !EsComandoPermitidoMientrasEnfoca(comando))
                {
                    return Finalizar(
                        false,
                        "enfoque_inseguro",
                        "La IA intentó realizar otra acción antes de enfocar la aplicación abierta. La orden se ha detenido sin ejecutar ese comando.",
                        pasos,
                        informar);
                }

                Informar(
                    informar,
                    new EventoControl(
                        "comando",
                        "La IA ha propuesto un comando.",
                        comando));

                ResultadoEjecucionPowerShell ejecucion =
                    await EjecutorPowerShell.EjecutarAsync(
                        comando,
                        cancellationToken);

                bool inicioAplicacion =
                    ejecucion.Ejecutado
                    && ejecucion.CodigoSalida == 0
                    && EsInicioAplicacion(comando);

                if (inicioAplicacion)
                {
                    // Las aplicaciones empaquetadas de Windows suelen publicar
                    // su ventana unas décimas después de terminar Start-Process.
                    await Task.Delay(1_200, cancellationToken);
                }

                var paso = new ResultadoPasoControl(
                    indice + 1,
                    comando,
                    ejecucion.Ejecutado,
                    ejecucion.CodigoSalida,
                    ejecucion.Salida,
                    ejecucion.Error);

                pasos.Add(paso);

                Informar(
                    informar,
                    new EventoControl(
                        ejecucion.Ejecutado ? "ejecutado" : "bloqueado",
                        ejecucion.Ejecutado
                            ? $"Comando ejecutado con código {ejecucion.CodigoSalida}."
                            : ejecucion.Error,
                        comando,
                        paso));

                string informacionResultado =
                    ejecucion.Ejecutado
                        ? CrearResultadoEjecutado(
                            ejecucion,
                            incluirEstadoInterfaz: inicioAplicacion)
                        : CrearResultadoBloqueado(ejecucion);

                mensajes.Add(new MensajeOllama("assistant", comando));
                mensajes.Add(new MensajeOllama("user", informacionResultado));
            }

            return Finalizar(
                false,
                "limite_pasos",
                $"El agente alcanzó el límite de {MaximoPasos} pasos sin completar la petición.",
                pasos,
                informar);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return Finalizar(
                false,
                "cancelado",
                "La petición fue cancelada.",
                pasos,
                informar);
        }
        catch (Exception ex)
        {
            return Finalizar(
                false,
                "error",
                ex.Message,
                pasos,
                informar);
        }
    }

    private static string CrearResultadoBloqueado(
        ResultadoEjecucionPowerShell resultado)
    {
        return $"""
            EL COMANDO FUE BLOQUEADO Y NO SE EJECUTÓ.

            Código de salida:
            {resultado.CodigoSalida}

            Error:
            {LimitarTexto(resultado.Error)}

            La petición original NO está completada. No inventes rutas de
            instalación ni repitas la misma estrategia cambiando carpetas al
            azar. Consulta el sistema si necesitas descubrir cómo está registrada
            una aplicación y después intenta otra estrategia permitida.
            """;
    }

    private static string CrearResultadoEjecutado(
        ResultadoEjecucionPowerShell resultado,
        bool incluirEstadoInterfaz)
    {
        string estadoActual = incluirEstadoInterfaz
            ? CrearAvisoEnfoquePendiente()
            : string.Empty;

        return $"""
            RESULTADO DEL COMANDO:

            Código de salida:
            {resultado.CodigoSalida}

            Salida:
            {LimitarTexto(resultado.Salida)}

            Error:
            {LimitarTexto(resultado.Error)}

            {estadoActual}

            Decide si la petición original ya está completada o si necesitas
            ejecutar otro comando.
            """;
    }

    private static string CrearAvisoEnfoquePendiente()
    {
        ResultadoAutomatizacionAplicacion ventanas =
            AutomatizadorAplicaciones.ListarVentanas();
        string listaVentanas = ventanas.CodigoSalida == 0
            ? ventanas.Salida
            : "No se pudo enumerar la interfaz: " + ventanas.Error;

        return $"""
            ESTADO DE VENTANAS DESPUÉS DEL COMANDO:

            Ventana activa: {ObservadorWindows.ObtenerVentanaActiva()}

            Ventanas controlables:
            {listaVentanas}

            Si la petición era abrir o iniciar una aplicación, todavía NO está
            completada: enfoca su ventana real con
            ControlPCIA.exe ui focus "Ventana" antes de responder FIN.
            """;
    }

    internal static bool RequiereEnfoqueTrasInicio(
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        int ultimoInicio = -1;
        int ultimaActivacion = -1;

        for (int indice = 0; indice < pasos.Count; indice++)
        {
            ResultadoPasoControl paso = pasos[indice];

            if (!paso.Ejecutado || paso.CodigoSalida != 0)
            {
                continue;
            }

            if (EsInicioAplicacion(paso.Comando))
            {
                ultimoInicio = indice;
            }

            if (EsAccionInterfazQueActiva(paso.Comando))
            {
                ultimaActivacion = indice;
            }
        }

        return ultimoInicio >= 0 && ultimaActivacion < ultimoInicio;
    }

    private static bool EsInicioAplicacion(string comando)
    {
        return Regex.IsMatch(
            comando,
            @"(?:^|[;|]\s*)(?:Start-Process|start|saps)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool EsAccionInterfazQueActiva(string comando)
    {
        return Regex.IsMatch(
            comando,
            @"\bControlPCIA(?:\.exe)?\s+ui\s+(?:focus|invoke|select|toggle|expand|collapse|text|shortcut)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static bool EsComandoPermitidoMientrasEnfoca(string comando)
    {
        return Regex.IsMatch(
            comando,
            @"^\s*ControlPCIA(?:\.exe)?\s+ui\s+(?:windows|focus\s+.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static bool TryObtenerPreguntaConfirmacion(
        string respuesta,
        out string pregunta)
    {
        const string prefijo = "CONFIRMAR:";
        string limpia = respuesta.Trim();

        if (!limpia.StartsWith(
                prefijo,
                StringComparison.OrdinalIgnoreCase))
        {
            pregunta = string.Empty;
            return false;
        }

        pregunta = limpia[prefijo.Length..].Trim();

        if (pregunta.Length == 0)
        {
            pregunta = "Esta acción necesita confirmación. ¿Quieres que continúe?";
        }
        else if (pregunta.Length > 300)
        {
            pregunta = pregunta[..300].Trim();
        }

        return true;
    }

    private static ResultadoControl Finalizar(
        bool completado,
        string estado,
        string mensaje,
        IReadOnlyList<ResultadoPasoControl> pasos,
        Action<EventoControl>? informar,
        bool aprendido = false)
    {
        Informar(informar, new EventoControl("final", mensaje));

        return new ResultadoControl(
            completado,
            estado,
            mensaje,
            pasos.ToArray(),
            aprendido);
    }

    private static async Task<IReadOnlyList<RecetaReferencia>>
        BuscarRecetasAsync(
            string instruccion,
            Action<EventoControl>? informar,
            CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<RecetaReferencia> recetas =
                await MemoriaRecetas.Predeterminada.BuscarAsync(
                    instruccion,
                    cancellationToken: cancellationToken);

            if (recetas.Count > 0)
            {
                Informar(
                    informar,
                    new EventoControl(
                        "memoria",
                        $"Se encontraron {recetas.Count} recetas relacionadas."));
            }

            return recetas;
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
                    "La memoria local no está disponible; la orden continuará sin recetas."));

            return [];
        }
    }

    private static async Task<bool> AprenderRecetaAsync(
        string instruccion,
        IReadOnlyList<ResultadoPasoControl> pasos,
        Action<EventoControl>? informar,
        CancellationToken cancellationToken)
    {
        string[] comandos = pasos
            .Where(paso => paso.Ejecutado && paso.CodigoSalida == 0)
            .Select(paso => paso.Comando)
            .ToArray();

        if (comandos.Length == 0)
        {
            return false;
        }

        try
        {
            bool aprendido =
                await MemoriaRecetas.Predeterminada.AprenderAsync(
                    instruccion,
                    comandos,
                    cancellationToken);

            if (aprendido)
            {
                Informar(
                    informar,
                    new EventoControl(
                        "aprendido",
                        "La secuencia que funcionó quedó guardada en la memoria local."));
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
                    "La petición terminó, pero no se pudo actualizar la memoria local."));

            return false;
        }
    }

    private static string CrearResumenRecetas(
        IReadOnlyList<RecetaReferencia> recetas)
    {
        if (recetas.Count == 0)
        {
            return "No hay recetas relacionadas. Investiga con consultas seguras si lo necesitas.";
        }

        const int limite = 8000;
        var resumen = new StringBuilder();

        for (int indice = 0; indice < recetas.Count; indice++)
        {
            RecetaReferencia receta = recetas[indice];
            var bloque = new StringBuilder();

            bloque.AppendLine($"Receta {indice + 1}:");
            bloque.AppendLine($"Intención anterior: {receta.Intencion}");
            bloque.AppendLine($"Éxitos registrados: {receta.Exitos}");
            bloque.AppendLine("Comandos que funcionaron:");

            foreach (string comando in receta.Comandos)
            {
                bloque.AppendLine("- " + comando);
            }

            bloque.AppendLine();

            if (resumen.Length + bloque.Length > limite)
            {
                break;
            }

            resumen.Append(bloque);
        }

        return resumen.Length == 0
            ? "No hay recetas que quepan de forma segura en el contexto."
            : resumen.ToString();
    }

    private static void Informar(
        Action<EventoControl>? informar,
        EventoControl evento)
    {
        informar?.Invoke(evento);
    }

    private static string LimpiarComando(string respuesta)
    {
        string resultado = respuesta.Trim();

        if (resultado.StartsWith(
                "```powershell",
                StringComparison.OrdinalIgnoreCase))
        {
            resultado = resultado["```powershell".Length..];
        }
        else if (resultado.StartsWith(
                     "```",
                     StringComparison.OrdinalIgnoreCase))
        {
            resultado = resultado[3..];
        }

        if (resultado.EndsWith(
                "```",
                StringComparison.OrdinalIgnoreCase))
        {
            resultado = resultado[..^3];
        }

        return resultado.Trim();
    }

    private static string LimitarTexto(string texto)
    {
        const int limite = 6000;

        if (string.IsNullOrEmpty(texto)
            ||
            texto.Length <= limite)
        {
            return texto;
        }

        return texto[..limite] +
               Environment.NewLine +
               "[Salida recortada]";
    }
}
