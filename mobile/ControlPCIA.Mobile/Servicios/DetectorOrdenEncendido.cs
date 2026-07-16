using System.Globalization;
using System.Text;

namespace ControlPCIA.Mobile.Servicios;

public static class DetectorOrdenEncendido
{
    public static bool EsOrdenEncender(string? texto)
    {
        string normalizada = Normalizar(texto);

        if (string.IsNullOrWhiteSpace(normalizada)
            || normalizada.StartsWith("no ", StringComparison.Ordinal))
        {
            return false;
        }

        string[] palabras = normalizada.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);
        bool accion = palabras.Any(palabra =>
            palabra.StartsWith("enciend", StringComparison.Ordinal)
            || palabra.StartsWith("encend", StringComparison.Ordinal)
            || palabra.StartsWith("arranc", StringComparison.Ordinal)
            || palabra.StartsWith("despiert", StringComparison.Ordinal)
            || palabra.StartsWith("prend", StringComparison.Ordinal));
        bool dispositivo = palabras.Any(palabra =>
            palabra is "pc"
                or "ordenador"
                or "computador"
                or "computadora"
                or "equipo");

        return accion && dispositivo;
    }

    private static string Normalizar(string? texto)
    {
        string descompuesto = (texto ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);
        var resultado = new StringBuilder(descompuesto.Length);

        foreach (char caracter in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caracter)
                != UnicodeCategory.NonSpacingMark)
            {
                resultado.Append(
                    char.IsLetterOrDigit(caracter) || char.IsWhiteSpace(caracter)
                        ? caracter
                        : ' ');
            }
        }

        return resultado
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }
}
