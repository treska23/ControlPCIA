using System.Reflection;
using System.IO;
using Microsoft.Win32;

namespace ControlPCIA;

internal static class GestorInicioWindows
{
    private const string RutaRun =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NombreValor = "ControlPCIA";
    private const string RutaPreferencias = @"Software\ControlPCIA";
    private const string NombrePreferencia = "InicioConWindows";

    public static bool EstaActivado()
    {
        using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(RutaRun);
        return clave?.GetValue(NombreValor) is string valor
               && !string.IsNullOrWhiteSpace(valor);
    }

    public static void Activar()
    {
        string comando = CrearComandoInicio(
            Environment.ProcessPath,
            Assembly.GetEntryAssembly()?.Location);

        using RegistryKey clave =
            Registry.CurrentUser.CreateSubKey(RutaRun, writable: true);
        clave.SetValue(
            NombreValor,
            comando,
            RegistryValueKind.String);
        GuardarPreferencia(activado: true);
    }

    public static void Desactivar()
    {
        using RegistryKey? clave =
            Registry.CurrentUser.OpenSubKey(RutaRun, writable: true);
        clave?.DeleteValue(NombreValor, throwOnMissingValue: false);
        GuardarPreferencia(activado: false);
    }

    public static void AsegurarConfiguracionInicial()
    {
        using RegistryKey? preferencias =
            Registry.CurrentUser.OpenSubKey(RutaPreferencias);
        object? valor = preferencias?.GetValue(NombrePreferencia);

        if (valor is int entero && entero == 0)
        {
            return;
        }

        if (valor is int configurado && configurado == 1
            && EstaActivado())
        {
            return;
        }

        Activar();
    }

    internal static string CrearComandoInicio(
        string? proceso,
        string? ensamblado)
    {
        if (string.IsNullOrWhiteSpace(proceso))
        {
            throw new InvalidOperationException(
                "Windows no ha proporcionado la ruta del proceso de ControlPCIA.");
        }

        string ejecutable = Path.GetFullPath(proceso);
        string nombre = Path.GetFileNameWithoutExtension(ejecutable);

        if (nombre.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(ensamblado))
            {
                throw new InvalidOperationException(
                    "No se encontró el ensamblado de ControlPCIA para iniciar con Windows.");
            }

            return $"{EntreComillas(ejecutable)} " +
                   $"{EntreComillas(Path.GetFullPath(ensamblado))} " +
                   "--servidor --oculto";
        }

        return $"{EntreComillas(ejecutable)} --servidor --oculto";
    }

    private static string EntreComillas(string valor)
    {
        return "\"" + valor.Replace("\"", "", StringComparison.Ordinal) + "\"";
    }

    private static void GuardarPreferencia(bool activado)
    {
        using RegistryKey clave =
            Registry.CurrentUser.CreateSubKey(
                RutaPreferencias,
                writable: true);
        clave.SetValue(
            NombrePreferencia,
            activado ? 1 : 0,
            RegistryValueKind.DWord);
    }
}
