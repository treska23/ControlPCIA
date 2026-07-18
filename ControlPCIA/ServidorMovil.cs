using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlPCIA;

internal sealed record SolicitudEmparejado(string? Codigo);
internal sealed record SolicitudOrden(
    string? Texto,
    IReadOnlyList<MensajeConversacionControl>? Contexto);
internal sealed record EstadoInicioServidor(
    int Puerto,
    string CodigoEmparejado,
    EstadoOllama Diagnostico);

internal static class ServidorMovil
{
    private const int PuertoPredeterminado = 5187;
    private const int LongitudMaximaOrden = 1000;
    private const int LongitudMaximaPeticion = 12_000;
    internal const string RutaDescargaApkAndroid = "/app-android.apk";
    internal const string NombreArchivoApkAndroid = "ControlPCIA.Mobile.apk";

    public static async Task IniciarAsync(
        CancellationToken cancellationToken = default,
        Action<EstadoInicioServidor>? alIniciar = null)
    {
        int puerto = ObtenerPuerto();
        var seguridad = new SeguridadMovil();
        var exclusividad = new SemaphoreSlim(1, 1);

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = []
            });

        builder.WebHost
            .UseUrls($"http://0.0.0.0:{puerto}")
            .ConfigureKestrel(opciones =>
            {
                opciones.Limits.MaxRequestBodySize =
                    LongitudMaximaPeticion;

                opciones.Limits.RequestHeadersTimeout =
                    TimeSpan.FromSeconds(10);

                opciones.Limits.KeepAliveTimeout =
                    TimeSpan.FromMinutes(2);
            });

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(opciones =>
        {
            opciones.SingleLine = true;
            opciones.TimestampFormat = "HH:mm:ss ";
        });

        WebApplication app = builder.Build();

        app.Use(async (contexto, siguiente) =>
        {
            contexto.Response.Headers.CacheControl =
                "no-store, no-cache, must-revalidate";

            contexto.Response.Headers.Append(
                "X-Content-Type-Options",
                "nosniff");

            contexto.Response.Headers.Append(
                "X-Frame-Options",
                "DENY");

            contexto.Response.Headers.Append(
                "Referrer-Policy",
                "no-referrer");

            contexto.Response.Headers.Append(
                "Permissions-Policy",
                "camera=(), geolocation=(), microphone=(self)");

            if (!EsDireccionPermitida(
                    contexto.Connection.RemoteIpAddress))
            {
                contexto.Response.StatusCode =
                    StatusCodes.Status403Forbidden;

                await contexto.Response.WriteAsJsonAsync(
                    new { error = "Solo se permite el acceso desde la red local." });

                return;
            }

            await siguiente(contexto);
        });

        app.MapGet(
            "/",
            (HttpContext contexto) =>
            {
                string nonce = CrearToken(18);

                contexto.Response.Headers.Append(
                    "Content-Security-Policy",
                    "default-src 'self'; " +
                    $"script-src 'nonce-{nonce}'; " +
                    $"style-src 'nonce-{nonce}'; " +
                    "connect-src 'self'; img-src 'self' data:; " +
                    "font-src 'none'; worker-src 'self'; manifest-src 'self'; " +
                    "object-src 'none'; base-uri 'none'; " +
                    "frame-ancestors 'none'; form-action 'self'");

                return Results.Content(
                    PaginaMovil.Replace("__NONCE__", nonce),
                    "text/html; charset=utf-8");
            });

        app.MapGet(
            "/manifest.webmanifest",
            (HttpContext contexto) =>
            {
                contexto.Response.Headers.CacheControl =
                    "no-cache, must-revalidate";

                return Results.Content(
                    ManifiestoPwa,
                    "application/manifest+json; charset=utf-8");
            });

        app.MapGet(
            "/sw.js",
            (HttpContext contexto) =>
            {
                contexto.Response.Headers.CacheControl =
                    "no-cache, must-revalidate";

                contexto.Response.Headers.Append(
                    "Service-Worker-Allowed",
                    "/");

                return Results.Content(
                    ServiceWorkerPwa,
                    "text/javascript; charset=utf-8");
            });

        app.MapGet(
            "/icons/controlpcia.svg",
            (HttpContext contexto) =>
            {
                contexto.Response.Headers.CacheControl =
                    "public, max-age=31536000, immutable";

                return Results.Content(
                    IconoPwa,
                    "image/svg+xml; charset=utf-8");
            });

        app.MapGet(
            "/icons/controlpcia-maskable.svg",
            (HttpContext contexto) =>
            {
                contexto.Response.Headers.CacheControl =
                    "public, max-age=31536000, immutable";

                return Results.Content(
                    IconoPwaMaskable,
                    "image/svg+xml; charset=utf-8");
            });

        app.MapGet(
            RutaDescargaApkAndroid,
            DescargarApkAndroid);

        app.MapPost(
            "/api/emparejar",
            (HttpContext contexto, SolicitudEmparejado solicitud) =>
            {
                string direccion =
                    contexto.Connection.RemoteIpAddress?.ToString()
                    ?? "desconocida";

                ResultadoEmparejado resultado =
                    seguridad.Emparejar(
                        direccion,
                        solicitud.Codigo);

                return resultado.Estado switch
                {
                    EstadoEmparejado.Correcto =>
                        Results.Ok(
                            new
                            {
                                token = resultado.Token,
                                caducaEnHoras = 12
                            }),

                    EstadoEmparejado.Limitado =>
                        Results.Json(
                            new { error = resultado.Mensaje },
                            statusCode: StatusCodes.Status429TooManyRequests),

                    _ =>
                        Results.Json(
                            new { error = resultado.Mensaje },
                            statusCode: StatusCodes.Status401Unauthorized)
                };
            });

        app.MapGet(
            "/api/estado",
            async (HttpContext contexto) =>
            {
                if (!seguridad.Autorizar(contexto))
                {
                    return Results.Unauthorized();
                }

                EstadoOllama ollama =
                    await ClienteOllama.DiagnosticarAsync(
                        contexto.RequestAborted);

                int recetas =
                    await MemoriaRecetas.Predeterminada.ContarAsync(
                        contexto.RequestAborted);

                return Results.Ok(
                    new
                    {
                        disponible = ollama.Disponible,
                        ocupado = exclusividad.CurrentCount == 0,
                        recetasAprendidas = recetas,
                        modelo = ollama.Modelo,
                        mensaje = ollama.Mensaje,
                        wakeOnLan = InformacionWakeOnLan.ObtenerDestinos()
                    });
            });

        app.MapPost(
            "/api/orden",
            async (HttpContext contexto, SolicitudOrden solicitud) =>
            {
                if (!seguridad.Autorizar(contexto))
                {
                    return Results.Unauthorized();
                }

                string texto = solicitud.Texto?.Trim() ?? string.Empty;

                if (!EsOrdenValida(texto))
                {
                    return Results.BadRequest(
                        new
                        {
                            error =
                                $"La orden debe contener entre 1 y " +
                                $"{LongitudMaximaOrden} caracteres."
                        });
                }

                if (!await exclusividad.WaitAsync(0))
                {
                    return Results.Conflict(
                        new
                        {
                            error =
                                "Ya se está procesando otra orden. " +
                                "Espera a que termine."
                        });
                }

                try
                {
                    ResultadoControl resultado =
                        await ControlWindows.ControlarAsync(
                            texto,
                            contextoConversacion: solicitud.Contexto,
                            cancellationToken:
                                contexto.RequestAborted);

                    return Results.Ok(resultado);
                }
                finally
                {
                    exclusividad.Release();
                }
            });

        app.MapFallback(() => Results.NotFound());

        EstadoOllama diagnostico =
            await ClienteOllama.DiagnosticarAsync(
                cancellationToken);

        MostrarInicio(
            puerto,
            seguridad.Codigo,
            diagnostico);
        alIniciar?.Invoke(
            new EstadoInicioServidor(
                puerto,
                seguridad.Codigo,
                diagnostico));

        using var cancelacionDescubrimiento =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        Task descubrimiento =
            ServidorDescubrimiento.EscucharAsync(
                puerto,
                cancelacionDescubrimiento.Token);

        try
        {
            await app.RunAsync(cancellationToken);
        }
        finally
        {
            await cancelacionDescubrimiento.CancelAsync();
            await descubrimiento;
        }
    }

    internal static bool EsDireccionPermitida(IPAddress? direccion)
    {
        if (direccion is null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(direccion))
        {
            return true;
        }

        if (direccion.IsIPv4MappedToIPv6)
        {
            direccion = direccion.MapToIPv4();
        }

        byte[] bytes = direccion.GetAddressBytes();

        if (direccion.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10
                   ||
                   bytes[0] == 127
                   ||
                   bytes[0] == 192 && bytes[1] == 168
                   ||
                   bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                   ||
                   bytes[0] == 169 && bytes[1] == 254;
        }

        if (direccion.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return direccion.IsIPv6LinkLocal
                   ||
                   (bytes[0] & 0xFE) == 0xFC;
        }

        return false;
    }

    private static bool EsOrdenValida(string texto)
    {
        return texto.Length is > 0 and <= LongitudMaximaOrden
               &&
               !texto.Any(caracter =>
                   char.IsControl(caracter)
                   &&
                   caracter is not '\r' and not '\n' and not '\t');
    }

    internal static string ObtenerRutaApkAndroid()
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            NombreArchivoApkAndroid);
    }

    private static IResult DescargarApkAndroid()
    {
        string ruta = ObtenerRutaApkAndroid();

        if (!File.Exists(ruta))
        {
            return Results.NotFound(
                new
                {
                    error =
                        "La aplicación Android todavía no está disponible."
                });
        }

        return Results.File(
            ruta,
            contentType: "application/vnd.android.package-archive",
            fileDownloadName: "ControlPCIA-Android.apk",
            enableRangeProcessing: true);
    }

    internal static int ObtenerPuerto()
    {
        string? valor =
            Environment.GetEnvironmentVariable(
                "CONTROLPCIA_PUERTO");

        if (string.IsNullOrWhiteSpace(valor))
        {
            return PuertoPredeterminado;
        }

        if (!int.TryParse(valor, out int puerto)
            ||
            puerto is < 1024 or > 65535)
        {
            throw new InvalidOperationException(
                "CONTROLPCIA_PUERTO debe estar entre 1024 y 65535.");
        }

        return puerto;
    }

    private static void MostrarInicio(
        int puerto,
        string codigo,
        EstadoOllama diagnostico)
    {
        Console.WriteLine();
        Console.WriteLine("CONTROLPCIA ESTÁ PREPARADO");
        Console.WriteLine();
        Console.WriteLine(diagnostico.Mensaje);
        Console.WriteLine();
        Console.WriteLine("Abre una de estas direcciones en el móvil:");

        foreach (string direccion in ObtenerDireccionesLocales(puerto))
        {
            Console.WriteLine("  " + direccion);
        }

        Console.WriteLine();
        Console.WriteLine($"Código de emparejado: {codigo}");
        Console.WriteLine("El código solo se muestra en este PC.");
        Console.WriteLine();
        Console.WriteLine(
            "Si Windows pregunta por el firewall, permite solo redes privadas.");
        Console.WriteLine("Pulsa Ctrl+C para detener el servidor.");
        Console.WriteLine();
    }

    internal static IReadOnlyList<string> ObtenerDireccionesLocales(
        int puerto)
    {
        var resultado = new SortedSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            $"http://127.0.0.1:{puerto}"
        };

        try
        {
            foreach (IPAddress direccion in
                     Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (direccion.AddressFamily == AddressFamily.InterNetwork
                    &&
                    EsDireccionPermitida(direccion)
                    &&
                    !IPAddress.IsLoopback(direccion))
                {
                    resultado.Add($"http://{direccion}:{puerto}");
                }
            }
        }
        catch (SocketException)
        {
            // La dirección local puede obtenerse manualmente con ipconfig.
        }

        return resultado.ToArray();
    }

    private static string CrearToken(int bytes)
    {
        return Convert
            .ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    internal enum EstadoEmparejado
    {
        Correcto,
        Incorrecto,
        Limitado
    }

    internal sealed record ResultadoEmparejado(
        EstadoEmparejado Estado,
        string Mensaje,
        string? Token = null);

    internal sealed class SeguridadMovil
    {
        private static readonly TimeSpan DuracionSesion =
            TimeSpan.FromDays(90);
        private static readonly TimeSpan RenovarAntes =
            TimeSpan.FromDays(30);

        private readonly ConcurrentDictionary<string, DateTimeOffset>
            _sesiones = new(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<string, VentanaIntentos>
            _intentos = new(StringComparer.OrdinalIgnoreCase);
        private readonly AlmacenSesionesMoviles _almacen;

        public SeguridadMovil(
            AlmacenSesionesMoviles? almacen = null)
        {
            _almacen = almacen
                ?? AlmacenSesionesMoviles.Predeterminado;

            foreach ((string hash, DateTimeOffset caducidad)
                     in _almacen.Cargar())
            {
                _sesiones[hash] = caducidad;
            }

            Codigo = RandomNumberGenerator
                .GetInt32(100_000, 1_000_000)
                .ToString();
        }

        public string Codigo { get; }

        public ResultadoEmparejado Emparejar(
            string direccion,
            string? codigo)
        {
            VentanaIntentos intentos =
                _intentos.GetOrAdd(
                    direccion,
                    _ => new VentanaIntentos());

            if (!intentos.Permitir())
            {
                return new ResultadoEmparejado(
                    EstadoEmparejado.Limitado,
                    "Demasiados intentos. Espera un minuto.");
            }

            if (!CoincideCodigo(codigo))
            {
                return new ResultadoEmparejado(
                    EstadoEmparejado.Incorrecto,
                    "El código no es correcto.");
            }

            intentos.Reiniciar();
            LimpiarSesionesCaducadas();

            string token = CrearToken(32);
            string hash = CalcularHash(token);

            _sesiones[hash] =
                DateTimeOffset.UtcNow.Add(DuracionSesion);
            PersistirSesiones();

            return new ResultadoEmparejado(
                EstadoEmparejado.Correcto,
                "Dispositivo emparejado.",
                token);
        }

        public bool Autorizar(HttpContext contexto)
        {
            string cabecera =
                contexto.Request.Headers.Authorization.ToString();

            const string prefijo = "Bearer ";

            if (!cabecera.StartsWith(
                    prefijo,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string token = cabecera[prefijo.Length..].Trim();

            if (token.Length is < 32 or > 100)
            {
                return false;
            }

            string hash = CalcularHash(token);

            if (!_sesiones.TryGetValue(hash, out DateTimeOffset caducidad))
            {
                return false;
            }

            if (caducidad <= DateTimeOffset.UtcNow)
            {
                _sesiones.TryRemove(hash, out _);
                PersistirSesiones();
                return false;
            }

            if (caducidad - DateTimeOffset.UtcNow <= RenovarAntes)
            {
                _sesiones[hash] =
                    DateTimeOffset.UtcNow.Add(DuracionSesion);
                PersistirSesiones();
            }

            return true;
        }

        private bool CoincideCodigo(string? candidato)
        {
            if (candidato?.Length != Codigo.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(candidato),
                Encoding.ASCII.GetBytes(Codigo));
        }

        private void LimpiarSesionesCaducadas()
        {
            DateTimeOffset ahora = DateTimeOffset.UtcNow;
            bool cambio = false;

            foreach ((string hash, DateTimeOffset caducidad) in _sesiones)
            {
                if (caducidad <= ahora)
                {
                    cambio |= _sesiones.TryRemove(hash, out _);
                }
            }

            if (cambio)
            {
                PersistirSesiones();
            }
        }

        private void PersistirSesiones()
        {
            try
            {
                _almacen.Guardar(_sesiones);
            }
            catch (Exception ex) when (
                ex is IOException
                or UnauthorizedAccessException)
            {
                // La sesión actual sigue siendo válida aunque Windows no permita
                // conservarla para el siguiente reinicio.
            }
        }

        private static string CalcularHash(string token)
        {
            return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(token)));
        }
    }

    private sealed class VentanaIntentos
    {
        private static readonly TimeSpan Duracion =
            TimeSpan.FromMinutes(1);

        private const int MaximoIntentos = 5;
        private readonly Queue<DateTimeOffset> _intentos = new();

        public bool Permitir()
        {
            lock (_intentos)
            {
                DateTimeOffset limite =
                    DateTimeOffset.UtcNow.Subtract(Duracion);

                while (_intentos.TryPeek(out DateTimeOffset intento)
                       &&
                       intento < limite)
                {
                    _intentos.Dequeue();
                }

                if (_intentos.Count >= MaximoIntentos)
                {
                    return false;
                }

                _intentos.Enqueue(DateTimeOffset.UtcNow);
                return true;
            }
        }

        public void Reiniciar()
        {
            lock (_intentos)
            {
                _intentos.Clear();
            }
        }
    }

    internal const string ManifiestoPwa = """
        {
          "id": "/",
          "name": "ControlPCIA",
          "short_name": "ControlPCIA",
          "description": "Control por voz de un PC Windows mediante una IA local.",
          "lang": "es-ES",
          "dir": "ltr",
          "start_url": "/?source=pwa",
          "scope": "/",
          "display": "standalone",
          "display_override": ["standalone", "minimal-ui"],
          "orientation": "portrait-primary",
          "background_color": "#090d18",
          "theme_color": "#111827",
          "categories": ["utilities", "productivity"],
          "icons": [
            {
              "src": "/icons/controlpcia.svg",
              "sizes": "192x192",
              "type": "image/svg+xml",
              "purpose": "any"
            },
            {
              "src": "/icons/controlpcia.svg",
              "sizes": "512x512",
              "type": "image/svg+xml",
              "purpose": "any"
            },
            {
              "src": "/icons/controlpcia-maskable.svg",
              "sizes": "512x512",
              "type": "image/svg+xml",
              "purpose": "maskable"
            }
          ],
          "shortcuts": [
            {
              "name": "Nueva orden",
              "short_name": "Nueva orden",
              "description": "Abrir ControlPCIA para enviar una orden al PC.",
              "url": "/?source=shortcut",
              "icons": [
                {
                  "src": "/icons/controlpcia.svg",
                  "sizes": "192x192",
                  "type": "image/svg+xml"
                }
              ]
            }
          ]
        }
        """;

    internal const string ServiceWorkerPwa = """
        const CACHE_VERSION = 'controlpcia-shell-v1';
        const APP_SHELL = [
          '/',
          '/manifest.webmanifest',
          '/icons/controlpcia.svg',
          '/icons/controlpcia-maskable.svg'
        ];

        self.addEventListener('install', event => {
          event.waitUntil(
            caches.open(CACHE_VERSION)
              .then(cache => cache.addAll(APP_SHELL))
              .then(() => self.skipWaiting())
          );
        });

        self.addEventListener('activate', event => {
          event.waitUntil(
            caches.keys()
              .then(keys => Promise.all(
                keys
                  .filter(key => key !== CACHE_VERSION)
                  .map(key => caches.delete(key))
              ))
              .then(() => self.clients.claim())
          );
        });

        self.addEventListener('fetch', event => {
          const request = event.request;
          const url = new URL(request.url);

          if (request.method !== 'GET' || url.origin !== self.location.origin) return;
          if (url.pathname.startsWith('/api/')) return;
          if (url.pathname === '/app-android.apk') return;

          if (request.mode === 'navigate') {
            event.respondWith(
              fetch(request)
                .then(async response => {
                  if (response.ok) {
                    const cache = await caches.open(CACHE_VERSION);
                    await cache.put('/', response.clone());
                  }
                  return response;
                })
                .catch(async () => {
                  const cached = await caches.match('/');
                  return cached || Response.error();
                })
            );
            return;
          }

          event.respondWith(
            caches.match(request)
              .then(cached => cached || fetch(request))
          );
        });
        """;

    internal const string IconoPwa = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" role="img" aria-label="ControlPCIA">
          <defs>
            <linearGradient id="g" x1="80" y1="48" x2="432" y2="464" gradientUnits="userSpaceOnUse">
              <stop stop-color="#A78BFA"/>
              <stop offset="0.48" stop-color="#7C3AED"/>
              <stop offset="1" stop-color="#2563EB"/>
            </linearGradient>
          </defs>
          <rect width="512" height="512" rx="118" fill="#090D18"/>
          <rect x="52" y="52" width="408" height="408" rx="104" fill="url(#g)"/>
          <path d="M156 274c42 0 42-76 84-76s42 116 84 116 42-76 84-76" fill="none" stroke="#fff" stroke-width="38" stroke-linecap="round" stroke-linejoin="round"/>
          <circle cx="156" cy="274" r="22" fill="#fff"/>
          <circle cx="408" cy="238" r="22" fill="#fff"/>
        </svg>
        """;

    internal const string IconoPwaMaskable = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" role="img" aria-label="ControlPCIA">
          <defs>
            <linearGradient id="g" x1="48" y1="32" x2="464" y2="480" gradientUnits="userSpaceOnUse">
              <stop stop-color="#A78BFA"/>
              <stop offset="0.48" stop-color="#7C3AED"/>
              <stop offset="1" stop-color="#2563EB"/>
            </linearGradient>
          </defs>
          <rect width="512" height="512" fill="#090D18"/>
          <circle cx="256" cy="256" r="220" fill="url(#g)"/>
          <path d="M144 274c37 0 37-68 74-68s37 100 74 100 37-68 74-68" fill="none" stroke="#fff" stroke-width="36" stroke-linecap="round" stroke-linejoin="round"/>
          <circle cx="144" cy="274" r="20" fill="#fff"/>
          <circle cx="366" cy="238" r="20" fill="#fff"/>
        </svg>
        """;

    internal const string PaginaMovil = """
        <!doctype html>
        <html lang="es">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
          <meta name="color-scheme" content="dark">
          <meta name="theme-color" content="#111827">
          <meta name="application-name" content="ControlPCIA">
          <meta name="mobile-web-app-capable" content="yes">
          <meta name="apple-mobile-web-app-capable" content="yes">
          <meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
          <meta name="apple-mobile-web-app-title" content="ControlPCIA">
          <link rel="manifest" href="/manifest.webmanifest">
          <link rel="icon" href="/icons/controlpcia.svg" type="image/svg+xml">
          <link rel="apple-touch-icon" href="/icons/controlpcia.svg">
          <title>ControlPCIA</title>
          <style nonce="__NONCE__">
            :root { color-scheme: dark; font-family: system-ui, -apple-system, sans-serif; }
            * { box-sizing: border-box; }
            body { margin: 0; min-height: 100dvh; background: #090d18; color: #edf2ff; }
            main { width: min(100%, 760px); min-height: 100dvh; margin: auto; padding: max(24px, env(safe-area-inset-top)) 18px max(24px, env(safe-area-inset-bottom)); display: grid; align-content: center; gap: 18px; }
            header { display: flex; align-items: center; gap: 12px; }
            .logo { width: 46px; height: 46px; border-radius: 15px; display: grid; place-items: center; background: linear-gradient(145deg, #8b5cf6, #2563eb); font-size: 24px; box-shadow: 0 12px 40px #4f46e555; }
            h1 { margin: 0; font-size: 1.45rem; }
            header p { margin: 3px 0 0; color: #9ca9c8; font-size: .9rem; }
            .card { background: #111827; border: 1px solid #243047; border-radius: 20px; padding: 18px; box-shadow: 0 20px 60px #0006; }
            form { display: grid; gap: 13px; }
            label { color: #c7d2eb; font-size: .92rem; font-weight: 650; }
            input, textarea { width: 100%; border: 1px solid #34415d; border-radius: 14px; background: #080d18; color: #fff; font: inherit; padding: 14px; outline: none; }
            input:focus, textarea:focus { border-color: #8b5cf6; box-shadow: 0 0 0 3px #8b5cf633; }
            input { text-align: center; letter-spacing: .35em; font-size: 1.25rem; }
            textarea { min-height: 132px; resize: vertical; line-height: 1.45; }
            .actions { display: grid; grid-template-columns: auto 1fr; gap: 10px; }
            button { min-height: 50px; border: 0; border-radius: 14px; padding: 0 17px; color: #fff; background: #27344d; font: inherit; font-weight: 750; cursor: pointer; }
            button.primary { background: linear-gradient(135deg, #7c3aed, #2563eb); }
            button:disabled { opacity: .5; cursor: wait; }
            .hint, .state { color: #9ca9c8; font-size: .86rem; line-height: 1.45; margin: 0; }
            .state { min-height: 22px; }
            .state.error { color: #fca5a5; }
            .state.ok { color: #86efac; }
            .state.warn { color: #fcd34d; }
            #result { display: grid; gap: 10px; }
            .step { border: 1px solid #2b3852; border-radius: 14px; padding: 12px; background: #0b1220; }
            .chat-user { background: #12315a; border-color: #315f9e; }
            .chat-assistant { background: #102a25; border-color: #285a4d; }
            .step strong { display: block; margin-bottom: 7px; }
            code, pre { white-space: pre-wrap; overflow-wrap: anywhere; font-family: ui-monospace, SFMono-Regular, Consolas, monospace; font-size: .78rem; }
            code { color: #c4b5fd; }
            pre { color: #b7c4df; margin: 8px 0 0; }
            .install-card { display: flex; align-items: center; gap: 12px; padding: 12px 14px; border: 1px solid #2b3852; border-radius: 16px; background: #0c1424; }
            .install-card button { min-height: 42px; white-space: nowrap; }
            .install-card p { margin: 0; color: #9ca9c8; font-size: .8rem; line-height: 1.4; }
            .download-link { min-height: 42px; border-radius: 14px; padding: 0 17px; display: inline-flex; align-items: center; justify-content: center; color: #fff; background: linear-gradient(135deg, #059669, #047857); font-weight: 750; text-decoration: none; white-space: nowrap; }
            [hidden] { display: none !important; }
          </style>
        </head>
        <body>
          <main>
            <header>
              <div class="logo" aria-hidden="true">⌁</div>
              <div><h1>ControlPCIA</h1><p>Tu voz se convierte en una orden para la IA del PC</p></div>
            </header>

            <section id="installCard" class="install-card">
              <button id="install" type="button">Instalar app</button>
              <p id="installHint">Instálala para abrir ControlPCIA desde la pantalla de inicio.</p>
            </section>

            <section class="install-card">
              <a class="download-link" href="/app-android.apk" download>Descargar app Android</a>
              <p>Instala la aplicación nativa o descarga aquí sus actualizaciones, directamente desde este PC.</p>
            </section>

            <section id="pairCard" class="card">
              <form id="pairForm">
                <label for="code">Código mostrado en el PC</label>
                <input id="code" name="code" inputmode="numeric" autocomplete="one-time-code" pattern="[0-9]{6}" maxlength="6" required>
                <button class="primary" type="submit">Emparejar móvil</button>
                <p id="pairState" class="state" role="status"></p>
              </form>
            </section>

            <section id="controlCard" class="card" hidden>
              <form id="orderForm">
                <label for="order">Habla con la IA del PC</label>
                <textarea id="order" maxlength="1000" placeholder="Por ejemplo: abre la calculadora y colócala a la izquierda" required></textarea>
                <div class="actions">
                  <button id="mic" type="button" aria-label="Dictar orden">🎙 Dictar</button>
                  <button id="send" class="primary" type="submit">Enviar a la IA</button>
                </div>
                <p class="hint">También puedes usar el micrófono del teclado del móvil. La voz solo sirve para escribir el texto; Llama decide los comandos en el PC.</p>
                <p id="controlState" class="state" role="status"></p>
              </form>
              <div id="result" aria-live="polite"></div>
            </section>
          </main>

          <script nonce="__NONCE__">
            const pairCard = document.querySelector('#pairCard');
            const controlCard = document.querySelector('#controlCard');
            const pairForm = document.querySelector('#pairForm');
            const orderForm = document.querySelector('#orderForm');
            const pairState = document.querySelector('#pairState');
            const controlState = document.querySelector('#controlState');
            const order = document.querySelector('#order');
            const mic = document.querySelector('#mic');
            const send = document.querySelector('#send');
            const result = document.querySelector('#result');
            const installCard = document.querySelector('#installCard');
            const installButton = document.querySelector('#install');
            const installHint = document.querySelector('#installHint');
            let token = localStorage.getItem('controlpcia_token') || '';
            let installPrompt = null;
            let conversation = [];

            function showControl(show) {
              pairCard.hidden = show;
              controlCard.hidden = !show;
              if (show) order.focus();
            }

            async function request(path, options = {}) {
              const headers = { 'Content-Type': 'application/json', ...(options.headers || {}) };
              if (token) headers.Authorization = `Bearer ${token}`;
              const response = await fetch(path, { ...options, headers });
              let data = {};
              try { data = await response.json(); } catch { }
              if (response.status === 401 && path !== '/api/emparejar') {
                token = '';
                localStorage.removeItem('controlpcia_token');
                showControl(false);
              }
              if (!response.ok) throw new Error(data.error || `Error ${response.status}`);
              return data;
            }

            function setState(element, message, type = '') {
              element.textContent = message;
              element.className = `state ${type}`;
            }

            pairForm.addEventListener('submit', async event => {
              event.preventDefault();
              const button = pairForm.querySelector('button');
              button.disabled = true;
              setState(pairState, 'Comprobando código…');
              try {
                const data = await request('/api/emparejar', {
                  method: 'POST',
                  body: JSON.stringify({ codigo: document.querySelector('#code').value })
                });
                token = data.token;
                localStorage.setItem('controlpcia_token', token);
                showControl(true);
                setState(controlState, 'Móvil conectado al PC.', 'ok');
              } catch (error) {
                setState(pairState, error.message, 'error');
              } finally {
                button.disabled = false;
              }
            });

            orderForm.addEventListener('submit', async event => {
              event.preventDefault();
              const text = order.value.trim();
              if (!text) return;
              send.disabled = true;
              mic.disabled = true;
              setState(controlState, 'Llama está interpretando la orden…');
              try {
                const context = conversation.slice(-12);
                const data = await request('/api/orden', {
                  method: 'POST',
                  body: JSON.stringify({ texto: text, contexto: context })
                });
                conversation.push(
                  { rol: 'user', texto: text.slice(0, 800) },
                  { rol: 'assistant', texto: (data.mensaje || '').slice(0, 800) }
                );
                conversation = conversation.slice(-12);
                const learned = data.aprendido ? ' Receta guardada en la memoria local.' : '';
                const needsInput =
                  data.estado === 'requiere_confirmacion' ||
                  data.estado === 'requiere_aclaracion';
                setState(
                  controlState,
                  data.mensaje + learned,
                  needsInput ? 'warn' : (data.completado ? 'ok' : 'error'));
                const userBox = document.createElement('div');
                userBox.className = 'step chat-user';
                const userTitle = document.createElement('strong');
                userTitle.textContent = 'Tú';
                const userText = document.createElement('div');
                userText.textContent = text;
                userBox.append(userTitle, userText);
                const assistantBox = document.createElement('div');
                assistantBox.className = 'step chat-assistant';
                const assistantTitle = document.createElement('strong');
                assistantTitle.textContent = 'IA';
                const assistantText = document.createElement('div');
                assistantText.textContent = data.mensaje || 'Sin respuesta.';
                assistantBox.append(assistantTitle, assistantText);
                result.append(userBox, assistantBox);
                for (const step of data.pasos || []) {
                  const box = document.createElement('div');
                  box.className = 'step';
                  const title = document.createElement('strong');
                  title.textContent = `Paso ${step.numero} · ${step.ejecutado ? 'ejecutado' : 'bloqueado'}`;
                  const command = document.createElement('code');
                  command.textContent = step.comando;
                  box.append(title, command);
                  const output = [step.salida, step.error].filter(Boolean).join('\n');
                  if (output) {
                    const pre = document.createElement('pre');
                    pre.textContent = output;
                    box.append(pre);
                  }
                  result.append(box);
                }
                while (result.children.length > 30) result.firstElementChild.remove();
                order.value = '';
              } catch (error) {
                setState(controlState, error.message, 'error');
              } finally {
                send.disabled = false;
                mic.disabled = false;
              }
            });

            const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
            if (!SpeechRecognition) {
              mic.addEventListener('click', () => {
                order.focus();
                setState(controlState, 'Usa el micrófono del teclado del móvil para dictar.', 'error');
              });
            } else {
              const recognition = new SpeechRecognition();
              recognition.lang = 'es-ES';
              recognition.interimResults = false;
              recognition.continuous = false;
              recognition.onstart = () => setState(controlState, 'Escuchando…');
              recognition.onresult = event => {
                order.value = event.results[0][0].transcript;
                setState(controlState, 'Orden transcrita. Revísala y envíala.', 'ok');
              };
              recognition.onerror = () => {
                order.focus();
                setState(controlState, 'No se pudo usar el dictado del navegador. Usa el micrófono del teclado.', 'error');
              };
              mic.addEventListener('click', () => recognition.start());
            }

            showControl(Boolean(token));
            if (token) {
              request('/api/estado')
                .then(data => setState(controlState, data.mensaje, data.disponible ? 'ok' : 'error'))
                .catch(error => setState(controlState, error.message, 'error'));
            }

            const standalone = window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
            if (standalone) installCard.hidden = true;

            window.addEventListener('beforeinstallprompt', event => {
              event.preventDefault();
              installPrompt = event;
              installHint.textContent = 'ControlPCIA está lista para instalarse en este dispositivo.';
            });

            window.addEventListener('appinstalled', () => {
              installPrompt = null;
              installCard.hidden = true;
            });

            installButton.addEventListener('click', async () => {
              if (installPrompt) {
                installPrompt.prompt();
                await installPrompt.userChoice;
                installPrompt = null;
                return;
              }

              installHint.textContent = 'En el menú del navegador elige “Añadir a pantalla de inicio” o “Instalar aplicación”.';
            });

            if ('serviceWorker' in navigator) {
              window.addEventListener('load', async () => {
                try {
                  const registration = await navigator.serviceWorker.register('/sw.js', { scope: '/' });
                  await registration.update();
                } catch {
                  installHint.textContent = 'Para la instalación completa el navegador debe considerar segura esta dirección. Puedes añadir un acceso desde su menú.';
                }
              });
            } else {
              installHint.textContent = 'Este navegador no admite instalación PWA; puedes añadir un acceso a la pantalla de inicio.';
            }
          </script>
        </body>
        </html>
        """;
}
