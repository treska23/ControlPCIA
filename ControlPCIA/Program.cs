using System.Net.Http.Json;
using System.Text.Json;
using System.Diagnostics;
using ControlPCIA;

var cliente = new HttpClient();

Console.Write("Escribe una orden para el PC: ");
string orden = Console.ReadLine() ?? "";

var ventanas = ObservadorWindows.ObtenerVentanasAbiertas();

string estadoWindows = string.Join(
    "\n",
    ventanas.Select(ventana => "- " + ventana)
);

string prompt =
    "Actúa como intérprete de órdenes para controlar un PC con Windows.\n\n" +
    "Devuelve únicamente JSON válido.\n" +
    "No añadas explicaciones.\n\n" +
    "Acciones permitidas:\n" +
    "- abrir_aplicacion\n" +
    "- cerrar_aplicacion\n" +
    "- establecer_volumen\n\n" +
    "Orden del usuario:\n" +
    orden;

var peticion = new
{
    model = "qwen3:8b",

    messages = new[]
    {
        new
        {
            role = "user",
            content =
                "Este es el estado actual de las ventanas visibles de Windows:\n" +
                estadoWindows +
                "\n\nPetición del usuario:\n" +
                orden
        }
    },

    tools = new object[]
    {
        new
        {
            type = "function",
            function = new
            {
                name = "controlar_windows",
                description =
                    "Controla el entorno interactivo permitido de Windows y las aplicaciones abiertas " +
                    "según la intención del usuario.",

                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        instruccion = new
                        {
                            type = "string",
                            description =
                                "La intención completa del usuario expresada como una instrucción de control."
                        }
                    },
                    required = new[] { "instruccion" }
                }
            }
        }
    },

    stream = false,
    think = false
};

var respuesta = await cliente.PostAsJsonAsync(
    "http://localhost:11434/api/chat",
    peticion
);

var contenido = await respuesta.Content.ReadAsStringAsync();

using var json = JsonDocument.Parse(contenido);

var mensaje = json.RootElement.GetProperty("message");

if (mensaje.TryGetProperty("tool_calls", out var toolCalls))
{
    foreach (var toolCall in toolCalls.EnumerateArray())
    {
        var funcion = toolCall.GetProperty("function");

        string nombreFuncion =
            funcion.GetProperty("name").GetString() ?? "";

        var argumentos =
            funcion.GetProperty("arguments");

        Console.WriteLine("Herramienta elegida por la IA: " + nombreFuncion);
        Console.WriteLine("Argumentos: " + argumentos);

        await ControlWindows.EjecutarHerramienta(nombreFuncion, argumentos);
    }
}
else
{
    Console.WriteLine("La IA no ha solicitado ninguna herramienta.");
}

Console.ReadLine();