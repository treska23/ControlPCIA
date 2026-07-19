using Xunit;

namespace ControlPCIA.Tests;

public sealed class ControlWindowsTests
{
    [Fact]
    public void Instruccion_del_sistema_es_general_y_sin_catalogos()
    {
        string instruccion = ControlWindows.InstruccionSistema;

        Assert.Contains(
            "Traduce lo que diga el usuario",
            instruccion,
            StringComparison.Ordinal);
        Assert.Contains(
            "comandos aprendidos",
            instruccion,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "PowerShell",
            instruccion,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Cubase",
            instruccion,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Get-StartApps",
            instruccion,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "catálogo",
            instruccion,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "ventanas.estado",
            instruccion,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Modo_seguro_traduce_sin_invocar_el_ejecutor()
    {
        int ejecuciones = 0;
        IReadOnlyList<MensajeOllama>? mensajesRecibidos = null;
        DependenciasControlWindows dependencias = CrearDependencias(
            respuestas: ["Get-Process"],
            ejecutarAsync: (_, _) =>
            {
                ejecuciones++;
                throw new InvalidOperationException(
                    "No debe ejecutarse.");
            },
            recetas:
            [
                new RecetaReferencia(
                    "programas abiertos",
                    ["Get-Process | Where-Object MainWindowTitle"],
                    2,
                    0.9)
            ],
            observarMensajes: mensajes =>
                mensajesRecibidos = mensajes);

        ResultadoControl resultado =
            await ControlWindows.ControlarConDependenciasAsync(
                "qué programas tengo abiertos",
                informar: null,
                contextoConversacion: null,
                TestContext.Current.CancellationToken,
                soloTraducir: true,
                dependencias);

        Assert.Equal(0, ejecuciones);
        Assert.False(resultado.Completado);
        Assert.Equal("prueba_sin_ejecucion", resultado.Estado);
        ResultadoPasoControl paso = Assert.Single(resultado.Pasos);
        Assert.False(paso.Ejecutado);
        Assert.Equal("Get-Process", paso.Comando);
        Assert.NotNull(mensajesRecibidos);
        Assert.Equal(
            ControlWindows.InstruccionSistema,
            mensajesRecibidos![0].Contenido);
        Assert.Contains(
            "Get-Process | Where-Object MainWindowTitle",
            mensajesRecibidos[^1].Contenido,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TODAS LAS TAREAS",
            mensajesRecibidos[^1].Contenido,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Una_consulta_informativa_devuelve_stdout_en_un_paso()
    {
        DependenciasControlWindows dependencias = CrearDependencias(
            ["Get-Process | Select-Object ProcessName"],
            (_, _) => Task.FromResult(
                new ResultadoEjecucionPowerShell(
                    true,
                    0,
                    "ProcessName=msedge",
                    string.Empty)));

        ResultadoControl resultado =
            await ControlWindows.ControlarConDependenciasAsync(
                "qué programas tengo abiertos",
                null,
                null,
                TestContext.Current.CancellationToken,
                false,
                dependencias);

        Assert.True(resultado.Completado);
        Assert.Equal("respuesta", resultado.Estado);
        Assert.Equal("ProcessName=msedge", resultado.Mensaje);
        Assert.Single(resultado.Pasos);
    }

    [Fact]
    public async Task Una_accion_puede_investigar_y_usar_el_resultado()
    {
        var ejecutados = new List<string>();
        IReadOnlyList<string>? aprendidos = null;
        DependenciasControlWindows dependencias = CrearDependencias(
            [
                "Get-Command -Name 'cubase*' -ErrorAction SilentlyContinue",
                "Start-Process -FilePath 'C:\\Program Files\\Steinberg\\Cubase.exe' -PassThru | Select-Object Id,ProcessName"
            ],
            (comando, _) =>
            {
                ejecutados.Add(comando);

                return Task.FromResult(
                    comando.StartsWith(
                        "Get-Command",
                        StringComparison.Ordinal)
                        ? new ResultadoEjecucionPowerShell(
                            true,
                            0,
                            "C:\\Program Files\\Steinberg\\Cubase.exe",
                            string.Empty)
                        : new ResultadoEjecucionPowerShell(
                            true,
                            0,
                            "Id=42 ProcessName=Cubase",
                            string.Empty));
            },
            aprenderAsync: (_, comandos, _) =>
            {
                aprendidos = comandos.ToArray();
                return Task.FromResult(true);
            });

        ResultadoControl resultado =
            await ControlWindows.ControlarConDependenciasAsync(
                "abre Cubase",
                null,
                null,
                TestContext.Current.CancellationToken,
                false,
                dependencias);

        Assert.True(resultado.Completado);
        Assert.Equal(2, ejecutados.Count);
        Assert.Equal(2, resultado.Pasos.Count);
        Assert.True(resultado.Aprendido);
        Assert.Equal(ejecutados, aprendidos);
    }

    [Fact]
    public async Task Una_accion_sin_salida_se_verifica_antes_de_dar_exito()
    {
        DependenciasControlWindows dependencias = CrearDependencias(
            [
                "Start-Process calc.exe",
                "Get-Process -Name CalculatorApp | Select-Object Id,ProcessName"
            ],
            (comando, _) => Task.FromResult(
                comando.StartsWith(
                    "Start-Process",
                    StringComparison.Ordinal)
                    ? new ResultadoEjecucionPowerShell(
                        true,
                        0,
                        string.Empty,
                        string.Empty)
                    : new ResultadoEjecucionPowerShell(
                        true,
                        0,
                        "Id=7 ProcessName=CalculatorApp",
                        string.Empty)));

        ResultadoControl resultado =
            await ControlWindows.ControlarConDependenciasAsync(
                "abre la calculadora",
                null,
                null,
                TestContext.Current.CancellationToken,
                false,
                dependencias);

        Assert.True(resultado.Completado);
        Assert.Equal("respuesta", resultado.Estado);
        Assert.Equal(2, resultado.Pasos.Count);
        Assert.Contains(
            "CalculatorApp",
            resultado.Mensaje,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Abrir_aplicacion_no_da_exito_si_la_primera_verificacion_esta_vacia()
    {
        int ejecuciones = 0;
        DependenciasControlWindows dependencias = CrearDependencias(
            [
                "Start-Process calc.exe",
                "Get-Process -Name CalculatorApp -ErrorAction SilentlyContinue",
                "Get-Process -Name CalculatorApp -ErrorAction SilentlyContinue | Select-Object Id,ProcessName"
            ],
            (comando, _) =>
            {
                ejecuciones++;

                return Task.FromResult(
                    ejecuciones switch
                    {
                        1 => new ResultadoEjecucionPowerShell(
                            true,
                            0,
                            string.Empty,
                            string.Empty),
                        2 => new ResultadoEjecucionPowerShell(
                            true,
                            0,
                            string.Empty,
                            string.Empty),
                        _ => new ResultadoEjecucionPowerShell(
                            true,
                            0,
                            "Id=7 ProcessName=CalculatorApp",
                            string.Empty)
                    });
            });

        ResultadoControl resultado =
            await ControlWindows.ControlarConDependenciasAsync(
                "abre la calculadora",
                null,
                null,
                TestContext.Current.CancellationToken,
                false,
                dependencias);

        Assert.True(resultado.Completado);
        Assert.Equal(3, ejecuciones);
        Assert.Equal(3, resultado.Pasos.Count);
        Assert.Contains(
            "CalculatorApp",
            resultado.Mensaje,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_error_real_vuelve_a_Llama_y_admite_otra_estrategia()
    {
        int ejecuciones = 0;
        IReadOnlyList<MensajeOllama>? segundaPeticion = null;
        DependenciasControlWindows dependencias = CrearDependencias(
            [
                "Start-Process AplicacionInventada.exe",
                "Start-Process calc.exe -PassThru | Select-Object Id,ProcessName"
            ],
            (comando, _) =>
            {
                ejecuciones++;

                return Task.FromResult(
                    ejecuciones == 1
                        ? new ResultadoEjecucionPowerShell(
                            true,
                            1,
                            string.Empty,
                            "No se encuentra el archivo.")
                        : new ResultadoEjecucionPowerShell(
                            true,
                            0,
                            "Id=9 ProcessName=CalculatorApp",
                            string.Empty));
            },
            observarMensajes: mensajes =>
            {
                if (mensajes.Count > 2)
                {
                    segundaPeticion = mensajes;
                }
            });

        ResultadoControl resultado =
            await ControlWindows.ControlarConDependenciasAsync(
                "abre la calculadora",
                null,
                null,
                TestContext.Current.CancellationToken,
                false,
                dependencias);

        Assert.True(resultado.Completado);
        Assert.Equal(2, ejecuciones);
        Assert.NotNull(segundaPeticion);
        Assert.Contains(
            "No se encuentra el archivo",
            segundaPeticion![^1].Contenido,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Las_tres_prohibiciones_se_bloquean_antes_de_ejecutar()
    {
        int ejecuciones = 0;
        DependenciasControlWindows dependencias = CrearDependencias(
            ["Remove-Item -LiteralPath 'C:\\Datos\\nota.txt'"],
            (_, _) =>
            {
                ejecuciones++;
                return Task.FromResult(
                    new ResultadoEjecucionPowerShell(
                        true,
                        0,
                        string.Empty,
                        string.Empty));
            });

        ResultadoControl resultado =
            await ControlWindows.ControlarConDependenciasAsync(
                "elimina la nota",
                null,
                null,
                TestContext.Current.CancellationToken,
                false,
                dependencias);

        Assert.False(resultado.Completado);
        Assert.Equal("comando_rechazado", resultado.Estado);
        Assert.Equal(0, ejecuciones);
        Assert.False(Assert.Single(resultado.Pasos).Ejecutado);
    }

    [Fact]
    public async Task Una_pregunta_de_Llama_vuelve_al_movil_sin_ejecutarse()
    {
        int ejecuciones = 0;
        DependenciasControlWindows dependencias = CrearDependencias(
            ["¿Qué nombre quieres ponerle al proyecto?"],
            (_, _) =>
            {
                ejecuciones++;
                throw new InvalidOperationException();
            });

        ResultadoControl resultado =
            await ControlWindows.ControlarConDependenciasAsync(
                "crea un proyecto nuevo",
                null,
                null,
                TestContext.Current.CancellationToken,
                false,
                dependencias);

        Assert.False(resultado.Completado);
        Assert.Equal("requiere_aclaracion", resultado.Estado);
        Assert.Equal(0, ejecuciones);
        Assert.Contains(
            "nombre",
            resultado.Mensaje,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Una_pregunta_emitida_por_PowerShell_abre_conversacion()
    {
        DependenciasControlWindows dependencias = CrearDependencias(
            ["Write-Output '¿Qué nombre quieres ponerle al proyecto?'"],
            (_, _) => Task.FromResult(
                new ResultadoEjecucionPowerShell(
                    true,
                    0,
                    "¿Qué nombre quieres ponerle al proyecto?",
                    string.Empty)));

        ResultadoControl resultado =
            await ControlWindows.ControlarConDependenciasAsync(
                "crea un proyecto nuevo",
                null,
                null,
                TestContext.Current.CancellationToken,
                false,
                dependencias);

        Assert.False(resultado.Completado);
        Assert.Equal("requiere_aclaracion", resultado.Estado);
        Assert.Single(resultado.Pasos);
    }

    [Fact]
    public async Task Una_orden_multitarea_se_ejecuta_como_un_solo_script()
    {
        const string script =
            "Start-Process notepad.exe; Start-Process calc.exe; Get-Process notepad,CalculatorApp | Select-Object ProcessName";
        string? ejecutado = null;
        DependenciasControlWindows dependencias = CrearDependencias(
            [script],
            (comando, _) =>
            {
                ejecutado = comando;
                return Task.FromResult(
                    new ResultadoEjecucionPowerShell(
                        true,
                        0,
                        "notepad\nCalculatorApp",
                        string.Empty));
            });

        ResultadoControl resultado =
            await ControlWindows.ControlarConDependenciasAsync(
                "abre el bloc de notas y la calculadora",
                null,
                null,
                TestContext.Current.CancellationToken,
                false,
                dependencias);

        Assert.True(resultado.Completado);
        Assert.Equal(script, ejecutado);
        Assert.Single(resultado.Pasos);
    }

    [Theory]
    [InlineData("Get-Process | Select-Object ProcessName")]
    [InlineData("Get-Command cubase")]
    [InlineData("Invoke-WebRequest 'https://learn.microsoft.com/' | Select-Object StatusCode")]
    [InlineData("winget search Cubase")]
    [InlineData("2 + 5")]
    public void Reconoce_consultas_generales(
        string comando)
    {
        Assert.True(ControlWindows.EsComandoDeConsulta(comando));
    }

    [Theory]
    [InlineData("Start-Process calc.exe")]
    [InlineData("Set-ItemProperty HKCU:\\Software\\X -Name Y -Value 1")]
    [InlineData("winget install Ejemplo")]
    [InlineData("Get-Process | ForEach-Object { $_.CloseMainWindow() }")]
    public void No_confunde_acciones_con_consultas(
        string comando)
    {
        Assert.False(ControlWindows.EsComandoDeConsulta(comando));
    }

    [Theory]
    [InlineData("abre Cubase")]
    [InlineData("pon un vídeo")]
    [InlineData("maximiza Edge")]
    [InlineData("crea un proyecto nuevo")]
    public void Detecta_peticiones_de_accion(
        string peticion)
    {
        Assert.True(ControlWindows.EsPeticionDeAccion(peticion));
    }

    [Theory]
    [InlineData("qué programas tengo abiertos")]
    [InlineData("dónde está el archivo informe")]
    [InlineData("cuánto es dos más cinco")]
    public void No_marca_consultas_como_acciones(
        string peticion)
    {
        Assert.False(ControlWindows.EsPeticionDeAccion(peticion));
    }

    [Fact]
    public async Task Conserva_el_contexto_de_conversacion()
    {
        IReadOnlyList<MensajeOllama>? mensajesRecibidos = null;
        DependenciasControlWindows dependencias = CrearDependencias(
            ["Write-Output 'Proyecto Demo'"],
            (_, _) => Task.FromResult(
                new ResultadoEjecucionPowerShell(
                    true,
                    0,
                    "Proyecto Demo",
                    string.Empty)),
            observarMensajes: mensajes =>
                mensajesRecibidos = mensajes);

        await ControlWindows.ControlarConDependenciasAsync(
            "Demo",
            null,
            [
                new MensajeConversacionControl(
                    "assistant",
                    "¿Qué nombre quieres ponerle al proyecto?")
            ],
            TestContext.Current.CancellationToken,
            false,
            dependencias);

        Assert.NotNull(mensajesRecibidos);
        Assert.Contains(
            mensajesRecibidos!,
            mensaje =>
                mensaje.Rol == "assistant"
                && mensaje.Contenido.Contains(
                    "¿Qué nombre",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_repite_un_comando_que_no_aporta_evidencia()
    {
        int ejecuciones = 0;
        DependenciasControlWindows dependencias = CrearDependencias(
            [
                "Start-Process calc.exe",
                "Start-Process calc.exe"
            ],
            (_, _) =>
            {
                ejecuciones++;
                return Task.FromResult(
                    new ResultadoEjecucionPowerShell(
                        true,
                        0,
                        string.Empty,
                        string.Empty));
            });

        ResultadoControl resultado =
            await ControlWindows.ControlarConDependenciasAsync(
                "abre la calculadora",
                null,
                null,
                TestContext.Current.CancellationToken,
                false,
                dependencias);

        Assert.False(resultado.Completado);
        Assert.Equal("sin_progreso", resultado.Estado);
        Assert.Equal(1, ejecuciones);
    }

    private static DependenciasControlWindows CrearDependencias(
        IEnumerable<string> respuestas,
        Func<
            string,
            CancellationToken,
            Task<ResultadoEjecucionPowerShell>> ejecutarAsync,
        IReadOnlyList<RecetaReferencia>? recetas = null,
        Action<IReadOnlyList<MensajeOllama>>? observarMensajes = null,
        Func<
            string,
            IReadOnlyList<string>,
            CancellationToken,
            Task<bool>>? aprenderAsync = null)
    {
        var cola = new Queue<string>(respuestas);

        return new DependenciasControlWindows(
            (mensajes, _) =>
            {
                observarMensajes?.Invoke(mensajes);

                if (cola.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No quedan respuestas simuladas.");
                }

                return Task.FromResult(cola.Dequeue());
            },
            ejecutarAsync,
            (_, _) => Task.FromResult(
                recetas
                ?? (IReadOnlyList<RecetaReferencia>)[]),
            aprenderAsync
            ?? ((_, _, _) => Task.FromResult(false)));
    }
}
