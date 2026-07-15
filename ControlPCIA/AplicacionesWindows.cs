using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;

namespace ControlPCIA
{
    internal static class AplicacionesWindows
    {
        public static bool Iniciar(string nombreSolicitado)
        {
            if (string.IsNullOrWhiteSpace(nombreSolicitado))
                return false;

            if (ActivarSiEstaAbierta(nombreSolicitado))
                return true;

            Type? tipoShell =
                Type.GetTypeFromProgID("Shell.Application");

            if (tipoShell == null)
                return false;

            object? shellObject = null;
            object? folderObject = null;
            object? itemsObject = null;

            try
            {
                shellObject =
                    Activator.CreateInstance(tipoShell);

                if (shellObject == null)
                    return false;

                dynamic shell = shellObject;

                folderObject =
                    shell.NameSpace("shell:AppsFolder");

                if (folderObject == null)
                    return false;

                dynamic folder = folderObject;

                itemsObject = folder.Items();

                dynamic items = itemsObject;

                string buscado =
                    Normalizar(nombreSolicitado);

                dynamic? mejorCoincidencia = null;

                // Primero buscamos coincidencia exacta.
                for (int i = 0;
                     i < items.Count;
                     i++)
                {
                    dynamic item =
                        items.Item(i);

                    string nombre =
                        item.Name?.ToString() ?? "";

                    if (Normalizar(nombre) == buscado)
                    {
                        item.InvokeVerb();

                        LiberarCom(item);

                        return true;
                    }

                    LiberarCom(item);
                }

                // Si no hay coincidencia exacta,
                // buscamos una coincidencia parcial.
                for (int i = 0;
                     i < items.Count;
                     i++)
                {
                    dynamic item =
                        items.Item(i);

                    string nombre =
                        item.Name?.ToString() ?? "";

                    string nombreNormalizado =
                        Normalizar(nombre);

                    if (nombreNormalizado.Contains(buscado)
                        ||
                        buscado.Contains(nombreNormalizado))
                    {
                        item.InvokeVerb();

                        LiberarCom(item);

                        return true;
                    }

                    LiberarCom(item);
                }

                return false;
            }
            finally
            {
                LiberarCom(itemsObject);
                LiberarCom(folderObject);
                LiberarCom(shellObject);
            }
        }

        private static string Normalizar(
            string texto)
        {
            string normalizado =
                texto.ToLowerInvariant()
                    .Normalize(
                        NormalizationForm.FormD);

            var resultado =
                new StringBuilder();

            foreach (char caracter
                     in normalizado)
            {
                UnicodeCategory categoria =
                    CharUnicodeInfo
                        .GetUnicodeCategory(caracter);

                if (categoria !=
                    UnicodeCategory.NonSpacingMark)
                {
                    resultado.Append(caracter);
                }
            }

            return resultado
                .ToString()
                .Normalize(
                    NormalizationForm.FormC)
                .Trim();
        }

        private static void LiberarCom(
            object? objeto)
        {
            if (objeto != null
                &&
                Marshal.IsComObject(objeto))
            {
                Marshal.FinalReleaseComObject(
                    objeto);
            }
        }

        private static bool ActivarSiEstaAbierta(
    string nombreSolicitado)
        {
            string buscado =
                Normalizar(nombreSolicitado);

            foreach (Process proceso
                     in Process.GetProcesses())
            {
                try
                {
                    if (proceso.MainWindowHandle ==
                        IntPtr.Zero)
                    {
                        continue;
                    }

                    string nombreProceso =
                        Normalizar(
                            proceso.ProcessName);

                    string tituloVentana =
                        Normalizar(
                            proceso.MainWindowTitle);

                    bool coincide =
                        nombreProceso.Contains(buscado)
                        ||
                        buscado.Contains(nombreProceso)
                        ||
                        tituloVentana.Contains(buscado);

                    if (!coincide)
                        continue;

                    ShowWindow(
                        proceso.MainWindowHandle,
                        SW_RESTORE);

                    SetForegroundWindow(
                        proceso.MainWindowHandle);

                    return true;
                }
                catch
                {
                    // Algunos procesos del sistema
                    // no permiten consultar toda
                    // su información.
                }
                finally
                {
                    proceso.Dispose();
                }
            }

            return false;
        }

        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(
            IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(
            IntPtr hWnd,
            int nCmdShow);
    }
}