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
            },
            new
            {
                type = "function",

                function = new
                {
                    name = "usar_control_ui",

                    description =
                        "Interactúa con un control de una ventana mediante " +
                        "Windows UI Automation. " +
                        "Acciones permitidas: establecer_valor, invocar, " +
                        "seleccionar, alternar, expandir, contraer y confirmar.",

                    parameters = new
                    {
                        type = "object",

                        properties = new
                        {
                            ventana = new
                            {
                                type = "string",
                                description =
                                    "Nombre de la ventana."
                            },

                            control = new
                            {
                                type = "string",
                                description =
                                    "Nombre o AutomationId del control."
                            },

                            accion = new
                            {
                                type = "string",
                                description =
                                    "Acción: establecer_valor, invocar, " +
                                    "seleccionar, alternar, expandir, contraer o confirmar."
                            },

                            valor = new
                            {
                                type = "string",
                                description =
                                    "Valor que debe establecerse cuando " +
                                    "la acción sea establecer_valor."
                            }
                        },

                        required = new[]
                        {
                            "ventana",
                            "control",
                            "accion"
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

            var mensajes =
                new List<object>();

            mensajes.Add(new
            {
                role = "system",

                content = """
            Eres un agente que controla Windows
            utilizando exclusivamente las herramientas
            seguras disponibles.

            Debes realizar realmente la petición
            del usuario.

            REGLAS:

            - No describas lo que habría que hacer.
              Utiliza las herramientas.

            - Si el usuario pide únicamente abrir
              o iniciar una aplicación, utiliza
              iniciar_aplicacion.

            - Si el usuario pide realizar una acción
              dentro de una aplicación que ya está abierta,
              NO utilices iniciar_aplicacion innecesariamente.

            - Si conoces exactamente el nombre del control
              que debes utilizar, puedes usar directamente
              usar_control_ui.

            - Si necesitas saber qué controles existen
              dentro de una ventana, utiliza primero
              inspeccionar_ventana.

            - Después de recibir el resultado de una
              herramienta, decide si necesitas realizar
              otro paso para completar la petición.

            - Cuando la petición esté completamente
              realizada, no utilices más herramientas.

            - Nunca inventes acciones adicionales.

            - No puedes manipular archivos.

            - No puedes usar PowerShell, CMD ni scripts.

            - No puedes modificar el registro.

            - No puedes ejecutar rutas arbitrarias.

            - Solo puedes utilizar las capacidades
              explícitamente disponibles.
            """
            });

            mensajes.Add(new
            {
                role = "user",

                content = $"""
            PETICIÓN:

            {instruccion}

            VENTANAS VISIBLES:

            {estadoWindows}
            """
            });

            const int maximoPasos = 6;

            for (int paso = 0;
                 paso < maximoPasos;
                 paso++)
            {
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

                mensajes.Add(
                    mensaje.Clone());

                if (!mensaje.TryGetProperty(
                        "tool_calls",
                        out var toolCalls)
                    ||
                    toolCalls.GetArrayLength() == 0)
                {
                    string respuestaFinal = "";

                    if (mensaje.TryGetProperty(
                            "content",
                            out var contentJson))
                    {
                        respuestaFinal =
                            contentJson.GetString() ?? "";
                    }

                    Console.WriteLine();
                    Console.WriteLine(
                        "PETICIÓN COMPLETADA:");

                    Console.WriteLine(
                        respuestaFinal);

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

                    string resultadoCompleto =
                        resultado;

                    // Si acabamos de interactuar con una ventana,
                    // volvemos a observar su estado actualizado.
                    if (nombre == "usar_control_ui"
                        &&
                        argumentos.TryGetProperty(
                            "ventana",
                            out var ventanaJson))
                    {
                        string nombreVentana =
                            ventanaJson.GetString() ?? "";

                        await Task.Delay(500);

                        string controlesActualizados =
                            ObservadorUIWindows
                                .ObtenerControles(
                                    nombreVentana);

                        resultadoCompleto +=
                            Environment.NewLine +
                            Environment.NewLine +
                            "ESTADO ACTUALIZADO DE LA INTERFAZ:" +
                            Environment.NewLine +
                            controlesActualizados;
                    }

                    mensajes.Add(new
                    {
                        role = "tool",

                        tool_name = nombre,

                        content = resultadoCompleto
                    });

                    await Task.Delay(300);
                }
            }
            Console.WriteLine();
            Console.WriteLine(
                "El agente alcanzó el límite de pasos.");
        }
    }
}