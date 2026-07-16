using ControlPCIA.Mobile.Modelos;
using ControlPCIA.Mobile.Servicios;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;

namespace ControlPCIA.Mobile;

public partial class MainPage : ContentPage
{
    private static readonly Color ColorNormal =
        Color.FromArgb("#AFC2D8");
    private static readonly Color ColorCorrecto =
        Color.FromArgb("#86EFAC");
    private static readonly Color ColorAviso =
        Color.FromArgb("#FCD34D");
    private static readonly Color ColorError =
        Color.FromArgb("#FCA5A5");

    private readonly ControlPciaApi _api = new();
    private readonly DescubrimientoPc _descubrimiento = new();
    private readonly WakeOnLan _wake = new();
    private SesionReconocimientoVoz? _sesionVoz;
    private CancellationTokenSource? _temporizadorVoz;
    private DateTimeOffset _inicioEscucha;
    private bool _inicializada;
    private bool _modoBloqueado;
    private bool _iniciandoVoz;
    private bool _soltarPendiente;
    private bool _ejecutandoOrden;
    private int _idSesionVoz;

    public MainPage()
    {
        InitializeComponent();
        _wake.Cargar();
        ActualizarModoVoz();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_inicializada)
        {
            return;
        }

        _inicializada = true;
        await _api.CargarAsync();

        if (!_api.EstaConfigurada)
        {
            if (_wake.EstaConfigurado)
            {
                MostrarControl(pcDisponible: false);
                MostrarMensaje(
                    ControlStatusLabel,
                    "El PC no esta conectado, pero el encendido por voz esta preparado.");
            }
            else
            {
                MostrarConexion();
            }

            return;
        }

        ConnectionStatusLabel.Text = "Comprobando la conexion guardada…";

        try
        {
            EstadoPc estado = await _api.ObtenerEstadoAsync();
            MostrarEstadoPc(estado);
            MostrarControl(pcDisponible: true);
        }
        catch (Exception ex)
        {
            AddressEntry.Text = _api.Direccion;
            MostrarControl(pcDisponible: false);
            MostrarMensaje(
                ControlStatusLabel,
                _wake.EstaConfigurado
                    ? "El PC no responde. Puedes usar este mismo microfono y decir «enciende el ordenador»."
                    : MensajeAmigable(ex),
                error: !_wake.EstaConfigurado);
        }
    }

    private async void OnSearchClicked(object? sender, EventArgs e)
    {
        SearchButton.IsEnabled = false;
        ConnectionActivity.IsVisible = true;
        ConnectionActivity.IsRunning = true;
        MostrarMensaje(
            ConnectionStatusLabel,
            "Buscando ControlPCIA en tu red Wi‑Fi…");

        try
        {
            IReadOnlyList<PcDescubierto> encontrados =
                await _descubrimiento.BuscarAsync();

            PcPicker.ItemsSource = encontrados.ToArray();
            PcPicker.IsVisible = encontrados.Count > 1;

            if (encontrados.Count == 0)
            {
                MostrarMensaje(
                    ConnectionStatusLabel,
                    "No he encontrado el PC automaticamente. Comprueba que ControlPCIA este abierto en el PC o escribe su direccion.",
                    error: true);
                return;
            }

            PcPicker.SelectedIndex = 0;
            AddressEntry.Text = encontrados[0].Direccion;
            MostrarMensaje(
                ConnectionStatusLabel,
                encontrados.Count == 1
                    ? $"PC encontrado: {encontrados[0].Nombre}. Introduce el codigo que aparece en el PC."
                    : $"He encontrado {encontrados.Count} direcciones. Elige tu PC e introduce el codigo.",
                correcto: true);
        }
        catch (Exception ex)
        {
            MostrarMensaje(
                ConnectionStatusLabel,
                MensajeAmigable(ex),
                error: true);
        }
        finally
        {
            SearchButton.IsEnabled = true;
            ConnectionActivity.IsRunning = false;
            ConnectionActivity.IsVisible = false;
        }
    }

    private void OnPcSelected(object? sender, EventArgs e)
    {
        if (PcPicker.SelectedItem is PcDescubierto pc)
        {
            AddressEntry.Text = pc.Direccion;
        }
    }

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        string codigo = CodeEntry.Text?.Trim() ?? string.Empty;

        if (codigo.Length != 6 || !codigo.All(char.IsDigit))
        {
            MostrarMensaje(
                ConnectionStatusLabel,
                "Escribe las 6 cifras que aparecen en la ventana de ControlPCIA del PC.",
                error: true);
            return;
        }

        ConnectButton.IsEnabled = false;
        SearchButton.IsEnabled = false;
        ConnectionActivity.IsVisible = true;
        ConnectionActivity.IsRunning = true;
        MostrarMensaje(ConnectionStatusLabel, "Conectando con el PC…");

        try
        {
            await _api.EmparejarAsync(
                AddressEntry.Text ?? string.Empty,
                codigo);

            EstadoPc estado = await _api.ObtenerEstadoAsync();
            CodeEntry.Text = string.Empty;
            MostrarEstadoPc(estado);
            MostrarControl(pcDisponible: true);
        }
        catch (Exception ex)
        {
            MostrarMensaje(
                ConnectionStatusLabel,
                MensajeAmigable(ex),
                error: true);
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            SearchButton.IsEnabled = true;
            ConnectionActivity.IsRunning = false;
            ConnectionActivity.IsVisible = false;
        }
    }

    private async void OnForgetClicked(object? sender, EventArgs e)
    {
        await CancelarEscuchaAsync(mostrarMensaje: false);
        _api.Olvidar();
        _wake.Olvidar();
        AddressEntry.Text = string.Empty;
        CodeEntry.Text = string.Empty;
        MostrarConexion();
        MostrarMensaje(
            ConnectionStatusLabel,
            "Busca el PC o escribe su direccion para conectarlo de nuevo.");
    }

    private async void OnVoicePressed(object? sender, EventArgs e)
    {
        if (_ejecutandoOrden)
        {
            return;
        }

        if (_sesionVoz is not null)
        {
            if (_modoBloqueado)
            {
                await DetenerEscuchaAsync();
            }

            return;
        }

        if (_iniciandoVoz)
        {
            return;
        }

        _soltarPendiente = false;
        await IniciarEscuchaAsync();

        if (_soltarPendiente && !_modoBloqueado)
        {
            await DetenerEscuchaAsync();
        }
    }

    private async void OnVoiceReleased(object? sender, EventArgs e)
    {
        if (_modoBloqueado)
        {
            return;
        }

        if (_iniciandoVoz)
        {
            _soltarPendiente = true;
            return;
        }

        if (_sesionVoz is not null)
        {
            await DetenerEscuchaAsync();
        }
    }

    private void OnVoiceModeClicked(object? sender, EventArgs e)
    {
        if (_sesionVoz is not null || _iniciandoVoz)
        {
            return;
        }

        _modoBloqueado = !_modoBloqueado;
        ActualizarModoVoz();
        Vibrar();
    }

    private async void OnCancelVoiceClicked(object? sender, EventArgs e)
    {
        await CancelarEscuchaAsync(mostrarMensaje: true);
    }

    private async Task IniciarEscuchaAsync()
    {
        _iniciandoVoz = true;
        int idSesion = ++_idSesionVoz;
        PrepararInterfazParaEscuchar();
        Vibrar();

        try
        {
            SesionReconocimientoVoz sesion =
                await ReconocimientoVoz.IniciarAsync(
                    _modoBloqueado,
                    estado => ActualizarEstadoVoz(idSesion, estado));

            if (idSesion != _idSesionVoz)
            {
                await sesion.CancelarAsync();
                await sesion.DisposeAsync();
                return;
            }

            _sesionVoz = sesion;
            _inicioEscucha = DateTimeOffset.UtcNow;
            VoiceButton.Text = _modoBloqueado
                ? "Detener y enviar"
                : "Suelta para enviar";
            IniciarTemporizadorVoz(idSesion);
            _ = RecibirResultadoVozAsync(sesion, idSesion);
        }
        catch (Exception ex)
        {
            if (idSesion == _idSesionVoz)
            {
                RestablecerInterfazVoz();
                VoiceStateTitle.Text = "No se pudo iniciar el microfono";
                VoiceTranscriptLabel.Text = MensajeAmigable(ex);
                VoiceTranscriptLabel.TextColor = ColorError;
            }
        }
        finally
        {
            _iniciandoVoz = false;
        }
    }

    private async Task DetenerEscuchaAsync()
    {
        SesionReconocimientoVoz? sesion = _sesionVoz;

        if (sesion is null)
        {
            return;
        }

        DetenerTemporizadorVoz();
        VoiceButton.IsEnabled = false;
        VoiceStateTitle.Text = "Transcribiendo…";
        VoiceTranscriptLabel.Text = "Espera un momento mientras preparo la orden.";
        VoiceTranscriptLabel.TextColor = ColorNormal;

        try
        {
            await sesion.DetenerAsync();
        }
        catch (Exception ex)
        {
            await CancelarEscuchaAsync(mostrarMensaje: false);
            VoiceStateTitle.Text = "No se pudo terminar la escucha";
            VoiceTranscriptLabel.Text = MensajeAmigable(ex);
            VoiceTranscriptLabel.TextColor = ColorError;
        }
    }

    private async Task RecibirResultadoVozAsync(
        SesionReconocimientoVoz sesion,
        int idSesion)
    {
        try
        {
            TimeSpan limite = _modoBloqueado
                ? TimeSpan.FromMinutes(2)
                : TimeSpan.FromSeconds(45);
            string texto = await sesion.Resultado.WaitAsync(limite);

            if (idSesion != _idSesionVoz)
            {
                return;
            }

            await CerrarSesionVozAsync(sesion, idSesion);
            Vibrar();
            await ProcesarTextoReconocidoAsync(texto);
        }
        catch (OperationCanceledException)
        {
            // La cancelacion solicitada por el usuario ya actualiza la interfaz.
        }
        catch (Exception ex)
        {
            if (idSesion == _idSesionVoz)
            {
                await CerrarSesionVozAsync(sesion, idSesion);
                VoiceStateTitle.Text = "No se pudo entender la orden";
                VoiceTranscriptLabel.Text = MensajeAmigable(ex);
                VoiceTranscriptLabel.TextColor = ColorError;
            }
        }
        finally
        {
            await sesion.DisposeAsync();
        }
    }

    private async Task CerrarSesionVozAsync(
        SesionReconocimientoVoz sesion,
        int idSesion)
    {
        if (idSesion != _idSesionVoz
            || !ReferenceEquals(_sesionVoz, sesion))
        {
            return;
        }

        _sesionVoz = null;
        DetenerTemporizadorVoz();
        await sesion.DisposeAsync();
        RestablecerInterfazVoz();
    }

    private async Task CancelarEscuchaAsync(bool mostrarMensaje)
    {
        ++_idSesionVoz;
        _soltarPendiente = false;
        SesionReconocimientoVoz? sesion = _sesionVoz;
        _sesionVoz = null;
        DetenerTemporizadorVoz();

        if (sesion is not null)
        {
            await sesion.CancelarAsync();
            await sesion.DisposeAsync();
        }

        RestablecerInterfazVoz();

        if (mostrarMensaje)
        {
            VoiceStateTitle.Text = "Escucha cancelada";
            VoiceTranscriptLabel.Text = "No se ha enviado ninguna orden.";
            VoiceTranscriptLabel.TextColor = ColorNormal;
        }
    }

    private void ActualizarEstadoVoz(
        int idSesion,
        EstadoReconocimientoVoz estado)
    {
        if (idSesion != _idSesionVoz)
        {
            return;
        }

        switch (estado.Fase)
        {
            case FaseReconocimientoVoz.Preparando:
                VoiceStateTitle.Text = "Preparando el microfono…";
                break;
            case FaseReconocimientoVoz.Preparado:
                VoiceStateTitle.Text = "Escuchando";
                VoiceTranscriptLabel.Text = _modoBloqueado
                    ? "El microfono queda abierto. Toca Detener cuando termines."
                    : "Habla ahora. Suelta el boton cuando termines.";
                break;
            case FaseReconocimientoVoz.VozDetectada:
                VoiceStateTitle.Text = "Te estoy escuchando";
                break;
            case FaseReconocimientoVoz.TextoParcial:
                VoiceStateTitle.Text = "Te estoy escuchando";
                VoiceTranscriptLabel.Text = $"«{estado.Texto}»";
                break;
            case FaseReconocimientoVoz.Transcribiendo:
                VoiceStateTitle.Text = "Transcribiendo…";
                break;
        }
    }

    private void PrepararInterfazParaEscuchar()
    {
        VoiceActivity.IsVisible = true;
        VoiceActivity.IsRunning = true;
        VoiceIdleIndicator.IsVisible = false;
        VoiceDurationLabel.IsVisible = true;
        VoiceDurationLabel.Text = "00:00";
        VoiceModeButton.IsEnabled = false;
        CancelVoiceButton.IsVisible = true;
        SendButton.IsEnabled = false;
        VoiceStateTitle.Text = "Preparando el microfono…";
        VoiceTranscriptLabel.Text = _modoBloqueado
            ? "Escucha bloqueada: podras soltar el boton y tocarlo otra vez para detener."
            : "Manten el boton pulsado mientras hablas.";
        VoiceTranscriptLabel.TextColor = ColorNormal;
    }

    private void RestablecerInterfazVoz()
    {
        VoiceActivity.IsRunning = false;
        VoiceActivity.IsVisible = false;
        VoiceIdleIndicator.IsVisible = true;
        VoiceDurationLabel.IsVisible = false;
        CancelVoiceButton.IsVisible = false;
        VoiceModeButton.IsEnabled = !_ejecutandoOrden;
        VoiceButton.IsEnabled = !_ejecutandoOrden;
        SendButton.IsEnabled = !_ejecutandoOrden;
        VoiceStateTitle.Text = "Preparado para escucharte";
        ActualizarModoVoz();
    }

    private void ActualizarModoVoz()
    {
        VoiceModeButton.Text = _modoBloqueado
            ? "Modo: escucha bloqueada"
            : "Modo: mantener pulsado";
        VoiceModeHelpLabel.Text = _modoBloqueado
            ? "Toca una vez para empezar. Puedes soltar el movil; toca de nuevo para detener y enviar."
            : "Manten pulsado mientras hablas y suelta para transcribir y enviar.";

        if (_sesionVoz is null && !_iniciandoVoz)
        {
            VoiceButton.Text = _modoBloqueado
                ? "Toca para empezar a escuchar"
                : "Manten pulsado para hablar";
            SemanticProperties.SetHint(
                VoiceButton,
                _modoBloqueado
                    ? "Toca para empezar una escucha bloqueada"
                    : "Manten pulsado para hablar y suelta para enviar la orden");
        }
    }

    private void IniciarTemporizadorVoz(int idSesion)
    {
        DetenerTemporizadorVoz();
        _temporizadorVoz = new CancellationTokenSource();
        CancellationToken token = _temporizadorVoz.Token;

        _ = ActualizarDuracionAsync(idSesion, token);
    }

    private async Task ActualizarDuracionAsync(
        int idSesion,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested
                   && idSesion == _idSesionVoz)
            {
                TimeSpan duracion = DateTimeOffset.UtcNow - _inicioEscucha;
                VoiceDurationLabel.Text = $"{(int)duracion.TotalMinutes:00}:{duracion.Seconds:00}";
                await Task.Delay(250, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void DetenerTemporizadorVoz()
    {
        _temporizadorVoz?.Cancel();
        _temporizadorVoz?.Dispose();
        _temporizadorVoz = null;
    }

    private async Task ProcesarTextoReconocidoAsync(string texto)
    {
        string orden = texto.Trim();
        OrderEditor.Text = orden;
        VoiceStateTitle.Text = "He entendido la orden";
        VoiceTranscriptLabel.Text = $"«{orden}»";
        VoiceTranscriptLabel.TextColor = ColorCorrecto;

        if (WakeOnLan.EsOrdenEncender(orden))
        {
            if (!_wake.EstaConfigurado)
            {
                MostrarMensaje(
                    ControlStatusLabel,
                    "He entendido que quieres encender el ordenador, pero antes debes conectarlo una vez para guardar sus datos Wake-on-LAN.",
                    error: true);
                return;
            }

            await EjecutarWakeOnLanAsync();
            return;
        }

        await EjecutarOrdenAsync(orden);
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        string orden = OrderEditor.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(orden))
        {
            MostrarMensaje(
                ControlStatusLabel,
                "Escribe primero lo que quieres que haga el PC.",
                error: true);
            return;
        }

        await EjecutarOrdenAsync(orden);
    }

    private async Task EjecutarOrdenAsync(string orden)
    {
        if (_ejecutandoOrden)
        {
            return;
        }

        _ejecutandoOrden = true;
        BloquearControlesDuranteOrden();
        MostrarMensaje(
            ControlStatusLabel,
            "Llama esta interpretando la orden y decidiendo los comandos…");

        try
        {
            ResultadoOrden resultado =
                await _api.EnviarOrdenAsync(orden);

            MostrarResultado(ControlStatusLabel, resultado);
            AgregarHistorial(orden, resultado);
        }
        catch (Exception ex)
        {
            MostrarMensaje(
                ControlStatusLabel,
                MensajeAmigable(ex),
                error: true);
            ComprobarSesion();
        }
        finally
        {
            _ejecutandoOrden = false;
            RestablecerInterfazVoz();
        }
    }

    private async Task EjecutarWakeOnLanAsync()
    {
        if (_ejecutandoOrden)
        {
            return;
        }

        _ejecutandoOrden = true;
        BloquearControlesDuranteOrden();

        try
        {
            MostrarMensaje(
                ControlStatusLabel,
                $"Orden reconocida. Enviando Wake-on-LAN por UDP {_wake.Puerto}…");

            int destinos = await _wake.EncenderAsync();

            MostrarMensaje(
                ControlStatusLabel,
                $"Señal de encendido enviada a {destinos} destinos. El PC puede tardar unos segundos en arrancar.",
                correcto: true);
        }
        catch (Exception ex)
        {
            MostrarMensaje(
                ControlStatusLabel,
                MensajeAmigable(ex),
                error: true);
        }
        finally
        {
            _ejecutandoOrden = false;
            RestablecerInterfazVoz();
        }
    }

    private void BloquearControlesDuranteOrden()
    {
        VoiceButton.IsEnabled = false;
        VoiceModeButton.IsEnabled = false;
        SendButton.IsEnabled = false;
        CancelVoiceButton.IsVisible = false;
    }

    private void MostrarConexion()
    {
        ConnectionPanel.IsVisible = true;
        ConnectedBar.IsVisible = false;
        ControlPanel.IsVisible = false;
    }

    private void MostrarControl(bool pcDisponible)
    {
        bool controlGuardado =
            _api.EstaConfigurada || _wake.EstaConfigurado;

        ConnectionPanel.IsVisible = !controlGuardado;
        ConnectedBar.IsVisible = controlGuardado;
        ControlPanel.IsVisible = controlGuardado;

        if (!controlGuardado)
        {
            return;
        }

        PcConnectionLabel.Text = pcDisponible
            ? "PC conectado"
            : "PC apagado o sin conexion";
        PcConnectionLabel.TextColor = pcDisponible
            ? ColorCorrecto
            : ColorAviso;
        ConnectedAddressLabel.Text = pcDisponible
            ? _api.Direccion
            : _wake.EstaConfigurado
                ? "El mismo microfono puede enviar la orden de encendido."
                : _api.Direccion;
    }

    private void MostrarEstadoPc(EstadoPc estado)
    {
        _wake.Guardar(estado.WakeOnLan);

        string recetas = estado.RecetasAprendidas == 1
            ? "1 receta aprendida"
            : $"{estado.RecetasAprendidas} recetas aprendidas";
        string mensaje = estado.Disponible
            ? $"IA preparada · {estado.Modelo} · {recetas}"
            : estado.Mensaje ?? "La IA local no esta disponible.";

        MostrarMensaje(
            ControlStatusLabel,
            mensaje,
            correcto: estado.Disponible,
            error: !estado.Disponible);
    }

    private void MostrarResultado(
        Label etiqueta,
        ResultadoOrden resultado)
    {
        int realizados = resultado.Pasos?.Count(
            paso => paso.Ejecutado && paso.CodigoSalida == 0) ?? 0;
        string aprendido = resultado.Aprendido
            ? " La IA guardo lo que funciono para la proxima vez."
            : string.Empty;
        string detalle = realizados > 0
            ? $" ({realizados} pasos realizados)"
            : string.Empty;

        MostrarMensaje(
            etiqueta,
            (resultado.Mensaje ?? "Orden terminada.") + detalle + aprendido,
            correcto: resultado.Completado,
            error: !resultado.Completado);
    }

    private void AgregarHistorial(
        string orden,
        ResultadoOrden resultado)
    {
        EmptyHistoryLabel.IsVisible = false;

        var contenido = new VerticalStackLayout
        {
            Spacing = 2
        };

        contenido.Children.Add(
            new Label
            {
                Text = orden,
                FontAttributes = FontAttributes.Bold,
                FontSize = 13,
                TextColor = Color.FromArgb("#E8F2FF"),
                MaxLines = 2,
                LineBreakMode = LineBreakMode.TailTruncation
            });

        contenido.Children.Add(
            new Label
            {
                Text = resultado.Completado ? "Completada" : "No completada",
                FontSize = 12,
                TextColor = resultado.Completado
                    ? ColorCorrecto
                    : ColorError
            });

        var tarjeta = new Border
        {
            BackgroundColor = Color.FromArgb("#081421"),
            Stroke = Color.FromArgb("#203853"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = 12
            },
            Padding = 11,
            Content = contenido
        };

        HistoryStack.Children.Insert(0, tarjeta);

        while (HistoryStack.Children.Count > 6)
        {
            HistoryStack.Children.RemoveAt(
                HistoryStack.Children.Count - 1);
        }
    }

    private void ComprobarSesion()
    {
        if (!_api.EstaConfigurada)
        {
            MostrarControl(pcDisponible: false);
        }
    }

    private static void Vibrar()
    {
        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch
        {
            // Algunos dispositivos no disponen de respuesta haptica.
        }
    }

    private static void MostrarMensaje(
        Label etiqueta,
        string mensaje,
        bool correcto = false,
        bool error = false)
    {
        etiqueta.Text = mensaje;
        etiqueta.TextColor = error
            ? ColorError
            : correcto
                ? ColorCorrecto
                : ColorNormal;
    }

    private static string MensajeAmigable(Exception ex)
    {
        return ex switch
        {
            HttpRequestException =>
                "No puedo contactar con el PC. Si esta apagado, di «enciende el ordenador»; si esta encendido, comprueba que ControlPCIA esta abierto y que ambos dispositivos usan la misma Wi‑Fi.",
            TaskCanceledException =>
                "La operacion ha tardado demasiado. Intentalo otra vez.",
            TimeoutException =>
                "La escucha ha durado demasiado y se ha detenido.",
            _ => ex.Message
        };
    }
}
