using Xunit;

namespace ControlPCIA.Tests;

public sealed class ControlWindowsTests
{
    [Fact]
    public async Task Modo_seguro_no_ejecuta_el_inventario_previo()
    {
        int ejecuciones = 0;

        string inventario =
            await ControlWindows.ObtenerNombresAplicacionesAsync(
                soloTraducir: true,
                TestContext.Current.CancellationToken,
                ejecutarAsync: (_, _) =>
                {
                    ejecuciones++;
                    throw new InvalidOperationException(
                        "El ejecutor no debe invocarse.");
                });

        Assert.Equal(0, ejecuciones);
        Assert.Contains(
            "sin ejecución",
            inventario,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resultado_de_prueba_nunca_marca_un_paso_ejecutado()
    {
        ResultadoControl resultado =
            ControlWindows.CrearResultadoPrueba(
                new ResultadoTraduccionControl(
                    "comando_propuesto",
                    new PlanTareasControl(
                        ["abrir la calculadora"]),
                    ["aplicaciones.abrir"],
                    "Start-Process calc.exe",
                    "Start-Process calc.exe",
                    true,
                    "Comando permitido."));

        Assert.False(resultado.Completado);
        Assert.Equal("prueba_sin_ejecucion", resultado.Estado);
        Assert.All(
            resultado.Pasos,
            paso => Assert.False(paso.Ejecutado));
    }

    [Fact]
    public void Rechaza_un_ejecutable_y_argumento_inventados()
    {
        ResultadoValidacionPowerShell resultado =
            ControlWindows.ValidarProcedenciaInicioGeneral(
                "Start-Process -FilePath 'Cubase.exe' -ArgumentList '-newproject','Demo'",
                [],
                []);

        Assert.False(resultado.Permitido);
        Assert.Contains(
            "Cubase.exe",
            resultado.Motivo,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Permite_ejecutable_y_opcion_observados_en_stdout()
    {
        ResultadoPasoControl descubrimiento = new(
            1,
            "Get-Command Cubase.exe",
            true,
            0,
            "EXECUTABLE_PATH=C:\\Program Files\\Steinberg\\Cubase.exe\nOPTION=--new-project",
            string.Empty);

        ResultadoValidacionPowerShell resultado =
            ControlWindows.ValidarProcedenciaInicioGeneral(
                "Start-Process -FilePath 'C:\\Program Files\\Steinberg\\Cubase.exe' -ArgumentList '--new-project','Demo'",
                [descubrimiento],
                []);

        Assert.True(resultado.Permitido, resultado.Motivo);
    }

    [Fact]
    public void Permite_appid_y_archivo_observados_en_stdout()
    {
        ResultadoPasoControl aplicaciones = new(
            1,
            "Get-StartApps",
            true,
            0,
            "APP_ID=Steinberg.Cubase_123!App",
            string.Empty);
        ResultadoPasoControl archivos = new(
            2,
            "Get-ChildItem",
            true,
            0,
            "FULL_NAME=D:\\Proyectos\\Cancion.cpr",
            string.Empty);

        Assert.True(
            ControlWindows.ValidarProcedenciaInicioGeneral(
                "explorer.exe 'shell:AppsFolder\\Steinberg.Cubase_123!App'",
                [aplicaciones],
                []).Permitido);
        Assert.True(
            ControlWindows.ValidarProcedenciaInicioGeneral(
                "Start-Process -FilePath 'D:\\Proyectos\\Cancion.cpr'",
                [archivos],
                []).Permitido);
    }

    [Fact]
    public void Permite_una_url_literal_sin_inventario_local()
    {
        Assert.True(
            ControlWindows.ValidarProcedenciaInicioGeneral(
                "Start-Process 'https://www.youtube.com/results?search_query=prueba'",
                [],
                []).Permitido);
    }

    [Theory]
    [InlineData(1, 24)]
    [InlineData(4, 24)]
    [InlineData(8, 48)]
    [InlineData(24, 96)]
    [InlineData(40, 96)]
    public void Amplia_los_pasos_para_ordenes_multitarea(
        int tareas,
        int esperado)
    {
        Assert.Equal(
            esperado,
            ControlWindows.CalcularMaximoPasos(tareas));
    }

    [Theory]
    [InlineData(
        "CONFIRMAR: ¿Quieres cambiar ahora la escala de todas las pantallas?",
        "¿Quieres cambiar ahora la escala de todas las pantallas?")]
    [InlineData(
        "confirmar:",
        "Esta acción necesita confirmación. ¿Quieres que continúe?")]
    public void Reconoce_una_pregunta_de_confirmacion(
        string respuesta,
        string esperada)
    {
        Assert.True(
            ControlWindows.TryObtenerPreguntaConfirmacion(
                respuesta,
                out string pregunta));
        Assert.Equal(esperada, pregunta);
    }

    [Theory]
    [InlineData(
        "PREGUNTAR: ¿Qué nombre quieres poner al proyecto?",
        "¿Qué nombre quieres poner al proyecto?")]
    [InlineData(
        "preguntar:",
        "¿Qué dato falta para continuar?")]
    public void Reconoce_una_pregunta_de_datos_para_el_usuario(
        string respuesta,
        string esperada)
    {
        Assert.True(
            ControlWindows.TryObtenerPreguntaUsuario(
                respuesta,
                out string pregunta));
        Assert.Equal(esperada, pregunta);
    }

    [Fact]
    public void Un_cierre_exige_enumerar_despues()
    {
        ResultadoPasoControl[] pasos =
        [
            Exito("Get-Process -Name devenv"),
            Exito("Get-Process -Name devenv | ForEach-Object { $_.CloseMainWindow() }")
        ];

        Assert.True(
            ControlWindows.RequiereVerificacionTrasCambio(pasos));

        pasos =
        [
            .. pasos,
            Exito("Get-Process -Name devenv")
        ];

        Assert.False(
            ControlWindows.RequiereVerificacionTrasCambio(pasos));
    }

    [Fact]
    public void Un_appid_aprendido_se_reutiliza_sin_repetir_el_inventario()
    {
        const string appId = "Aplicacion.Verificada_123!App";
        var receta = new RecetaReferencia(
            "abre aplicacion",
            [
                $"explorer.exe 'shell:AppsFolder\\{appId}'",
                "Get-Process -Name Aplicacion"
            ],
            3,
            1);
        IReadOnlySet<string> aprendidos =
            ControlWindows.ObtenerAppIdsAprendidos([receta]);

        Assert.Contains(appId, aprendidos);
    }

    [Fact]
    public void Un_appid_clasico_deduce_carpetas_reales_del_fabricante()
    {
        const string appId =
            "{6D809377-6AF0-444B-8957-A3773F02200E}\\Microsoft\\Aplicacion\\Aplicacion.exe";
        var receta = new RecetaReferencia(
            "abre aplicacion",
            [
                $"explorer.exe 'shell:AppsFolder\\{appId}'",
                "Get-Process -Name Aplicacion"
            ],
            1,
            1);

        IReadOnlyList<string> carpetas =
            ControlWindows.ObtenerCarpetasAplicacionesAprendidas(
                [receta]);

        Assert.Contains(
            carpetas,
            ruta => Path.GetFileName(ruta).Equals(
                "Microsoft",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Una_plantilla_aprendida_evitar_repetir_su_busqueda()
    {
        string plantilla = Path.Combine(
            Path.GetTempPath(),
            "ControlPCIA-Plantilla-" + Guid.NewGuid().ToString("N")
            + ".cpr");
        File.WriteAllText(plantilla, "plantilla");

        try
        {
            var receta = new RecetaReferencia(
                "crear proyecto",
                [
                    $"Copy-Item -LiteralPath '{plantilla}' -Destination 'C:\\Proyecto.cpr'"
                ],
                2,
                1);

            Assert.Equal(
                Path.GetFullPath(plantilla),
                Assert.Single(
                    ControlWindows
                        .ObtenerOrigenesCopyItemAprendidos(
                            [receta])));
            Assert.True(
                ControlWindows.EsBusquedaDeRecursosRepetida(
                    "Get-ChildItem -Path C:\\ -Filter '*.cpr' -Recurse"));
            Assert.False(
                ControlWindows.EsBusquedaDeRecursosRepetida(
                    $"Copy-Item -LiteralPath '{plantilla}' -Destination 'C:\\Proyecto.cpr'"));
        }
        finally
        {
            File.Delete(plantilla);
        }
    }

    [Fact]
    public void Abrir_por_appid_exige_comprobar_el_proceso_despues()
    {
        ResultadoPasoControl[] pasos =
        [
            Exito(
                "explorer.exe 'shell:AppsFolder\\identificador!App'")
        ];

        Assert.True(
            ControlWindows.RequiereVerificacionTrasCambio(pasos));

        pasos =
        [
            .. pasos,
            new ResultadoPasoControl(
                2,
                "Get-Process -Name Aplicacion",
                true,
                0,
                "Id ProcessName\n12 Aplicacion",
                string.Empty)
        ];

        Assert.False(
            ControlWindows.RequiereVerificacionTrasCambio(pasos));
    }

    [Fact]
    public void Un_appid_clasico_se_comprueba_con_su_proceso_real()
    {
        const string apertura =
            "explorer.exe 'shell:AppsFolder\\{6D809377-6AF0-444B-8957-A3773F02200E}\\Steinberg\\Cubase 15\\Cubase15.exe'";

        string comprobacion =
            ControlWindows.CrearComandoVerificacionApertura(
                apertura);

        Assert.Contains(
            "Get-Process -Name 'Cubase15'",
            comprobacion,
            StringComparison.Ordinal);
        Assert.Contains(
            "PROCESS_NAME=",
            comprobacion,
            StringComparison.Ordinal);
    }

    [Fact]
    public void El_proceso_concreto_demuestra_apertura_aunque_siga_cargando()
    {
        ResultadoPasoControl apertura = new(
            1,
            "explorer.exe 'shell:AppsFolder\\{GUID}\\Cubase15.exe'",
            true,
            0,
            string.Empty,
            string.Empty);
        ResultadoPasoControl comprobacion = new(
            2,
            "Get-Process -Name 'Cubase15' -ErrorAction SilentlyContinue | ForEach-Object { Write-Output ('PROCESS_NAME=' + $_.ProcessName); Write-Output ('WINDOW_TITLE=' + $_.MainWindowTitle) }",
            true,
            0,
            "PROCESS_NAME=Cubase15\nWINDOW_TITLE=Comprobando licencias...",
            string.Empty);

        Assert.True(
            ControlWindows.HayAperturaVerificada(
                [apertura, comprobacion]));
        Assert.True(
            ControlWindows.EsAperturaUnicaVerificada(
                new PlanTareasControl(["abrir Cubase"]),
                [apertura, comprobacion]));
        Assert.False(
            ControlWindows.EsAperturaUnicaVerificada(
                new PlanTareasControl(
                    ["abrir Cubase", "crear una pista"]),
                [apertura, comprobacion]));
    }

    [Fact]
    public void Una_consulta_vacia_no_demuestra_que_la_aplicacion_se_abrio()
    {
        ResultadoPasoControl apertura =
            Exito("Start-Process Aplicacion");
        ResultadoPasoControl consultaVacia =
            Exito("Get-Process -Name Aplicacion");

        Assert.True(
            ControlWindows.RequiereVerificacionTrasCambio(
                [apertura, consultaVacia]));

        ResultadoPasoControl consultaConProceso = new(
            3,
            "Get-Process -Name Aplicacion",
            true,
            0,
            "Id ProcessName\n12 Aplicacion",
            string.Empty);

        Assert.False(
            ControlWindows.RequiereVerificacionTrasCambio(
                [apertura, consultaConProceso]));
    }

    [Theory]
    [InlineData("ControlPCIA.exe ui inspect 'Calculator' 4", false)]
    [InlineData("ControlPCIA.exe window keys 'Calculator' '2+5='", false)]
    [InlineData("Get-Process CalculatorApp", true)]
    [InlineData("$shell = New-Object -ComObject WScript.Shell", true)]
    [InlineData("$shell.AppActivate('Microsoft Edge')", true)]
    [InlineData("[Ventanas]::ShowWindowAsync($p.MainWindowHandle, 3)", true)]
    [InlineData("[Ventanas]::SetForegroundWindow($p.MainWindowHandle)", true)]
    [InlineData("$excel = New-Object -ComObject Excel.Application", true)]
    [InlineData("$shell.SendKeys('texto')", false)]
    [InlineData("[System.Windows.Forms.SendKeys]::SendWait('%{F4}')", false)]
    public void El_agente_solo_usa_el_modo_consola(
        string comando,
        bool esperado)
    {
        Assert.Equal(
            esperado,
            ControlWindows.EsComandoCompatibleConModoConsola(comando));
    }

    [Fact]
    public void Reconoce_un_plan_dedicado_al_estado_de_ventanas()
    {
        var plan = new PlanTareasControl(
            [
                "maximiza la ventana de Microsoft Edge",
                "activa la ventana de Microsoft Edge"
            ]);

        Assert.True(
            ControlWindows.PlanSolicitaSoloEstadoDeVentanas(plan));
        Assert.False(
            ControlWindows.PlanSolicitaSoloEstadoDeVentanas(
                new PlanTareasControl(
                    ["cierra y vuelve a abrir Microsoft Edge"])));
        Assert.False(
            ControlWindows.PlanSolicitaSoloEstadoDeVentanas(
                new PlanTareasControl(
                    [
                        "abre Microsoft Edge",
                        "maximiza su ventana"
                    ],
                    [
                        "aplicaciones.abrir",
                        "ventanas.estado"
                    ])));
    }

    [Theory]
    [InlineData("Stop-Process -Name msedge")]
    [InlineData("taskkill.exe /IM msedge.exe")]
    [InlineData("$p.CloseMainWindow()")]
    public void No_confunde_cerrar_el_proceso_con_cambiar_su_ventana(
        string comando)
    {
        Assert.True(
            ControlWindows.EsEstrategiaQueCierraLaAplicacion(
                comando));
    }

    [Theory]
    [InlineData("[System.Windows.Forms.SendKeys]::SendWait('%{F4}')")]
    [InlineData("Start-Process msedge")]
    [InlineData("Get-StartApps | Where-Object Name -Like '*Edge*'")]
    [InlineData("Set-ItemProperty -Path 'HKCU:\\Control Panel\\Desktop' -Name WinPos -Value 1")]
    public void Rechaza_estrategias_ajenas_a_una_receta_de_ventana(
        string comando)
    {
        Assert.True(
            ControlWindows.EsEstrategiaInvalidaParaEstadoVentana(
                comando));
    }

    [Fact]
    public void Detecta_una_negativa_inventada_sobre_control_de_ventanas()
    {
        Assert.True(
            ControlWindows.RespuestaNiegaControlEstadoVentana(
                "No se puede maximizar la ventana por restricciones de seguridad."));
    }

    [Fact]
    public void Reconoce_evidencia_estructurada_del_estado_de_una_ventana()
    {
        const string salida =
            "PROCESS_NAME=msedge\r\nACTIVATED=True\r\nMAXIMIZED=True";

        Assert.True(
            ControlWindows.SalidaDemuestraEstadoVentana(salida));
        Assert.True(
            ControlWindows.SalidaCompletaPlanEstadoVentana(
                new PlanTareasControl(
                    [
                        "maximiza la ventana de Microsoft Edge",
                        "activa la ventana de Microsoft Edge"
                    ],
                    ["ventanas.estado"]),
                salida));
    }

    [Fact]
    public void Reconoce_una_respuesta_natural()
    {
        Assert.True(
            ControlWindows.TryObtenerRespuestaNatural(
                "RESPONDER: Tienes abiertas Calculadora y Word.",
                out string respuesta));
        Assert.Equal(
            "Tienes abiertas Calculadora y Word.",
            respuesta);
    }

    [Theory]
    [InlineData(
        "LIMITACION: Calculadora no admite expresiones por consola.",
        "Calculadora no admite expresiones por consola.")]
    [InlineData(
        "limitacion:",
        "Esta acción no dispone de un comando, API o protocolo permitido que pueda ejecutarse íntegramente desde consola.")]
    public void Reconoce_un_limite_de_consola(
        string respuesta,
        string esperada)
    {
        Assert.True(
            ControlWindows.TryObtenerLimitacion(
                respuesta,
                out string texto));
        Assert.Equal(esperada, texto);
    }

    [Theory]
    [InlineData(
        "`Get-StartApps | Select-Object Name, AppID`",
        "Get-StartApps | Select-Object Name, AppID")]
    [InlineData(
        "```powershell\nGet-Process\n```",
        "Get-Process")]
    [InlineData(
        "**[ ] 1. abrir**\nExplicación previa.\n```powershell\nStart-Process Calculator\n```",
        "Start-Process Calculator")]
    [InlineData(
        "El primer paso es identificar Cubase. Vamos a usar el comando `Get-StartApps` para buscarlo.",
        "Get-StartApps")]
    [InlineData(
        "> explorer.exe 'shell:AppsFolder\\Aplicacion_123!App'\n>",
        "explorer.exe 'shell:AppsFolder\\Aplicacion_123!App'")]
    public void Limpia_el_formato_markdown_sin_alterar_el_comando(
        string respuesta,
        string esperado)
    {
        Assert.Equal(
            esperado,
            ControlWindows.LimpiarComando(respuesta));
    }

    [Theory]
    [InlineData("APP_NAME=Calculator", true)]
    [InlineData("APP_ID=Microsoft.WindowsCalculator_123!App", true)]
    [InlineData("Get-StartApps | Select-Object Name,AppID", false)]
    public void No_ejecuta_datos_inventados_como_si_fueran_comandos(
        string respuesta,
        bool esperado)
    {
        Assert.Equal(
            esperado,
            ControlWindows.PareceDatoDeAplicacionSinComando(respuesta));
    }

    [Fact]
    public void Un_inicio_fallido_obliga_a_investigar_antes_de_preguntar()
    {
        ResultadoPasoControl fallo = new(
            1,
            "Start-Process Aplicacion",
            true,
            1,
            string.Empty,
            "No se encontró el archivo.");

        Assert.True(
            ControlWindows.RequiereInvestigarAplicacion([fallo]));

        ResultadoPasoControl consulta = new(
            2,
            "Get-StartApps | Where-Object Name -Like '*Aplicacion*'",
            true,
            0,
            "Aplicacion Aplicacion_123!App",
            string.Empty);

        Assert.False(
            ControlWindows.RequiereInvestigarAplicacion(
                [fallo, consulta]));
    }

    [Fact]
    public void Una_apertura_de_aplicacion_no_admite_limitacion_sin_inventario()
    {
        var plan = new PlanTareasControl(["abrir Cubase"]);

        Assert.True(
            ControlWindows.RequiereConsultarAplicacionesAntesDeLimitar(
                plan,
                []));

        ResultadoPasoControl consulta = new(
            1,
            "Get-StartApps | Where-Object Name -Like '*Cubase*' | ForEach-Object { Write-Output ('APP_NAME=' + $_.Name); Write-Output ('APP_ID=' + $_.AppID) }",
            true,
            0,
            "APP_NAME=Cubase 15\nAPP_ID=identificador",
            string.Empty);

        Assert.False(
            ControlWindows.RequiereConsultarAplicacionesAntesDeLimitar(
                plan,
                [consulta]));
    }

    [Fact]
    public void Una_creacion_con_nombre_no_admite_preguntar_otra_vez()
    {
        var plan = new PlanTareasControl(
            [
                "crear un proyecto nuevo en Cubase",
                "nombrar el proyecto como Cancion nueva"
            ]);

        Assert.True(
            ControlWindows.RequiereContinuarCreacionDesdePlantilla(
                plan,
                []));
        Assert.False(
            ControlWindows.RequiereContinuarCreacionDesdePlantilla(
                new PlanTareasControl(
                    ["crear un proyecto nuevo en Cubase"]),
                []));
        Assert.True(
            ControlWindows.RequiereContinuarCreacionDesdePlantilla(
                plan,
                [
                    Exito(
                        "New-Item -Path 'D:\\Cancion nueva' -ItemType Directory")
                ]));
        Assert.False(
            ControlWindows.RequiereContinuarCreacionDesdePlantilla(
                plan,
                [
                    Exito(
                        "Copy-Item -LiteralPath 'C:\\Plantilla.cpr' -Destination 'D:\\Cancion nueva.cpr'")
                ]));
    }

    [Fact]
    public void Una_copia_de_proyecto_debe_conservar_el_nombre_exacto()
    {
        const string instruccion =
            "crea un proyecto nuevo en Cubase llamado ControlPCIA IA 20260718 O";
        var plan = new PlanTareasControl([instruccion]);
        const string comando =
            "Copy-Item -LiteralPath 'C:\\Plantilla.cpr' -Destination 'D:\\Documentos\\ControlPCIA IA 20260718 O.cpr'";

        Assert.True(
            ControlWindows.TryObtenerDestinoCopyItem(
                comando,
                out string destino));
        Assert.True(
            ControlWindows.DestinoConservaNombreSolicitado(
                destino,
                instruccion,
                plan));
        Assert.False(
            ControlWindows.DestinoConservaNombreSolicitado(
                "D:\\Documentos\\Plantilla Cubase Personalizada.cpr",
                instruccion,
                plan));
    }

    [Fact]
    public void Reconoce_un_proyecto_creado_y_abierto_desde_una_plantilla()
    {
        string carpeta = Path.Combine(
            Path.GetTempPath(),
            "ControlPCIA-Proyecto-" + Guid.NewGuid().ToString("N"));
        string archivo = Path.Combine(
            carpeta,
            "Proyecto exacto.cpr");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(archivo, "proyecto");

        try
        {
            const string instruccion =
                "crea un proyecto nuevo llamado Proyecto exacto";
            var plan = new PlanTareasControl([instruccion]);
            ResultadoPasoControl copia = new(
                1,
                $"Copy-Item -LiteralPath 'C:\\Plantilla.cpr' -Destination '{archivo}'",
                true,
                0,
                string.Empty,
                string.Empty);
            ResultadoPasoControl apertura = new(
                2,
                $"Start-Process -FilePath '{archivo}'",
                true,
                0,
                string.Empty,
                string.Empty);

            Assert.True(
                ControlWindows
                    .TryObtenerCreacionDesdePlantillaVerificada(
                        plan,
                        instruccion,
                        [copia, apertura],
                        out string destino));
            Assert.Equal(
                Path.GetFullPath(archivo),
                destino);
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public void Tras_observar_carpetas_reales_rechaza_rutas_adivinadas_aunque_haya_comentarios()
    {
        ResultadoPasoControl carpetas = new(
            1,
            """
            # Buscar versiones instaladas
            Get-ChildItem -LiteralPath 'C:\Datos\Fabricante' -Directory
            """,
            true,
            0,
            "C:\\Datos\\Fabricante\\Aplicacion 15",
            string.Empty);

        Assert.True(
            ControlWindows.EsConsultaEspeculativaTrasCarpetaObservada(
                """
                # Probar una subcarpeta inventada
                Test-Path 'C:\Datos\Fabricante\Aplicacion 15\Templates'
                """,
                [carpetas]));

        Assert.True(
            ControlWindows.EsConsultaEspeculativaTrasCarpetaObservada(
                """
                # Consultar otra ubicación inventada
                Get-ItemProperty 'HKLM:\Software\Fabricante'
                """,
                [carpetas]));
    }

    [Theory]
    [InlineData(
        "explorer.exe 'shell:AppsFolder\\Aplicacion_123!App'",
        true)]
    [InlineData(
        "Start-Process Aplicacion",
        true)]
    [InlineData(
        "Start-Process -FilePath \"C:\\Aplicaciones\\Aplicacion.exe\"",
        true)]
    [InlineData(
        "Start-Process Aplicacion -ArgumentList '--new-project','D:\\Proyecto'",
        false)]
    [InlineData(
        "Start-Process 'D:\\Proyecto\\Cancion.cpr'",
        false)]
    public void Distingue_abrir_sin_mas_de_crear_mediante_comandos(
        string comando,
        bool aperturaSimple)
    {
        Assert.Equal(
            aperturaSimple,
            ControlWindows.EsAperturaSimpleSinParametros(comando));
    }

    [Fact]
    public void Abrir_un_documento_no_obliga_a_consultarlo_como_aplicacion()
    {
        var plan = new PlanTareasControl(
            ["abrir el documento informe.pdf"]);

        Assert.False(
            ControlWindows.RequiereConsultarAplicacionesAntesDeLimitar(
                plan,
                []));
    }

    [Fact]
    public void Una_busqueda_bloqueada_obliga_a_reintentar_con_ruta_literal()
    {
        ResultadoPasoControl fallo = new(
            1,
            "Get-ChildItem -Path $env:USERPROFILE -Filter 'README.md' -Recurse",
            false,
            -1,
            string.Empty,
            "BLOQUEADO: se requiere una ruta literal.");

        Assert.True(
            ControlWindows.RequiereReintentarBusquedaArchivos(
                [fallo]));

        ResultadoPasoControl exito = new(
            2,
            $"Get-ChildItem -LiteralPath '{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}' -Filter 'README.md' -File -Recurse",
            true,
            0,
            string.Empty,
            string.Empty);

        Assert.False(
            ControlWindows.RequiereReintentarBusquedaArchivos(
                [fallo, exito]));
    }

    [Fact]
    public void Reconoce_una_apertura_comprobada_por_titulo_de_ventana()
    {
        ResultadoPasoControl apertura = new(
            1,
            "explorer.exe 'shell:AppsFolder\\Aplicacion_123!App'",
            true,
            0,
            string.Empty,
            string.Empty);
        ResultadoPasoControl comprobacion = new(
            2,
            "Get-Process | Where-Object MainWindowTitle",
            true,
            0,
            "PROCESS_NAME=Aplicacion\nWINDOW_TITLE=Aplicacion",
            string.Empty);

        Assert.True(
            ControlWindows.HayAperturaVerificada(
                [apertura, comprobacion]));
    }

    [Fact]
    public void Devuelve_consultas_del_pc_sin_reinterpretar_la_salida_real()
    {
        var plan = new PlanTareasControl(
            [
                "Mostrar los programas que tienen una ventana abierta",
                "Localizar el archivo README.md",
                "No abrir ni leer el contenido del archivo"
            ]);
        ResultadoPasoControl ventanas = new(
            1,
            "Get-Process | Where-Object MainWindowTitle | ForEach-Object { Write-Output ('PROCESS_NAME=' + $_.ProcessName); Write-Output ('WINDOW_TITLE=' + $_.MainWindowTitle) }",
            true,
            0,
            "PROCESS_NAME=Code\nWINDOW_TITLE=ControlWindows.cs",
            string.Empty);
        ResultadoPasoControl archivo = new(
            2,
            "Get-ChildItem -LiteralPath 'D:\\Documentos' -Filter 'README.md' -File -Recurse | Select-Object -First 20 | ForEach-Object { Write-Output ('FULL_NAME=' + $_.FullName); Write-Output ('LENGTH=' + $_.Length) }",
            true,
            0,
            "FULL_NAME=D:\\Documentos\\README.md\nLENGTH=1234",
            string.Empty);

        Assert.True(
            ControlWindows.TryCrearRespuestaConsultasEstructuradas(
                plan,
                [ventanas, archivo],
                out string respuesta));
        Assert.Contains("Code", respuesta);
        Assert.Contains(
            "D:\\Documentos\\README.md",
            respuesta,
            StringComparison.Ordinal);
        Assert.Contains(
            "sin abrir ni leer su contenido",
            respuesta,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_responde_una_consulta_multiple_si_falta_una_evidencia()
    {
        var plan = new PlanTareasControl(
            [
                "Mostrar los programas que tienen una ventana abierta",
                "Localizar el archivo README.md"
            ]);
        ResultadoPasoControl ventanas = new(
            1,
            "Get-Process | Where-Object MainWindowTitle | ForEach-Object { Write-Output ('PROCESS_NAME=' + $_.ProcessName); Write-Output ('WINDOW_TITLE=' + $_.MainWindowTitle) }",
            true,
            0,
            "PROCESS_NAME=Code\nWINDOW_TITLE=ControlWindows.cs",
            string.Empty);

        Assert.False(
            ControlWindows.TryCrearRespuestaConsultasEstructuradas(
                plan,
                [ventanas],
                out _));
    }

    private static ResultadoPasoControl Exito(string comando)
    {
        return new ResultadoPasoControl(1, comando, true, 0, string.Empty, string.Empty);
    }
}
