using System;
using System.Runtime.InteropServices;

namespace ControlPCIA
{
    internal static class EjecutorInterfazWindows
    {
        private const int INPUT_KEYBOARD = 1;

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        private const ushort VK_LWIN = 0x5B;
        private const ushort VK_RETURN = 0x0D;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_A = 0x41;

        public static void PulsarWindows()
        {
            PulsarTecla(VK_LWIN);
        }

        public static void PulsarEnter()
        {
            PulsarTecla(VK_RETURN);
        }

        public static void EscribirTexto(string texto)
        {
            foreach (char caracter in texto)
            {
                INPUT pulsar = CrearEntradaUnicode(caracter, false);
                INPUT soltar = CrearEntradaUnicode(caracter, true);

                INPUT[] entradas = { pulsar, soltar };

                SendInput(
                    (uint)entradas.Length,
                    entradas,
                    Marshal.SizeOf<INPUT>());
            }
        }

        private static void PulsarTecla(ushort tecla)
        {
            INPUT pulsar = CrearEntradaTecla(tecla, false);
            INPUT soltar = CrearEntradaTecla(tecla, true);

            INPUT[] entradas = { pulsar, soltar };

            SendInput(
                (uint)entradas.Length,
                entradas,
                Marshal.SizeOf<INPUT>());
        }

        private static INPUT CrearEntradaTecla(
            ushort tecla,
            bool soltar)
        {
            return new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = tecla,
                        wScan = 0,
                        dwFlags = soltar ? KEYEVENTF_KEYUP : 0,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        private static INPUT CrearEntradaUnicode(
            char caracter,
            bool soltar)
        {
            return new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = caracter,
                        dwFlags =
                            KEYEVENTF_UNICODE |
                            (soltar ? KEYEVENTF_KEYUP : 0),
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        private static extern uint SendInput(
            uint nInputs,
            INPUT[] pInputs,
            int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;

            [FieldOffset(0)]
            public KEYBDINPUT ki;

            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        public static void SeleccionarTodo()
        {
            INPUT ctrlDown =
                CrearEntradaTecla(VK_CONTROL, false);

            INPUT aDown =
                CrearEntradaTecla(VK_A, false);

            INPUT aUp =
                CrearEntradaTecla(VK_A, true);

            INPUT ctrlUp =
                CrearEntradaTecla(VK_CONTROL, true);

            INPUT[] entradas =
            {
        ctrlDown,
        aDown,
        aUp,
        ctrlUp
    };

            SendInput(
                (uint)entradas.Length,
                entradas,
                Marshal.SizeOf<INPUT>());
        }
    }
}