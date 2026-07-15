using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http;

namespace ControlPCIA
{
    internal static class ControlWindows
    {
        private static readonly HttpClient Cliente =
            new();

        private static readonly object[]
            Herramientas =
        {
            new
            {
                type = "function",

                function = new
                {
                    name =
                        "iniciar_aplicacion",

                    description =
                        "Inicia una aplicación instalada " +
                        "en Windows por su nombre. " +
                        "No acepta rutas de archivos ni comandos.",

                    parameters = new
                    {
                        type = "object",

                        properties = new
                        {
                            nombre = new
                            {
                                type = "string",

                                description =
                                    "Nombre común de la " +
                                    "aplicación que el " +
                                    "usuario quiere iniciar."
                            }
                        },

                        required = new[]
                        {
                            "nombre"
                        }
                    }
                }
            },
            new
            {
                type = "function",

                function = new
                {
                    name = "inspeccionar_ventana",

                    description =
                        "Observa los controles accesibles " +
                        "de una ventana abierta mediante " +
                        "Windows UI Automation. " +
                        "No modifica nada.",

                    parameters = new
                    {
                        type = "object",

                        properties = new
                        {
                            nombre = new
                            {
                                type = "string",

                                description =
                                    "Nombre o parte del título " +
                                    "de la ventana que se quiere inspeccionar."
                            }
                        },

                        required = new[]
                        {
                            "nombre"
                        }
                    }
                }
            }
        };

        public static async Task ControlarAsync(
            string instruccion)
        {
            var ventanas =
                ObservadorWindows
                    .ObtenerVentanasAbiertas();

            string estadoWindows =
                string.Join(
                    Environment.NewLine,
                    ventanas.Select(
                        ventana =>
                            "- " + ventana));

            var mensajes = new object[]
            {
                new
                {
                    role = "system",

                    content = """
                        Eres un agente que controla
                        Windows.

                        Debes realizar la petición
                        del usuario utilizando
                        únicamente las herramientas
                        disponibles.

                        REGLAS OBLIGATORIAS:

                        - Si el usuario quiere abrir
                          o iniciar una aplicación,
                          utiliza iniciar_aplicacion.

                        - No describas cómo hacerlo:
                          ejecuta la herramienta.

                        - Nunca inventes acciones
                          adicionales.

                        - No puedes acceder,
                          modificar, borrar, mover
                          ni crear archivos.

                        - No puedes ejecutar
                          PowerShell, CMD ni scripts.

                        - No puedes modificar
                          el registro de Windows.

                        - No puedes ejecutar rutas
                          proporcionadas por el modelo.

                        - Solo puedes utilizar
                          las capacidades que el
                          programa expone explícitamente.
                        """
                },

                new
                {
                    role = "user",

                    content = $"""
                        PETICIÓN:

                        {instruccion}

                        VENTANAS VISIBLES:

                        {estadoWindows}
                        """
                }
            };

            var peticion = new
            {
                model = "qwen3:8b",

                messages = mensajes,

                tools = Herramientas,

                stream = false,

                think = false
            };

            var respuesta =
                await Cliente.PostAsJsonAsync(
                    "http://localhost:11434/api/chat",
                    peticion);

            respuesta.EnsureSuccessStatusCode();

            string contenido =
                await respuesta.Content
                    .ReadAsStringAsync();

            using var json =
                JsonDocument.Parse(contenido);

            var mensaje =
                json.RootElement
                    .GetProperty("message");

            if (!mensaje.TryGetProperty(
                    "tool_calls",
                    out var toolCalls))
            {
                Console.WriteLine(
                    "La IA no ha solicitado " +
                    "ninguna acción.");

                return;
            }

            foreach (var toolCall
                     in toolCalls.EnumerateArray())
            {
                var funcion =
                    toolCall.GetProperty(
                        "function");

                string nombre =
                    funcion
                        .GetProperty("name")
                        .GetString() ?? "";

                var argumentos =
                    funcion.GetProperty(
                        "arguments");

                Console.WriteLine();
                Console.WriteLine(
                    "CAPACIDAD ELEGIDA:");

                Console.WriteLine(nombre);

                string resultado =
                    await EjecutorWindows
                        .EjecutarHerramientaAsync(
                            nombre,
                            argumentos);

                Console.WriteLine(resultado);

                // Terminamos aquí.
                // No dejamos que la IA improvise
                // más acciones después.
                return;
            }
        }
    }
}