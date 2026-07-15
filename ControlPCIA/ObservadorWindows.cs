using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ControlPCIA
{
    internal static class ObservadorWindows
    {
        public static List<string> ObtenerVentanasAbiertas()
        {
            var ventanas = new List<string>();

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true;

                int longitud = GetWindowTextLength(hWnd);

                if (longitud == 0)
                    return true;

                var texto = new StringBuilder(longitud + 1);

                GetWindowText(
                    hWnd,
                    texto,
                    texto.Capacity);

                string titulo = texto.ToString().Trim();

                if (!string.IsNullOrWhiteSpace(titulo))
                {
                    ventanas.Add(titulo);
                }

                return true;

            }, IntPtr.Zero);

            return ventanas;
        }

        private delegate bool EnumWindowsProc(
            IntPtr hWnd,
            IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(
            EnumWindowsProc lpEnumFunc,
            IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(
            IntPtr hWnd);

        [DllImport(
            "user32.dll",
            CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(
            IntPtr hWnd,
            StringBuilder lpString,
            int nMaxCount);

        [DllImport(
            "user32.dll",
            CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(
            IntPtr hWnd);
    }
}