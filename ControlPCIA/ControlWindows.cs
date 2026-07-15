using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace ControlPCIA
{
    internal static class ControlWindows
    {
        private static readonly HttpClient Cliente =
            new();

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
                    Eres un agente que controla un PC con Windows
                    mediante PowerShell.

                    Debes realizar la petición real del usuario.

                    Puedes proponer el comando PowerShell que
                    consideres más adecuado para realizar la tarea.

                    NO estás limitado a una lista de comandos
                    indicada en este prompt.

                    El programa dispone de una capa de seguridad
                    independiente que validará cada comando antes
                    de ejecutarlo.

                    FUNCIONAMIENTO:

                    - Genera UN comando PowerShell por paso.
                    - Devuelve únicamente el comando.
                    - No uses Markdown.
                    - No expliques el comando.
                    - Después recibirás el resultado real de su
                      ejecución y podrás decidir el siguiente paso.
                    - Si un comando es bloqueado, utiliza la
                      información recibida para buscar otra forma
                      segura de realizar la petición.
                    - No intentes eludir deliberadamente un bloqueo
                      de seguridad.
                    - Cuando la petición esté completamente
                      realizada, responde exactamente:

                      FIN

                    - Si no existe ninguna forma de realizarla,
                      responde exactamente:

                      SIN_COMANDO

                    El sistema no permite manipular archivos,
                    modificar el registro ni realizar operaciones
                    administrativas sensibles.

                    - Nunca inventes ni adivines rutas de instalación.

                    - Si una aplicación no puede iniciarse directamente
                      mediante Start-Process, no pruebes distintas carpetas
                      Program Files al azar.

                    - Si conoces un protocolo URI registrado para la aplicación,
                      puedes utilizarlo con Start-Process.

                    - Si no conoces cómo iniciar una aplicación instalada,
                      consulta primero Windows mediante Get-StartApps para
                      obtener su nombre y AppID reales.

                    - Cuando un comando falle, analiza el error y cambia de
                      estrategia. No repitas el mismo comando cambiando rutas
                      inventadas.

                    - Un comando con código de salida distinto de 0 NO significa
                      que la petición se haya completado.
                    """
            });

            mensajes.Add(new
            {
                role = "user",

                content = $"""
                    PETICIÓN DEL USUARIO:

                    {instruccion}

                    VENTANAS VISIBLES:

                    {estadoWindows}

                    Decide el primer paso necesario.
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

                    stream = false,

                    think = false
                };

                try
                {
                    var respuesta =
                        await Cliente.PostAsJsonAsync(
                            "http://localhost:11434/api/chat",
                            peticion);

                    respuesta.EnsureSuccessStatusCode();

                    string contenido =
                        await respuesta.Content
                            .ReadAsStringAsync();

                    using var json =
                        JsonDocument.Parse(
                            contenido);

                    string comando =
                        json.RootElement
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString() ?? "";

                    comando =
                        LimpiarComando(
                            comando);

                    if (comando.Equals(
                            "FIN",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine();
                        Console.WriteLine(
                            "PETICIÓN COMPLETADA.");

                        return;
                    }

                    if (comando.Equals(
                            "SIN_COMANDO",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine();
                        Console.WriteLine(
                            "No se encontró una forma permitida " +
                            "de realizar la petición.");

                        return;
                    }

                    Console.WriteLine();
                    Console.WriteLine(
                        "COMANDO PROPUESTO:");

                    Console.WriteLine(
                        comando);

                    ResultadoEjecucionPowerShell resultado =
                        await EjecutorPowerShell
                            .EjecutarAsync(
                                comando);

                    string informacionResultado;

                    if (!resultado.Ejecutado)
                    {
                        Console.WriteLine();
                        Console.WriteLine(
                            resultado.Error);

                        informacionResultado =
                        $"""
                        EL COMANDO SE EJECUTÓ PERO FALLÓ.

                        Código de salida:
                        {resultado.CodigoSalida}

                        Error:
                        {LimitarTexto(resultado.Error)}

                        La petición original NO está completada.

                        No inventes rutas de instalación.
                        No repitas la misma estrategia cambiando carpetas al azar.

                        Consulta el sistema si necesitas descubrir
                        cómo está registrada la aplicación y después
                        intenta otra estrategia.
                        """;
                    }
                    else
                    {
                        Console.WriteLine();
                        Console.WriteLine(
                            "COMANDO EJECUTADO");

                        Console.WriteLine(
                            $"Código de salida: " +
                            $"{resultado.CodigoSalida}");

                        if (!string.IsNullOrWhiteSpace(
                                resultado.Salida))
                        {
                            Console.WriteLine();
                            Console.WriteLine(
                                "RESULTADO:");

                            Console.WriteLine(
                                resultado.Salida);
                        }

                        if (!string.IsNullOrWhiteSpace(
                                resultado.Error))
                        {
                            Console.WriteLine();
                            Console.WriteLine(
                                "ERROR DE POWERSHELL:");

                            Console.WriteLine(
                                resultado.Error);
                        }

                        informacionResultado =
                            $"""
                            RESULTADO DEL COMANDO:

                            Código de salida:
                            {resultado.CodigoSalida}

                            Salida:
                            {LimitarTexto(resultado.Salida)}

                            Error:
                            {LimitarTexto(resultado.Error)}

                            Decide si la petición original ya está
                            completada o si necesitas ejecutar otro
                            comando.
                            """;
                    }

                    mensajes.Add(new
                    {
                        role = "assistant",

                        content = comando
                    });

                    mensajes.Add(new
                    {
                        role = "user",

                        content =
                            informacionResultado
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine(
                        "ERROR:");

                    Console.WriteLine(
                        ex.Message);

                    return;
                }
            }

            Console.WriteLine();
            Console.WriteLine(
                "El agente alcanzó el límite de pasos.");
        }

        private static string LimpiarComando(
            string respuesta)
        {
            string resultado =
                respuesta.Trim();

            if (resultado.StartsWith(
                    "```powershell",
                    StringComparison.OrdinalIgnoreCase))
            {
                resultado =
                    resultado[
                        "```powershell".Length..];
            }
            else if (resultado.StartsWith(
                         "```",
                         StringComparison.OrdinalIgnoreCase))
            {
                resultado =
                    resultado[3..];
            }

            if (resultado.EndsWith(
                    "```",
                    StringComparison.OrdinalIgnoreCase))
            {
                resultado =
                    resultado[..^3];
            }

            return resultado.Trim();
        }

        private static string LimitarTexto(
            string texto)
        {
            const int limite = 6000;

            if (string.IsNullOrEmpty(texto)
                ||
                texto.Length <= limite)
            {
                return texto;
            }

            return texto[..limite] +
                   Environment.NewLine +
                   "[Salida recortada]";
        }
    }
}