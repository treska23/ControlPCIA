using System.Text.Json;
using ControlPCIA.Mobile.Modelos;
using Microsoft.Maui.Storage;

namespace ControlPCIA.Mobile.Servicios;

public sealed class WakeOnLan
{
    private const string ClaveDestinos = "controlpcia_wake_on_lan";
    private static readonly JsonSerializerOptions OpcionesJson =
        new(JsonSerializerDefaults.Web);

    private IReadOnlyList<DestinoWakeOnLan> _destinos = [];

    public bool EstaConfigurado => _destinos.Count > 0;
    public int Puerto =>
        _destinos.FirstOrDefault()?.Puerto is > 0 and <= 65535
            ? _destinos[0].Puerto
            : 9;

    public void Cargar()
    {
        string json = Preferences.Default.Get(ClaveDestinos, string.Empty);

        if (string.IsNullOrWhiteSpace(json))
        {
            _destinos = [];
            return;
        }

        try
        {
            _destinos =
                JsonSerializer.Deserialize<DestinoWakeOnLan[]>(
                    json,
                    OpcionesJson)
                ?? [];
        }
        catch (JsonException)
        {
            _destinos = [];
        }
    }

    public void Guardar(IReadOnlyList<DestinoWakeOnLan>? destinos)
    {
        DestinoWakeOnLan[] validos = (destinos ?? [])
            .Where(destino =>
                EmisorWakeOnLan.TryObtenerMac(destino.Mac, out _)
                &&
                destino.Puerto is > 0 and <= 65535
                &&
                destino.DireccionesBroadcast is { Count: > 0 })
            .ToArray();

        if (validos.Length == 0)
        {
            return;
        }

        _destinos = validos;
        Preferences.Default.Set(
            ClaveDestinos,
            JsonSerializer.Serialize(validos, OpcionesJson));
    }

    public void Olvidar()
    {
        _destinos = [];
        Preferences.Default.Remove(ClaveDestinos);
    }

    public async Task<int> EncenderAsync(
        CancellationToken cancellationToken = default)
    {
        if (_destinos.Count == 0)
        {
            throw new InvalidOperationException(
                "Conecta la app con el PC una vez para que aprenda su tarjeta de red.");
        }

        return await EmisorWakeOnLan.EnviarAsync(
            _destinos,
            cancellationToken);
    }

    public static bool EsOrdenEncender(string? texto)
    {
        return DetectorOrdenEncendido.EsOrdenEncender(texto);
    }

}
