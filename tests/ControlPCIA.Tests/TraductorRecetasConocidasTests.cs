using Xunit;

namespace ControlPCIA.Tests;

public sealed class TraductorRecetasConocidasTests
{
    [Theory]
    [InlineData(
        "pon el navegador de Microsoft delante y a pantalla grande",
        "ventanas.estado")]
    [InlineData(
        "inicia Cubase",
        "aplicaciones.abrir")]
    [InlineData(
        "dime qué programas tengo abiertos",
        "aplicaciones.inventario")]
    [InlineData(
        "dónde está informe.pdf",
        "archivos.buscar")]
    [InlineData(
        "abre informe.pdf",
        "archivos.abrir")]
    public void La_receta_se_aplica_a_formulaciones_naturales(
        string tarea,
        string conocimiento)
    {
        var plan = new PlanTareasControl(
            [tarea],
            [conocimiento]);

        Assert.True(
            TraductorRecetasConocidas.PuedeResolverPlan(plan));
    }

    [Fact]
    public void Un_plan_mixto_puede_combinar_recetas()
    {
        var plan = new PlanTareasControl(
            [
                "abrir Microsoft Edge",
                "maximizar su ventana y traerla al frente"
            ],
            [
                "aplicaciones.abrir",
                "ventanas.estado"
            ]);

        Assert.True(
            TraductorRecetasConocidas.PuedeResolverPlan(plan));
        Assert.Equal(
            [
                "aplicaciones.abrir",
                "ventanas.estado"
            ],
            TraductorRecetasConocidas
                .ObtenerConocimientosAplicables(plan));
    }

    [Fact]
    public void Una_tarea_desconocida_no_se_fuerza_en_una_receta()
    {
        var plan = new PlanTareasControl(
            [
                "abrir Cubase",
                "crear una pista de instrumento"
            ],
            ["aplicaciones.abrir"]);

        Assert.False(
            TraductorRecetasConocidas.PuedeResolverPlan(plan));
    }

    [Theory]
    [InlineData("Stop-Process -Name msedge")]
    [InlineData("Start-Process msinfo32")]
    [InlineData("Set-ItemProperty -Path HKCU:\\Software\\X -Name Y -Value 1")]
    [InlineData("[System.Windows.Forms.SendKeys]::SendWait('%{F4}')")]
    public void Una_receta_de_ventana_rechaza_acciones_ajenas(
        string comando)
    {
        var plan = new PlanTareasControl(
            ["maximizar y activar Microsoft Edge"],
            ["ventanas.estado"]);

        ResultadoValidacionPowerShell resultado =
            TraductorRecetasConocidas.ValidarAlineacion(
                plan,
                comando,
                []);

        Assert.False(resultado.Permitido);
    }

    [Fact]
    public void Una_receta_de_ventana_acepta_solo_su_api_de_consola()
    {
        var plan = new PlanTareasControl(
            ["maximizar y activar Microsoft Edge"],
            ["ventanas.estado"]);
        const string comando =
            "$p=Get-Process -Name msedge | Where-Object MainWindowHandle; "
            + "[Ventana]::ShowWindowAsync($p.MainWindowHandle,3); "
            + "$activada=(New-Object -ComObject WScript.Shell).AppActivate($p.Id); "
            + "$maximizada=[Ventana]::IsZoomed($p.MainWindowHandle); "
            + "Write-Output ('PROCESS_NAME=' + $p.ProcessName); "
            + "Write-Output ('ACTIVATED=' + $activada); "
            + "Write-Output ('MAXIMIZED=' + $maximizada)";
        ResultadoPasoControl inventario = new(
            1,
            "Get-Process | Where-Object MainWindowTitle",
            true,
            0,
            "PROCESS_NAME=msedge\nWINDOW_TITLE=Microsoft Edge",
            string.Empty);

        ResultadoValidacionPowerShell resultado =
            TraductorRecetasConocidas.ValidarAlineacion(
                plan,
                comando,
                [inventario]);

        Assert.True(resultado.Permitido, resultado.Motivo);
    }

    [Fact]
    public void Abrir_una_aplicacion_exige_un_appid_observado()
    {
        var plan = new PlanTareasControl(
            ["abrir Cubase"],
            ["aplicaciones.abrir"]);
        const string apertura =
            "explorer.exe 'shell:AppsFolder\\Steinberg.Cubase!App'";

        Assert.False(
            TraductorRecetasConocidas.ValidarAlineacion(
                plan,
                apertura,
                []).Permitido);

        ResultadoPasoControl inventario = new(
            1,
            "Get-StartApps",
            true,
            0,
            "APP_NAME=Cubase\nAPP_ID=Steinberg.Cubase!App",
            string.Empty);

        Assert.True(
            TraductorRecetasConocidas.ValidarAlineacion(
                plan,
                apertura,
                [inventario]).Permitido);
    }

    [Fact]
    public void Un_appid_de_una_receta_aprendida_conserva_su_procedencia()
    {
        var plan = new PlanTareasControl(
            ["abrir Cubase"],
            ["aplicaciones.abrir"]);
        const string apertura =
            "explorer.exe 'shell:AppsFolder\\Steinberg.Cubase!App'";
        var receta = new RecetaReferencia(
            "abrir cubase",
            [apertura, "Get-Process -Name Cubase"],
            3,
            1);

        Assert.True(
            TraductorRecetasConocidas.ValidarAlineacion(
                plan,
                apertura,
                [],
                [receta]).Permitido);
    }

    [Fact]
    public void Abrir_un_archivo_exige_una_ruta_observada()
    {
        var plan = new PlanTareasControl(
            ["abrir informe.pdf"],
            ["archivos.abrir"]);
        const string apertura =
            "Start-Process -FilePath 'D:\\Documentos\\informe.pdf'";

        Assert.False(
            TraductorRecetasConocidas.ValidarAlineacion(
                plan,
                apertura,
                []).Permitido);

        ResultadoPasoControl busqueda = new(
            1,
            "Get-ChildItem -Filter informe.pdf",
            true,
            0,
            "FULL_NAME=D:\\Documentos\\informe.pdf",
            string.Empty);

        Assert.True(
            TraductorRecetasConocidas.ValidarAlineacion(
                plan,
                apertura,
                [busqueda]).Permitido);
    }

    [Fact]
    public void El_modo_diagnostico_declara_que_no_ejecuta()
    {
        var resultado = new ResultadoTraduccionControl(
            "comando_propuesto",
            new PlanTareasControl(
                ["abrir Cubase"],
                ["aplicaciones.abrir"]),
            ["aplicaciones.abrir"],
            "Get-StartApps",
            "Get-StartApps",
            true,
            "Válido");

        Assert.False(resultado.Ejecutado);
    }

    [Fact]
    public void El_prompt_corto_prohibe_internet_y_ejecucion_del_modelo()
    {
        List<MensajeOllama> mensajes =
            TraductorRecetasConocidas.CrearMensajes(
                "dime qué programas tengo abiertos",
                new PlanTareasControl(
                    ["listar programas abiertos"],
                    ["aplicaciones.inventario"]),
                []);
        string prompt = string.Join(
            "\n",
            mensajes.Select(mensaje => mensaje.Contenido));

        Assert.Contains(
            "No busques en Internet",
            prompt,
            StringComparison.Ordinal);
        Assert.Contains(
            "Tú no ejecutas nada",
            prompt,
            StringComparison.Ordinal);
        Assert.Contains(
            "PROCESS_NAME",
            prompt,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "maximiza Edge",
        "ventanas.estado",
        "Get-Process")]
    [InlineData(
        "dime qué programas tengo abiertos",
        "aplicaciones.inventario",
        "Get-Process")]
    [InlineData(
        "abre Cubase",
        "aplicaciones.abrir",
        "Get-StartApps")]
    public void El_primer_paso_conocido_es_fijo_y_de_solo_lectura(
        string tarea,
        string conocimiento,
        string fragmento)
    {
        var plan = new PlanTareasControl(
            [tarea],
            [conocimiento]);

        Assert.True(
            TraductorRecetasConocidas
                .TryCrearPrimerComandoDeterminista(
                    plan,
                    [],
                    out string comando));
        Assert.Contains(
            fragmento,
            comando,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Stop-Process",
            comando,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Start-Process",
            comando,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        "buscar informe.pdf",
        "informe.pdf")]
    [InlineData(
        "abrir el informe anual.pdf",
        "informe anual.pdf")]
    public void La_consulta_de_archivo_con_nombre_es_fija_y_estructurada(
        string tarea,
        string nombre)
    {
        var plan = new PlanTareasControl(
            [tarea],
            ["archivos.buscar"]);

        Assert.True(
            TraductorRecetasConocidas
                .TryCrearConsultaArchivoDeterminista(
                    plan,
                    out string comando));
        Assert.Contains(
            $"-Filter '{nombre}'",
            comando,
            StringComparison.Ordinal);
        Assert.Contains(
            "FULL_NAME=",
            comando,
            StringComparison.Ordinal);
        Assert.Contains(
            "LAST_WRITE_TIME=",
            comando,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Start-Process",
            comando,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_inserta_una_cadena_de_comandos_como_nombre_de_archivo()
    {
        var plan = new PlanTareasControl(
            ["buscar informe.pdf; Stop-Process explorer"],
            ["archivos.buscar"]);

        Assert.False(
            TraductorRecetasConocidas
                .TryCrearConsultaArchivoDeterminista(
                    plan,
                    out _));
    }

    [Theory]
    [InlineData(
        "maximizar y activar Microsoft Edge",
        "msedge")]
    [InlineData(
        "traer al frente el navegador de Microsoft",
        "msedge")]
    public void Selecciona_solo_un_proceso_observado(
        string tarea,
        string esperado)
    {
        var plan = new PlanTareasControl(
            [tarea],
            ["ventanas.estado"]);
        ResultadoPasoControl inventario = new(
            1,
            "Get-Process",
            true,
            0,
            "PROCESS_NAME=msedge\nWINDOW_TITLE=Microsoft Edge\n"
            + "PROCESS_NAME=CalculatorApp\nWINDOW_TITLE=Calculadora",
            string.Empty);

        Assert.Equal(
            esperado,
            TraductorRecetasConocidas
                .TrySeleccionarProcesoVentana(
                    plan,
                    [inventario]));
    }

    [Fact]
    public void No_elige_un_proceso_si_el_inventario_es_ambiguo()
    {
        var plan = new PlanTareasControl(
            ["trae la aplicación al frente"],
            ["ventanas.estado"]);
        ResultadoPasoControl inventario = new(
            1,
            "Get-Process",
            true,
            0,
            "PROCESS_NAME=uno\nWINDOW_TITLE=Aplicación uno\n"
            + "PROCESS_NAME=dos\nWINDOW_TITLE=Aplicación dos",
            string.Empty);

        Assert.Null(
            TraductorRecetasConocidas
                .TrySeleccionarProcesoVentana(
                    plan,
                    [inventario]));
    }

    [Fact]
    public void La_plantilla_fija_de_ventana_publica_toda_la_evidencia()
    {
        var plan = new PlanTareasControl(
            [
                "maximizar Microsoft Edge",
                "activar Microsoft Edge"
            ],
            ["ventanas.estado"]);
        string comando =
            TraductorRecetasConocidas
                .CrearComandoEstadoVentana(
                    plan,
                    "msedge");
        ResultadoPasoControl inventario = new(
            1,
            "Get-Process",
            true,
            0,
            "PROCESS_NAME=msedge\nWINDOW_TITLE=Microsoft Edge",
            string.Empty);

        Assert.Contains(
            "Get-Process -Name 'msedge'",
            comando,
            StringComparison.Ordinal);
        Assert.Contains(
            "MAXIMIZED=",
            comando,
            StringComparison.Ordinal);
        Assert.Contains(
            "ACTIVATED=",
            comando,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsZoomed",
            comando,
            StringComparison.Ordinal);
        Assert.True(
            ValidadorPowerShell.Validar(comando).Permitido);
        Assert.True(
            TraductorRecetasConocidas.ValidarAlineacion(
                plan,
                comando,
                [inventario]).Permitido);
    }
}
