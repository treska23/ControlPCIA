using System.Text;
using System.Text.Json;
using ControlPCIA;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(
    encoderShouldEmitUTF8Identifier: false);

if (args.Length > 0
    && args[0].Equals("ui", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine(
        "La automatización de interfaz, ratón o teclado está deshabilitada. ControlPCIA sólo ejecuta comandos, API o protocolos invocables íntegramente desde consola.");
    Environment.ExitCode = 2;
    return;
}

if (args.Length > 0
    && args[0].Equals(
        "display",
        StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode =
        await ComandoPantallas.EjecutarAsync(
            args.Skip(1).ToArray(),
            Console.Out,
            Console.Error);
    return;
}

if (args.Length > 0
    && args[0].Equals(
        "media",
        StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode =
        await ComandoMultimedia.EjecutarAsync(
            args.Skip(1).ToArray(),
            Console.Out,
            Console.Error);
    return;
}

if (args.Length > 0
    && args[0].Equals("--activar-inicio", StringComparison.OrdinalIgnoreCase))
{
    GestorInicioWindows.Activar();
    Console.WriteLine("ControlPCIA se iniciará automáticamente con Windows.");
    return;
}

if (args.Length > 0
    && args[0].Equals("--desactivar-inicio", StringComparison.OrdinalIgnoreCase))
{
    GestorInicioWindows.Desactivar();
    Console.WriteLine("Inicio automático de ControlPCIA desactivado.");
    return;
}

if (args.Length == 0
    ||
    args[0].Equals("--servidor", StringComparison.OrdinalIgnoreCase))
{
    using var instancia = new Mutex(
        initiallyOwned: true,
        name: @"Local\ControlPCIA.Servidor",
        createdNew: out bool primeraInstancia);

    if (!primeraInstancia)
    {
        Console.WriteLine("ControlPCIA ya está activo en esta sesión.");
        return;
    }

    bool configurarInicio = !args.Any(argumento =>
        argumento.Equals(
            "--sin-inicio",
            StringComparison.OrdinalIgnoreCase));

    try
    {
        if (configurarInicio)
        {
            GestorInicioWindows.AsegurarConfiguracionInicial();
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            "No se pudo configurar el inicio automático: " + ex.Message);
    }

    using var cancelacion = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelar = (_, evento) =>
    {
        evento.Cancel = true;
        cancelacion.Cancel();
    };
    Console.CancelKeyPress += cancelar;
    bool oculto = args.Any(argumento =>
        argumento.Equals("--oculto", StringComparison.OrdinalIgnoreCase));
    bool soloTraducir = args.Any(argumento =>
        argumento.Equals(
            "--solo-traducir",
            StringComparison.OrdinalIgnoreCase));
    using var bandeja = new AgenteBandeja(cancelacion, oculto);

    try
    {
        await ServidorMovil.IniciarAsync(
            cancelacion.Token,
            bandeja.ActualizarEstado,
            soloTraducir);
    }
    catch (OperationCanceledException)
        when (cancelacion.IsCancellationRequested)
    {
    }
    finally
    {
        Console.CancelKeyPress -= cancelar;
    }

    return;
}

if (args[0].Equals("--diagnostico", StringComparison.OrdinalIgnoreCase))
{
    EstadoControlBasico diagnostico =
        ControlBasico.Estado;

    Console.WriteLine(diagnostico.Mensaje);
    Environment.ExitCode = diagnostico.Disponible ? 0 : 1;
    return;
}

if (args[0].Equals(
        "--traducir-sin-ejecutar",
        StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine(
            "Uso: ControlPCIA --traducir-sin-ejecutar <petición>.");
        Environment.ExitCode = 2;
        return;
    }

    string peticion = string.Join(' ', args.Skip(1));
    long inicio = Environment.TickCount64;
    ResultadoControl traduccion =
        await ControlBasico.ControlarAsync(
            peticion,
            soloTraducir: true);
    long duracion = Environment.TickCount64 - inicio;
    ResultadoPasoControl? propuesta =
        traduccion.Pasos.FirstOrDefault(paso =>
            !paso.Ejecutado
            && string.IsNullOrWhiteSpace(paso.Error));

    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                estado = traduccion.Estado,
                comando = propuesta?.Comando,
                permitido =
                    propuesta is not null
                    && !propuesta.Ejecutado
                    && string.IsNullOrWhiteSpace(propuesta.Error),
                motivo = traduccion.Mensaje,
                ejecutado = false,
                duracionMs = duracion,
                pasos = traduccion.Pasos
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            }));
    Environment.ExitCode =
        propuesta is not null
        && !propuesta.Ejecutado
        && string.IsNullOrWhiteSpace(propuesta.Error)
            ? 0
            : 1;
    return;
}

if (args.Length > 0
    && args[0].Equals("--comando-powershell", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine(
            "Uso: ControlPCIA --comando-powershell <comando>.");
        Environment.ExitCode = 2;
        return;
    }

    string comando = string.Join(' ', args.Skip(1));
    ResultadoEjecucionPowerShell resultado =
        await EjecutorPowerShell.EjecutarAsync(comando);

    if (!string.IsNullOrWhiteSpace(resultado.Salida))
    {
        Console.WriteLine(resultado.Salida);
    }

    if (!string.IsNullOrWhiteSpace(resultado.Error))
    {
        Console.Error.WriteLine(resultado.Error);
    }

    Environment.ExitCode = resultado.Ejecutado
        ? resultado.CodigoSalida
        : 3;
    return;
}

string orden;

if (args[0].Equals("--consola", StringComparison.OrdinalIgnoreCase))
{
    Console.Write("Escribe una orden para el PC: ");
    orden = Console.ReadLine() ?? "";
}
else
{
    orden = string.Join(' ', args);
}

if (string.IsNullOrWhiteSpace(orden))
{
    Console.Error.WriteLine("No se ha introducido ninguna orden.");
    Environment.ExitCode = 2;
    return;
}

ResultadoControl resultadoControl =
    await ControlBasico.ControlarAsync(
        orden);

Console.WriteLine();
Console.WriteLine(resultadoControl.Mensaje);

foreach (ResultadoPasoControl paso in resultadoControl.Pasos)
{
    Console.WriteLine();
    Console.WriteLine(
        paso.Ejecutado
            ? "COMANDO EJECUTADO:"
            : "COMANDO PREPARADO:");
    Console.WriteLine(paso.Comando);
}

Environment.ExitCode = resultadoControl.Completado ? 0 : 1;
