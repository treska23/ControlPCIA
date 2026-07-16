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
    private int _liberada;

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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _liberada, 1) == 0)
        {
            await _liberar();
        }
    }
}

public static class ReconocimientoVoz
{
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
        CancellationTokenRegistration registroCancelacion = default;
        var resultado =
            new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        void Publicar(EstadoReconocimientoVoz estado)
        {
            if (alCambiarEstado is null)
            {
                return;
            }

            MainThread.BeginInvokeOnMainThread(
                () => alCambiarEstado(estado));
        }

        async Task CancelarInternamenteAsync()
        {
            resultado.TrySetCanceled();
            await MainThread.InvokeOnMainThreadAsync(
                () => reconocedor?.Cancel());
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                reconocedor =
                    SpeechRecognizer.CreateSpeechRecognizer(contexto);
                oyente = new OyenteReconocimiento(resultado, Publicar);
                reconocedor!.SetRecognitionListener(oyente);

                var intencion =
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
            detener: async () =>
            {
                if (!resultado.Task.IsCompleted)
                {
                    Publicar(
                        new EstadoReconocimientoVoz(
                            FaseReconocimientoVoz.Transcribiendo));
                    await MainThread.InvokeOnMainThreadAsync(
                        () => reconocedor?.StopListening());
                }
            },
            cancelar: CancelarInternamenteAsync,
            liberar: async () =>
            {
                registroCancelacion.Dispose();
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    reconocedor?.Cancel();
                    reconocedor?.Destroy();
                    reconocedor?.Dispose();
                    oyente?.Dispose();
                });
            });
    }

    private sealed class OyenteReconocimiento(
        TaskCompletionSource<string> resultado,
        Action<EstadoReconocimientoVoz> publicar)
        : Java.Lang.Object,
          IRecognitionListener
    {
        public void OnResults(Bundle? resultados)
        {
            string? texto = ObtenerPrimerTexto(resultados);

            if (string.IsNullOrWhiteSpace(texto))
            {
                resultado.TrySetException(
                    new InvalidOperationException(
                        "No te he entendido. Repítelo, por favor."));
                return;
            }

            resultado.TrySetResult(texto.Trim());
        }

        public void OnError(SpeechRecognizerError error)
        {
            string mensaje = error switch
            {
                SpeechRecognizerError.NoMatch =>
                    "No te he entendido. Repítelo, por favor.",
                SpeechRecognizerError.SpeechTimeout =>
                    "No te he oído. Vuelve a tocar el botón y repítelo, por favor.",
                SpeechRecognizerError.InsufficientPermissions =>
                    "El movil no tiene permiso para usar el microfono.",
                SpeechRecognizerError.Network or
                SpeechRecognizerError.NetworkTimeout =>
                    "El reconocimiento de voz del movil no tiene conexion.",
                SpeechRecognizerError.RecognizerBusy =>
                    "El microfono ya esta ocupado. Espera un momento y vuelve a intentarlo.",
                _ =>
                    "No se pudo usar el reconocimiento de voz del movil."
            };

            resultado.TrySetException(
                new InvalidOperationException(mensaje));
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
            string? texto = ObtenerPrimerTexto(partialResults);

            if (!string.IsNullOrWhiteSpace(texto))
            {
                publicar(
                    new EstadoReconocimientoVoz(
                        FaseReconocimientoVoz.TextoParcial,
                        texto.Trim()));
            }
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
#endif
}
