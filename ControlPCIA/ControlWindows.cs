using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Net.Http.Json;

namespace ControlPCIA
{
    internal static class ControlWindows
    {
        public static void EjecutarAccion(JsonElement accion)
        {
            string tipo = accion.GetProperty("tipo").GetString() ?? "";

            switch (tipo)
            {
                case "abrir_aplicacion":

                    if (accion.TryGetProperty("aplicacion", out var app))
                    {
                        string nombreApp = app.GetString() ?? "";
                        AbrirAplicacion(nombreApp);
                    }

                    break;

                case "establecer_volumen":

                    // Lo implementaremos después.
                    break;

                case "cerrar_aplicacion":

                    // Lo implementaremos después.
                    break;
            }
        }

        private static void AbrirAplicacion(string nombreApp)
        {
            if (nombreApp.ToLower().Contains("bloc de notas"))
            {
                Process.Start("notepad.exe");
            }
            else if (nombreApp.ToLower().Contains("spotify"))
            {
                Process.Start(new ProcessStartInfo("spotify:")
                {
                    UseShellExecute = true
                });
            }
        }

        private static void EstablecerVolumen(int porcentaje)
        {
            porcentaje = Math.Clamp(porcentaje, 0, 100);

            var enumerador = (IMMDeviceEnumerator)new MMDeviceEnumerator();

            enumerador.GetDefaultAudioEndpoint(
                EDataFlow.eRender,
                ERole.eMultimedia,
                out IMMDevice dispositivo);

            Guid iid = typeof(IAudioEndpointVolume).GUID;

            dispositivo.Activate(
                ref iid,
                23,
                IntPtr.Zero,
                out object objeto);

            var volumen = (IAudioEndpointVolume)objeto;

            volumen.SetMasterVolumeLevelScalar(
                porcentaje / 100f,
                IntPtr.Zero);
        }

        private enum EDataFlow
        {
            eRender,
            eCapture,
            eAll
        }

        private enum ERole
        {
            eConsole,
            eMultimedia,
            eCommunications
        }

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator
        {
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(
                EDataFlow dataFlow,
                int dwStateMask,
                out IntPtr ppDevices);

            int GetDefaultAudioEndpoint(
                EDataFlow dataFlow,
                ERole role,
                out IMMDevice ppEndpoint);

            int GetDevice(
                string pwstrId,
                out IMMDevice ppDevice);

            int RegisterEndpointNotificationCallback(
                IntPtr pClient);

            int UnregisterEndpointNotificationCallback(
                IntPtr pClient);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(
                ref Guid iid,
                int dwClsCtx,
                IntPtr pActivationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [ComImport]
        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out uint pnChannelCount);

            int SetMasterVolumeLevel(
                float fLevelDB,
                IntPtr pguidEventContext);

            int SetMasterVolumeLevelScalar(
                float fLevel,
                IntPtr pguidEventContext);
        }

        private static async Task ControlarWindows(string instruccion)
        {
            var ventanas = ObservadorWindows.ObtenerVentanasAbiertas();

            string estadoWindows = string.Join(
                "\n",
                ventanas.Select(ventana => "- " + ventana)
            );

            var cliente = new HttpClient();

            string prompt =
             "Eres un agente que controla la parte interactiva de Windows.\n\n" +

             "REGLAS OBLIGATORIAS:\n" +
             "1. Haz únicamente las acciones estrictamente necesarias para cumplir la petición del usuario.\n" +
             "2. Nunca cierres, muevas, minimices, modifiques ni interfieras con aplicaciones o ventanas " +
             "que no estén directamente relacionadas con la petición.\n" +
             "3. No realices acciones adicionales para optimizar, organizar, liberar espacio o mejorar la concentración.\n" +
             "4. No inventes objetivos que el usuario no haya pedido.\n" +
             "5. Si la petición es 'abre Spotify', el siguiente paso debe limitarse a abrir Spotify.\n" +
             "6. Solo puedes interactuar con aplicaciones, ventanas, pantallas, controles de interfaz, audio y multimedia.\n" +
             "7. No puedes manipular archivos, usar PowerShell, CMD, modificar el registro ni realizar tareas administrativas.\n\n" +

             "INSTRUCCIÓN DEL USUARIO:\n" +
             instruccion +
             "\n\n" +

             "ESTADO ACTUAL OBSERVADO:\n" +
             estadoWindows +
             "\n\n" +

             "Decide únicamente el siguiente paso mínimo necesario.\n" +
             "Responde solo con JSON válido usando exactamente estas propiedades:\n" +
             "{\n" +
             "  \"completado\": false,\n" +
             "  \"siguiente_paso\": \"Descripción concreta del siguiente paso\"\n" +
             "}";

            var peticion = new
            {
                model = "qwen3:8b",

                messages = new[]
                {
            new
            {
                role = "user",
                content = prompt
            }
        },

                stream = false,
                think = false,
                format = "json"
            };

            var respuesta = await cliente.PostAsJsonAsync(
                "http://localhost:11434/api/chat",
                peticion
            );

            var contenido = await respuesta.Content.ReadAsStringAsync();

            using var json = JsonDocument.Parse(contenido);

            string resultado = json.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            Console.WriteLine();
            Console.WriteLine("DECISIÓN DEL AGENTE:");
            Console.WriteLine(resultado);

            using var decisionJson = JsonDocument.Parse(resultado);

            string siguientePaso = decisionJson.RootElement
                .GetProperty("siguiente_paso")
                .GetString() ?? "";

            Console.WriteLine();
            Console.WriteLine("SIGUIENTE PASO:");
            Console.WriteLine(siguientePaso);

            await EjecutorWindows.EjecutarAsync(siguientePaso);
        }

        public static async Task EjecutarHerramienta(
    string nombreHerramienta,
    JsonElement argumentos)
        {
            if (nombreHerramienta != "controlar_windows")
                return;

            if (argumentos.TryGetProperty("instruccion", out var instruccionJson))
            {
                string instruccion = instruccionJson.GetString() ?? "";

                await ControlarWindows(instruccion);
            }
        }



        private static async Task EjecutarPasoInterfaz(string siguientePaso)
        {
            var cliente = new HttpClient();

            string prompt =
                "Convierte una instrucción de interacción visual con Windows " +
                "en una secuencia mínima de acciones de teclado.\n\n" +

                "Esta función es un método de respaldo. " +
                "No inventes acciones adicionales.\n\n" +

                "Acciones permitidas:\n" +
                "- tecla WINDOWS\n" +
                "- tecla ENTER\n" +
                "- tecla ESCAPE\n" +
                "- tecla TAB\n" +
                "- escribir texto\n\n" +

                "INSTRUCCIÓN:\n" +
                siguientePaso +
                "\n\n" +

                "Responde únicamente con JSON válido con este formato:\n" +
                "{\n" +
                "  \"acciones\": [\n" +
                "    { \"tipo\": \"tecla\", \"valor\": \"WINDOWS\" },\n" +
                "    { \"tipo\": \"texto\", \"valor\": \"Spotify\" },\n" +
                "    { \"tipo\": \"tecla\", \"valor\": \"ENTER\" }\n" +
                "  ]\n" +
                "}";

            var peticion = new
            {
                model = "qwen3:8b",

                messages = new[]
                {
            new
            {
                role = "user",
                content = prompt
            }
        },

                stream = false,
                think = false,
                format = "json"
            };

            var respuesta = await cliente.PostAsJsonAsync(
                "http://localhost:11434/api/chat",
                peticion
            );

            var contenido = await respuesta.Content.ReadAsStringAsync();

            using var jsonRespuesta = JsonDocument.Parse(contenido);

            string resultado = jsonRespuesta.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            using var accionesJson = JsonDocument.Parse(resultado);

            foreach (var accion in accionesJson.RootElement
                         .GetProperty("acciones")
                         .EnumerateArray())
            {
                string tipo = accion
                    .GetProperty("tipo")
                    .GetString() ?? "";

                string valor = accion
                    .GetProperty("valor")
                    .GetString() ?? "";

                if (tipo == "texto")
                {
                    EjecutorInterfazWindows.EscribirTexto(valor);
                }
                else if (tipo == "tecla")
                {
                    switch (valor.ToUpper())
                    {
                        case "WINDOWS":
                            EjecutorInterfazWindows.PulsarWindows();
                            break;

                        case "ENTER":
                            EjecutorInterfazWindows.PulsarEnter();
                            break;
                    }
                }

                await Task.Delay(300);
            }
        }
    }
}