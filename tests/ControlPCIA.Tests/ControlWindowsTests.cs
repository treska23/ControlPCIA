using Xunit;

namespace ControlPCIA.Tests;

public sealed class ControlWindowsTests
{
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
    public void Un_appid_inventado_se_bloquea()
    {
        const string comando =
            "explorer.exe 'shell:AppsFolder\\identificador.inventado!App'";

        string? motivo =
            ControlWindows.ValidarProcedenciaAppId(
                comando,
                []);

        Assert.NotNull(motivo);
        Assert.Contains(
            "Get-StartApps",
            motivo,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Un_appid_devuelto_por_windows_se_admite_literalmente()
    {
        const string appId =
            "{6D809377-6AF0-444B-8957-A3773F02200E}\\Steinberg\\Aplicacion 15\\Aplicacion15.exe";
        ResultadoPasoControl consulta = new(
            1,
            "Get-StartApps | Where-Object Name -Like '*Aplicacion*'",
            true,
            0,
            $"Name          AppID{Environment.NewLine}Aplicacion 15 {appId}",
            string.Empty);

        string? motivo =
            ControlWindows.ValidarProcedenciaAppId(
                $"explorer.exe 'shell:AppsFolder\\{appId}'",
                [consulta]);

        Assert.Null(motivo);
    }

    [Fact]
    public void Una_lista_generica_de_procesos_no_responde_que_ventanas_hay()
    {
        var plan = new PlanTareasControl(
            ["Mostrar los programas que tienen una ventana abierta"]);

        Assert.NotNull(
            ControlWindows.ValidarAdecuacionConsultaInformativa(
                "Get-Process | Select-Object Name,Id",
                plan,
                []));

        Assert.NotNull(
            ControlWindows.ValidarAdecuacionConsultaInformativa(
                "Get-Process | Where-Object MainWindowTitle | Select-Object ProcessName,MainWindowTitle",
                plan,
                []));

        Assert.Null(
            ControlWindows.ValidarAdecuacionConsultaInformativa(
                "Get-Process | Where-Object MainWindowTitle | ForEach-Object { Write-Output ('PROCESS_NAME=' + $_.ProcessName); Write-Output ('WINDOW_TITLE=' + $_.MainWindowTitle) }",
                plan,
                []));
    }

    [Fact]
    public void Una_busqueda_conserva_nombre_limite_y_ruta_completa()
    {
        var plan = new PlanTareasControl(
            ["Localizar el archivo README.md"]);
        const string valida =
            "Get-ChildItem -LiteralPath 'D:\\Documentos' -Filter 'README.md' -File -Recurse | Select-Object -First 20 | ForEach-Object { Write-Output ('FULL_NAME=' + $_.FullName) }";

        Assert.Null(
            ControlWindows.ValidarAdecuacionConsultaInformativa(
                valida,
                plan,
                []));
        Assert.Contains(
            "README.md",
            ControlWindows.ValidarAdecuacionConsultaInformativa(
                valida.Replace("README.md", "README", StringComparison.Ordinal),
                plan,
                [])!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Select-Object -First 20",
            ControlWindows.ValidarAdecuacionConsultaInformativa(
                valida.Replace(
                    " | Select-Object -First 20",
                    string.Empty,
                    StringComparison.Ordinal),
                plan,
                [])!,
            StringComparison.OrdinalIgnoreCase);
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
    [InlineData("$shell = New-Object -ComObject WScript.Shell", false)]
    public void El_agente_solo_usa_el_modo_consola(
        string comando,
        bool esperado)
    {
        Assert.Equal(
            esperado,
            ControlWindows.EsComandoCompatibleConModoConsola(comando));
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
