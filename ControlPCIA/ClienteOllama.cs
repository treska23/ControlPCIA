using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlPCIA;

internal sealed record EstadoOllama(
    bool Disponible,
    string Modelo,
    string Mensaje);

internal sealed record MensajeOllama(
    [property: JsonPropertyName("role")] string Rol,
    [property: JsonPropertyName("content")] string Contenido);

internal static class ClienteOllama
{
    private static readonly Uri DireccionBase =
        ObtenerDireccionBase();

    private static readonly HttpClient Cliente = new()
    {
        BaseAddress = DireccionBase,
        Timeout = TimeSpan.FromMinutes(5)
    };

    public static string Modelo { get; } =
        ObtenerModelo();

    public static Uri Direccion => DireccionBase;

    public static async Task<EstadoOllama> DiagnosticarAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage respuesta =
                await Cliente.GetAsync(
                    "api/tags",
                    cancellationToken);

            respuesta.EnsureSuccessStatusCode();

            await using Stream contenido =
                await respuesta.Content.ReadAsStreamAsync(
                    cancellationToken);

            using JsonDocument json =
                await JsonDocument.ParseAsync(
                    contenido,
                    cancellationToken: cancellationToken);

            bool modeloInstalado =
                json.RootElement
                    .GetProperty("models")
                    .EnumerateArray()
                    .Any(modelo =>
                        string.Equals(
                            modelo.GetProperty("name").GetString(),
                            Modelo,
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        string.Equals(
                            modelo.GetProperty("model").GetString(),
                            Modelo,
                            StringComparison.OrdinalIgnoreCase));

            if (!modeloInstalado)
            {
                return new EstadoOllama(
                    false,
                    Modelo,
                    $"Ollama responde, pero el modelo {Modelo} no está instalado.");
            }

            return new EstadoOllama(
                true,
                Modelo,
                $"Ollama y el modelo {Modelo} están disponibles.");
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or TaskCanceledException
            or JsonException
            or InvalidOperationException)
        {
            return new EstadoOllama(
                false,
                Modelo,
                "No se pudo conectar con Ollama en " +
                $"{DireccionBase}: {ex.Message}");
        }
    }

    public static async Task<string> ConversarAsync(
        IReadOnlyList<MensajeOllama> mensajes,
        CancellationToken cancellationToken = default)
    {
        var peticion = new
        {
            model = Modelo,
            messages = mensajes,
            stream = false,
            think = false
        };

        using HttpResponseMessage respuesta =
            await Cliente.PostAsJsonAsync(
                "api/chat",
                peticion,
                cancellationToken);

        respuesta.EnsureSuccessStatusCode();

        await using Stream contenido =
            await respuesta.Content.ReadAsStreamAsync(
                cancellationToken);

        using JsonDocument json =
            await JsonDocument.ParseAsync(
                contenido,
                cancellationToken: cancellationToken);

        return json.RootElement
                   .GetProperty("message")
                   .GetProperty("content")
                   .GetString()
               ?? string.Empty;
    }

    private static Uri ObtenerDireccionBase()
    {
        string valor =
            Environment.GetEnvironmentVariable(
                "CONTROLPCIA_OLLAMA_URL")
            ?? "http://127.0.0.1:11434/";

        if (!Uri.TryCreate(valor, UriKind.Absolute, out Uri? direccion)
            ||
            direccion.Scheme != Uri.UriSchemeHttp
            ||
            !direccion.IsLoopback)
        {
            throw new InvalidOperationException(
                "CONTROLPCIA_OLLAMA_URL debe ser una dirección HTTP local.");
        }

        string normalizada =
            direccion.AbsoluteUri.EndsWith('/')
                ? direccion.AbsoluteUri
                : direccion.AbsoluteUri + "/";

        return new Uri(normalizada);
    }

    private static string ObtenerModelo()
    {
        string modelo =
            Environment.GetEnvironmentVariable(
                "CONTROLPCIA_OLLAMA_MODELO")
            ?? "qwen3:8b";

        modelo = modelo.Trim();

        if (modelo.Length is < 1 or > 120
            ||
            modelo.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "CONTROLPCIA_OLLAMA_MODELO no es válido.");
        }

        return modelo;
    }
}
