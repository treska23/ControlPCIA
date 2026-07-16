using System.Globalization;
using System.Text;

namespace ControlPCIA.Mobile.Servicios;

public static class DetectorConfirmacion
{
    private static readonly HashSet<string> Afirmativas =
        new(StringComparer.Ordinal)
        {
            "si", "sí", "confirmo", "confirmar", "adelante",
            "continua", "continúa", "hazlo", "de acuerdo", "vale"
        };

    private static readonly HashSet<string> Negativas =
        new(StringComparer.Ordinal)
        {
            "no", "cancelar", "cancela", "cancelalo", "cancélalo",
            "dejalo", "déjalo", "olvidalo", "olvídalo"
        };

    public static bool EsAfirmativa(string texto)
    {
        return Afirmativas.Contains(Normalizar(texto));
    }

    public static bool EsNegativa(string texto)
    {
        return Negativas.Contains(Normalizar(texto));
    }

    private static string Normalizar(string texto)
    {
        string descompuesto = texto
            .Trim()
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);
        var resultado = new StringBuilder(descompuesto.Length);
        bool espacio = false;

        foreach (char caracter in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caracter)
                == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(caracter))
            {
                resultado.Append(caracter);
                espacio = false;
            }
            else if (!espacio && resultado.Length > 0)
            {
                resultado.Append(' ');
                espacio = true;
            }
        }

        return resultado.ToString().Trim();
    }
}
