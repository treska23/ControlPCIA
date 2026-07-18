using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ControlPCIA;

internal static class ValidadorAutomatizacionAplicaciones
{
    private const int LongitudMaximaArgumento = 300;
    private const int LongitudMaximaTexto = 1000;

    private static readonly HashSet<string> Acciones =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "windows", "inspect", "status", "focus", "close",
            "invoke", "select", "toggle",
            "expand", "collapse", "text", "shortcut"
        };

    private static readonly HashSet<string> TiposControl =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Button", "MenuItem", "ListItem", "TreeItem", "TabItem",
            "CheckBox", "RadioButton", "ComboBox", "Edit", "Document",
            "Hyperlink", "DataItem", "Custom"
        };

    private static readonly string[] SuperficiesProtegidas =
    [
        "powershell", "terminal", "simbolo del sistema", "command prompt",
        "editor del registro", "registry editor",
        "administrador de tareas", "task manager",
        "seguridad de windows", "windows security", "credenciales",
        "credentials", "control de cuentas de usuario", "user account control"
    ];

    private static readonly string[] AccionesDestructivasDeArchivos =
    [
        "eliminar archivo", "delete file", "eliminar carpeta",
        "delete folder", "remove file", "remove folder", "borrar archivo",
        "borrar carpeta", "cortar", "cut", "mover a", "move to",
        "vaciar papelera", "empty recycle bin", "eliminar permanentemente",
        "delete permanently"
    ];

    private static readonly string[] AccionesSiempreProtegidas =
    [
        "desinstalar", "uninstall", "formatear", "format disk",
        "particionar", "partition disk", "borrar disco", "wipe disk",
        "credencial", "credential", "contraseña", "password",
        "control de cuentas de usuario", "user account control"
    ];

    private static readonly string[] AccionesDescarte =
    [
        "descartar", "discard", "no guardar", "don't save",
        "dont save", "cerrar sin guardar", "close without saving"
    ];

    private static readonly HashSet<string> AtajosBloqueados =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ALT+F4", "CTRL+ALT+DELETE", "SHIFT+DELETE"
        };

    private static readonly HashSet<string> AtajosArchivosDestructivos =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CTRL+X", "DELETE"
        };

    public static ResultadoValidacionPowerShell Validar(
        IReadOnlyList<string> argumentos,
        bool permitirDescarte = false)
    {
        if (argumentos.Count < 2
            || !argumentos[0].Equals("ui", StringComparison.OrdinalIgnoreCase))
        {
            return Bloquear(
                "ControlPCIA.exe sólo puede invocarse desde la IA con el subcomando literal 'ui'.");
        }

        string accion = argumentos[1];

        if (!Acciones.Contains(accion))
        {
            return Bloquear(
                $"La acción de interfaz '{accion}' no existe o no está permitida.");
        }

        if (!TieneNumeroArgumentosValido(accion, argumentos.Count))
        {
            return Bloquear(
                $"La acción de interfaz '{accion}' tiene un número de argumentos no válido.");
        }

        foreach (string argumento in argumentos)
        {
            if (string.IsNullOrWhiteSpace(argumento)
                || argumento.Length > LongitudMaximaArgumento
                || argumento.Any(char.IsControl))
            {
                return Bloquear(
                    "Los argumentos de interfaz deben ser literales breves y no pueden contener caracteres de control.");
            }
        }

        if (accion.Equals("windows", StringComparison.OrdinalIgnoreCase))
        {
            return Permitir();
        }

        string ventana = argumentos[2];

        if (ContieneFrase(ventana, SuperficiesProtegidas))
        {
            return Bloquear(
                "La ventana indicada pertenece a una superficie protegida.");
        }

        if (accion.Equals("inspect", StringComparison.OrdinalIgnoreCase))
        {
            if (argumentos.Count == 4
                && (!int.TryParse(argumentos[3], out int profundidad)
                    || profundidad is < 1 or > 6))
            {
                return Bloquear(
                    "La profundidad de inspección debe ser un número literal entre 1 y 6.");
            }

            return Permitir();
        }

        if (accion.Equals("status", StringComparison.OrdinalIgnoreCase)
            || accion.Equals("focus", StringComparison.OrdinalIgnoreCase)
            || accion.Equals("close", StringComparison.OrdinalIgnoreCase))
        {
            return Permitir();
        }

        if (accion.Equals("shortcut", StringComparison.OrdinalIgnoreCase))
        {
            return EsAtajoSeguro(argumentos[3])
                ? Permitir()
                : Bloquear(
                    "El atajo solicitado puede abrir, guardar, imprimir o cerrar contenido, o contiene teclas no permitidas.");
        }

        string selector = argumentos[3];

        if (ContieneFrase(selector, AccionesSiempreProtegidas)
            || !permitirDescarte
               && ContieneFrase(selector, AccionesDescarte))
        {
            return Bloquear(
                "El control solicitado pertenece a una operación destructiva del sistema o a una superficie de credenciales.");
        }

        if (accion.Equals("text", StringComparison.OrdinalIgnoreCase))
        {
            string texto = argumentos[4];

            if (texto.Length > LongitudMaximaTexto)
            {
                return Bloquear(
                    $"El texto de interfaz no puede superar los {LongitudMaximaTexto} caracteres.");
            }

            if (texto.Any(char.IsControl))
            {
                return Bloquear(
                    "El texto de interfaz no puede contener caracteres de control.");
            }

            return Permitir();
        }

        if (argumentos.Count == 5
            && !TiposControl.Contains(argumentos[4]))
        {
            return Bloquear(
                $"El tipo de control '{argumentos[4]}' no está permitido.");
        }

        return Permitir();
    }

    internal static bool EsAtajoSeguro(string atajo)
    {
        string normalizado = atajo
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        if (AtajosBloqueados.Contains(normalizado)
            || normalizado.Contains("WIN", StringComparison.Ordinal))
        {
            return false;
        }

        string[] partes = normalizado.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries);

        if (partes.Length == 0 || partes.Length > 4)
        {
            return false;
        }

        string[] modificadores = partes[..^1];
        string tecla = partes[^1];

        if (modificadores.Distinct(StringComparer.Ordinal).Count()
            != modificadores.Length
            || modificadores.Any(modificador =>
                modificador is not ("CTRL" or "ALT" or "SHIFT")))
        {
            return false;
        }

        bool teclaNavegacion = tecla is
            "ENTER" or "ESC" or "TAB" or "SPACE" or "UP" or "DOWN"
            or "LEFT" or "RIGHT" or "HOME" or "END" or "PGUP" or "PGDN"
            or "BACKSPACE" or "DELETE";
        bool teclaFuncion = Regex.IsMatch(
            tecla,
            "^F(?:[1-9]|1[0-9]|2[0-4])$",
            RegexOptions.CultureInvariant);
        bool letraONumero = Regex.IsMatch(
            tecla,
            "^[A-Z0-9]$",
            RegexOptions.CultureInvariant);

        if (!teclaNavegacion && !teclaFuncion && !letraONumero)
        {
            return false;
        }

        return !letraONumero || modificadores.Length > 0;
    }

    internal static bool EsAtajoPermitidoEnVentana(
        string atajo,
        bool superficieArchivos)
    {
        if (!EsAtajoSeguro(atajo))
        {
            return false;
        }

        string normalizado = atajo
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        return !superficieArchivos
               || !AtajosArchivosDestructivos.Contains(normalizado);
    }

    internal static string Normalizar(string texto)
    {
        string descompuesto = texto
            .Trim()
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);
        var resultado = new StringBuilder(descompuesto.Length);

        foreach (char caracter in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caracter)
                != UnicodeCategory.NonSpacingMark)
            {
                resultado.Append(caracter);
            }
        }

        return resultado
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }

    internal static bool EsControlSiempreProtegido(
        string texto,
        bool permitirDescarte = false)
    {
        return ContieneFrase(texto, AccionesSiempreProtegidas)
               || !permitirDescarte
                  && ContieneFrase(texto, AccionesDescarte);
    }

    internal static bool EsAccionDestructivaDeArchivos(string texto)
    {
        return ContieneFrase(texto, AccionesDestructivasDeArchivos);
    }

    private static bool TieneNumeroArgumentosValido(
        string accion,
        int cantidad)
    {
        return accion.ToLowerInvariant() switch
        {
            "windows" => cantidad == 2,
            "inspect" => cantidad is 3 or 4,
            "status" or "focus" or "close" => cantidad == 3,
            "text" => cantidad == 5,
            "shortcut" => cantidad == 4,
            _ => cantidad is 4 or 5
        };
    }

    private static bool ContieneFrase(
        string texto,
        IEnumerable<string> frases)
    {
        string normalizado = Normalizar(texto);

        return frases.Any(frase =>
            normalizado.Contains(
                Normalizar(frase),
                StringComparison.Ordinal));
    }

    private static ResultadoValidacionPowerShell Permitir()
    {
        return new(
            true,
            "La orden usa una primitiva segura de automatización de aplicaciones.");
    }

    private static ResultadoValidacionPowerShell Bloquear(string motivo)
    {
        return new(false, motivo);
    }
}
