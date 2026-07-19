using Microsoft.Maui.ApplicationModel;

#if ANDROID
using Android.Content;
using Android.OS;
using Android.Speech;
#endif

namespace ControlPCIA.Mobile.Servicios;

public enum FaseReconocimientoVoz
{
    Preparando,
    Preparado,
    VozDetectada,
    TextoParcial,
    Transcribiendo
}

public sealed record EstadoReconocimientoVoz(
    FaseReconocimientoVoz Fase,
    string? Texto = null);

public sealed class SesionReconocimientoVoz : IAsyncDisposable
{
    private readonly Func<Task> _detener;
    private readonly Func<Task> _cancelar;
    private readonly Func<Task> _liberar;
    private readonly object _sincronizacionLiberacion = new();
    private Task? _tareaLiberacion;

    internal SesionReconocimientoVoz(
        Task<string> resultado,
        Func<Task> detener,
        Func<Task> cancelar,
        Func<Task> liberar)
    {
        Resultado = resultado;
        _detener = detener;
        _cancelar = cancelar;
        _liberar = liberar;
    }

    public Task<string> Resultado { get; }

    public Task DetenerAsync()
    {
        return _detener();
    }

    public Task CancelarAsync()
    {
        return _cancelar();
    }

    public ValueTask DisposeAsync()
    {
        Task tarea;

        lock (_sincronizacionLiberacion)
        {
            _tareaLiberacion ??= _liberar();
            tarea = _tareaLiberacion;
        }

        return new ValueTask(tarea);
    }
}

public static class ReconocimientoVoz
{
#if ANDROID
    private static readonly object SincronizacionOyentesEnDrenaje =
        new();
    private static readonly HashSet<OyenteReconocimiento>
        OyentesEnDrenaje = [];
#endif

    public static async Task<SesionReconocimientoVoz> IniciarAsync(
        bool escuchaBloqueada,
        Action<EstadoReconocimientoVoz>? alCambiarEstado = null,
        CancellationToken cancellationToken = default)
    {
        alCambiarEstado?.Invoke(
            new EstadoReconocimientoVoz(
                FaseReconocimientoVoz.Preparando));

        PermissionStatus permiso =
            await Permissions.RequestAsync<Permissions.Microphone>();

        if (permiso != PermissionStatus.Granted)
        {
            throw new InvalidOperationException(
                "Activa el permiso de microfono para hablar al PC.");
        }

#if ANDROID
        return await IniciarAndroidAsync(
            escuchaBloqueada,
            alCambiarEstado,
            cancellationToken);
#else
        throw new PlatformNotSupportedException(
            "En este dispositivo usa el microfono del teclado para dictar.");
#endif
    }

#if ANDROID
    private static async Task<SesionReconocimientoVoz> IniciarAndroidAsync(
        bool escuchaBloqueada,
        Action<EstadoReconocimientoVoz>? alCambiarEstado,
        CancellationToken cancellationToken)
    {
        Context contexto = Android.App.Application.Context;

        if (!SpeechRecognizer.IsRecognitionAvailable(contexto))
        {
            throw new InvalidOperationException(
                "Este movil no tiene un servicio de reconocimiento de voz disponible.");
        }

        SpeechRecognizer? reconocedor = null;
        OyenteReconocimiento? oyente = null;
        Intent? intencion = null;
        CancellationTokenRegistration registroCancelacion = default;
        var resultado =
            new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var acumulador = new AcumuladorDictado();
        int detencionSolicitada = 0;
        int cancelacionSolicitada = 0;
        int cancelacionNativaSolicitada = 0;
        int reinicioEnCurso = 0;

        void Publicar(EstadoReconocimientoVoz estado)
        {
            if (alCambiarEstado is null)
            {
                return;
            }

            MainThread.BeginInvokeOnMainThread(
                () => alCambiarEstado(estado));
        }

        void CompletarConTexto(string? ultimoSegmento = null)
        {
            if (!string.IsNullOrWhiteSpace(ultimoSegmento))
            {
                acumulador.ConfirmarSegmento(ultimoSegmento);
            }

            string texto = acumulador.ObtenerTexto();

            if (string.IsNullOrWhiteSpace(texto))
            {
                resultado.TrySetException(
                    new InvalidOperationException(
                        "No te he entendido. Repítelo, por favor."));
                return;
            }

            resultado.TrySetResult(texto);
        }

        async Task ReiniciarEscuchaAsync()
        {
            if (!escuchaBloqueada
                || resultado.Task.IsCompleted
                || Volatile.Read(ref detencionSolicitada) != 0
                || Volatile.Read(ref cancelacionSolicitada) != 0
                || Interlocked.Exchange(ref reinicioEnCurso, 1) != 0)
            {
                return;
            }

            try
            {
                await Task.Delay(200);

                if (resultado.Task.IsCompleted
                    || Volatile.Read(ref detencionSolicitada) != 0
                    || Volatile.Read(ref cancelacionSolicitada) != 0)
                {
                    return;
                }

                Publicar(
                    new EstadoReconocimientoVoz(
                        FaseReconocimientoVoz.Preparando,
                        acumulador.ObtenerTexto()));

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (reconocedor is not null
                        && intencion is not null
                        && !resultado.Task.IsCompleted
                        && Volatile.Read(ref detencionSolicitada) == 0
                        && Volatile.Read(ref cancelacionSolicitada) == 0)
                    {
                        reconocedor.StartListening(intencion);
                    }
                });
            }
            catch (Exception ex)
            {
                if (Volatile.Read(ref detencionSolicitada) == 0
                    && Volatile.Read(ref cancelacionSolicitada) == 0)
                {
                    resultado.TrySetException(
                        new InvalidOperationException(
                            "No se pudo mantener abierta la escucha del móvil.",
                            ex));
                }
            }
            finally
            {
                Interlocked.Exchange(ref reinicioEnCurso, 0);
            }
        }

        void AlRecibirResultado(string? texto)
        {
            acumulador.ConfirmarSegmento(texto);

            if (escuchaBloqueada
                && Volatile.Read(ref detencionSolicitada) == 0
                && Volatile.Read(ref cancelacionSolicitada) == 0)
            {
                string acumulado = acumulador.ObtenerTexto();

                if (acumulado.Length > 0)
                {
                    Publicar(
                        new EstadoReconocimientoVoz(
                            FaseReconocimientoVoz.TextoParcial,
                            acumulado));
                }

                _ = ReiniciarEscuchaAsync();
                return;
            }

            CompletarConTexto();
        }

        void AlRecibirParcial(string? texto)
        {
            acumulador.ActualizarParcial(texto);
            string acumulado = acumulador.ObtenerTexto();

            if (acumulado.Length > 0)
            {
                Publicar(
                    new EstadoReconocimientoVoz(
                        FaseReconocimientoVoz.TextoParcial,
                        acumulado));
            }
        }

        void AlRecibirError(SpeechRecognizerError error)
        {
            if (resultado.Task.IsCompleted)
            {
                return;
            }

            bool deteniendo =
                Volatile.Read(ref detencionSolicitada) != 0;

            if (escuchaBloqueada && !deteniendo)
            {
                acumulador.ConfirmarParcial();

                if (error is SpeechRecognizerError.NoMatch
                    or SpeechRecognizerError.SpeechTimeout
                    or SpeechRecognizerError.RecognizerBusy)
                {
                    _ = ReiniciarEscuchaAsync();
                    return;
                }
            }

            if (deteniendo)
            {
                acumulador.ConfirmarParcial();
                CompletarConTexto();
                return;
            }

            resultado.TrySetException(
                new InvalidOperationException(
                    ObtenerMensajeError(error)));
        }

        async Task CompletarDetencionTrasEsperaAsync()
        {
            await Task.Delay(2_000);

            if (resultado.Task.IsCompleted
                || Volatile.Read(ref cancelacionSolicitada) != 0)
            {
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(
                () => reconocedor?.Cancel());
            acumulador.ConfirmarParcial();
            CompletarConTexto();
        }

        async Task DetenerInternamenteAsync()
        {
            if (resultado.Task.IsCompleted
                || Interlocked.Exchange(ref detencionSolicitada, 1) != 0)
            {
                return;
            }

            Publicar(
                new EstadoReconocimientoVoz(
                    FaseReconocimientoVoz.Transcribiendo,
                    acumulador.ObtenerTexto()));

            try
            {
                await MainThread.InvokeOnMainThreadAsync(
                    () => reconocedor?.StopListening());
            }
            catch
            {
                acumulador.ConfirmarParcial();
                CompletarConTexto();
                return;
            }

            _ = CompletarDetencionTrasEsperaAsync();
        }

        async Task CancelarInternamenteAsync()
        {
            Interlocked.Exchange(ref cancelacionSolicitada, 1);
            Interlocked.Exchange(ref detencionSolicitada, 1);
            resultado.TrySetCanceled();

            if (Interlocked.Exchange(
                    ref cancelacionNativaSolicitada,
                    1)
                != 0)
            {
                return;
            }

            try
            {
                await MainThread.InvokeOnMainThreadAsync(
                    () => reconocedor?.Cancel());
            }
            catch
            {
                // Android puede haber destruido el reconocedor justo antes de
                // recibir Cancel. El resultado ya está cancelado.
            }
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                reconocedor =
                    SpeechRecognizer.CreateSpeechRecognizer(contexto);
                oyente = new OyenteReconocimiento(
                    AlRecibirResultado,
                    AlRecibirError,
                    AlRecibirParcial,
                    Publicar);
                reconocedor!.SetRecognitionListener(oyente);

                intencion =
                    new Intent(RecognizerIntent.ActionRecognizeSpeech);
                intencion.PutExtra(
                    RecognizerIntent.ExtraLanguageModel,
                    RecognizerIntent.LanguageModelFreeForm);
                intencion.PutExtra(
                    RecognizerIntent.ExtraLanguage,
                    "es-ES");
                intencion.PutExtra(
                    RecognizerIntent.ExtraLanguagePreference,
                    "es-ES");
                intencion.PutExtra(
                    RecognizerIntent.ExtraPartialResults,
                    true);
                intencion.PutExtra(
                    RecognizerIntent.ExtraMaxResults,
                    5);
                intencion.PutExtra(
                    RecognizerIntent.ExtraSpeechInputMinimumLengthMillis,
                    1_000L);
                intencion.PutExtra(
                    RecognizerIntent.ExtraSpeechInputCompleteSilenceLengthMillis,
                    escuchaBloqueada ? 1_800_000L : 4_000L);
                intencion.PutExtra(
                    RecognizerIntent.ExtraSpeechInputPossiblyCompleteSilenceLengthMillis,
                    escuchaBloqueada ? 1_800_000L : 2_000L);

                reconocedor.StartListening(intencion);
            });
        }
        catch
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                reconocedor?.Destroy();
                reconocedor?.Dispose();
                oyente?.Dispose();
            });
            throw;
        }

        if (cancellationToken.CanBeCanceled)
        {
            registroCancelacion = cancellationToken.Register(
                () => _ = CancelarInternamenteAsync());
        }

        return new SesionReconocimientoVoz(
            resultado.Task,
            detener: DetenerInternamenteAsync,
            cancelar: CancelarInternamenteAsync,
            liberar: async () =>
            {
                Interlocked.Exchange(ref cancelacionSolicitada, 1);
                registroCancelacion.Dispose();

                if (oyente is not null)
                {
                    ConservarOyenteDuranteDrenaje(oyente);
                }

                try
                {
                    if (!resultado.Task.IsCompleted
                        && Interlocked.Exchange(
                               ref cancelacionNativaSolicitada,
                               1)
                           == 0)
                    {
                        await MainThread.InvokeOnMainThreadAsync(
                            () => reconocedor?.Cancel());
                    }

                    // Android puede dejar callbacks del reconocimiento ya
                    // encolados después de Cancel. Damos tiempo a que lleguen
                    // antes de destruir el reconocedor y mantenemos vivo el
                    // listener durante todo el drenaje.
                    await Task.Delay(250);

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        reconocedor?.SetRecognitionListener(null);
                        reconocedor?.Destroy();
                        reconocedor?.Dispose();
                        reconocedor = null;
                    });
                }
                catch
                {
                    // Una carrera con el ciclo de vida de Android no debe
                    // propagarse a la interfaz ni cerrar la aplicación.
                }
            });
    }

    private static void ConservarOyenteDuranteDrenaje(
        OyenteReconocimiento oyente)
    {
        lock (SincronizacionOyentesEnDrenaje)
        {
            OyentesEnDrenaje.Add(oyente);
        }

        _ = LiberarOyenteTrasDrenajeAsync(oyente);
    }

    private static async Task LiberarOyenteTrasDrenajeAsync(
        OyenteReconocimiento oyente)
    {
        // Algunos servicios de voz envían callbacks tardíos aun después de
        // Cancel/Destroy. Mantener el peer administrado evita que Android
        // intente reactivarlo desde un handle ya liberado.
        await Task.Delay(TimeSpan.FromSeconds(5));

        lock (SincronizacionOyentesEnDrenaje)
        {
            OyentesEnDrenaje.Remove(oyente);
        }
    }

    private sealed class OyenteReconocimiento(
        Action<string?> alRecibirResultado,
        Action<SpeechRecognizerError> alRecibirError,
        Action<string?> alRecibirParcial,
        Action<EstadoReconocimientoVoz> publicar)
        : Java.Lang.Object,
          IRecognitionListener
    {
        public void OnResults(Bundle? resultados)
        {
            alRecibirResultado(ObtenerPrimerTexto(resultados));
        }

        public void OnError(SpeechRecognizerError error)
        {
            alRecibirError(error);
        }

        public void OnBeginningOfSpeech()
        {
            publicar(
                new EstadoReconocimientoVoz(
                    FaseReconocimientoVoz.VozDetectada));
        }

        public void OnEndOfSpeech()
        {
            publicar(
                new EstadoReconocimientoVoz(
                    FaseReconocimientoVoz.Transcribiendo));
        }

        public void OnPartialResults(Bundle? partialResults)
        {
            alRecibirParcial(ObtenerPrimerTexto(partialResults));
        }

        public void OnReadyForSpeech(Bundle? parameters)
        {
            publicar(
                new EstadoReconocimientoVoz(
                    FaseReconocimientoVoz.Preparado));
        }

        public void OnBufferReceived(byte[]? buffer)
        {
        }

        public void OnEndOfSegmentedSession()
        {
        }

        public void OnEvent(int eventType, Bundle? parameters)
        {
        }

        public void OnLanguageDetection(Bundle? results)
        {
        }

        public void OnRmsChanged(float rmsdB)
        {
        }

        public void OnSegmentResults(Bundle? segmentResults)
        {
        }

        private static string? ObtenerPrimerTexto(Bundle? resultados)
        {
            return resultados?
                .GetStringArrayList(SpeechRecognizer.ResultsRecognition)?
                .FirstOrDefault();
        }
    }

    private static string ObtenerMensajeError(
        SpeechRecognizerError error)
    {
        return error switch
        {
            SpeechRecognizerError.NoMatch =>
                "No te he entendido. Repítelo, por favor.",
            SpeechRecognizerError.SpeechTimeout =>
                "No te he oído. Vuelve a tocar el botón y repítelo, por favor.",
            SpeechRecognizerError.InsufficientPermissions =>
                "El móvil no tiene permiso para usar el micrófono.",
            SpeechRecognizerError.Network or
            SpeechRecognizerError.NetworkTimeout =>
                "El reconocimiento de voz del móvil no tiene conexión.",
            SpeechRecognizerError.RecognizerBusy =>
                "El micrófono ya está ocupado. Espera un momento y vuelve a intentarlo.",
            _ =>
                "No se pudo usar el reconocimiento de voz del móvil."
        };
    }

#endif
}
