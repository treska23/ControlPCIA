using System.Text;
using ControlPCIA;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(
    encoderShouldEmitUTF8Identifier: false);

if (args.Length == 0
    ||
    args[0].Equals("--servidor", StringComparison.OrdinalIgnoreCase))
{
    await ServidorMovil.IniciarAsync();
    return;
}

if (args[0].Equals("--diagnostico", StringComparison.OrdinalIgnoreCase))
{
    EstadoOllama diagnostico =
        await ClienteOllama.DiagnosticarAsync();

    Console.WriteLine(diagnostico.Mensaje);
    Environment.ExitCode = diagnostico.Disponible ? 0 : 1;
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
    await ControlWindows.ControlarAsync(
        orden,
        evento =>
        {
            Console.WriteLine();

            if (evento.Tipo == "comando"
                &&
                evento.Comando is not null)
            {
                Console.WriteLine("COMANDO PROPUESTO:");
                Console.WriteLine(evento.Comando);
                return;
            }

            Console.WriteLine(evento.Mensaje);
        });

Environment.ExitCode = resultadoControl.Completado ? 0 : 1;
