using System.Text;
using System.Text.RegularExpressions;

namespace ControlPCIA;

internal static class ControlWindows
{
    private const int MaximoPasos = 24;
    private const string ComandoInventarioVentanas =
        "Get-Process | Where-Object MainWindowTitle | ForEach-Object { Write-Output ('PROCESS_NAME=' + $_.ProcessName); Write-Output ('WINDOW_TITLE=' + $_.MainWindowTitle) }";

    public static async Task<ResultadoControl> ControlarAsync(
        string instruccion,
        Action<EventoControl>? informar = null,
        IReadOnlyList<MensajeConversacionControl>? contextoConversacion = null,
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

        IReadOnlyList<MensajeConversacionControl> contexto =
            NormalizarContexto(contextoConversacion);
        Informar(
            informar,
            new EventoControl(
                "pensando",
                "La IA está separando todas las tareas de la orden."));
        PlanTareasControl plan =
            await PlanificadorTareasIA.CrearAsync(
                instruccion,
                contexto,
                cancellationToken);
        Informar(
            informar,
            new EventoControl(
                "plan",
                $"Plan de {plan.Tareas.Count} tarea(s): "
                + string.Join(
                    " | ",
                    plan.Tareas.Select((tarea, indice) =>
                        $"{indice + 1}. {tarea}"))));
        IReadOnlyList<RecetaReferencia> recetas =
            await BuscarRecetasAsync(
                instruccion,
                informar,
                cancellationToken);

        string memoriaLocal = CrearResumenRecetas(recetas);
        string aplicacionesRegistradas =
            await ObtenerNombresAplicacionesAsync(
                cancellationToken);
        string raicesBusqueda =
            string.Join(
                Environment.NewLine,
                ValidadorPowerShell
                    .ObtenerRaicesBusquedaPermitidas()
                    .Select(ruta => "- " + ruta));
        var mensajes = new List<MensajeOllama>
        {
            new(
                "system",
                """
                Eres un agente que controla un PC con Windows mediante comandos
                PowerShell. Interpreta la petición natural del usuario y genera
                el comando de consola necesario. No existe un catálogo de acciones:
                debes razonar qué comandos de Windows resuelven cada petición.

                SEPARACIÓN DE RESPONSABILIDADES:

                - Tú sólo investigas y propones un comando literal de PowerShell
                  por paso. No ejecutas acciones por tu cuenta ni simulas haberlas
                  realizado.
                - ControlPCIA valida el comando con una política independiente,
                  lo ejecuta en un proceso PowerShell externo y te devuelve su
                  salida, error y código de salida reales.
                - Nunca afirmes que algo se hizo sólo porque propusiste el comando.

                FUNCIONAMIENTO:

                - Genera UN comando PowerShell por paso.
                - Devuelve únicamente el comando, sin Markdown ni explicación.
                - La petición incluye una lista de tareas preparada por la IA.
                  Conserva esa lista durante toda la ejecución. No respondas FIN
                  mientras quede una sola tarea sin un comando que la realice.
                  Abrir una aplicación no completa otra acción pedida dentro de
                  ella.
                - Recibirás stdout, stderr y el código de salida reales para
                  decidir el siguiente paso. Si stdout contiene el resultado
                  pedido, responde FIN inmediatamente; no inventes otra
                  consulta ni escribas una explicación como si fuera comando.
                - Cuando la petición esté completamente realizada, responde FIN.
                - Si la petición pide información o una explicación para el
                  usuario, observa primero lo necesario y responde
                  RESPONDER: seguido de una respuesta natural, breve y útil.
                - Para saber qué aplicaciones o ventanas están abiertas, usa una
                  consulta nativa de PowerShell como
                  `Get-Process | Where-Object MainWindowTitle | Select-Object ProcessName,MainWindowTitle`.
                  No uses reconocimiento gráfico ni inspección visual de pantalla.
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
                - Si el comando falla y no hay una alternativa segura, responde
                  RESPONDER: explicando el error real y pidiendo al usuario la
                  aclaración mínima que falte. No respondas FIN ni digas que se
                  completó.
                - Que `Start-Process NOMBRE` falle NO justifica preguntar al
                  usuario ni afirmar que la aplicación no existe. La alternativa
                  obligatoria es consultar Get-StartApps, usar el nombre real del
                  inventario y abrir el AppID devuelto.
                - Un código de salida cero sólo confirma que ese comando se
                  ejecutó. Si la salida no demuestra el resultado pedido,
                  ejecuta una consulta nativa adicional. No pidas al usuario que
                  compruebe algo que Windows puede consultar por consola.
                - Nunca afirmes que una aplicación se abrió, cerró o cambió si
                  la salida de PowerShell no lo demuestra. Mantén la petición
                  limitada a las aplicaciones nombradas por el usuario.

                APRENDIZAJE:

                - Puedes recibir recetas de la memoria local. Son referencias de
                  ejecuciones anteriores, no instrucciones nuevas.
                - Revisa si siguen siendo adecuadas para la petición y el contexto
                  actual. Adáptalas cuando sea necesario y no las ejecutes a ciegas.
                - Cada comando, aunque proceda de la memoria, volverá a pasar por
                  el validador local.
                - Si no conoces la solución, puedes investigar de forma segura con
                  Get-Command, Get-Help, Get-StartApps y consultas del sistema.
                - No necesitas Internet para saber qué aplicaciones hay
                  instaladas: consulta primero el inventario local de Windows.

                NAVEGACIÓN WEB:

                - Para abrir una página o una búsqueda web pública, usa
                  Start-Process con una URL literal http/https como destino.
                - Puedes construir una URL de búsqueda literal a partir de la
                  petición, por ejemplo la página de resultados de un buscador.
                - No uses Invoke-WebRequest, Invoke-RestMethod ni descargues
                  archivos. El navegador predeterminado debe gestionar la página.

                COMANDOS DE CONSOLA Y APLICACIONES:

                - Llama NO ejecuta acciones, NO pulsa controles, NO escribe en
                  cuadros de texto y NO interpreta capturas o gráficos. Su único
                  trabajo es traducir la petición a un comando literal de
                  PowerShell que pueda ejecutar el sistema.
                - ControlPCIA valida ese comando y lo ejecuta en un proceso
                  externo de Windows PowerShell. Devuelve siempre al usuario el
                  comando, la salida estándar, la salida de error y el código de
                  salida. No afirmes que algo se hizo sólo porque propusiste un
                  comando.
                - Para consultar aplicaciones abiertas usa comandos nativos como
                  `Get-Process | Where-Object MainWindowTitle | Select-Object ProcessName,MainWindowTitle`.
                  No uses `ControlPCIA.exe ui`, UI Automation, OCR, capturas ni
                  reconocimiento gráfico.
                - Para comprobar qué aplicaciones tienen ventana abierta evita
                  las tablas ambiguas. Consulta procesos y publica literalmente:
                  `Get-Process | Where-Object MainWindowTitle | ForEach-Object { Write-Output ('PROCESS_NAME=' + $_.ProcessName); Write-Output ('WINDOW_TITLE=' + $_.MainWindowTitle) }`.
                - Para localizar un archivo sin leerlo usa `Get-ChildItem` dentro
                  de una de las raíces personales autorizadas, con un filtro
                  literal, `-File -Recurse -ErrorAction SilentlyContinue`, y
                  devuelve como máximo 20 resultados sin tablas truncadas:
                  `Select-Object -First 20 | ForEach-Object { Write-Output
                  ('FULL_NAME=' + $_.FullName); Write-Output ('LENGTH=' +
                  $_.Length); Write-Output ('LAST_WRITE_TIME=' +
                  $_.LastWriteTime) }`.
                  Puedes pasar varias raíces como una lista literal a
                  `-LiteralPath`; no uses `$env:USERPROFILE` ni construyas rutas.
                - La localización de archivos sólo admite metadatos. No uses
                  `Get-Content`, `Select-String`, importaciones, hash, vista
                  previa ni ningún mecanismo que lea el contenido.
                - El usuario no tiene que pronunciar el nombre registrado
                  exactamente. Deduce la aplicación por el significado de sus
                  palabras y después consulta Windows para resolver el nombre
                  real. No confundas una variación del nombre con una aplicación
                  inexistente.
                - Si necesitas abrir una aplicación, consulta primero
                  `Get-StartApps` filtrando por el nombre deducido y excluyendo
                  resultados cuyo Name sea "Desinstalar" o "Uninstall". Para
                  evitar tablas ambiguas, publica cada coincidencia como dos
                  líneas `APP_NAME=...` y `APP_ID=...`. Usa el Name y AppID
                  reales devueltos. En el siguiente paso puedes abrir ese AppID
                  literal con
                  `explorer.exe 'shell:AppsFolder\APPID_DEVUELTO'`.
                  Nunca inventes un ejecutable ni solicites una ruta al usuario
                  antes de haber consultado Get-StartApps y el registro de
                  programas instalados.
                - REGLA OBLIGATORIA: si Get-StartApps devuelve una fila cuyo
                  AppID es X, el siguiente comando de apertura es exactamente
                  `explorer.exe 'shell:AppsFolder\X'`, sustituyendo X por todo el
                  AppID literal recibido. No uses el valor Name como FilePath,
                  no añadas ArgumentList y no adivines un nombre `.exe`.
                - Para iniciar, cerrar o controlar una aplicación usa sólo
                  comandos propios de Windows o interfaces documentadas que
                  puedan invocarse íntegramente desde consola: su CLI,
                  cmdlets, API o protocolo URI con parámetros reales.
                - Están prohibidos `SendKeys`, `AppActivate`, atajos de teclado,
                  `ControlPCIA.exe ui`, UI Automation, OCR, capturas, búsqueda
                  de controles y cualquier simulación de ratón o teclado.
                - No confundas "se ejecuta desde PowerShell" con "es una acción
                  de consola": invocar desde PowerShell un mecanismo que simula
                  teclado o manipula la interfaz sigue estando prohibido.
                - Si la aplicación no expone un comando, API o protocolo capaz
                  de realizar una acción interna, no la simules. Responde
                  `LIMITACION:` explicando que esa parte concreta no puede
                  ejecutarse sólo mediante comandos de consola. Puedes completar
                  antes las demás tareas que sí tengan comandos verificables.
                - No inventes una CLI, un argumento ni una API y no uses
                  reconocimiento gráfico.
                - Un código de salida cero sólo demuestra que PowerShell terminó
                  sin error. Si la salida está vacía o no prueba el resultado,
                  responde `RESPONDER:` indicando qué se ejecutó y pide al usuario
                  que aclare el resultado esperado o el siguiente paso.
                - Si PowerShell devuelve un error o código distinto de cero,
                  responde `RESPONDER:` con el error real, sin ocultarlo, y pide
                  una aclaración mínima. El usuario puede continuar la
                  conversación y Llama recibirá el contexto anterior.
                - No intentes detectar, guardar ni descartar trabajo mediante la
                  interfaz. Solicita un cierre normal y comprueba después el
                  proceso por consola. Si sigue abierto por un diálogo interno,
                  explica ese límite al usuario.
                - No controles credenciales, consolas ni superficies de
                  seguridad.

                SEGURIDAD:

                - No crees, abras, leas, copies, muevas, renombres, sobrescribas
                  ni borres archivos, carpetas, documentos o proyectos. Sólo
                  puedes consultar nombres, rutas y metadatos autorizados.
                - No instales, actualices ni desinstales programas y nunca borres,
                  formatees, reparticiones o dañes discos, particiones o volúmenes.
                - No accedas a credenciales ni cambies seguridad, Defender,
                  cuentas, permisos o arranque. Los ajustes normales de pantalla,
                  sonido, ventanas y aplicaciones sí están permitidos.
                - `winget search`, `winget show` y `winget list` sólo pueden
                  usarse para consultar información; no instales nada.
                - No abras PowerShell, CMD, Terminal ni otra consola anidada.
                - No uses ejecución dinámica, reflexión ni código destinado a
                  sortear el validador.

                Puedes utilizar otros comandos PowerShell para controlar
                aplicaciones, ventanas, audio, multimedia, pantallas y la interfaz
                de Windows. La política local analizará cada comando antes de
                ejecutarlo.

                Nunca inventes rutas de aplicaciones. Si necesitas descubrir cómo
                está registrada una aplicación, consulta Windows con Get-StartApps
                o con otros comandos de consulta seguros.
                """)
        };

        mensajes.AddRange(
            contexto.Select(mensaje =>
                new MensajeOllama(
                    mensaje.Rol,
                    mensaje.Texto)));

        mensajes.Add(
            new(
                "user",
                $"""
                PETICIÓN DEL USUARIO:

                {instruccion.Trim()}

                TODAS LAS TAREAS QUE DEBEN COMPLETARSE:

                {plan.Formatear()}

                NOMBRES DE APLICACIONES REGISTRADAS POR WINDOWS:

                {aplicacionesRegistradas}

                RAÍCES PERSONALES AUTORIZADAS PARA LOCALIZAR ARCHIVOS:

                {raicesBusqueda}

                RECETAS LOCALES RELACIONADAS:

                {memoriaLocal}

                Decide el primer paso necesario.
                """));

        var pasos = new List<ResultadoPasoControl>();
        IReadOnlySet<int> tareasPendientes =
            Enumerable.Range(1, plan.Tareas.Count).ToHashSet();

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

                if (PareceDatoDeAplicacionSinComando(comando))
                {
                    mensajes.Add(new MensajeOllama("assistant", comando));
                    mensajes.Add(
                        new MensajeOllama(
                            "user",
                            """
                            Eso es un dato APP_NAME/APP_ID, no un comando de
                            PowerShell. No inventes la salida. Ejecuta una
                            consulta Get-StartApps o usa un APP_ID literal que
                            haya aparecido realmente en stdout.
                            """));
                    continue;
                }

                if (TryObtenerLimitacion(
                        comando,
                        out string limitacion))
                {
                    return Finalizar(
                        false,
                        "sin_comando",
                        limitacion,
                        pasos,
                        informar);
                }

                if (TryObtenerRespuestaNatural(
                        comando,
                        out string respuestaNatural))
                {
                    if (RequiereInvestigarAplicacion(pasos))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                """
                                No preguntes todavía al usuario. La apertura
                                directa por nombre falló y aún no has consultado
                                Get-StartApps después del fallo. Deduce el nombre
                                real usando el inventario incluido, ejecuta una
                                consulta Get-StartApps y continúa.
                                """));
                        continue;
                    }

                    if (RequiereReintentarBusquedaArchivos(pasos))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                CrearAyudaBusquedaArchivos()));
                        continue;
                    }

                    bool esInformativa =
                        PlanificadorTareasIA.EsPeticionInformativa(instruccion);
                    bool pideAclaracion = PlanificadorTareasIA
                        .PareceAclaracionPendiente(respuestaNatural);

                    if (esInformativa)
                    {
                        RevisionTareasControl revision =
                            await RevisarTareasAsync(
                                plan,
                                pasos,
                                cancellationToken,
                                respuestaNatural);

                        if (!revision.Completa)
                        {
                            tareasPendientes =
                                ObtenerTareasPendientes(plan, revision);
                            mensajes.Add(
                                new MensajeOllama(
                                    "assistant",
                                    comando));
                            mensajes.Add(
                                new MensajeOllama(
                                    "user",
                                    CrearRespuestaInformativaRechazada(
                                        plan,
                                        revision)));
                            continue;
                        }

                        if (pideAclaracion)
                        {
                            mensajes.Add(
                                new MensajeOllama(
                                    "assistant",
                                    comando));
                            mensajes.Add(
                                new MensajeOllama(
                                    "user",
                                    """
                                    Las tareas informativas ya tienen evidencia
                                    suficiente. No pidas una aclaración ni digas
                                    que falta la petición: responde `RESPONDER:`
                                    resumiendo los resultados reales.
                                    """));
                            continue;
                        }
                    }

                    return Finalizar(
                        esInformativa && !pideAclaracion,
                        esInformativa && !pideAclaracion
                            ? "respuesta"
                            : "requiere_aclaracion",
                        respuestaNatural,
                        pasos,
                        informar);
                }

                // Algunos modelos devuelven una explicación sin el prefijo
                // RESPONDER:. Nunca la envíes a PowerShell como si fuera un
                // comando: entrégala al móvil para que el usuario pueda
                // continuar la conversación.
                if (TryObtenerExplicacionNatural(
                        comando,
                        out string explicacion,
                        out _))
                {
                    if (RequiereInvestigarAplicacion(pasos))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                """
                                La explicación es prematura. Investiga primero
                                la aplicación con Get-StartApps y usa el AppID
                                real devuelto por Windows.
                                """));
                        continue;
                    }

                    if (RequiereReintentarBusquedaArchivos(pasos))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                explicacion));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                CrearAyudaBusquedaArchivos()));
                        continue;
                    }

                    bool esInformativa =
                        PlanificadorTareasIA.EsPeticionInformativa(instruccion);
                    bool pideAclaracion = PlanificadorTareasIA
                        .PareceAclaracionPendiente(explicacion);

                    if (esInformativa)
                    {
                        RevisionTareasControl revision =
                            await RevisarTareasAsync(
                                plan,
                                pasos,
                                cancellationToken,
                                explicacion);

                        if (!revision.Completa)
                        {
                            tareasPendientes =
                                ObtenerTareasPendientes(plan, revision);
                            mensajes.Add(
                                new MensajeOllama(
                                    "assistant",
                                    explicacion));
                            mensajes.Add(
                                new MensajeOllama(
                                    "user",
                                    CrearRespuestaInformativaRechazada(
                                        plan,
                                        revision)));
                            continue;
                        }

                        if (pideAclaracion)
                        {
                            mensajes.Add(
                                new MensajeOllama(
                                    "assistant",
                                    explicacion));
                            mensajes.Add(
                                new MensajeOllama(
                                    "user",
                                    """
                                    La petición original sigue visible en el
                                    contexto y sus tareas ya tienen evidencia.
                                    No solicites otra aclaración. Responde
                                    `RESPONDER:` con los resultados reales.
                                    """));
                            continue;
                        }
                    }

                    return Finalizar(
                        esInformativa && !pideAclaracion,
                        esInformativa && !pideAclaracion
                            ? "respuesta"
                            : "requiere_aclaracion",
                        explicacion,
                        pasos,
                        informar);
                }

                if (comando.Equals(
                        "FIN",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Informar(
                        informar,
                        new EventoControl(
                            "pensando",
                            "La IA está comprobando que no quede ninguna tarea pendiente."));
                    RevisionTareasControl revision =
                        await RevisarTareasAsync(
                            plan,
                            pasos,
                            cancellationToken);

                    if (!revision.Completa)
                    {
                        tareasPendientes =
                            ObtenerTareasPendientes(plan, revision);
                        string pendientes = revision.Pendientes.Count == 0
                            ? plan.Formatear()
                            : string.Join(
                                Environment.NewLine,
                                revision.Pendientes.Select(numero =>
                                    $"[ ] {numero}. {plan.Tareas[numero - 1]}"));

                        mensajes.Add(new MensajeOllama("assistant", "FIN"));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                $"""
                                FINALIZACIÓN RECHAZADA.

                                Aún faltan estas tareas:
                                {pendientes}

                                Motivo de la auditoría:
                                {revision.Motivo}

                                TÍTULOS DE VENTANA OBSERVADOS POR CONSOLA:
                                {FormatearTitulosObservados(pasos)}

                                Continúa con UN comando PowerShell que avance una
                                tarea pendiente. No repitas una acción ya
                                completada ni vuelvas a abrir una aplicación que
                                los comandos y procesos ya demuestran abierta.
                                Para una acción pendiente dentro de esa
                                aplicación, usa sólo su CLI, cmdlets, API o
                                protocolo invocable íntegramente desde consola.
                                Si no existe, responde `LIMITACION:` explicando
                                ese límite. No respondas FIN todavía.
                                """));
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
                    if (HayAperturaVerificada(pasos)
                        && tareasPendientes.Count > 0
                        && !tareasPendientes.Any(numero =>
                            TareaPareceApertura(
                                plan.Tareas[numero - 1])))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                $"""
                                No pidas confirmar si la aplicación está abierta:
                                ya está demostrado y no queda una tarea de
                                apertura. Usa sólo una interfaz de comandos
                                documentada para la tarea interna; si no existe,
                                responde explicando el límite.
                                """));
                        continue;
                    }

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

                string? bloqueoProcedencia =
                    ValidarProcedenciaAppId(comando, pasos);
                bloqueoProcedencia ??=
                    ValidarAdecuacionConsultaInformativa(
                        comando,
                        plan,
                        pasos);

                if (!EsComandoCompatibleConModoConsola(comando))
                {
                    bloqueoProcedencia =
                        "ControlPCIA funciona mediante comandos de consola; no se permite usar el subcomando legado 'ui'.";
                }

                if (bloqueoProcedencia is not null)
                {
                    var ejecucionBloqueada =
                        new ResultadoEjecucionPowerShell(
                            false,
                            -1,
                            string.Empty,
                            "BLOQUEADO: " + bloqueoProcedencia);
                    var pasoBloqueado = new ResultadoPasoControl(
                        pasos.Count + 1,
                        comando,
                        false,
                        -1,
                        string.Empty,
                        ejecucionBloqueada.Error);
                    pasos.Add(pasoBloqueado);

                    Informar(
                        informar,
                        new EventoControl(
                            "bloqueado",
                            ejecucionBloqueada.Error,
                            comando,
                            pasoBloqueado));
                    mensajes.Add(new MensajeOllama("assistant", comando));
                    mensajes.Add(
                        new MensajeOllama(
                            "user",
                            CrearResultadoBloqueado(
                                ejecucionBloqueada,
                                comando)));
                    continue;
                }

                bool aperturaSinTareaPendiente =
                    EsInicioAplicacion(comando)
                    && HayAperturaVerificada(pasos)
                    && !tareasPendientes.Any(numero =>
                        TareaPareceApertura(
                            plan.Tareas[numero - 1]));

                if (aperturaSinTareaPendiente)
                {
                    mensajes.Add(new MensajeOllama("assistant", comando));
                    mensajes.Add(
                        new MensajeOllama(
                            "user",
                            $"""
                            APERTURA RECHAZADA: las tareas de apertura ya tienen
                            evidencia y ninguna tarea de apertura sigue
                            pendiente. No abras otra instancia. Avanza la tarea
                            interna usando uno de estos WINDOW_TITLE literales:
                            {FormatearTitulosObservados(pasos)}
                            """));
                    continue;
                }

                ResultadoPasoControl? intentoFallidoIgual = pasos
                    .LastOrDefault(paso =>
                        paso.Comando.Equals(
                            comando,
                            StringComparison.OrdinalIgnoreCase)
                        && (!paso.Ejecutado || paso.CodigoSalida != 0));

                if (intentoFallidoIgual is not null)
                {
                    mensajes.Add(new MensajeOllama("assistant", comando));
                    mensajes.Add(
                        new MensajeOllama(
                            "user",
                            """
                            ESTRATEGIA RECHAZADA: ese mismo comando ya falló y
                            no se volverá a ejecutar. Usa la salida real de las
                            consultas anteriores y elige una estrategia
                            diferente. Si Get-StartApps devolvió un AppID, copia
                            ese AppID literal mediante
                            explorer.exe 'shell:AppsFolder\APPID'.
                            """));
                    continue;
                }

                bool accionRepetidaCorrecta =
                    pasos.Any(paso =>
                        paso.Ejecutado
                        && paso.CodigoSalida == 0
                        && paso.Comando.Equals(
                            comando,
                            StringComparison.OrdinalIgnoreCase));

                if (accionRepetidaCorrecta)
                {
                    mensajes.Add(new MensajeOllama("assistant", comando));
                    mensajes.Add(
                        new MensajeOllama(
                            "user",
                            """
                            ACCIÓN REPETIDA RECHAZADA: ese mismo comando ya se
                            ejecutó correctamente. No crees otra instancia ni
                            vuelvas a ejecutar la misma acción. Usa esa ejecución
                            como evidencia, comprueba otra tarea pendiente o
                            responde FIN si todo está completado.
                            """));
                    continue;
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

                var paso = new ResultadoPasoControl(
                    pasos.Count + 1,
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
                            comando,
                            ejecucion)
                        : CrearResultadoBloqueado(
                            ejecucion,
                            comando);

                if (ejecucion.Ejecutado
                    && ejecucion.CodigoSalida == 0
                    && EsInicioAplicacion(comando))
                {
                    Informar(
                        informar,
                        new EventoControl(
                            "pensando",
                            "ControlPCIA está comprobando por consola las ventanas abiertas."));
                    ResultadoEjecucionPowerShell comprobacion =
                        await EjecutorPowerShell.EjecutarAsync(
                            ComandoInventarioVentanas,
                            cancellationToken);
                    var pasoComprobacion = new ResultadoPasoControl(
                        pasos.Count + 1,
                        ComandoInventarioVentanas,
                        comprobacion.Ejecutado,
                        comprobacion.CodigoSalida,
                        comprobacion.Salida,
                        comprobacion.Error);
                    pasos.Add(pasoComprobacion);

                    Informar(
                        informar,
                        new EventoControl(
                            comprobacion.Ejecutado
                                ? "ejecutado"
                                : "bloqueado",
                            comprobacion.Ejecutado
                                ? "Ventanas comprobadas por consola."
                                : comprobacion.Error,
                            ComandoInventarioVentanas,
                            pasoComprobacion));

                    informacionResultado +=
                        Environment.NewLine
                        + Environment.NewLine
                        + "COMPROBACIÓN AUTOMÁTICA DE VENTANAS TRAS LA APERTURA:"
                        + Environment.NewLine
                        + (comprobacion.Ejecutado
                            ? CrearResultadoEjecutado(
                                ComandoInventarioVentanas,
                                comprobacion)
                            : CrearResultadoBloqueado(
                                comprobacion,
                                ComandoInventarioVentanas));
                }

                if (TryCrearRespuestaConsultasEstructuradas(
                        plan,
                        pasos,
                        out string respuestaEstructurada))
                {
                    bool aprendido =
                        await AprenderRecetaAsync(
                            instruccion,
                            pasos,
                            informar,
                            cancellationToken);

                    return Finalizar(
                        true,
                        "respuesta",
                        respuestaEstructurada,
                        pasos,
                        informar,
                        aprendido);
                }

                mensajes.Add(new MensajeOllama("assistant", comando));
                mensajes.Add(new MensajeOllama("user", informacionResultado));
            }

            return Finalizar(
                false,
                "limite_pasos",
                CrearMensajeLimitePasos(pasos),
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
        ResultadoEjecucionPowerShell resultado,
        string comando)
    {
        string ayudaBusqueda = Regex.IsMatch(
                comando,
                @"(?:^|[;|]\s*)(?:Get-ChildItem|gci|dir|ls)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            ? Environment.NewLine + CrearAyudaBusquedaArchivos()
            : string.Empty;

        return $"""
            EL COMANDO FUE BLOQUEADO Y NO SE EJECUTÓ.

            Código de salida:
            {resultado.CodigoSalida}

            Error:
            {LimitarTexto(resultado.Error)}

            La petición original NO está completada. No inventes rutas de
            instalación ni repitas la misma estrategia cambiando carpetas al
            azar. Consulta el sistema si necesitas descubrir cómo está registrada
            una aplicación y después intenta otra estrategia permitida. No
            uses teclado, ratón ni automatización de interfaz. Si no existe una
            alternativa íntegramente invocable por consola, responde al usuario
            explicando ese límite.

            {ayudaBusqueda}
            """;
    }

    private static string CrearMensajeLimitePasos(
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        ResultadoPasoControl? ultimo = pasos.LastOrDefault();
        string detalle = ultimo is null
            ? string.Empty
            : !string.IsNullOrWhiteSpace(ultimo.Error)
                ? $" Último error de PowerShell: {LimitarTexto(ultimo.Error)}"
                : ultimo.CodigoSalida != 0
                    ? $" Último código de salida: {ultimo.CodigoSalida}."
                    : string.Empty;

        return
            $"No se ha completado la petición tras {MaximoPasos} pasos.{detalle} " +
            "Puedes explicarme qué resultado esperabas o repetir la orden con más detalle.";
    }

    private static string CrearResultadoEjecutado(
        string comando,
        ResultadoEjecucionPowerShell resultado)
    {
        bool consultaAplicaciones = Regex.IsMatch(
            comando,
            @"(?:^|[;|]\s*)Get-StartApps\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        string instruccionAppId =
            consultaAplicaciones
            && string.IsNullOrWhiteSpace(resultado.Salida)
                ? """

                  GET-STARTAPPS NO DEVOLVIÓ NINGUNA COINCIDENCIA.
                  No inventes un Name ni un AppID. Revisa la lista de nombres
                  registrados incluida con la petición, deduce el nombre real
                  aunque esté en otro idioma y repite la consulta con ese nombre
                  o con una parte más amplia.
                  """
                : consultaAplicaciones
                    ? $"""

                  GET-STARTAPPS HA DEVUELTO ESTOS DATOS:
                  Copia un AppID completo y literal de la salida adecuada.
                  El siguiente comando de apertura debe ser exactamente:
                  explorer.exe 'shell:AppsFolder\APPID_COMPLETO'
                  No uses Name con Start-Process, no uses ArgumentList y no
                  inventes un ejecutable.

                  IDENTIFICADORES LITERALES PRESENTES EN STDOUT:
                  {FormatearAppIdsLiterales(resultado.Salida)}
                  """
                    : string.Empty;

        return $"""
            RESULTADO DEL COMANDO:

            Código de salida:
            {resultado.CodigoSalida}

            Salida:
            {LimitarTexto(resultado.Salida)}

            Error:
            {LimitarTexto(resultado.Error)}

            Decide si la petición original ya está completada o si necesitas
            ejecutar otro comando. Revisa TODAS las tareas de la petición, no
            sólo la primera. Si la salida está vacía o no demuestra el resultado
            esperado, ejecuta una consulta de comprobación por consola. Sólo pide
            una aclaración cuando la intención del usuario sea realmente ambigua
            y Windows no pueda aportar el dato.

            {instruccionAppId}
            """;
    }

    internal static bool RequiereVerificacionTrasCambio(
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        int ultimoCambio = -1;
        int ultimaObservacion = -1;

        for (int indice = 0; indice < pasos.Count; indice++)
        {
            ResultadoPasoControl paso = pasos[indice];

            if (!paso.Ejecutado || paso.CodigoSalida != 0)
            {
                continue;
            }

            if (EsComandoQueCambiaEstado(paso.Comando))
            {
                ultimoCambio = indice;
            }

            if (ultimoCambio >= 0
                && indice > ultimoCambio
                && EsComandoDeObservacion(paso.Comando)
                && ObservacionDemuestraCambio(
                    paso,
                    pasos[ultimoCambio].Comando))
            {
                ultimaObservacion = indice;
            }
        }

        return ultimoCambio >= 0 && ultimaObservacion < ultimoCambio;
    }

    private static bool ObservacionDemuestraCambio(
        ResultadoPasoControl observacion,
        string comandoCambio)
    {
        if (EsInicioAplicacion(comandoCambio))
        {
            return !string.IsNullOrWhiteSpace(observacion.Salida);
        }

        return true;
    }

    private static bool EsComandoQueCambiaEstado(string comando)
    {
        return Regex.IsMatch(
                   comando,
                   @"(?:^|[;|]\s*)(?:Stop-Process|spps|kill|New-Item|ni|md|mkdir|Copy-Item|cp|copy|cpi)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               || Regex.IsMatch(
                   comando,
                   @"^\s*winget(?:\.exe)?\s+install\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               || Regex.IsMatch(
                   comando,
                   @"^\s*explorer(?:\.exe)?\s+['""]shell:AppsFolder\\",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               || Regex.IsMatch(
                   comando,
                   @"(?:^|[;|\r\n]\s*)(?:Start-Process|start|saps)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               || comando.Contains(
                   ".CloseMainWindow(",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool EsComandoDeObservacion(string comando)
    {
        return Regex.IsMatch(
                   comando,
                   @"(?:^|[;|]\s*)(?:Get-Process|gps|Test-Path|Get-Item|gi|Get-ChildItem|gci|dir|ls)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               || Regex.IsMatch(
                   comando,
                   @"^\s*winget(?:\.exe)?\s+(?:list|show)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool EsInicioAplicacion(string comando)
    {
        return Regex.IsMatch(
            comando,
            @"(?:^|[;|\r\n]\s*)(?:Start-Process|start|saps)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || Regex.IsMatch(
                comando,
                @"^\s*explorer(?:\.exe)?\s+['""]shell:AppsFolder\\",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static bool EsComandoCompatibleConModoConsola(string comando)
    {
        return !Regex.IsMatch(
            comando,
            @"\bControlPCIA(?:\.exe)?\s+(?:ui|window\s+keys)\b|\.SendKeys\s*\(|\.AppActivate\s*\(|\bNew-Object\b[^\r\n;|]*-ComObject\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static string? ValidarProcedenciaAppId(
        string comando,
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        Match destino = Regex.Match(
            comando,
            @"^\s*explorer(?:\.exe)?\s+(?:""shell:AppsFolder\\(?<doble>[^""]+)""|'shell:AppsFolder\\(?<simple>[^']+)')\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!destino.Success)
        {
            return null;
        }

        string appId = destino.Groups["doble"].Success
            ? destino.Groups["doble"].Value
            : destino.Groups["simple"].Value;
        bool procedeDeWindows = pasos.Any(paso =>
            paso.Ejecutado
            && paso.CodigoSalida == 0
            && Regex.IsMatch(
                paso.Comando,
                @"(?:^|[;|]\s*)Get-StartApps\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            && paso.Salida.Contains(
                appId,
                StringComparison.OrdinalIgnoreCase));

        return procedeDeWindows
            ? null
            : "El AppID no aparece literalmente en la salida de una consulta Get-StartApps ejecutada en esta petición. Consulta Windows y copia el identificador real antes de abrirlo.";
    }

    internal static string? ValidarAdecuacionConsultaInformativa(
        string comando,
        PlanTareasControl plan,
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        bool faltaConsultaVentanas =
            plan.Tareas
                .Select((tarea, indice) => (tarea, indice))
                .Any(elemento =>
                    TareaPareceConsultaVentanas(elemento.tarea)
                    && !HayEvidenciaConsultaVentanas(pasos));

        if (faltaConsultaVentanas)
        {
            bool consultaProcesos = Regex.IsMatch(
                comando,
                @"(?:^|[;|]\s*)(?:Get-Process|gps|tasklist)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (consultaProcesos
                && (!comando.Contains(
                        "MainWindowTitle",
                        StringComparison.OrdinalIgnoreCase)
                    || !comando.Contains(
                        "PROCESS_NAME=",
                        StringComparison.OrdinalIgnoreCase)
                    || !comando.Contains(
                        "WINDOW_TITLE=",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return "La petición solicita programas con ventana abierta. Consulta Get-Process filtrando MainWindowTitle y publica cada resultado completo como PROCESS_NAME=... y WINDOW_TITLE=... mediante Write-Output; no uses una tabla.";
            }
        }

        bool faltaBusquedaArchivos =
            plan.Tareas.Any(tarea =>
                TareaPareceBusquedaArchivo(tarea)
                && !HayEvidenciaBusquedaArchivo(pasos));
        bool consultaArchivos = Regex.IsMatch(
            comando,
            @"(?:^|[;|]\s*)(?:Get-ChildItem|gci|dir|ls)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (faltaBusquedaArchivos && consultaArchivos)
        {
            string? nombreOmitido =
                ObtenerNombresArchivoLiterales(plan)
                    .FirstOrDefault(nombre =>
                        !comando.Contains(
                            nombre,
                            StringComparison.OrdinalIgnoreCase));

            if (nombreOmitido is not null)
            {
                return $"La búsqueda debe conservar literalmente el nombre solicitado: '{nombreOmitido}'. No quites ni cambies su extensión.";
            }

            bool tieneLimitePrevio = Regex.IsMatch(
                comando,
                @"\|\s*(?:Select-Object|select)\s+-First\s+(?:[1-9]|1\d|20)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!tieneLimitePrevio)
            {
                return "La búsqueda recursiva debe limitarse antes de formatear la salida: añade `| Select-Object -First 20 |` inmediatamente después de Get-ChildItem.";
            }
        }

        return faltaBusquedaArchivos
               && consultaArchivos
               && (!comando.Contains(
                       "FullName",
                       StringComparison.OrdinalIgnoreCase)
                   || !comando.Contains(
                       "FULL_NAME=",
                       StringComparison.OrdinalIgnoreCase))
            ? "La petición solicita la ubicación exacta de un archivo. Usa Get-ChildItem y publica cada ruta completa como FULL_NAME=... mediante Write-Output; no uses una tabla que pueda truncar la ruta."
            : null;
    }

    private static IReadOnlyList<string> ObtenerNombresArchivoLiterales(
        PlanTareasControl plan)
    {
        return plan.Tareas
            .SelectMany(tarea =>
                Regex.Matches(
                        tarea,
                        @"(?<![\p{L}\p{N}_.-])[\p{L}\p{N}_()-]+(?:\.[\p{L}\p{N}_()-]+)+(?![\p{L}\p{N}_.-])",
                        RegexOptions.CultureInvariant)
                    .Select(coincidencia => coincidencia.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    internal static bool TryObtenerRespuestaNatural(
        string respuesta,
        out string texto)
    {
        const string prefijo = "RESPONDER:";
        string limpia = respuesta.Trim();

        if (!limpia.StartsWith(
                prefijo,
                StringComparison.OrdinalIgnoreCase))
        {
            texto = string.Empty;
            return false;
        }

        texto = limpia[prefijo.Length..].Trim();

        if (texto.Length == 0)
        {
            texto =
                "No he podido preparar una respuesta útil con la información disponible.";
        }
        else if (texto.Length > 1200)
        {
            texto = texto[..1200].Trim();
        }

        return true;
    }

    internal static bool TryObtenerLimitacion(
        string respuesta,
        out string texto)
    {
        const string prefijo = "LIMITACION:";
        string limpia = respuesta.Trim();

        if (!limpia.StartsWith(
                prefijo,
                StringComparison.OrdinalIgnoreCase))
        {
            texto = string.Empty;
            return false;
        }

        texto = limpia[prefijo.Length..].Trim();

        if (texto.Length == 0)
        {
            texto =
                "Esta acción no dispone de un comando, API o protocolo permitido que pueda ejecutarse íntegramente desde consola.";
        }

        return true;
    }

    private static bool TryObtenerExplicacionNatural(
        string respuesta,
        out string texto,
        out bool completada)
    {
        string limpia = respuesta.Trim();
        string minusculas = limpia.ToLowerInvariant();

        bool pareceExplicacion =
            minusculas.StartsWith("la petición", StringComparison.Ordinal)
            || minusculas.StartsWith("el comando", StringComparison.Ordinal)
            || minusculas.StartsWith("la salida", StringComparison.Ordinal)
            || minusculas.Contains("ejecuta el siguiente comando", StringComparison.Ordinal)
            || minusculas.Contains("si necesitas información", StringComparison.Ordinal);

        if (!pareceExplicacion)
        {
            texto = string.Empty;
            completada = false;
            return false;
        }

        texto = limpia.Length > 1200 ? limpia[..1200].Trim() : limpia;
        completada =
            !minusculas.Contains("no está completada", StringComparison.Ordinal)
            && !minusculas.Contains("aún no", StringComparison.Ordinal)
            && !minusculas.Contains("todavía no", StringComparison.Ordinal)
            && !minusculas.Contains("no proporcionó", StringComparison.Ordinal);
        return true;
    }

    private static async Task<RevisionTareasControl> RevisarTareasAsync(
        PlanTareasControl plan,
        IReadOnlyList<ResultadoPasoControl> pasos,
        CancellationToken cancellationToken,
        string? respuestaCandidata = null)
    {
        if (RequiereVerificacionTrasCambio(pasos))
        {
            return new RevisionTareasControl(
                false,
                Enumerable.Range(1, plan.Tareas.Count).ToArray(),
                "El último cambio de estado todavía no se ha comprobado mediante una consulta de consola posterior.");
        }

        int[] pendientesDeterministas =
            plan.Tareas
                .Select((tarea, indice) => (tarea, numero: indice + 1))
                .Where(elemento =>
                    TareaPareceConsultaVentanas(elemento.tarea)
                        && !HayEvidenciaConsultaVentanas(pasos)
                    || TareaPareceBusquedaArchivo(elemento.tarea)
                        && !HayEvidenciaBusquedaArchivo(pasos))
                .Select(elemento => elemento.numero)
                .ToArray();

        if (pendientesDeterministas.Length > 0)
        {
            return new RevisionTareasControl(
                false,
                pendientesDeterministas,
                "Falta evidencia estructurada completa: las ventanas requieren Get-Process con líneas PROCESS_NAME/WINDOW_TITLE y la localización de archivos requiere Get-ChildItem con líneas FULL_NAME, sin tablas truncadas.");
        }

        return await PlanificadorTareasIA.RevisarAsync(
            plan,
            pasos,
            cancellationToken,
            respuestaCandidata);
    }

    private static string CrearRespuestaInformativaRechazada(
        PlanTareasControl plan,
        RevisionTareasControl revision)
    {
        string pendientes = revision.Pendientes.Count == 0
            ? plan.Formatear()
            : string.Join(
                Environment.NewLine,
                revision.Pendientes.Select(numero =>
                    $"[ ] {numero}. {plan.Tareas[numero - 1]}"));

        return $"""
            RESPUESTA INFORMATIVA RECHAZADA.

            Aún faltan estas tareas:
            {pendientes}

            Motivo de la auditoría:
            {revision.Motivo}

            No expliques ni resumas todavía. Devuelve UN comando PowerShell
            que obtenga evidencia real para una tarea pendiente. Para programas
            con ventana usa MainWindowTitle. Para localizar archivos usa
            Get-ChildItem con un filtro literal y devuelve FullName sin leer
            contenido. Sólo responde `RESPONDER:` cuando todas las tareas estén
            demostradas. Si una acción no tiene interfaz de consola, responde
            `LIMITACION:` con el motivo concreto.
            """;
    }

    private static IReadOnlySet<int> ObtenerTareasPendientes(
        PlanTareasControl plan,
        RevisionTareasControl revision)
    {
        return (revision.Pendientes.Count == 0
                ? Enumerable.Range(1, plan.Tareas.Count)
                : revision.Pendientes)
            .ToHashSet();
    }

    private static bool TareaPareceConsultaVentanas(string tarea)
    {
        string normalizada = InventarioTexto.Normalizar(tarea);

        return normalizada.Contains("ventana", StringComparison.Ordinal)
               && (normalizada.Contains("abiert", StringComparison.Ordinal)
                   || normalizada.Contains("visible", StringComparison.Ordinal))
               || normalizada.Contains("program", StringComparison.Ordinal)
               && normalizada.Contains("abiert", StringComparison.Ordinal)
               || normalizada.Contains("aplicacion", StringComparison.Ordinal)
               && normalizada.Contains("abiert", StringComparison.Ordinal);
    }

    private static bool TareaPareceBusquedaArchivo(string tarea)
    {
        string normalizada = InventarioTexto.Normalizar(tarea);

        return (normalizada.Contains("archivo", StringComparison.Ordinal)
                || normalizada.Contains("fichero", StringComparison.Ordinal))
               && (normalizada.Contains("busc", StringComparison.Ordinal)
                   || normalizada.Contains("localiz", StringComparison.Ordinal)
                   || normalizada.Contains("encontr", StringComparison.Ordinal)
                   || normalizada.Contains("donde", StringComparison.Ordinal));
    }

    private static bool HayEvidenciaConsultaVentanas(
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        return pasos.Any(paso =>
            EsEvidenciaConsultaVentanas(paso));
    }

    private static bool HayEvidenciaBusquedaArchivo(
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        return pasos.Any(paso =>
            EsEvidenciaBusquedaArchivo(paso));
    }

    internal static bool TryCrearRespuestaConsultasEstructuradas(
        PlanTareasControl plan,
        IReadOnlyList<ResultadoPasoControl> pasos,
        out string respuesta)
    {
        bool consultaVentanas =
            plan.Tareas.Any(TareaPareceConsultaVentanas);
        bool buscaArchivos =
            plan.Tareas.Any(TareaPareceBusquedaArchivo);
        bool soloConsultasEstructuradas =
            plan.Tareas.All(tarea =>
                TareaPareceConsultaVentanas(tarea)
                || TareaPareceBusquedaArchivo(tarea)
                || TareaPareceRestriccionDeLectura(tarea));

        if (!soloConsultasEstructuradas
            || !consultaVentanas && !buscaArchivos
            || consultaVentanas && !HayEvidenciaConsultaVentanas(pasos)
            || buscaArchivos && !HayEvidenciaBusquedaArchivo(pasos))
        {
            respuesta = string.Empty;
            return false;
        }

        var secciones = new List<string>();

        if (consultaVentanas)
        {
            ResultadoPasoControl pasoVentanas =
                pasos.Last(EsEvidenciaConsultaVentanas);
            string salida = LimitarTexto(pasoVentanas.Salida).Trim();
            secciones.Add(
                salida.Length == 0
                    ? "Programas con ventana abierta:\nNo se detectó ninguno."
                    : "Programas con ventana abierta:\n" + salida);
        }

        if (buscaArchivos)
        {
            ResultadoPasoControl pasoArchivos =
                pasos.Last(EsEvidenciaBusquedaArchivo);
            string salida = LimitarTexto(pasoArchivos.Salida).Trim();
            secciones.Add(
                salida.Length == 0
                    ? "Archivos encontrados:\nNo se encontró ninguno que coincida."
                    : "Rutas encontradas (sin abrir ni leer su contenido):\n"
                      + salida);
        }

        respuesta = string.Join(
            Environment.NewLine + Environment.NewLine,
            secciones);
        return true;
    }

    private static bool EsEvidenciaConsultaVentanas(
        ResultadoPasoControl paso)
    {
        return paso.Ejecutado
               && paso.CodigoSalida == 0
               && Regex.IsMatch(
                   paso.Comando,
                   @"(?:^|[;|]\s*)(?:Get-Process|gps)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               && paso.Comando.Contains(
                   "MainWindowTitle",
                   StringComparison.OrdinalIgnoreCase)
               && paso.Comando.Contains(
                   "PROCESS_NAME=",
                   StringComparison.OrdinalIgnoreCase)
               && paso.Comando.Contains(
                   "WINDOW_TITLE=",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool EsEvidenciaBusquedaArchivo(
        ResultadoPasoControl paso)
    {
        return paso.Ejecutado
               && paso.CodigoSalida == 0
               && Regex.IsMatch(
                   paso.Comando,
                   @"(?:^|[;|]\s*)(?:Get-ChildItem|gci|dir|ls)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               && paso.Comando.Contains(
                   "FullName",
                   StringComparison.OrdinalIgnoreCase)
               && paso.Comando.Contains(
                   "FULL_NAME=",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool TareaPareceRestriccionDeLectura(string tarea)
    {
        string normalizada = InventarioTexto.Normalizar(tarea);

        return (normalizada.Contains("sin abrir", StringComparison.Ordinal)
                || normalizada.Contains("no abrir", StringComparison.Ordinal)
                || normalizada.Contains("sin leer", StringComparison.Ordinal)
                || normalizada.Contains("no leer", StringComparison.Ordinal))
               && (normalizada.Contains("archivo", StringComparison.Ordinal)
                   || normalizada.Contains("contenido", StringComparison.Ordinal));
    }

    private static ResultadoControl Finalizar(
        bool completado,
        string estado,
        string mensaje,
        IReadOnlyList<ResultadoPasoControl> pasos,
        Action<EventoControl>? informar,
        bool aprendido = false)
    {
        if (!completado)
        {
            mensaje = AñadirDetalleUltimoFallo(mensaje, pasos);
        }

        Informar(informar, new EventoControl("final", mensaje));

        return new ResultadoControl(
            completado,
            estado,
            mensaje,
            pasos.ToArray(),
            aprendido);
    }

    private static string AñadirDetalleUltimoFallo(
        string mensaje,
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        ResultadoPasoControl? ultimo = pasos.LastOrDefault();

        if (ultimo is null
            || ultimo.CodigoSalida == 0
            || string.IsNullOrWhiteSpace(ultimo.Error)
            || mensaje.Contains(
                ultimo.Error,
                StringComparison.OrdinalIgnoreCase))
        {
            return mensaje;
        }

        return mensaje
            + Environment.NewLine
            + "Error real de PowerShell: "
            + LimitarTexto(ultimo.Error);
    }

    private static IReadOnlyList<MensajeConversacionControl>
        NormalizarContexto(
            IReadOnlyList<MensajeConversacionControl>? contexto)
    {
        if (contexto is null || contexto.Count == 0)
        {
            return [];
        }

        const int maximoMensajes = 12;
        const int maximoCaracteresMensaje = 800;
        const int maximoCaracteresTotal = 6000;
        var normalizados = new List<MensajeConversacionControl>();
        int caracteres = 0;

        foreach (MensajeConversacionControl mensaje in contexto
                     .TakeLast(maximoMensajes))
        {
            string rol = mensaje.Rol.Trim().ToLowerInvariant();
            string texto = mensaje.Texto.Trim();

            if (rol is not ("user" or "assistant")
                || texto.Length == 0
                || texto.Any(caracter =>
                    char.IsControl(caracter)
                    && caracter is not '\r' and not '\n' and not '\t'))
            {
                continue;
            }

            if (texto.Length > maximoCaracteresMensaje)
            {
                texto = texto[..maximoCaracteresMensaje].Trim();
            }

            if (caracteres + texto.Length > maximoCaracteresTotal)
            {
                break;
            }

            normalizados.Add(new MensajeConversacionControl(rol, texto));
            caracteres += texto.Length;
        }

        return normalizados;
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
            RecetaReferencia[] recetasDeConsola = recetas
                .Where(receta => receta.Comandos.All(comando => !EsComandoInterfazProhibido(comando)))
                .ToArray();

            if (recetasDeConsola.Length > 0)
            {
                Informar(
                    informar,
                    new EventoControl(
                        "memoria",
                        $"Se encontraron {recetasDeConsola.Length} recetas relacionadas."));
            }

            return recetasDeConsola;
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
        RecetaReferencia[] recetasDeConsola = recetas
            .Where(receta => receta.Comandos.All(comando => !EsComandoInterfazProhibido(comando)))
            .ToArray();

        if (recetasDeConsola.Length == 0)
        {
            return "No hay recetas relacionadas. Investiga con consultas seguras si lo necesitas.";
        }

        const int limite = 8000;
        var resumen = new StringBuilder();

        for (int indice = 0; indice < recetasDeConsola.Length; indice++)
        {
            RecetaReferencia receta = recetasDeConsola[indice];
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

    private static bool EsComandoInterfazProhibido(string comando)
    {
        return Regex.IsMatch(
            comando,
            @"\bControlPCIA(?:\.exe)?\s+(?:ui|window\s+keys)\b|\.SendKeys\s*\(|\.AppActivate\s*\(|\bNew-Object\b[^\r\n;|]*-ComObject\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static void Informar(
        Action<EventoControl>? informar,
        EventoControl evento)
    {
        informar?.Invoke(evento);
    }

    internal static string LimpiarComando(string respuesta)
    {
        string resultado = respuesta.Trim();
        Match bloque = Regex.Match(
            resultado,
            @"```(?:powershell|ps1|pwsh)?\s*(?:\r?\n)?(?<comando>.*?)(?:```|$)",
            RegexOptions.IgnoreCase
            | RegexOptions.CultureInvariant
            | RegexOptions.Singleline);

        if (bloque.Success
            && !string.IsNullOrWhiteSpace(
                bloque.Groups["comando"].Value))
        {
            return bloque.Groups["comando"].Value.Trim();
        }

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

        resultado = resultado.Trim();

        if (resultado.Length >= 2
            && resultado[0] == '`'
            && resultado[^1] == '`'
            && !resultado.StartsWith(
                "```",
                StringComparison.Ordinal))
        {
            resultado = resultado[1..^1];
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

    private static string FormatearAppIdsLiterales(string salida)
    {
        var identificadores = new List<string>();

        foreach (string lineaOriginal in salida.Split('\n'))
        {
            string linea = lineaOriginal.Trim();

            if (linea.StartsWith(
                    "APP_ID=",
                    StringComparison.OrdinalIgnoreCase))
            {
                identificadores.Add(linea["APP_ID=".Length..].Trim());
                continue;
            }

            if (Regex.IsMatch(
                    linea,
                    @"^(?:\{[0-9A-F-]{36}\}\\.+|[A-Za-z][A-Za-z0-9_.-]+(?:![A-Za-z0-9_.-]+)?)$",
                    RegexOptions.IgnoreCase
                    | RegexOptions.CultureInvariant))
            {
                identificadores.Add(linea);
                continue;
            }

            Match tabla = Regex.Match(
                linea,
                @"^(?<nombre>.+?)\s{2,}(?<appid>(?:\{[0-9A-F-]{36}\}\\|[A-Za-z][A-Za-z0-9_.-]+)[^\r\n]*)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!tabla.Success)
            {
                continue;
            }

            string nombre = InventarioTexto.Normalizar(
                tabla.Groups["nombre"].Value);

            if (nombre.Contains(
                    "desinstalar",
                    StringComparison.Ordinal)
                || nombre.Contains(
                    "uninstall",
                    StringComparison.Ordinal))
            {
                continue;
            }

            identificadores.Add(
                tabla.Groups["appid"].Value.Trim());
        }

        return identificadores.Count == 0
            ? "No se pudo aislar una fila; ejecuta otra consulta con formato APP_ID=."
            : string.Join(
                Environment.NewLine,
                identificadores
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(appId => "APP_ID_LITERAL=" + appId));
    }

    private static async Task<string> ObtenerNombresAplicacionesAsync(
        CancellationToken cancellationToken)
    {
        ResultadoEjecucionPowerShell resultado =
            await EjecutorPowerShell.EjecutarAsync(
                "Get-StartApps | Where-Object { $_.Name -notmatch '^(Desinstalar|Uninstall|Remove)' } | Select-Object -ExpandProperty Name | Sort-Object -Unique",
                cancellationToken);

        if (!resultado.Ejecutado
            || resultado.CodigoSalida != 0
            || string.IsNullOrWhiteSpace(resultado.Salida))
        {
            return "Windows no pudo proporcionar el inventario de nombres. Usa Get-StartApps para consultarlo.";
        }

        return LimitarTexto(resultado.Salida);
    }

    internal static bool PareceDatoDeAplicacionSinComando(string respuesta)
    {
        return Regex.IsMatch(
            respuesta,
            @"^\s*APP_(?:NAME|ID)\s*=",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static bool RequiereInvestigarAplicacion(
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        int ultimoInicioFallido = -1;
        int ultimaConsultaPosterior = -1;

        for (int indice = 0; indice < pasos.Count; indice++)
        {
            ResultadoPasoControl paso = pasos[indice];

            if (Regex.IsMatch(
                    paso.Comando,
                    @"(?:^|[;|\r\n]\s*)(?:Start-Process|start|saps)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                && (!paso.Ejecutado || paso.CodigoSalida != 0))
            {
                ultimoInicioFallido = indice;
            }

            if (ultimoInicioFallido >= 0
                && indice > ultimoInicioFallido
                && paso.Ejecutado
                && paso.CodigoSalida == 0
                && Regex.IsMatch(
                    paso.Comando,
                    @"(?:^|[;|]\s*)Get-StartApps\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                ultimaConsultaPosterior = indice;
            }
        }

        return ultimoInicioFallido >= 0
               && ultimaConsultaPosterior < ultimoInicioFallido;
    }

    internal static bool RequiereReintentarBusquedaArchivos(
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        int ultimaBusquedaFallida = -1;
        int ultimaBusquedaCorrectaPosterior = -1;

        for (int indice = 0; indice < pasos.Count; indice++)
        {
            ResultadoPasoControl paso = pasos[indice];
            bool esBusqueda = Regex.IsMatch(
                paso.Comando,
                @"(?:^|[;|]\s*)(?:Get-ChildItem|gci|dir|ls)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!esBusqueda)
            {
                continue;
            }

            if (!paso.Ejecutado || paso.CodigoSalida != 0)
            {
                ultimaBusquedaFallida = indice;
                continue;
            }

            if (ultimaBusquedaFallida >= 0
                && indice > ultimaBusquedaFallida)
            {
                ultimaBusquedaCorrectaPosterior = indice;
            }
        }

        return ultimaBusquedaFallida >= 0
               && ultimaBusquedaCorrectaPosterior
               < ultimaBusquedaFallida;
    }

    private static string CrearAyudaBusquedaArchivos()
    {
        string rutas = string.Join(
            ",",
            ValidadorPowerShell
                .ObtenerRaicesBusquedaPermitidas()
                .Select(ruta =>
                    $"'{ruta.Replace("'", "''", StringComparison.Ordinal)}'"));

        return $$"""
            BÚSQUEDA DE ARCHIVOS:
            No pidas al usuario una ruta que Windows ya conoce y no uses
            variables de entorno. Reintenta con las raíces literales
            autorizadas:
            Get-ChildItem -LiteralPath {{rutas}} -Filter 'NOMBRE' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 20 | ForEach-Object { Write-Output ('FULL_NAME=' + $_.FullName); Write-Output ('LENGTH=' + $_.Length); Write-Output ('LAST_WRITE_TIME=' + $_.LastWriteTime) }
            Sustituye sólo NOMBRE por el nombre o patrón literal solicitado.
            No abras ni leas el contenido.
            """;
    }

    internal static bool HayAperturaVerificada(
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        int ultimaApertura = -1;

        for (int indice = 0; indice < pasos.Count; indice++)
        {
            ResultadoPasoControl paso = pasos[indice];

            if (paso.Ejecutado
                && paso.CodigoSalida == 0
                && EsInicioAplicacion(paso.Comando))
            {
                ultimaApertura = indice;
                continue;
            }

            if (ultimaApertura >= 0
                && indice > ultimaApertura
                && paso.Ejecutado
                && paso.CodigoSalida == 0
                && Regex.IsMatch(
                    paso.Comando,
                    @"(?:^|[;|]\s*)Get-Process\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                && Regex.IsMatch(
                    paso.Salida,
                    @"(?:^|\r?\n)WINDOW_TITLE=.+(?:\r?\n|$)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TareaPareceApertura(string tarea)
    {
        string normalizada = InventarioTexto.Normalizar(tarea);
        return Regex.IsMatch(
            normalizada,
            @"\b(?:abre|abrir|inicia|iniciar|lanza|lanzar|ejecuta|ejecutar|open|start|launch)\b",
            RegexOptions.CultureInvariant);
    }

    private static string FormatearTitulosObservados(
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        string[] titulos = pasos
            .SelectMany(paso =>
                Regex.Matches(
                        paso.Salida,
                        @"(?:^|\r?\n)WINDOW_TITLE=(?<titulo>[^\r\n]+)",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    .Select(coincidencia =>
                        coincidencia.Groups["titulo"].Value.Trim()))
            .Where(titulo => titulo.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToArray();

        return titulos.Length == 0
            ? "No hay títulos observados."
            : string.Join(
                Environment.NewLine,
                titulos.Select(titulo => "WINDOW_TITLE=" + titulo));
    }
}
