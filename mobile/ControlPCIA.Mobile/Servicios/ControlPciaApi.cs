using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPCIA.Mobile.Modelos;
using Microsoft.Maui.Storage;

namespace ControlPCIA.Mobile.Servicios;

public sealed class ControlPciaApi
{
    private const string ClaveDireccion = "controlpcia_direccion";
    private const string ClaveToken = "controlpcia_token";

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    private string? _direccion;
    private string? _token;

    public string? Direccion => _direccion;
    public bool EstaConfigurada =>
        !string.IsNullOrWhiteSpace(_direccion)
        &&
        !string.IsNullOrWhiteSpace(_token);

    public async Task CargarAsync()
    {
        string direccionGuardada =
            Preferences.Default.Get(ClaveDireccion, string.Empty);

        _direccion = string.IsNullOrWhiteSpace(direccionGuardada)
            ? null
            : direccionGuardada;

        try
        {
            _token = await SecureStorage.Default.GetAsync(ClaveToken);
        }
        catch
        {
            _token = null;
        }
    }

    public async Task EmparejarAsync(
        string direccion,
        string codigo,
        CancellationToken cancellationToken = default)
    {
        if (!DescubrimientoPc.TryNormalizarDireccion(
                direccion,
                permitirBucleLocal: true,
                out string normalizada))
        {
            throw new InvalidOperationException(
                "La direccion debe ser una IP local, por ejemplo http://192.168.1.15:5187.");
        }

        using HttpResponseMessage respuesta =
            await _http.PostAsJsonAsync(
                new Uri(normalizada + "/api/emparejar"),
                new { codigo = codigo.Trim() },
                cancellationToken);

        RespuestaEmparejado? contenido =
            await LeerAsync<RespuestaEmparejado>(
                respuesta,
                cancellationToken);

        if (!respuesta.IsSuccessStatusCode
            ||
            string.IsNullOrWhiteSpace(contenido?.Token))
        {
            throw new InvalidOperationException(
                contenido?.Error ?? "No se pudo emparejar el movil con el PC.");
        }

        _direccion = normalizada;
        _token = contenido.Token;
        Preferences.Default.Set(ClaveDireccion, normalizada);

        try
        {
            await SecureStorage.Default.SetAsync(ClaveToken, _token);
        }
        catch
        {
            // La sesion actual sigue funcionando aunque el sistema no permita
            // conservar el token para el siguiente inicio.
        }
    }

    public Task<EstadoPc> ObtenerEstadoAsync(
        CancellationToken cancellationToken = default)
    {
        return ObtenerAsync<EstadoPc>("/api/estado", cancellationToken);
    }

    public async Task<ResultadoOrden> EnviarOrdenAsync(
        string texto,
        IReadOnlyList<MensajeConversacion>? contexto = null,
        CancellationToken cancellationToken = default)
    {
        ComprobarConfiguracion();

        using var peticion = new HttpRequestMessage(
            HttpMethod.Post,
            CrearUri("/api/orden"))
        {
            Content = JsonContent.Create(
                new
                {
                    texto = texto.Trim(),
                    contexto = contexto ?? []
                })
        };

        Autorizar(peticion);

        using HttpResponseMessage respuesta =
            await _http.SendAsync(peticion, cancellationToken);

        ResultadoOrden? contenido =
            await LeerAsync<ResultadoOrden>(
                respuesta,
                cancellationToken);

        if (!respuesta.IsSuccessStatusCode || contenido is null)
        {
            await LanzarErrorAsync(respuesta, cancellationToken);
        }

        return contenido!;
    }

    public Task<ResultadoEntradaRemotaMovil> EnviarRatonAsync(
        string accion,
        int deltaX = 0,
        int deltaY = 0,
        int rueda = 0,
        CancellationToken cancellationToken = default)
    {
        return EnviarEntradaAsync(
            "/api/entrada/raton",
            new
            {
                accion,
                deltaX,
                deltaY,
                rueda
            },
            cancellationToken);
    }

    public Task<ResultadoEntradaRemotaMovil> EnviarTextoAsync(
        string texto,
        CancellationToken cancellationToken = default)
    {
        return EnviarEntradaAsync(
            "/api/entrada/teclado",
            new
            {
                texto,
                tecla = (string?)null,
                modificadores = Array.Empty<string>()
            },
            cancellationToken);
    }

    public Task<ResultadoEntradaRemotaMovil> EnviarTeclaAsync(
        string tecla,
        IReadOnlyList<string>? modificadores = null,
        CancellationToken cancellationToken = default)
    {
        return EnviarEntradaAsync(
            "/api/entrada/teclado",
            new
            {
                texto = (string?)null,
                tecla,
                modificadores =
                    modificadores ?? []
            },
            cancellationToken);
    }

    public void Olvidar()
    {
        _direccion = null;
        _token = null;
        Preferences.Default.Remove(ClaveDireccion);
        SecureStorage.Default.Remove(ClaveToken);
    }

    private async Task<T> ObtenerAsync<T>(
        string ruta,
        CancellationToken cancellationToken)
    {
        ComprobarConfiguracion();

        using var peticion = new HttpRequestMessage(
            HttpMethod.Get,
            CrearUri(ruta));

        Autorizar(peticion);

        using HttpResponseMessage respuesta =
            await _http.SendAsync(peticion, cancellationToken);

        T? contenido =
            await LeerAsync<T>(respuesta, cancellationToken);

        if (!respuesta.IsSuccessStatusCode || contenido is null)
        {
            await LanzarErrorAsync(respuesta, cancellationToken);
        }

        return contenido!;
    }

    private async Task<ResultadoEntradaRemotaMovil>
        EnviarEntradaAsync(
            string ruta,
            object contenidoPeticion,
            CancellationToken cancellationToken)
    {
        ComprobarConfiguracion();

        using var peticion = new HttpRequestMessage(
            HttpMethod.Post,
            CrearUri(ruta))
        {
            Content = JsonContent.Create(
                contenidoPeticion)
        };
        Autorizar(peticion);

        using HttpResponseMessage respuesta =
            await _http.SendAsync(
                peticion,
                cancellationToken);
        ResultadoEntradaRemotaMovil? contenido =
            await LeerAsync<ResultadoEntradaRemotaMovil>(
                respuesta,
                cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            if (!string.IsNullOrWhiteSpace(
                    contenido?.Detalle))
            {
                throw new InvalidOperationException(
                    contenido.Detalle);
            }

            await LanzarErrorAsync(
                respuesta,
                cancellationToken);
        }

        if (contenido is null || !contenido.Correcto)
        {
            throw new InvalidOperationException(
                contenido?.Detalle
                ?? "El PC no ha aceptado la entrada remota.");
        }

        return contenido;
    }

    private void ComprobarConfiguracion()
    {
        if (!EstaConfigurada)
        {
            throw new InvalidOperationException(
                "Primero conecta la aplicacion con el PC.");
        }
    }

    private Uri CrearUri(string ruta)
    {
        return new Uri(_direccion + ruta, UriKind.Absolute);
    }

    private void Autorizar(HttpRequestMessage peticion)
    {
        peticion.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _token);
    }

    private async Task LanzarErrorAsync(
        HttpResponseMessage respuesta,
        CancellationToken cancellationToken)
    {
        if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
        {
            Olvidar();
            throw new InvalidOperationException(
                "La sesion ha caducado. Vuelve a introducir el codigo del PC.");
        }

        ErrorApi? error =
            await LeerAsync<ErrorApi>(respuesta, cancellationToken);

        throw new InvalidOperationException(
            error?.Error
            ?? $"El PC respondio con el error {(int)respuesta.StatusCode}.");
    }

    private static async Task<T?> LeerAsync<T>(
        HttpResponseMessage respuesta,
        CancellationToken cancellationToken)
    {
        try
        {
            return await respuesta.Content.ReadFromJsonAsync<T>(
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private sealed record RespuestaEmparejado(
        string? Token,
        string? Error);

    private sealed record ErrorApi(string? Error);
}
