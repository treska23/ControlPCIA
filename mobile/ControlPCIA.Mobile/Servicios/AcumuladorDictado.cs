namespace ControlPCIA.Mobile.Servicios;

internal sealed class AcumuladorDictado
{
    private readonly object _sincronizacion = new();
    private readonly List<string> _segmentos = [];
    private string _parcial = string.Empty;

    public void ActualizarParcial(string? texto)
    {
        lock (_sincronizacion)
        {
            _parcial = Limpiar(texto);
        }
    }

    public void ConfirmarSegmento(string? texto)
    {
        lock (_sincronizacion)
        {
            string limpio = Limpiar(texto);

            if (limpio.Length == 0)
            {
                limpio = _parcial;
            }

            _parcial = string.Empty;
            AñadirSiEsNuevo(limpio);
        }
    }

    public void ConfirmarParcial()
    {
        lock (_sincronizacion)
        {
            string parcial = _parcial;
            _parcial = string.Empty;
            AñadirSiEsNuevo(parcial);
        }
    }

    public string ObtenerTexto()
    {
        lock (_sincronizacion)
        {
            IEnumerable<string> partes = _segmentos;

            if (_parcial.Length > 0
                && (_segmentos.Count == 0
                    || !_segmentos[^1].Equals(
                        _parcial,
                        StringComparison.OrdinalIgnoreCase)))
            {
                partes = partes.Append(_parcial);
            }

            return string.Join(' ', partes).Trim();
        }
    }

    private void AñadirSiEsNuevo(string texto)
    {
        if (texto.Length == 0)
        {
            return;
        }

        if (_segmentos.Count > 0
            && _segmentos[^1].Equals(
                texto,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _segmentos.Add(texto);
    }

    private static string Limpiar(string? texto)
    {
        return string.Join(
            ' ',
            (texto ?? string.Empty)
                .Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries));
    }
}
