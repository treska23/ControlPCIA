using System.Globalization;
using System.Text.Json;
using Windows.Media;
using Windows.Media.Control;

namespace ControlPCIA;

internal enum AccionComandoMultimedia
{
    Listar,
    Estado,
    Reproducir,
    Pausar,
    Alternar,
    Detener,
    Siguiente,
    Anterior,
    Avanzar,
    Retroceder,
    Desplazar,
    Aleatorio,
    Repeticion,
    Velocidad
}

internal sealed record OpcionesComandoMultimedia(
    AccionComandoMultimedia Accion,
    string Aplicacion = "",
    double? Valor = null,
    string Modo = "");

/// <summary>
/// Controla sesiones multimedia publicadas por las aplicaciones en el
/// transporte multimedia global de Windows. No simula teclas ni inspecciona
/// el contenido de ninguna ventana.
/// </summary>
internal static class ComandoMultimedia
{
    public static async Task<int> EjecutarAsync(
        IReadOnlyList<string> argumentos,
        TextWriter salida,
        TextWriter error)
    {
        if (!TryAnalizar(
                argumentos,
                out OpcionesComandoMultimedia? opciones,
                out string errorArgumentos))
        {
            await error.WriteLineAsync(errorArgumentos);
            return 2;
        }

        try
        {
            GlobalSystemMediaTransportControlsSessionManager manager =
                await GlobalSystemMediaTransportControlsSessionManager
                    .RequestAsync();
            IReadOnlyList<
                GlobalSystemMediaTransportControlsSession> sesiones =
                    manager.GetSessions().ToArray();

            if (opciones!.Accion
                == AccionComandoMultimedia.Listar)
            {
                object[] elementos =
                    await Task.WhenAll(
                        sesiones.Select(
                            CrearResumenAsync));
                await salida.WriteLineAsync(
                    JsonSerializer.Serialize(
                        new
                        {
                            correcto = true,
                            detalle =
                                "Consulta de sesiones multimedia completada.",
                            sesiones = elementos
                        }));
                return 0;
            }

            GlobalSystemMediaTransportControlsSession? sesion =
                SeleccionarSesion(
                    manager,
                    sesiones,
                    opciones.Aplicacion);

            if (sesion is null)
            {
                await error.WriteLineAsync(
                    JsonSerializer.Serialize(
                        new
                        {
                            correcto = false,
                            detalle = string.IsNullOrWhiteSpace(
                                opciones.Aplicacion)
                                ? "Windows no tiene ninguna sesión multimedia activa."
                                : $"Windows no encuentra una sesión multimedia de «{opciones.Aplicacion}»."
                        }));
                return 4;
            }

            if (opciones.Accion
                == AccionComandoMultimedia.Estado)
            {
                await salida.WriteLineAsync(
                    JsonSerializer.Serialize(
                        new
                        {
                            correcto = true,
                            detalle =
                                "Consulta multimedia completada.",
                            sesion =
                                await CrearResumenAsync(
                                    sesion)
                        }));
                return 0;
            }

            bool correcto =
                await EjecutarAccionAsync(
                    sesion,
                    opciones);
            object resumen =
                await CrearResumenAsync(sesion);
            TextWriter destino =
                correcto ? salida : error;
            await destino.WriteLineAsync(
                JsonSerializer.Serialize(
                    new
                    {
                        correcto,
                        detalle = correcto
                            ? "La aplicación multimedia aceptó la orden."
                            : "La sesión existe, pero la aplicación no admite o rechazó esa orden multimedia.",
                        sesion = resumen
                    }));
            return correcto ? 0 : 4;
        }
        catch (UnauthorizedAccessException)
        {
            await error.WriteLineAsync(
                JsonSerializer.Serialize(
                    new
                    {
                        correcto = false,
                        detalle =
                            "Windows no ha concedido acceso al control multimedia global."
                    }));
            return 4;
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync(
                JsonSerializer.Serialize(
                    new
                    {
                        correcto = false,
                        detalle =
                            "No se pudo usar el control multimedia de Windows: "
                            + ex.Message
                    }));
            return 4;
        }
    }

    internal static bool TryAnalizar(
        IReadOnlyList<string> argumentos,
        out OpcionesComandoMultimedia? opciones,
        out string error)
    {
        opciones = null;

        if (argumentos.Count == 0)
        {
            error =
                "Uso: ControlPCIA.exe media list|status|play|pause|toggle|stop|next|previous|forward|rewind|seek|shuffle|repeat|rate [--app aplicación].";
            return false;
        }

        string accion = argumentos[0].ToLowerInvariant();
        string aplicacion = string.Empty;
        double? valor = null;
        string modo = string.Empty;
        var restantes = new List<string>();

        for (int indice = 1;
             indice < argumentos.Count;
             indice++)
        {
            if (argumentos[indice].Equals(
                    "--app",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (indice + 1 >= argumentos.Count)
                {
                    error = "--app necesita un nombre.";
                    return false;
                }

                aplicacion = argumentos[++indice].Trim();
            }
            else
            {
                restantes.Add(argumentos[indice]);
            }
        }

        if (accion == "list")
        {
            if (restantes.Count > 0
                || aplicacion.Length > 0)
            {
                error = "media list no admite más argumentos.";
                return false;
            }

            opciones = new OpcionesComandoMultimedia(
                AccionComandoMultimedia.Listar);
            error = string.Empty;
            return true;
        }

        AccionComandoMultimedia? tipo = accion switch
        {
            "status" => AccionComandoMultimedia.Estado,
            "play" => AccionComandoMultimedia.Reproducir,
            "pause" => AccionComandoMultimedia.Pausar,
            "toggle" => AccionComandoMultimedia.Alternar,
            "stop" => AccionComandoMultimedia.Detener,
            "next" => AccionComandoMultimedia.Siguiente,
            "previous" => AccionComandoMultimedia.Anterior,
            "forward" => AccionComandoMultimedia.Avanzar,
            "rewind" => AccionComandoMultimedia.Retroceder,
            "seek" => AccionComandoMultimedia.Desplazar,
            "shuffle" => AccionComandoMultimedia.Aleatorio,
            "repeat" => AccionComandoMultimedia.Repeticion,
            "rate" => AccionComandoMultimedia.Velocidad,
            _ => null
        };

        if (tipo is null)
        {
            error =
                $"Acción multimedia no reconocida: {argumentos[0]}";
            return false;
        }

        switch (tipo)
        {
            case AccionComandoMultimedia.Desplazar:
            case AccionComandoMultimedia.Velocidad:
                if (restantes.Count != 1
                    || !double.TryParse(
                        restantes[0],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double numero))
                {
                    error = tipo
                            == AccionComandoMultimedia.Desplazar
                        ? "Uso: media seek <segundos relativos> [--app aplicación]."
                        : "Uso: media rate <velocidad> [--app aplicación].";
                    return false;
                }

                if (tipo
                    == AccionComandoMultimedia.Velocidad
                    && numero <= 0)
                {
                    error =
                        "La velocidad debe ser mayor que cero.";
                    return false;
                }

                valor = numero;
                break;

            case AccionComandoMultimedia.Aleatorio:
                if (restantes.Count != 1
                    || restantes[0].ToLowerInvariant()
                    is not ("on" or "off"))
                {
                    error =
                        "Uso: media shuffle on|off [--app aplicación].";
                    return false;
                }

                modo = restantes[0].ToLowerInvariant();
                break;

            case AccionComandoMultimedia.Repeticion:
                if (restantes.Count != 1
                    || restantes[0].ToLowerInvariant()
                    is not ("off" or "track" or "list"))
                {
                    error =
                        "Uso: media repeat off|track|list [--app aplicación].";
                    return false;
                }

                modo = restantes[0].ToLowerInvariant();
                break;

            default:
                if (restantes.Count > 0)
                {
                    error =
                        $"media {accion} no admite esos argumentos.";
                    return false;
                }

                break;
        }

        opciones = new OpcionesComandoMultimedia(
            tipo.Value,
            aplicacion,
            valor,
            modo);
        error = string.Empty;
        return true;
    }

    private static async Task<bool> EjecutarAccionAsync(
        GlobalSystemMediaTransportControlsSession sesion,
        OpcionesComandoMultimedia opciones)
    {
        switch (opciones.Accion)
        {
            case AccionComandoMultimedia.Reproducir:
                return await sesion.TryPlayAsync();
            case AccionComandoMultimedia.Pausar:
                return await sesion.TryPauseAsync();
            case AccionComandoMultimedia.Alternar:
                return await sesion.TryTogglePlayPauseAsync();
            case AccionComandoMultimedia.Detener:
                return await sesion.TryStopAsync();
            case AccionComandoMultimedia.Siguiente:
                return await sesion.TrySkipNextAsync();
            case AccionComandoMultimedia.Anterior:
                return await sesion.TrySkipPreviousAsync();
            case AccionComandoMultimedia.Avanzar:
                return await sesion.TryFastForwardAsync();
            case AccionComandoMultimedia.Retroceder:
                return await sesion.TryRewindAsync();
            case AccionComandoMultimedia.Desplazar:
                GlobalSystemMediaTransportControlsSessionTimelineProperties
                    tiempo = sesion.GetTimelineProperties();
                TimeSpan posicion =
                    tiempo.Position
                    + TimeSpan.FromSeconds(
                        opciones.Valor!.Value);
                posicion = posicion < tiempo.StartTime
                    ? tiempo.StartTime
                    : posicion > tiempo.EndTime
                        ? tiempo.EndTime
                        : posicion;
                return await sesion
                    .TryChangePlaybackPositionAsync(
                        posicion.Ticks);
            case AccionComandoMultimedia.Aleatorio:
                return await sesion
                    .TryChangeShuffleActiveAsync(
                        opciones.Modo == "on");
            case AccionComandoMultimedia.Repeticion:
                MediaPlaybackAutoRepeatMode repeticion =
                    opciones.Modo switch
                    {
                        "track" =>
                            MediaPlaybackAutoRepeatMode.Track,
                        "list" =>
                            MediaPlaybackAutoRepeatMode.List,
                        _ =>
                            MediaPlaybackAutoRepeatMode.None
                    };
                return await sesion
                    .TryChangeAutoRepeatModeAsync(
                        repeticion);
            case AccionComandoMultimedia.Velocidad:
                return await sesion
                    .TryChangePlaybackRateAsync(
                        opciones.Valor!.Value);
            default:
                return false;
        }
    }

    private static GlobalSystemMediaTransportControlsSession?
        SeleccionarSesion(
            GlobalSystemMediaTransportControlsSessionManager manager,
            IReadOnlyList<
                GlobalSystemMediaTransportControlsSession> sesiones,
            string aplicacion)
    {
        if (string.IsNullOrWhiteSpace(aplicacion))
        {
            return manager.GetCurrentSession()
                   ?? sesiones.FirstOrDefault(
                       EstaReproduciendo)
                   ?? sesiones.FirstOrDefault();
        }

        string objetivo =
            Normalizar(aplicacion);
        IEnumerable<
            GlobalSystemMediaTransportControlsSession> candidatas =
                sesiones.Where(
                    sesion =>
                    {
                        string origen =
                            Normalizar(
                                sesion.SourceAppUserModelId);
                        return objetivo == "browser"
                            ? EsNavegador(origen)
                            : origen.Contains(
                                objetivo,
                                StringComparison.Ordinal);
                    });
        return candidatas.FirstOrDefault(EstaReproduciendo)
               ?? candidatas.FirstOrDefault();
    }

    private static bool EstaReproduciendo(
        GlobalSystemMediaTransportControlsSession sesion)
    {
        return sesion.GetPlaybackInfo().PlaybackStatus
               == GlobalSystemMediaTransportControlsSessionPlaybackStatus
                   .Playing;
    }

    private static bool EsNavegador(
        string origen)
    {
        return new[]
            {
                "msedge",
                "chrome",
                "firefox",
                "brave",
                "opera",
                "vivaldi"
            }
            .Any(origen.Contains);
    }

    private static async Task<object> CrearResumenAsync(
        GlobalSystemMediaTransportControlsSession sesion)
    {
        GlobalSystemMediaTransportControlsSessionMediaProperties?
            propiedades = null;

        try
        {
            propiedades =
                await sesion.TryGetMediaPropertiesAsync();
        }
        catch
        {
            // Algunas aplicaciones publican controles, pero no metadatos.
        }

        GlobalSystemMediaTransportControlsSessionPlaybackInfo
            reproduccion = sesion.GetPlaybackInfo();
        GlobalSystemMediaTransportControlsSessionTimelineProperties
            tiempo = sesion.GetTimelineProperties();
        GlobalSystemMediaTransportControlsSessionPlaybackControls
            controles = reproduccion.Controls;
        return new
        {
            aplicacion = sesion.SourceAppUserModelId,
            titulo = propiedades?.Title ?? string.Empty,
            artista = propiedades?.Artist ?? string.Empty,
            album = propiedades?.AlbumTitle ?? string.Empty,
            estado =
                reproduccion.PlaybackStatus.ToString(),
            posicionSegundos =
                Math.Round(
                    tiempo.Position.TotalSeconds,
                    1),
            duracionSegundos =
                Math.Round(
                    (tiempo.EndTime - tiempo.StartTime)
                    .TotalSeconds,
                    1),
            permite = new
            {
                reproducir =
                    controles.IsPlayEnabled,
                pausar =
                    controles.IsPauseEnabled,
                alternar =
                    controles.IsPlayPauseToggleEnabled,
                detener =
                    controles.IsStopEnabled,
                siguiente =
                    controles.IsNextEnabled,
                anterior =
                    controles.IsPreviousEnabled,
                posicion =
                    controles.IsPlaybackPositionEnabled,
                velocidad =
                    controles.IsPlaybackRateEnabled,
                aleatorio =
                    controles.IsShuffleEnabled,
                repeticion =
                    controles.IsRepeatEnabled
            }
        };
    }

    private static string Normalizar(
        string texto)
    {
        return ControlBasico
            .Normalizar(texto)
            .Replace(
                " ",
                string.Empty,
                StringComparison.Ordinal);
    }
}
