using Microsoft.Maui.ApplicationModel;

#if ANDROID
using Android.Content;
using Android.OS;
using Android.Speech;
#endif

namespace ControlPCIA.Mobile.Servicios;

public static class ReconocimientoVoz
{
    public static async Task<string> EscucharAsync(
        CancellationToken cancellationToken = default)
    {
        PermissionStatus permiso =
            await Permissions.RequestAsync<Permissions.Microphone>();

        if (permiso != PermissionStatus.Granted)
        {
            throw new InvalidOperationException(
                "Activa el permiso de microfono para dictar. Tambien puedes usar el microfono del teclado.");
        }

#if ANDROID
        return await EscucharAndroidAsync(cancellationToken);
#else
        throw new PlatformNotSupportedException(
            "En este dispositivo usa el microfono del teclado para dictar.");
#endif
    }

#if ANDROID
    private static async Task<string> EscucharAndroidAsync(
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
        var resultado =
            new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            reconocedor = SpeechRecognizer.CreateSpeechRecognizer(contexto);
            oyente = new OyenteReconocimiento(resultado);
            reconocedor!.SetRecognitionListener(oyente);

            var intencion = new Intent(RecognizerIntent.ActionRecognizeSpeech);
            intencion.PutExtra(
                RecognizerIntent.ExtraLanguageModel,
                RecognizerIntent.LanguageModelFreeForm);
            intencion.PutExtra(RecognizerIntent.ExtraLanguage, "es-ES");
            intencion.PutExtra(
                RecognizerIntent.ExtraLanguagePreference,
                "es-ES");
            intencion.PutExtra(RecognizerIntent.ExtraPartialResults, false);
            reconocedor.StartListening(intencion);
        });

        try
        {
            return await resultado.Task.WaitAsync(
                TimeSpan.FromSeconds(25),
                cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                "No he oido ninguna frase. Toca Hablar e intentalo otra vez.");
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                reconocedor?.Cancel();
                reconocedor?.Destroy();
                reconocedor?.Dispose();
                oyente?.Dispose();
            });
        }
    }

    private sealed class OyenteReconocimiento(
        TaskCompletionSource<string> resultado)
        : Java.Lang.Object,
          IRecognitionListener
    {
        public void OnResults(Bundle? resultados)
        {
            string? texto = resultados?
                .GetStringArrayList(SpeechRecognizer.ResultsRecognition)?
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(texto))
            {
                resultado.TrySetException(
                    new InvalidOperationException(
                        "No he entendido la frase. Intentalo de nuevo."));
                return;
            }

            resultado.TrySetResult(texto.Trim());
        }

        public void OnError(SpeechRecognizerError error)
        {
            string mensaje = error switch
            {
                SpeechRecognizerError.NoMatch =>
                    "No he entendido la frase. Intentalo de nuevo.",
                SpeechRecognizerError.SpeechTimeout =>
                    "No he oido ninguna frase.",
                SpeechRecognizerError.InsufficientPermissions =>
                    "El movil no tiene permiso para usar el microfono.",
                SpeechRecognizerError.Network or
                SpeechRecognizerError.NetworkTimeout =>
                    "El reconocimiento de voz del movil no tiene conexion.",
                _ => "No se pudo usar el reconocimiento de voz del movil."
            };

            resultado.TrySetException(
                new InvalidOperationException(mensaje));
        }

        public void OnBeginningOfSpeech()
        {
        }

        public void OnBufferReceived(byte[]? buffer)
        {
        }

        public void OnEndOfSegmentedSession()
        {
        }

        public void OnEndOfSpeech()
        {
        }

        public void OnEvent(int eventType, Bundle? parameters)
        {
        }

        public void OnLanguageDetection(Bundle? results)
        {
        }

        public void OnPartialResults(Bundle? partialResults)
        {
        }

        public void OnReadyForSpeech(Bundle? parameters)
        {
        }

        public void OnRmsChanged(float rmsdB)
        {
        }

        public void OnSegmentResults(Bundle? segmentResults)
        {
        }
    }
#endif
}
