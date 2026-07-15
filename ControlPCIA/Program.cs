using ControlPCIA;

Console.Write("Escribe una orden para el PC: ");

string orden = Console.ReadLine() ?? "";

if (string.IsNullOrWhiteSpace(orden))
{
    Console.WriteLine("No se ha introducido ninguna orden.");
    return;
}

await ControlWindows.ControlarAsync(orden);

Console.ReadLine();