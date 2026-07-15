using System.Text.Json;

namespace ControlPCIA
{
    internal static class EjecutorWindows
    {
        public static async Task<string>
            EjecutarHerramientaAsync(
                string nombreHerramienta,
                JsonElement argumentos)
        {
            switch (nombreHerramienta)
            {
                case "iniciar_aplicacion":
                    {
                        if (!argumentos.TryGetProperty(
                                "nombre",
                                out var nombreJson))
                        {
                            return
                                "No se indicó ninguna aplicación.";
                        }

                        string nombre =
                            nombreJson.GetString() ?? "";

                        bool iniciada =
                            AplicacionesWindows
                                .Iniciar(nombre);

                        await Task.Delay(1500);

                        if (iniciada)
                        {
                            return
                                $"La aplicación '{nombre}' " +
                                "se ha iniciado.";
                        }

                        return
                            $"Windows no ha encontrado " +
                            $"la aplicación '{nombre}'.";
                    }

                    case "inspeccionar_ventana":
                    {
                        if (!argumentos.TryGetProperty(
                                "nombre",
                                out var nombreJson))
                        {
                            return
                                "No se indicó ninguna ventana.";
                        }

                        string nombre =
                            nombreJson.GetString() ?? "";

                        string controles =
                            ObservadorUIWindows
                                .ObtenerControles(nombre);

                        return controles;
                    }

                case "usar_control_ui":
                    {
                        string ventana =
                            argumentos.GetProperty("ventana")
                                .GetString() ?? "";

                        string control =
                            argumentos.GetProperty("control")
                                .GetString() ?? "";

                        string accion =
                            argumentos.GetProperty("accion")
                                .GetString() ?? "";

                        string? valor = null;

                        if (argumentos.TryGetProperty(
                                "valor",
                                out var valorJson))
                        {
                            valor =
                                valorJson.GetString();
                        }

                        return await InteraccionUIWindows.Ejecutar(
                            ventana,
                            control,
                            accion,
                            valor);
                    }

                default:
                    return
                        "La herramienta solicitada " +
                        "no está permitida.";
            }
        }
    }
}