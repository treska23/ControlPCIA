using System.Text;
using System.Text.RegularExpressions;

namespace ControlPCIA;

internal static class ControlWindows
{
    private const int MaximoPasosBase = 24;
    private const int PasosPorTarea = 6;
    private const int MaximoPasosAbsoluto = 96;
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
                "La IA está entendiendo y planificando la orden."));
        PlanTareasControl plan =
            await PlanificadorTareasIA.CrearAsync(
                instruccion,
                contexto,
                cancellationToken);

        if (!string.IsNullOrWhiteSpace(plan.Pregunta))
        {
            return Finalizar(
                false,
                "requiere_aclaracion",
                plan.Pregunta,
                [],
                informar);
        }

        int maximoPasos =
            CalcularMaximoPasos(plan.Tareas.Count);
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

        if (TraductorRecetasConocidas.PuedeResolverPlan(plan))
        {
            return await ControlarConRecetaConocidaAsync(
                instruccion,
                plan,
                recetas,
                informar,
                cancellationToken);
        }

        IReadOnlyList<string> recursosAprendidos =
            ObtenerOrigenesCopyItemAprendidos(recetas);
        IReadOnlyList<string> carpetasAprendidas =
            ObtenerCarpetasAplicacionesAprendidas(recetas);

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
        string carpetasDatos =
            string.Join(
                Environment.NewLine,
                ObtenerCarpetasDatosSugeridas()
                    .Select(ruta => "- " + ruta));
        string carpetasAplicaciones =
            string.Join(
                Environment.NewLine,
                ObtenerCarpetasAplicacionesSugeridas()
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
                - Si falta un dato que sólo puede decidir el usuario, responde
                  PREGUNTAR: seguido de una única pregunta concreta. Antes de
                  preguntar, consulta la memoria, Windows y los valores
                  predeterminados de la aplicación. No preguntes por una ruta,
                  nombre o aplicación que ya figure en la petición o en el
                  contexto de conversación.
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
                - Si no conoces la solución, puedes investigar con
                  Get-Command, Get-Help, Get-StartApps y consultas del sistema.
                - No necesitas Internet para saber qué aplicaciones hay
                  instaladas: consulta primero el inventario local de Windows.
                - Investigar localmente es tu responsabilidad. Nunca preguntes al
                  usuario si deseas buscar una aplicación, un comando, una
                  plantilla o un valor predeterminado: haz la consulta y
                  continúa. Guarda como receta la solución comprobada para no
                  repetir la investigación la próxima vez.

                NAVEGACIÓN WEB:

                - Para abrir una página o una búsqueda web pública, usa
                  Start-Process con una URL literal http/https como destino.
                - Puedes construir una URL de búsqueda literal a partir de la
                  petición, por ejemplo la página de resultados de un buscador.
                - Puedes usar el navegador, Invoke-WebRequest,
                  Invoke-RestMethod, gestores de paquetes u otras herramientas
                  de consola para consultar o descargar cuando la petición lo
                  requiera.

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
                - Para localizar archivos puedes usar `Get-ChildItem` en
                  cualquier unidad local. Cuando haya muchos resultados, es
                  conveniente limitar y publicar rutas completas, por ejemplo:
                  `Select-Object -First 20 | ForEach-Object { Write-Output
                  ('FULL_NAME=' + $_.FullName); Write-Output ('LENGTH=' +
                  $_.Length); Write-Output ('LAST_WRITE_TIME=' +
                  $_.LastWriteTime) }`.
                  Puedes usar rutas literales, variables de entorno y las
                  herramientas normales de PowerShell. Si el usuario pide leer,
                  buscar dentro, resumir o transformar contenido, está
                  permitido.
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
                - REGLA OBLIGATORIA: si una receta verificada ya contiene un
                  AppID literal, úsalo directamente y no repitas Get-StartApps.
                  Si no hay receta y Get-StartApps devuelve una fila cuyo AppID
                  es X, el siguiente comando de apertura es exactamente
                  `explorer.exe 'shell:AppsFolder\X'`, sustituyendo X por todo el
                  AppID literal recibido. No uses el valor Name como FilePath,
                  no añadas ArgumentList y no adivines un nombre `.exe`.
                - Para iniciar, cerrar o controlar una aplicación usa sólo
                  comandos propios de Windows o interfaces documentadas que
                  puedan invocarse íntegramente desde consola: su CLI,
                  cmdlets, API o protocolo URI con parámetros reales.
                - La política es de denegación concreta, no un catálogo de
                  funciones permitidas. Puedes usar comandos y parámetros
                  normales de una aplicación aunque no aparezcan en los
                  ejemplos de este mensaje.
                - Puedes iniciar una aplicación por su ruta ejecutable local y
                  pasarle argumentos literales. También puedes usar módulos,
                  cmdlets o una API COM documentada de la propia aplicación.
                - Activar, traer al frente, maximizar, restaurar, minimizar,
                  mover o redimensionar una ventana superior ESTÁ PERMITIDO.
                  Hazlo desde PowerShell con `WScript.Shell.AppActivate` o con
                  APIs Win32 invocables desde consola como `ShowWindowAsync`,
                  `SetForegroundWindow` y `SetWindowPos`, usando el
                  `MainWindowHandle` obtenido mediante `Get-Process`. Estas
                  operaciones no simulan teclado ni ratón y no requieren
                  reconocimiento gráfico. Nunca respondas que las restricciones
                  impiden controlar el estado de una ventana.
                - Para cambiar el estado de una ventana, selecciona un único
                  proceso con `MainWindowHandle -ne 0`. Nunca detengas, cierres
                  ni reinicies el proceso para maximizarlo, minimizarlo,
                  restaurarlo o activarlo. `WScript.Shell.AppActivate` recibe el
                  Id del proceso, NO su `MainWindowHandle`.
                - Puedes declarar por `Add-Type` las APIs `ShowWindowAsync`,
                  `IsZoomed`, `IsIconic`, `SetForegroundWindow` y `SetWindowPos`.
                  Los valores Win32 son 3 para maximizar, 6 para minimizar y 9
                  para restaurar. Realiza en un solo comando todos los cambios
                  de ventana solicitados y publica evidencia literal como
                  `PROCESS_NAME=...`, `ACTIVATED=True`,
                  `MAXIMIZED=True`, `MINIMIZED=True` o `RESTORED=True`.
                - Si el usuario pide crear, guardar, importar o exportar un
                  documento o proyecto dentro de una aplicación, esa operación
                  normal está permitida cuando la aplicación ofrezca una CLI,
                  cmdlet, API o protocolo real para hacerlo.
                - Una receta que sólo abre una aplicación no completa una tarea
                  de creación. Para crear contenido, produce primero el archivo
                  o proyecto mediante plantilla, CLI o API y abre después el
                  resultado. No abras la aplicación por sí sola esperando poder
                  manipular luego su interfaz.
                - También puedes crear contenido nuevo desde una plantilla
                  existente: crea una carpeta de datos
                  con `New-Item -Path 'RUTA_NUEVA' -ItemType Directory
                  -ErrorAction Stop`, copia el archivo de plantilla mediante
                  `Copy-Item -LiteralPath 'PLANTILLA' -Destination
                  'ARCHIVO_NUEVO' -ErrorAction Stop` y abre el archivo nuevo.
                  Busca primero la plantilla real por consola; no inventes rutas.
                  Esta vía sirve de forma general para proyectos basados en
                  plantillas y no debe codificarse como una función por programa.
                - Están prohibidos `SendKeys`, atajos de teclado,
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
                SEGURIDAD:

                - Sólo hay tres prohibiciones sobre el equipo: no elimines
                  elementos o contenido; no muevas ni cortes elementos; y no
                  formatees, limpies ni reinicialices discos o unidades.
                  Renombrar sin cambiar la ubicación está permitido.
                - Todo lo demás que pueda invocarse por consola está permitido:
                  leer, buscar, crear, copiar, sobrescribir, abrir, guardar,
                  descargar, instalar, configurar Windows, usar el registro,
                  servicios, red, módulos, ejecutables, intérpretes, APIs y
                  automatización propia de las aplicaciones.
                - No ocultes ni construyas dinámicamente una de las tres
                  operaciones prohibidas para sortear el validador.

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

                UNIDADES LOCALES AUTORIZADAS PARA LOCALIZAR ARCHIVOS:

                {raicesBusqueda}

                CARPETAS DE DATOS EXISTENTES SUGERIDAS PARA CONTENIDO NUEVO:

                {carpetasDatos}

                CARPETAS EXISTENTES DONDE LAS APLICACIONES GUARDAN PLANTILLAS:

                {carpetasAplicaciones}

                CARPETAS RELACIONADAS DEDUCIDAS DE RECETAS APRENDIDAS:

                {(carpetasAprendidas.Count == 0
                    ? "- Ninguna para esta petición."
                    : string.Join(
                        Environment.NewLine,
                        carpetasAprendidas.Select(ruta => "- " + ruta)))}

                RECURSOS REUTILIZABLES COMPROBADOS EN RECETAS:

                {(recursosAprendidos.Count == 0
                    ? "- Ninguno para esta petición."
                    : string.Join(
                        Environment.NewLine,
                        recursosAprendidos.Select(ruta => "- " + ruta)))}

                RECETAS LOCALES RELACIONADAS:

                {memoriaLocal}

                Decide el primer paso necesario.
                """));

        var pasos = new List<ResultadoPasoControl>();
        IReadOnlySet<int> tareasPendientes =
            Enumerable.Range(1, plan.Tareas.Count).ToHashSet();

        try
        {
            for (int indice = 0; indice < maximoPasos; indice++)
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
                    if (PlanSolicitaSoloEstadoDeVentanas(plan))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                CrearInstruccionControlEstadoVentana()));
                        continue;
                    }

                    if (RequiereContinuarCreacionDesdePlantilla(
                            plan,
                            pasos))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                CrearInstruccionCreacionDesdePlantilla(
                                    plan,
                                    carpetasAprendidas)));
                        continue;
                    }

                    if (RequiereConsultarAplicacionesAntesDeLimitar(
                            plan,
                            pasos))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                CrearInstruccionInvestigacionApertura()));
                        continue;
                    }

                    return Finalizar(
                        false,
                        "sin_comando",
                        limitacion,
                        pasos,
                        informar);
                }

                if (TryObtenerPreguntaUsuario(
                        comando,
                        out string preguntaUsuario))
                {
                    if (RequiereContinuarCreacionDesdePlantilla(
                            plan,
                            pasos))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                CrearInstruccionCreacionDesdePlantilla(
                                    plan,
                                    carpetasAprendidas)));
                        continue;
                    }

                    return Finalizar(
                        false,
                        "requiere_aclaracion",
                        preguntaUsuario,
                        pasos,
                        informar);
                }

                if (TryObtenerRespuestaNatural(
                        comando,
                        out string respuestaNatural))
                {
                    if (PlanSolicitaSoloEstadoDeVentanas(plan)
                        && RespuestaNiegaControlEstadoVentana(
                            respuestaNatural))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                CrearInstruccionControlEstadoVentana()));
                        continue;
                    }

                    if (RequiereContinuarCreacionDesdePlantilla(
                            plan,
                            pasos))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                CrearInstruccionCreacionDesdePlantilla(
                                    plan,
                                    carpetasAprendidas)));
                        continue;
                    }

                    if (RequiereConsultarAplicacionesAntesDeLimitar(
                            plan,
                            pasos)
                        || RequiereInvestigarAplicacion(pasos))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                """
                                No preguntes todavía al usuario ni declares una
                                limitación. Resuelve la aplicación mediante
                                Get-StartApps, publica APP_NAME y APP_ID completos,
                                usa el AppID literal y continúa.
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
                    if (PlanSolicitaSoloEstadoDeVentanas(plan)
                        && RespuestaNiegaControlEstadoVentana(
                            explicacion))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                CrearInstruccionControlEstadoVentana()));
                        continue;
                    }

                    if (RequiereContinuarCreacionDesdePlantilla(
                            plan,
                            pasos))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                explicacion));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                CrearInstruccionCreacionDesdePlantilla(
                                    plan,
                                    carpetasAprendidas)));
                        continue;
                    }

                    if (RequiereConsultarAplicacionesAntesDeLimitar(
                            plan,
                            pasos)
                        || RequiereInvestigarAplicacion(pasos))
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

                        if (RequiereContinuarCreacionDesdePlantilla(
                                plan,
                                pasos))
                        {
                            mensajes.Add(
                                new MensajeOllama(
                                    "assistant",
                                    "FIN"));
                            mensajes.Add(
                                new MensajeOllama(
                                    "user",
                                    CrearInstruccionCreacionDesdePlantilla(
                                        plan,
                                        carpetasAprendidas)));
                            continue;
                        }

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
                    if (PlanSolicitaSoloEstadoDeVentanas(plan))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                CrearInstruccionControlEstadoVentana()));
                        continue;
                    }

                    if (RequiereContinuarCreacionDesdePlantilla(
                            plan,
                            pasos))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                CrearInstruccionCreacionDesdePlantilla(
                                    plan,
                                    carpetasAprendidas)));
                        continue;
                    }

                    if (RequiereConsultarAplicacionesAntesDeLimitar(
                            plan,
                            pasos))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                CrearInstruccionInvestigacionApertura()));
                        continue;
                    }

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
                    if (RequiereContinuarCreacionDesdePlantilla(
                            plan,
                            pasos))
                    {
                        mensajes.Add(
                            new MensajeOllama(
                                "assistant",
                                comando));
                        mensajes.Add(
                            new MensajeOllama(
                                "user",
                                CrearInstruccionCreacionDesdePlantilla(
                                    plan,
                                    carpetasAprendidas)));
                        continue;
                    }

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

                if (PlanSolicitaSoloEstadoDeVentanas(plan)
                    && EsEstrategiaInvalidaParaEstadoVentana(comando))
                {
                    mensajes.Add(
                        new MensajeOllama(
                            "assistant",
                            comando));
                    mensajes.Add(
                        new MensajeOllama(
                            "user",
                            CrearInstruccionControlEstadoVentana()));
                    continue;
                }

                if (RequiereContinuarCreacionDesdePlantilla(
                        plan,
                        pasos)
                    && recursosAprendidos.Count > 0
                    && EsBusquedaDeRecursosRepetida(comando))
                {
                    mensajes.Add(new MensajeOllama("assistant", comando));
                    mensajes.Add(
                        new MensajeOllama(
                            "user",
                            $"""
                            INVESTIGACIÓN REPETIDA RECHAZADA.
                            La memoria ya contiene recursos relacionados que
                            existen en disco:
                            {string.Join(
                                Environment.NewLine,
                                recursosAprendidos.Select(ruta => "- " + ruta))}
                            Usa directamente uno de esos orígenes literales en
                            Copy-Item y adapta únicamente el destino al nombre
                            exacto de esta petición. No repitas Get-ChildItem.
                            """));
                    continue;
                }

                if (RequiereContinuarCreacionDesdePlantilla(
                        plan,
                        pasos)
                    && TryObtenerDestinoCopyItem(
                        comando,
                        out string destinoPropuesto)
                    && !DestinoConservaNombreSolicitado(
                        destinoPropuesto,
                        instruccion,
                        plan))
                {
                    string nombreExacto =
                        PlanificadorTareasIA
                            .ExtraerNombreLiteralSolicitado(
                                instruccion)
                        ?? PlanificadorTareasIA
                            .ExtraerNombreLiteralSolicitado(
                                plan.Formatear())
                        ?? "(nombre indicado por el usuario)";

                    mensajes.Add(new MensajeOllama("assistant", comando));
                    mensajes.Add(
                        new MensajeOllama(
                            "user",
                            $"""
                            DESTINO RECHAZADO POR NO CUMPLIR LA PETICIÓN.
                            El usuario pidió el nombre literal exacto:
                            {nombreExacto}
                            El destino propuesto fue:
                            {destinoPropuesto}
                            Repite Copy-Item usando ese nombre exacto para el
                            archivo de destino y una carpeta real de Documentos
                            incluida en la petición. No inventes otro nombre.
                            """));
                    continue;
                }

                if (EsAperturaSimpleSinParametros(comando)
                    && RequiereContinuarCreacionDesdePlantilla(
                        plan,
                        pasos))
                {
                    mensajes.Add(new MensajeOllama("assistant", comando));
                    mensajes.Add(
                        new MensajeOllama(
                            "user",
                            CrearInstruccionCreacionDesdePlantilla(
                                plan,
                                carpetasAprendidas)));
                    continue;
                }

                if (RequiereContinuarCreacionDesdePlantilla(
                        plan,
                        pasos)
                    && EsConsultaEspeculativaTrasCarpetaObservada(
                        comando,
                        pasos))
                {
                    mensajes.Add(new MensajeOllama("assistant", comando));
                    mensajes.Add(
                        new MensajeOllama(
                            "user",
                            CrearInstruccionBuscarEnCarpetaObservada(
                                pasos)));
                    continue;
                }

                string? bloqueoProcedencia = null;

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

                ResultadoValidacionPowerShell validacionLocal =
                    ValidadorPowerShell.Validar(comando);

                if (!validacionLocal.Permitido)
                {
                    var pasoBloqueado = new ResultadoPasoControl(
                        pasos.Count + 1,
                        comando,
                        false,
                        -1,
                        string.Empty,
                        "BLOQUEADO: " + validacionLocal.Motivo);
                    pasos.Add(pasoBloqueado);
                    Informar(
                        informar,
                        new EventoControl(
                            "bloqueado",
                            pasoBloqueado.Error,
                            comando,
                            pasoBloqueado));
                    mensajes.Add(
                        new MensajeOllama(
                            "assistant",
                            comando));
                    mensajes.Add(
                        new MensajeOllama(
                            "user",
                            "El validador local rechazó el comando y no se ejecutó: "
                            + validacionLocal.Motivo
                            + Environment.NewLine
                            + "Propón otra estrategia para una tarea pendiente."));
                    continue;
                }

                Informar(
                    informar,
                    new EventoControl(
                        "pensando",
                        "ControlPCIA está comprobando que el comando corresponda a la petición."));
                RevisionAlineacionComando alineacion =
                    await RevisorAlineacionComandoIA.RevisarAsync(
                        plan,
                        tareasPendientes,
                        comando,
                        pasos,
                        cancellationToken);

                if (!alineacion.Alineado)
                {
                    var pasoDesalineado = new ResultadoPasoControl(
                        pasos.Count + 1,
                        comando,
                        false,
                        -1,
                        string.Empty,
                        "NO EJECUTADO: " + alineacion.Motivo);
                    pasos.Add(pasoDesalineado);
                    Informar(
                        informar,
                        new EventoControl(
                            "bloqueado",
                            pasoDesalineado.Error,
                            comando,
                            pasoDesalineado));
                    mensajes.Add(
                        new MensajeOllama(
                            "assistant",
                            comando));
                    mensajes.Add(
                        new MensajeOllama(
                            "user",
                            $"""
                            PROPUESTA NO EJECUTADA POR NO CORRESPONDER A LA PETICIÓN:
                            {alineacion.Motivo}
                            Conserva las tareas pendientes y propón un comando
                            que actúe únicamente sobre sus objetivos.
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
                    && plan.Tareas.Any(
                        TareaPareceCreacionDeContenido)
                    && TryObtenerDestinoCopyItem(
                        comando,
                        out string archivoCreado))
                {
                    informacionResultado +=
                        Environment.NewLine
                        + Environment.NewLine
                        + $"""
                           ARCHIVO NUEVO CREADO:
                           {archivoCreado}
                           Si la tarea pide abrir el proyecto resultante,
                           ejecuta directamente
                           Start-Process -FilePath '{archivoCreado.Replace(
                               "'",
                               "''",
                               StringComparison.Ordinal)}'
                           para que Windows use la aplicación registrada. No
                           inventes ni busques una ruta de ejecutable.
                           """;
                }

                if (ejecucion.Ejecutado
                    && ejecucion.CodigoSalida == 0
                    && EsInicioAplicacion(comando))
                {
                    string comandoComprobacion =
                        CrearComandoVerificacionApertura(comando);
                    Informar(
                        informar,
                        new EventoControl(
                            "pensando",
                            "ControlPCIA está comprobando por consola el proceso abierto."));
                    ResultadoEjecucionPowerShell comprobacion =
                        await ComprobarAperturaAsync(
                            comandoComprobacion,
                            cancellationToken);
                    var pasoComprobacion = new ResultadoPasoControl(
                        pasos.Count + 1,
                        comandoComprobacion,
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
                            comandoComprobacion,
                            pasoComprobacion));

                    informacionResultado +=
                        Environment.NewLine
                        + Environment.NewLine
                        + "COMPROBACIÓN AUTOMÁTICA DE VENTANAS TRAS LA APERTURA:"
                        + Environment.NewLine
                        + (comprobacion.Ejecutado
                            ? CrearResultadoEjecutado(
                                comandoComprobacion,
                                comprobacion)
                            : CrearResultadoBloqueado(
                                comprobacion,
                                comandoComprobacion));
                }

                if (RequiereContinuarCreacionDesdePlantilla(
                        plan,
                        pasos)
                    && ejecucion.Ejecutado
                    && ejecucion.CodigoSalida == 0
                    && Regex.IsMatch(
                        ejecucion.Salida,
                        @"(?:^|\r?\n)EXTENSION=\.[^\r\n]+(?:\r?\n|$)",
                        RegexOptions.IgnoreCase
                        | RegexOptions.CultureInvariant))
                {
                    informacionResultado +=
                        Environment.NewLine
                        + Environment.NewLine
                        + """
                          EXTENSIONES REALES DE LA APLICACIÓN ENCONTRADAS.
                          Relaciona la aplicación y el tipo de contenido pedido
                          con una extensión de esta salida. Usa su
                          SAMPLE_FULL_NAME para deducir la carpeta y ejecuta
                          ahora Get-ChildItem con esa extensión concreta para
                          obtener FULL_NAME. No respondas FIN y no vuelvas a
                          enumerar extensiones.
                          """;
                }

                if (RequiereContinuarCreacionDesdePlantilla(
                        plan,
                        pasos)
                    && ejecucion.Ejecutado
                    && ejecucion.CodigoSalida == 0
                    && Regex.IsMatch(
                        ejecucion.Salida,
                        @"(?:^|\r?\n)FULL_NAME=.*(?:template|plantilla).*(?:\r?\n|$)",
                        RegexOptions.IgnoreCase
                        | RegexOptions.CultureInvariant))
                {
                    informacionResultado +=
                        Environment.NewLine
                        + Environment.NewLine
                        + """
                          PLANTILLA REAL ENCONTRADA. El siguiente paso debe usar
                          uno de esos FULL_NAME literales como origen de
                          Copy-Item y un destino nuevo con el nombre solicitado.
                          No inventes una CLI ni vuelvas a buscar.
                          """;
                }

                if (RequiereContinuarCreacionDesdePlantilla(
                        plan,
                        pasos)
                    && ejecucion.Ejecutado
                    && ejecucion.CodigoSalida == 0
                    && !string.IsNullOrWhiteSpace(ejecucion.Salida)
                    && Regex.IsMatch(
                        comando,
                        @"(?:^|\r?\n|[;|]\s*)Get-ChildItem\b[^\r\n;|]*-Directory\b",
                        RegexOptions.IgnoreCase
                        | RegexOptions.CultureInvariant))
                {
                    informacionResultado +=
                        Environment.NewLine
                        + Environment.NewLine
                        + CrearInstruccionBuscarEnCarpetaObservada(
                            pasos);
                }

                if (TryObtenerCreacionDesdePlantillaVerificada(
                        plan,
                        instruccion,
                        pasos,
                        out string proyectoCreado))
                {
                    bool aprendido =
                        await AprenderRecetaAsync(
                            instruccion,
                            pasos,
                            informar,
                            cancellationToken);

                    return Finalizar(
                        true,
                        "completado",
                        $"Proyecto creado y abierto: {proyectoCreado}",
                        pasos,
                        informar,
                        aprendido);
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

                if (EsAperturaUnicaVerificada(
                        plan,
                        pasos))
                {
                    bool aprendido =
                        await AprenderRecetaAsync(
                            instruccion,
                            pasos,
                            informar,
                            cancellationToken);

                    return Finalizar(
                        true,
                        "completado",
                        "Aplicación abierta y comprobada por consola.",
                        pasos,
                        informar,
                        aprendido);
                }

                if (PlanSolicitaSoloEstadoDeVentanas(plan)
                    && ejecucion.Ejecutado
                    && ejecucion.CodigoSalida == 0
                    && SalidaDemuestraEstadoVentana(
                        ejecucion.Salida))
                {
                    Informar(
                        informar,
                        new EventoControl(
                            "pensando",
                            "ControlPCIA está verificando el resultado de la ventana."));
                    RevisionTareasControl revisionRapida =
                        await RevisarTareasAsync(
                            plan,
                            pasos,
                            cancellationToken);

                    if (revisionRapida.Completa)
                    {
                        bool aprendido =
                            await AprenderRecetaAsync(
                                instruccion,
                                pasos,
                                informar,
                                cancellationToken);

                        return Finalizar(
                            true,
                            "completado",
                            "Ventana controlada y comprobada por consola.",
                            pasos,
                            informar,
                            aprendido);
                    }

                    tareasPendientes =
                        ObtenerTareasPendientes(
                            plan,
                            revisionRapida);
                }

                mensajes.Add(new MensajeOllama("assistant", comando));
                mensajes.Add(new MensajeOllama("user", informacionResultado));
            }

            return Finalizar(
                false,
                "limite_pasos",
                CrearMensajeLimitePasos(
                    pasos,
                    maximoPasos),
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

    internal static async Task<ResultadoTraduccionControl>
        TraducirSinEjecutarAsync(
            string instruccion,
            IReadOnlyList<MensajeConversacionControl>? contextoConversacion = null,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instruccion))
        {
            return new ResultadoTraduccionControl(
                "orden_vacia",
                new PlanTareasControl([]),
                [],
                string.Empty,
                null,
                false,
                "No se ha recibido ninguna orden.");
        }

        if (instruccion.Length > 1000)
        {
            return new ResultadoTraduccionControl(
                "orden_demasiado_larga",
                new PlanTareasControl([]),
                [],
                string.Empty,
                null,
                false,
                "La orden supera los 1000 caracteres.");
        }

        PlanTareasControl plan =
            await PlanificadorTareasIA.CrearAsync(
                instruccion,
                NormalizarContexto(contextoConversacion),
                cancellationToken);
        IReadOnlyList<string> conocimientos =
            TraductorRecetasConocidas
                .ObtenerConocimientosAplicables(plan);

        if (!string.IsNullOrWhiteSpace(plan.Pregunta))
        {
            return new ResultadoTraduccionControl(
                "requiere_aclaracion",
                plan,
                conocimientos,
                plan.Pregunta,
                null,
                false,
                plan.Pregunta);
        }

        if (!TraductorRecetasConocidas.PuedeResolverPlan(plan))
        {
            return new ResultadoTraduccionControl(
                "sin_receta_conocida",
                plan,
                conocimientos,
                string.Empty,
                null,
                false,
                "La petición necesita el traductor general; no se ha ejecutado nada.");
        }

        IReadOnlyList<RecetaReferencia> recetas =
            await BuscarRecetasAsync(
                instruccion,
                informar: null,
                cancellationToken);
        List<MensajeOllama> mensajes =
            TraductorRecetasConocidas.CrearMensajes(
                instruccion,
                plan,
                recetas);
        string respuestaModelo =
            TraductorRecetasConocidas
                .TryCrearPrimerComandoDeterminista(
                    plan,
                    recetas,
                    out string primerComando)
                ? primerComando
                : LimpiarComando(
                    await ClienteOllama.ConversarAsync(
                        mensajes,
                        cancellationToken));

        if (TryObtenerPreguntaUsuario(
                respuestaModelo,
                out string pregunta))
        {
            return new ResultadoTraduccionControl(
                "requiere_aclaracion",
                plan,
                conocimientos,
                respuestaModelo,
                null,
                false,
                pregunta);
        }

        if (string.IsNullOrWhiteSpace(respuestaModelo)
            || respuestaModelo.Equals(
                "FIN",
                StringComparison.OrdinalIgnoreCase)
            || respuestaModelo.Equals(
                "SIN_COMANDO",
                StringComparison.OrdinalIgnoreCase)
            || TryObtenerLimitacion(respuestaModelo, out _)
            || TryObtenerRespuestaNatural(respuestaModelo, out _)
            || TryObtenerExplicacionNatural(
                respuestaModelo,
                out _,
                out _))
        {
            return new ResultadoTraduccionControl(
                "sin_comando",
                plan,
                conocimientos,
                respuestaModelo,
                null,
                false,
                "Llama no devolvió un comando literal.");
        }

        ResultadoValidacionPowerShell alineacion =
            TraductorRecetasConocidas.ValidarAlineacion(
                plan,
                respuestaModelo,
                [],
                recetas);
        ResultadoValidacionPowerShell validacion =
            alineacion.Permitido
                ? ValidadorPowerShell.Validar(respuestaModelo)
                : alineacion;

        return new ResultadoTraduccionControl(
            validacion.Permitido
                ? "comando_propuesto"
                : "comando_rechazado",
            plan,
            conocimientos,
            respuestaModelo,
            respuestaModelo,
            validacion.Permitido,
            validacion.Motivo);
    }

    private static async Task<ResultadoControl>
        ControlarConRecetaConocidaAsync(
            string instruccion,
            PlanTareasControl plan,
            IReadOnlyList<RecetaReferencia> recetas,
            Action<EventoControl>? informar,
            CancellationToken cancellationToken)
    {
        var pasos = new List<ResultadoPasoControl>();
        List<MensajeOllama> mensajes =
            TraductorRecetasConocidas.CrearMensajes(
                instruccion,
                plan,
                recetas);
        int maximoIntentos =
            PlanSolicitaSoloEstadoDeVentanas(plan)
                ? 4
                : Math.Min(12, 2 + plan.Tareas.Count * 3);

        for (int intento = 0; intento < maximoIntentos; intento++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Informar(
                informar,
                new EventoControl(
                    "pensando",
                    intento == 0
                        ? "Llama está traduciendo la petición con una receta conocida."
                        : "Llama está adaptando la receta a la salida real."));

            string? comandoFijo = intento == 0
                && TraductorRecetasConocidas
                    .TryCrearPrimerComandoDeterminista(
                        plan,
                        recetas,
                        out string primerComando)
                    ? primerComando
                    : await TraductorRecetasConocidas
                        .CrearComandoEstadoVentanaAsync(
                            plan,
                            pasos,
                            cancellationToken);
            string respuestaModelo = comandoFijo
                ?? LimpiarComando(
                    await ClienteOllama.ConversarAsync(
                        mensajes,
                        cancellationToken));

            if (TryObtenerPreguntaUsuario(
                    respuestaModelo,
                    out string pregunta))
            {
                return Finalizar(
                    false,
                    "requiere_aclaracion",
                    pregunta,
                    pasos,
                    informar);
            }

            if (respuestaModelo.Equals(
                    "FIN",
                    StringComparison.OrdinalIgnoreCase))
            {
                RevisionTareasControl revision =
                    await RevisarTareasAsync(
                        plan,
                        pasos,
                        cancellationToken);

                if (revision.Completa)
                {
                    bool aprendido =
                        await AprenderRecetaAsync(
                            instruccion,
                            pasos,
                            informar,
                            cancellationToken);

                    return Finalizar(
                        true,
                        "completado",
                        "Petición completada y comprobada por consola.",
                        pasos,
                        informar,
                        aprendido);
                }

                mensajes.Add(
                    new MensajeOllama(
                        "assistant",
                        respuestaModelo));
                mensajes.Add(
                    new MensajeOllama(
                        "user",
                        "FIN fue rechazado porque falta evidencia: "
                        + revision.Motivo
                        + Environment.NewLine
                        + "Continúa únicamente con las recetas seleccionadas."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(respuestaModelo)
                || respuestaModelo.Equals(
                    "SIN_COMANDO",
                    StringComparison.OrdinalIgnoreCase)
                || TryObtenerLimitacion(respuestaModelo, out _)
                || TryObtenerRespuestaNatural(respuestaModelo, out _)
                || TryObtenerExplicacionNatural(
                    respuestaModelo,
                    out _,
                    out _))
            {
                mensajes.Add(
                    new MensajeOllama(
                        "assistant",
                        respuestaModelo));
                mensajes.Add(
                    new MensajeOllama(
                        "user",
                        TraductorRecetasConocidas.CrearCorreccion(
                            plan,
                            "La respuesta no es un comando literal ni una pregunta válida.")));
                continue;
            }

            ResultadoValidacionPowerShell alineacion =
                TraductorRecetasConocidas.ValidarAlineacion(
                    plan,
                    respuestaModelo,
                    pasos,
                    recetas);
            ResultadoValidacionPowerShell validacion =
                alineacion.Permitido
                    ? ValidadorPowerShell.Validar(respuestaModelo)
                    : alineacion;

            if (!validacion.Permitido)
            {
                mensajes.Add(
                    new MensajeOllama(
                        "assistant",
                        respuestaModelo));
                mensajes.Add(
                    new MensajeOllama(
                        "user",
                        TraductorRecetasConocidas.CrearCorreccion(
                            plan,
                            validacion.Motivo)));
                continue;
            }

            Informar(
                informar,
                new EventoControl(
                    "comando",
                    "Llama ha adaptado una receta conocida.",
                    respuestaModelo));
            ResultadoEjecucionPowerShell ejecucion =
                await EjecutorPowerShell.EjecutarAsync(
                    respuestaModelo,
                    cancellationToken);
            var paso = new ResultadoPasoControl(
                pasos.Count + 1,
                respuestaModelo,
                ejecucion.Ejecutado,
                ejecucion.CodigoSalida,
                ejecucion.Salida,
                ejecucion.Error);
            pasos.Add(paso);

            Informar(
                informar,
                new EventoControl(
                    ejecucion.Ejecutado
                        ? "ejecutado"
                        : "bloqueado",
                    ejecucion.Ejecutado
                        ? $"Comando ejecutado con código {ejecucion.CodigoSalida}."
                        : ejecucion.Error,
                    respuestaModelo,
                    paso));

            if (ejecucion.Ejecutado
                && ejecucion.CodigoSalida == 0
                && PlanSolicitaSoloEstadoDeVentanas(plan)
                && SalidaCompletaPlanEstadoVentana(
                    plan,
                    ejecucion.Salida))
            {
                bool aprendido =
                    await AprenderRecetaAsync(
                        instruccion,
                        pasos,
                        informar,
                        cancellationToken);

                return Finalizar(
                    true,
                    "completado",
                    "Ventana controlada y comprobada por consola.",
                    pasos,
                    informar,
                    aprendido);
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

            if (EsAperturaUnicaVerificada(plan, pasos))
            {
                bool aprendido =
                    await AprenderRecetaAsync(
                        instruccion,
                        pasos,
                        informar,
                        cancellationToken);

                return Finalizar(
                    true,
                    "completado",
                    "Aplicación abierta y comprobada por consola.",
                    pasos,
                    informar,
                    aprendido);
            }

            mensajes.Add(
                new MensajeOllama(
                    "assistant",
                    respuestaModelo));
            mensajes.Add(
                new MensajeOllama(
                    "user",
                    TraductorRecetasConocidas.CrearResultadoPaso(
                        paso)));
        }

        ResultadoPasoControl? ultimo = pasos.LastOrDefault();
        string detalle = ultimo is null
            ? "Llama no devolvió un comando alineado con las recetas seleccionadas."
            : !string.IsNullOrWhiteSpace(ultimo.Error)
                ? ultimo.Error
                : ultimo.Salida;

        return Finalizar(
            false,
            "sin_comando",
            $"No se pudo completar la receta tras {maximoIntentos} intentos. "
            + LimitarTexto(detalle),
            pasos,
            informar);
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

        return $$"""
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
        IReadOnlyList<ResultadoPasoControl> pasos,
        int maximoPasos)
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
            $"No se ha completado la petición tras {maximoPasos} pasos.{detalle} " +
            "Puedes explicarme qué resultado esperabas o repetir la orden con más detalle.";
    }

    internal static int CalcularMaximoPasos(int numeroTareas)
    {
        int tareas = Math.Max(1, numeroTareas);

        return Math.Min(
            MaximoPasosAbsoluto,
            Math.Max(
                MaximoPasosBase,
                tareas * PasosPorTarea));
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
            @"\bControlPCIA(?:\.exe)?\s+(?:ui|window\s+keys)\b|\.SendKeys\s*\(|\bSystem\.Windows\.Forms\.SendKeys\b|\.SendWait\s*\(|\bUIAutomation(?:Client)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static bool PlanSolicitaSoloEstadoDeVentanas(
        PlanTareasControl plan)
    {
        bool seleccionadaPorLlama =
            plan.ConocimientosSeleccionados.Contains(
                "ventanas.estado",
                StringComparer.Ordinal);

        return plan.Tareas.Count > 0
               && plan.Tareas.All(tarea =>
               {
                   string normalizada =
                       InventarioTexto.Normalizar(tarea);
                   bool operacionAjena =
                       Regex.IsMatch(
                           normalizada,
                           @"\b(?:abre|abrir|inicia|iniciar|lanza|lanzar|cierra|cerrar|busca|buscar|lista|listar|crea|crear)\b",
                           RegexOptions.CultureInvariant);

                   return ((seleccionadaPorLlama
                            && !operacionAjena)
                           || Regex.IsMatch(
                              normalizada,
                              @"\b(?:activ|primer plano|trae.*frente|maximiz|minimiz|restaur|recoloc|redimension|cambia.*taman|mueve.*ventana)\w*",
                              RegexOptions.CultureInvariant))
                          && !Regex.IsMatch(
                              normalizada,
                              @"\b(?:cerr|deten|termin|mata|reinici)\w*",
                              RegexOptions.CultureInvariant);
               });
    }

    internal static bool EsEstrategiaQueCierraLaAplicacion(
        string comando)
    {
        return Regex.IsMatch(
            comando,
            @"(?:^|[;|]\s*)(?:Stop-Process|kill|spps|taskkill(?:\.exe)?)\b|\.Kill\s*\(|\.CloseMainWindow\s*\(",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static bool EsEstrategiaInvalidaParaEstadoVentana(
        string comando)
    {
        return EsEstrategiaQueCierraLaAplicacion(comando)
               || Regex.IsMatch(
                   comando,
                   @"\b(?:SendKeys|SendWait)\b|(?:^|[;|]\s*)(?:Start-Process|Get-StartApps|explorer(?:\.exe)?|Set-ItemProperty)\b",
                   RegexOptions.IgnoreCase
                   | RegexOptions.CultureInvariant);
    }

    internal static bool RespuestaNiegaControlEstadoVentana(
        string respuesta)
    {
        string normalizada =
            InventarioTexto.Normalizar(respuesta);

        return Regex.IsMatch(
                   normalizada,
                   @"\b(?:no se puede|no puedo|restric|falta de permis|prohib|no dispone|no admite)\w*",
                   RegexOptions.CultureInvariant)
               && Regex.IsMatch(
                   normalizada,
                   @"\b(?:ventana|appactivate|maximiz|minimiz|restaur|primer plano|interfaz)\w*",
                   RegexOptions.CultureInvariant);
    }

    internal static bool SalidaDemuestraEstadoVentana(
        string salida)
    {
        return Regex.IsMatch(
                   salida,
                   @"(?:^|\r?\n)PROCESS_NAME=.+(?:\r?\n|$)",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               && Regex.IsMatch(
                   salida,
                   @"(?:^|\r?\n)(?:ACTIVATED|MAXIMIZED|MINIMIZED|RESTORED|MOVED|RESIZED)=True(?:\r?\n|$)",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static bool SalidaCompletaPlanEstadoVentana(
        PlanTareasControl plan,
        string salida)
    {
        if (!SalidaDemuestraEstadoVentana(salida))
        {
            return false;
        }

        foreach (string tarea in plan.Tareas)
        {
            string normalizada =
                InventarioTexto.Normalizar(tarea);
            string? marcador =
                Regex.IsMatch(
                    normalizada,
                    @"\b(?:activ|primer plano|trae.*frente)\w*",
                    RegexOptions.CultureInvariant)
                    ? "ACTIVATED"
                    : Regex.IsMatch(
                        normalizada,
                        @"\bmaximiz\w*",
                        RegexOptions.CultureInvariant)
                        ? "MAXIMIZED"
                        : Regex.IsMatch(
                            normalizada,
                            @"\bminimiz\w*",
                            RegexOptions.CultureInvariant)
                            ? "MINIMIZED"
                            : Regex.IsMatch(
                                normalizada,
                                @"\brestaur\w*",
                                RegexOptions.CultureInvariant)
                                ? "RESTORED"
                                : Regex.IsMatch(
                                    normalizada,
                                    @"\b(?:recoloc|mueve.*ventana)\w*",
                                    RegexOptions.CultureInvariant)
                                    ? "MOVED"
                                    : Regex.IsMatch(
                                        normalizada,
                                        @"\b(?:redimension|cambia.*taman)\w*",
                                        RegexOptions.CultureInvariant)
                                        ? "RESIZED"
                                        : null;

            if (marcador is not null
                && !Regex.IsMatch(
                    salida,
                    $@"(?:^|\r?\n){marcador}=True(?:\r?\n|$)",
                    RegexOptions.IgnoreCase
                    | RegexOptions.CultureInvariant))
            {
                return false;
            }
        }

        return true;
    }

    private static string CrearInstruccionControlEstadoVentana()
    {
        return """
            ESTRATEGIA DE VENTANA INCORRECTA RECHAZADA.

            Activar, traer al frente, maximizar, restaurar, minimizar, mover o
            redimensionar una ventana superior está permitido. No cierres,
            detengas ni reinicies la aplicación y no alegues restricciones.
            Selecciona el proceso real con MainWindowHandle distinto de cero.
            AppActivate recibe el Id del proceso, no el handle.

            Para maximizar y activar puedes adaptar este patrón en UN comando:

            $p = Get-Process -Name 'PROCESO_REAL' |
                Where-Object { $_.MainWindowHandle -ne 0 } |
                Select-Object -First 1
            if ($null -eq $p) { throw 'No hay una ventana abierta.' }
            Add-Type @'
            using System;
            using System.Runtime.InteropServices;
            public static class VentanaControlPCIA {
              [DllImport("user32.dll")]
              public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
              [DllImport("user32.dll")]
              public static extern bool IsZoomed(IntPtr hWnd);
            }
            '@
            [VentanaControlPCIA]::ShowWindowAsync(
                $p.MainWindowHandle, 3) | Out-Null
            $activada = (New-Object -ComObject WScript.Shell).AppActivate($p.Id)
            Start-Sleep -Milliseconds 300
            $p.Refresh()
            $maximizada =
                [VentanaControlPCIA]::IsZoomed($p.MainWindowHandle)
            Write-Output ('PROCESS_NAME=' + $p.ProcessName)
            Write-Output ('ACTIVATED=' + $activada)
            Write-Output ('MAXIMIZED=' + $maximizada)
            if (!$activada -or !$maximizada) { exit 1 }

            Usa 6 con ShowWindowAsync para minimizar y 9 para restaurar. Para
            mover o redimensionar declara y usa SetWindowPos. Adapta el patrón
            a todas las tareas pendientes y publica su evidencia real.
            """;
    }

    internal static string CrearComandoVerificacionApertura(
        string comandoApertura)
    {
        Match destino = Regex.Match(
            comandoApertura,
            @"^\s*explorer(?:\.exe)?\s+(?:""shell:AppsFolder\\(?<doble>[^""]+)""|'shell:AppsFolder\\(?<simple>[^']+)')\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!destino.Success)
        {
            return ComandoInventarioVentanas;
        }

        string appId = destino.Groups["doble"].Success
            ? destino.Groups["doble"].Value
            : destino.Groups["simple"].Value;
        Match ejecutable = Regex.Match(
            appId,
            @"(?:^|\\)(?<nombre>[^\\/:*?""<>|]{1,120})\.exe$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!ejecutable.Success)
        {
            return ComandoInventarioVentanas;
        }

        string proceso = ejecutable.Groups["nombre"].Value.Replace(
            "'",
            "''",
            StringComparison.Ordinal);

        return
            $"Get-Process -Name '{proceso}' -ErrorAction SilentlyContinue"
            + " | ForEach-Object { Write-Output ('PROCESS_NAME=' + $_.ProcessName);"
            + " Write-Output ('WINDOW_TITLE=' + $_.MainWindowTitle) }";
    }

    private static async Task<ResultadoEjecucionPowerShell>
        ComprobarAperturaAsync(
            string comando,
            CancellationToken cancellationToken)
    {
        bool procesoConcreto = Regex.IsMatch(
            comando,
            @"^\s*Get-Process\s+-Name\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        int intentos = procesoConcreto ? 16 : 1;
        ResultadoEjecucionPowerShell? ultimo = null;

        for (int intento = 0; intento < intentos; intento++)
        {
            ultimo = await EjecutorPowerShell.EjecutarAsync(
                comando,
                cancellationToken);

            if (!procesoConcreto
                || ultimo.Ejecutado
                && ultimo.CodigoSalida == 0
                && Regex.IsMatch(
                    ultimo.Salida,
                    @"(?:^|\r?\n)PROCESS_NAME=.+(?:\r?\n|$)",
                    RegexOptions.IgnoreCase
                    | RegexOptions.CultureInvariant))
            {
                return ultimo;
            }

            if (intento + 1 < intentos)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(500),
                    cancellationToken);
            }
        }

        return ultimo
               ?? new ResultadoEjecucionPowerShell(
                   false,
                   -1,
                   string.Empty,
                   "No se pudo comprobar el proceso abierto.");
    }

    internal static IReadOnlySet<string> ObtenerAppIdsAprendidos(
        IReadOnlyList<RecetaReferencia> recetas)
    {
        return recetas
            .SelectMany(receta => receta.Comandos)
            .Select(comando => Regex.Match(
                comando,
                @"^\s*explorer(?:\.exe)?\s+(?:""shell:AppsFolder\\(?<doble>[^""]+)""|'shell:AppsFolder\\(?<simple>[^']+)')\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .Where(coincidencia => coincidencia.Success)
            .Select(coincidencia =>
                coincidencia.Groups["doble"].Success
                    ? coincidencia.Groups["doble"].Value
                    : coincidencia.Groups["simple"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string>
        ObtenerOrigenesCopyItemAprendidos(
            IReadOnlyList<RecetaReferencia> recetas)
    {
        return recetas
            .SelectMany(receta => receta.Comandos)
            .Select(comando =>
            {
                Match coincidencia = Regex.Match(
                    comando,
                    """
                    (?:^|\r?\n|[;|]\s*)Copy-Item\b[^\r\n;|]*?
                    -LiteralPath(?:\s+|:)(?:"(?<doble>[^"]+)"|'(?<simple>[^']+)')
                    """,
                    RegexOptions.IgnoreCase
                    | RegexOptions.CultureInvariant
                    | RegexOptions.IgnorePatternWhitespace);

                if (!coincidencia.Success)
                {
                    return string.Empty;
                }

                return coincidencia.Groups["doble"].Success
                    ? coincidencia.Groups["doble"].Value
                    : coincidencia.Groups["simple"].Value;
            })
            .Where(ruta =>
                !string.IsNullOrWhiteSpace(ruta)
                && File.Exists(ruta))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool EsBusquedaDeRecursosRepetida(
        string comando)
    {
        return Regex.IsMatch(
            comando,
            @"(?:^|\r?\n|[;|]\s*)(?:Get-ChildItem|gci|dir|ls)\b",
            RegexOptions.IgnoreCase
            | RegexOptions.CultureInvariant);
    }

    internal static IReadOnlyList<string>
        ObtenerCarpetasAplicacionesAprendidas(
            IReadOnlyList<RecetaReferencia> recetas)
    {
        IReadOnlyList<string> raices =
            ObtenerCarpetasAplicacionesSugeridas();
        var resultado = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (string appId in ObtenerAppIdsAprendidos(recetas))
        {
            string[] partes = appId.Split(
                '\\',
                StringSplitOptions.RemoveEmptyEntries);

            if (partes.Length < 3
                || !partes[0].StartsWith(
                    "{",
                    StringComparison.Ordinal))
            {
                continue;
            }

            string fabricante = partes[1];

            if (fabricante.Length is < 2 or > 120
                || fabricante.Contains("..", StringComparison.Ordinal)
                || fabricante.IndexOfAny(
                    Path.GetInvalidFileNameChars()) >= 0)
            {
                continue;
            }

            foreach (string raiz in raices)
            {
                string candidata = Path.Combine(
                    raiz,
                    fabricante);

                if (Directory.Exists(candidata))
                {
                    resultado.Add(
                        Path.GetFullPath(candidata));
                }
            }
        }

        return resultado.ToArray();
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

    internal static bool TryObtenerPreguntaUsuario(
        string respuesta,
        out string pregunta)
    {
        const string prefijo = "PREGUNTAR:";
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
            pregunta = "¿Qué dato falta para continuar?";
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
            || minusculas.StartsWith("el primer paso", StringComparison.Ordinal)
            || minusculas.StartsWith("la salida", StringComparison.Ordinal)
            || minusculas.StartsWith("primero ", StringComparison.Ordinal)
            || minusculas.StartsWith("vamos a ", StringComparison.Ordinal)
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

            foreach (string appId in ObtenerAppIdsAprendidos([receta]))
            {
                bloque.AppendLine("APP_ID_APRENDIDO=" + appId);

                string[] partes = appId.Split(
                    '\\',
                    StringSplitOptions.RemoveEmptyEntries);

                if (partes.Length >= 3
                    && partes[0].StartsWith(
                        "{",
                        StringComparison.Ordinal))
                {
                    bloque.AppendLine(
                        "FABRICANTE_APRENDIDO=" + partes[1]);
                    bloque.AppendLine(
                        "EJECUTABLE_APRENDIDO=" + partes[^1]);
                }
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
            @"\bControlPCIA(?:\.exe)?\s+(?:ui|window\s+keys)\b|\.SendKeys\s*\(|\bSystem\.Windows\.Forms\.SendKeys\b|\.SendWait\s*\(|\bUIAutomation(?:Client)?\b",
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

        string[] lineas = resultado.Split(
            ["\r\n", "\n"],
            StringSplitOptions.None);
        string[] lineasConTexto = lineas
            .Where(linea => !string.IsNullOrWhiteSpace(linea))
            .ToArray();

        if (lineasConTexto.Length > 0
            && lineasConTexto.All(linea =>
                Regex.IsMatch(
                    linea,
                    @"^\s*>",
                    RegexOptions.CultureInvariant)))
        {
            resultado = string.Join(
                Environment.NewLine,
                lineas.Select(linea => Regex.Replace(
                    linea,
                    @"^\s*>\s?",
                    string.Empty,
                    RegexOptions.CultureInvariant)));
        }

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

        Match propuestaEnProsa = Regex.Match(
            resultado,
            @"(?:(?:vamos\s+a\s+)?usar|utiliza|ejecuta|propongo)\s+(?:el\s+)?comando\s+`(?<comando>[^`\r\n]{1,2000})`",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (propuestaEnProsa.Success)
        {
            return propuestaEnProsa.Groups["comando"].Value.Trim();
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

    internal static bool RequiereConsultarAplicacionesAntesDeLimitar(
        PlanTareasControl plan,
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        bool aperturaPendiente =
            plan.Tareas.Any(TareaPareceAperturaAplicacion)
            && !HayAperturaVerificada(pasos);

        if (!aperturaPendiente)
        {
            return false;
        }

        return !pasos.Any(paso =>
            paso.Ejecutado
            && paso.CodigoSalida == 0
            && Regex.IsMatch(
                paso.Comando,
                @"(?:^|[;|]\s*)Get-StartApps\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               && !string.IsNullOrWhiteSpace(paso.Salida));
    }

    internal static bool RequiereContinuarCreacionDesdePlantilla(
        PlanTareasControl plan,
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        bool pideCrearContenido =
            plan.Tareas.Any(TareaPareceCreacionDeContenido);
        bool contieneNombre =
            plan.Tareas.Any(TareaContieneNombreParaContenido);

        if (!pideCrearContenido || !contieneNombre)
        {
            return false;
        }

        return !pasos.Any(paso =>
            paso.Ejecutado
            && paso.CodigoSalida == 0
            && Regex.IsMatch(
                paso.Comando,
                @"\bCopy-Item\b|\.SaveAs\s*\(|\bNew-(?!Item\b)[\p{L}\p{N}_-]+\b|\b(?:Create|Save|Export)-[\p{L}\p{N}_-]+\b|--new(?:-project)?\b|/new\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    internal static bool TryObtenerDestinoCopyItem(
        string comando,
        out string destino)
    {
        Match coincidencia = Regex.Match(
            comando,
            """
            (?:^|\r?\n|[;|]\s*)Copy-Item\b[^\r\n;|]*?
            -Destination(?:\s+|:)(?:"(?<doble>[^"]+)"|'(?<simple>[^']+)')
            """,
            RegexOptions.IgnoreCase
            | RegexOptions.CultureInvariant
            | RegexOptions.IgnorePatternWhitespace);

        if (!coincidencia.Success)
        {
            destino = string.Empty;
            return false;
        }

        destino = coincidencia.Groups["doble"].Success
            ? coincidencia.Groups["doble"].Value
            : coincidencia.Groups["simple"].Value;
        return destino.Length > 0;
    }

    internal static bool DestinoConservaNombreSolicitado(
        string destino,
        string instruccion,
        PlanTareasControl plan)
    {
        string? nombre =
            PlanificadorTareasIA.ExtraerNombreLiteralSolicitado(
                instruccion)
            ?? PlanificadorTareasIA.ExtraerNombreLiteralSolicitado(
                plan.Formatear());

        if (string.IsNullOrWhiteSpace(nombre))
        {
            return true;
        }

        string archivo = Path.GetFileName(destino);
        string archivoSinExtension =
            Path.GetFileNameWithoutExtension(archivo);
        string nombreNormalizado =
            InventarioTexto.Normalizar(nombre);

        return InventarioTexto.Normalizar(archivo)
                   .Equals(
                       nombreNormalizado,
                       StringComparison.Ordinal)
               || InventarioTexto.Normalizar(archivoSinExtension)
                   .Equals(
                       nombreNormalizado,
                       StringComparison.Ordinal);
    }

    internal static bool TryObtenerCreacionDesdePlantillaVerificada(
        PlanTareasControl plan,
        string instruccion,
        IReadOnlyList<ResultadoPasoControl> pasos,
        out string destino)
    {
        destino = string.Empty;

        if (plan.Tareas.Count != 1
            || !TareaPareceCreacionDeContenido(plan.Tareas[0]))
        {
            return false;
        }

        for (int indice = 0; indice < pasos.Count; indice++)
        {
            ResultadoPasoControl copia = pasos[indice];

            if (!copia.Ejecutado
                || copia.CodigoSalida != 0
                || !TryObtenerDestinoCopyItem(
                    copia.Comando,
                    out string candidato)
                || !DestinoConservaNombreSolicitado(
                    candidato,
                    instruccion,
                    plan)
                || !File.Exists(candidato))
            {
                continue;
            }

            bool abierto = pasos
                .Skip(indice + 1)
                .Any(paso =>
                    paso.Ejecutado
                    && paso.CodigoSalida == 0
                    && paso.Comando.Contains(
                        candidato,
                        StringComparison.OrdinalIgnoreCase)
                    && Regex.IsMatch(
                        paso.Comando,
                        @"(?:^|\r?\n|[;|]\s*)(?:Start-Process|start|saps|Invoke-Item|ii)\b",
                        RegexOptions.IgnoreCase
                        | RegexOptions.CultureInvariant));

            if (!abierto)
            {
                continue;
            }

            destino = Path.GetFullPath(candidato);
            return true;
        }

        return false;
    }

    private static bool TareaPareceCreacionDeContenido(string tarea)
    {
        string normalizada = InventarioTexto.Normalizar(tarea);

        return Regex.IsMatch(
                   normalizada,
                   @"\b(?:crea|crear|nuevo|nueva|genera|generar)\b",
                   RegexOptions.CultureInvariant)
               && Regex.IsMatch(
                   normalizada,
                   @"\b(?:proyecto|documento|presentacion|hoja|libro|sesion)\b",
                   RegexOptions.CultureInvariant);
    }

    private static bool TareaContieneNombreParaContenido(string tarea)
    {
        string normalizada = InventarioTexto.Normalizar(tarea);

        return Regex.IsMatch(
            normalizada,
            @"\b(?:llamado|llamada|nombre|nombra|nombrar|nombrear|como)\b",
            RegexOptions.CultureInvariant);
    }

    private static string CrearInstruccionCreacionDesdePlantilla(
        PlanTareasControl plan,
        IReadOnlyList<string> carpetasAprendidas)
    {
        string carpetas = string.Join(
            Environment.NewLine,
            ObtenerCarpetasDatosSugeridas()
                .Select(ruta => "- " + ruta));
        string carpetasAplicaciones = string.Join(
            Environment.NewLine,
            ObtenerCarpetasAplicacionesSugeridas()
                .Select(ruta => "- " + ruta));
        string carpetasMemoria = carpetasAprendidas.Count == 0
            ? "- Ninguna; deduce primero fabricante o aplicación."
            : string.Join(
                Environment.NewLine,
                carpetasAprendidas.Select(ruta => "- " + ruta));

        return $$"""
            ACLARACIÓN O LIMITACIÓN PREMATURA RECHAZADA.

            La petición ya contiene el nombre necesario y todavía falta crear
            el contenido solicitado:
            {{plan.Formatear()}}

            Abrir la aplicación no completa esa tarea. Consulta primero las
            recetas aprendidas y las interfaces reales de la aplicación:
            Get-Command, Get-Help, su CLI, cmdlets o API documentada. Si trabaja
            con proyectos basados en plantillas, localiza por consola una
            plantilla real de la versión instalada, crea una carpeta nueva con
            New-Item y copia la plantilla a un archivo nuevo con el nombre
            solicitado mediante Copy-Item. Después abre el archivo nuevo.
            No uses la interfaz gráfica ni inventes rutas o argumentos.
            Deduce la extensión del proyecto a partir de la aplicación y del
            inventario local. Por ejemplo, Cubase usa proyectos y plantillas
            `.cpr`; no hace falta abrir Cubase para buscar esos archivos.
            Busca primero dentro de las carpetas de plantillas de aplicaciones;
            no recorras unidades completas si una ruta específica está
            disponible. El AppID de una receta aprendida puede contener el
            fabricante o una ruta de instalación: úsalo para deducir el nombre
            de la subcarpeta. Busca primero esa subcarpeta sin recursión y luego
            busca la extensión dentro de ella. No recorras recursivamente
            Roaming, Local y ProgramData juntos; consulta una raíz concreta por
            paso.

            Ejemplo general de descubrimiento de carpeta:
            Get-ChildItem -LiteralPath 'RAÍZ_DE_APLICACIONES' -Directory -Filter '*FABRICANTE_O_APLICACIÓN*' -ErrorAction SilentlyContinue | Select-Object -First 20 FullName

            Una búsqueda recursiva válida tiene esta forma, sustituyendo RAÍCES
            y EXTENSIÓN por literales reales:
            Get-ChildItem -LiteralPath 'RAÍZ1','RAÍZ2' -Filter '*.EXTENSIÓN' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 20 | ForEach-Object { Write-Output ('FULL_NAME=' + $_.FullName); Write-Output ('LENGTH=' + $_.Length); Write-Output ('LAST_WRITE_TIME=' + $_.LastWriteTime) }

            Carpetas de datos existentes sugeridas:
            {{carpetas}}

            Carpetas existentes de plantillas y datos de aplicaciones:
            {{carpetasAplicaciones}}

            Carpetas concretas deducidas de recetas aprendidas; úsalas primero:
            {{carpetasMemoria}}

            Continúa con UN comando PowerShell de investigación o creación. No
            vuelvas a preguntar por el nombre que ya dio el usuario.
            """;
    }

    internal static bool EsAperturaSimpleSinParametros(string comando)
    {
        if (!EsInicioAplicacion(comando))
        {
            return false;
        }

        if (Regex.IsMatch(
                comando,
                @"\bArgumentList\b|--[\p{L}\p{N}_-]+|/[A-Za-z][A-Za-z0-9_-]*",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        Match archivo = Regex.Match(
            comando,
            @"[A-Za-z]:[\\/][^'""\r\n]+\.(?<extension>[A-Za-z0-9]{1,12})",
            RegexOptions.CultureInvariant);

        return !archivo.Success
               || archivo.Groups["extension"].Value.Equals(
                   "exe",
                   StringComparison.OrdinalIgnoreCase)
               || archivo.Groups["extension"].Value.Equals(
                   "com",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ObtenerCarpetasDatosSugeridas()
    {
        string[] candidatas =
        [
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("OneDrive") ?? string.Empty,
            Environment.GetEnvironmentVariable(
                "OneDriveConsumer") ?? string.Empty
        ];

        return candidatas
            .Where(ruta =>
                !string.IsNullOrWhiteSpace(ruta)
                && Directory.Exists(ruta))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string>
        ObtenerCarpetasAplicacionesSugeridas()
    {
        string[] candidatas =
        [
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData)
        ];

        return candidatas
            .Where(ruta =>
                !string.IsNullOrWhiteSpace(ruta)
                && Directory.Exists(ruta))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool EsConsultaEspeculativaTrasCarpetaObservada(
        string comando,
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        return HayCarpetaAplicacionObservada(pasos)
               && Regex.IsMatch(
                   comando,
                   @"(?:^|\r?\n)\s*(?:Test-Path|Get-ItemProperty)\b",
                   RegexOptions.IgnoreCase
                   | RegexOptions.CultureInvariant);
    }

    private static bool HayCarpetaAplicacionObservada(
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        return pasos.Any(paso =>
            paso.Ejecutado
            && paso.CodigoSalida == 0
            && !string.IsNullOrWhiteSpace(paso.Salida)
            && Regex.IsMatch(
                paso.Comando,
                @"(?:^|\r?\n|[;|]\s*)Get-ChildItem\b[^\r\n;|]*-Directory\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    private static string CrearInstruccionBuscarEnCarpetaObservada(
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        string salida = pasos
            .LastOrDefault(paso =>
                paso.Ejecutado
                && paso.CodigoSalida == 0
                && !string.IsNullOrWhiteSpace(paso.Salida)
                && Regex.IsMatch(
                    paso.Comando,
                    @"(?:^|\r?\n|[;|]\s*)Get-ChildItem\b[^\r\n;|]*-Directory\b",
                    RegexOptions.IgnoreCase
                    | RegexOptions.CultureInvariant))
            ?.Salida
            ?? string.Empty;

        return $$"""
            CARPETAS REALES DE LA APLICACIÓN YA OBSERVADAS:
            {{LimitarTexto(salida)}}

            No adivines subcarpetas ni extensiones con Test-Path y no consultes
            el registro. Usa una de esas rutas literales devueltas y enumera
            primero las extensiones que existen realmente dentro de la versión
            instalada más reciente:

            Get-ChildItem -LiteralPath 'RUTA_OBSERVADA' -Filter '*.*' -File
            -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension } |
            Group-Object Extension | Sort-Object Count -Descending |
            Select-Object -First 30 | ForEach-Object {
            Write-Output ('EXTENSION=' + $_.Name);
            Write-Output ('COUNT=' + $_.Count);
            Write-Output ('SAMPLE_FULL_NAME=' + ($_.Group |
            Select-Object -First 1 -ExpandProperty FullName)) }

            Cuando la salida demuestre la extensión correcta, busca esa
            extensión concreta y usa un FULL_NAME real como origen de
            Copy-Item. No vuelvas a enumerar las raíces generales.
            """;
    }

    internal static bool EsAperturaUnicaVerificada(
        PlanTareasControl plan,
        IReadOnlyList<ResultadoPasoControl> pasos)
    {
        return plan.Tareas.Count == 1
               && TareaPareceAperturaAplicacion(plan.Tareas[0])
               && pasos.Any(paso =>
                   paso.Ejecutado
                   && paso.CodigoSalida == 0
                   && Regex.IsMatch(
                       paso.Comando,
                       @"^\s*Get-Process\s+-Name\b",
                       RegexOptions.IgnoreCase
                       | RegexOptions.CultureInvariant)
                   && Regex.IsMatch(
                       paso.Salida,
                       @"(?:^|\r?\n)PROCESS_NAME=.+(?:\r?\n|$)",
                       RegexOptions.IgnoreCase
                       | RegexOptions.CultureInvariant))
               && HayAperturaVerificada(pasos);
    }

    private static string CrearInstruccionInvestigacionApertura()
    {
        return """
            LIMITACIÓN PREMATURA RECHAZADA. Una aplicación instalada puede
            abrirse mediante consola. Ejecuta primero una consulta Get-StartApps
            filtrada por el nombre deducido y publica cada resultado así:
            Get-StartApps | Where-Object { ... } | ForEach-Object { Write-Output ('APP_NAME=' + $_.Name); Write-Output ('APP_ID=' + $_.AppID) }
            Después usa exactamente el APP_ID devuelto mediante
            explorer.exe 'shell:AppsFolder\APPID'. No confundas una aplicación
            instalada con un archivo que no se pueda abrir.
            """;
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
            variables de entorno. Reintenta con las unidades locales literales
            autorizadas:
            Get-ChildItem -LiteralPath {{rutas}} -Filter 'NOMBRE' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 20 | ForEach-Object { Write-Output ('FULL_NAME=' + $_.FullName); Write-Output ('LENGTH=' + $_.Length); Write-Output ('LAST_WRITE_TIME=' + $_.LastWriteTime) }
            Sustituye sólo NOMBRE por el nombre o patrón literal solicitado.
            Esta consulta no lee contenido. Si el usuario también ha pedido
            abrir un resultado, usa después `Start-Process 'FULL_NAME_LITERAL'`.
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
                && (Regex.IsMatch(
                        paso.Comando,
                        @"^\s*Get-Process\s+-Name\b",
                        RegexOptions.IgnoreCase
                        | RegexOptions.CultureInvariant)
                    && Regex.IsMatch(
                        paso.Salida,
                        @"(?:^|\r?\n)PROCESS_NAME=.+(?:\r?\n|$)",
                        RegexOptions.IgnoreCase
                        | RegexOptions.CultureInvariant)
                    || Regex.IsMatch(
                        paso.Salida,
                        @"(?:^|\r?\n)WINDOW_TITLE=.+(?:\r?\n|$)",
                        RegexOptions.IgnoreCase
                        | RegexOptions.CultureInvariant)))
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

    private static bool TareaPareceAperturaAplicacion(string tarea)
    {
        if (!TareaPareceApertura(tarea))
        {
            return false;
        }

        string normalizada = InventarioTexto.Normalizar(tarea);
        bool mencionaArchivo =
            normalizada.Contains("archivo", StringComparison.Ordinal)
            || normalizada.Contains("fichero", StringComparison.Ordinal)
            || normalizada.Contains("carpeta", StringComparison.Ordinal)
            || normalizada.Contains("documento", StringComparison.Ordinal)
            || normalizada.Contains("pdf", StringComparison.Ordinal)
            || normalizada.Contains("proyecto", StringComparison.Ordinal)
            || normalizada.Contains("imagen", StringComparison.Ordinal)
            || normalizada.Contains("foto", StringComparison.Ordinal)
            || normalizada.Contains("audio", StringComparison.Ordinal)
            || normalizada.Contains("video", StringComparison.Ordinal)
            || Regex.IsMatch(
                tarea,
                @"(?:[A-Za-z]:[\\/]|[\p{L}\p{N}_()-]+\.[\p{L}\p{N}_()-]+)",
                RegexOptions.CultureInvariant);

        return !mencionaArchivo;
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
