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
    private static readonly Color ColorAzul =
        Color.FromArgb("#2563EB");
    private static readonly Color ColorVerde =
        Color.FromArgb("#16A34A");
    private static readonly Color ColorAmbar =
        Color.FromArgb("#D97706");
    private static readonly Color ColorVioleta =
        Color.FromArgb("#7C3AED");
    private static readonly Color ColorRojo =
        Color.FromArgb("#B91C1C");
    private static readonly Color ColorFondoReposo =
        Color.FromArgb("#081421");
    private static readonly Color ColorFondoEscucha =
        Color.FromArgb("#0B241B");
    private static readonly Color ColorFondoProceso =
        Color.FromArgb("#211438");
    private static readonly Color ColorBordeReposo =
        Color.FromArgb("#2D4968");
    private static readonly Color ColorSecundario =
        Color.FromArgb("#172A42");

    private readonly ControlPciaApi _api = new();
    private readonly DescubrimientoPc _descubrimiento = new();
    private readonly WakeOnLan _wake = new();
    private readonly List<MensajeConversacion> _conversacion = [];
    private SesionReconocimientoVoz? _sesionVoz;
    private CancellationTokenSource? _temporizadorVoz;
    private DateTimeOffset _inicioEscucha;
    private bool _inicializada;
    private bool _modoBloqueado = true;
    private bool _iniciandoVoz;
    private bool _soltarPendiente;
    private bool _ejecutandoOrden;
    private bool _cancelandoVoz;
    private int _idSesionVoz;
    private string? _ordenPendienteConfirmacion;
    private string? _preguntaPendiente;

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
        _ordenPendienteConfirmacion = null;
        _preguntaPendiente = null;
        _conversacion.Clear();
        HistoryStack.Children.Clear();
        HistoryStack.Children.Add(EmptyHistoryLabel);
        EmptyHistoryLabel.IsVisible = true;
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
        if (_cancelandoVoz)
        {
            return;
        }

        _cancelandoVoz = true;
        CancelVoiceButton.IsEnabled = false;

        try
        {
            await CancelarEscuchaAsync(mostrarMensaje: true);
        }
        catch
        {
            // Cancelar es un descarte y nunca debe cerrar la aplicación,
            // aunque Android ya haya liberado su servicio de voz.
            RestablecerInterfazVoz();
            VoiceStateTitle.Text = "Escucha cancelada";
            VoiceTranscriptLabel.Text = "No se ha enviado ninguna orden.";
            VoiceTranscriptLabel.TextColor = ColorNormal;
        }
        finally
        {
            _cancelandoVoz = false;
            CancelVoiceButton.IsEnabled = true;
        }
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
                ? "Escuchando · toca para enviar"
                : "Escuchando · suelta para enviar";
            AplicarAspectoEscuchando();
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
        AplicarAspectoTranscribiendo();

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
                ? Timeout.InfiniteTimeSpan
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
                VoiceStateTitle.Text = "No te he entendido";
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
            try
            {
                await sesion.CancelarAsync();
            }
            catch
            {
                // La orden ya quedó invalidada por idSesion. El reconocedor
                // puede haber terminado entre el toque y esta cancelación.
            }

            try
            {
                await sesion.DisposeAsync();
            }
            catch
            {
                // La liberación nativa es de mejor esfuerzo: cancelar nunca
                // debe propagar una excepción a la interfaz.
            }
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
                AplicarAspectoEscuchando();
                VoiceTranscriptLabel.Text = _modoBloqueado
                    ? "El microfono queda abierto. Toca Detener cuando termines."
                    : "Habla ahora. Suelta el boton cuando termines.";
                break;
            case FaseReconocimientoVoz.VozDetectada:
                VoiceStateTitle.Text = "Te estoy escuchando";
                AplicarAspectoEscuchando();
                break;
            case FaseReconocimientoVoz.TextoParcial:
                VoiceStateTitle.Text = "Te estoy escuchando";
                AplicarAspectoEscuchando();
                VoiceTranscriptLabel.Text = $"«{estado.Texto}»";
                break;
            case FaseReconocimientoVoz.Transcribiendo:
                VoiceStateTitle.Text = "Transcribiendo…";
                AplicarAspectoTranscribiendo();
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
        CancelVoiceButton.IsEnabled = true;
        SendButton.IsEnabled = false;
        OrderEditor.IsEnabled = false;
        VoiceStateTitle.Text = "Preparando el microfono…";
        VoiceTranscriptLabel.Text = _modoBloqueado
            ? "Escucha bloqueada: podras soltar el boton y tocarlo otra vez para detener."
            : "Manten el boton pulsado mientras hablas.";
        VoiceTranscriptLabel.TextColor = ColorNormal;
        VoiceStateBorder.BackgroundColor = ColorFondoEscucha;
        VoiceStateBorder.Stroke =
            new SolidColorBrush(ColorVerde);
        VoiceButton.BackgroundColor = ColorVerde;
        VoiceButton.TextColor = Colors.White;
        VoiceModeButton.BackgroundColor = ColorSecundario;
        CancelVoiceButton.BackgroundColor = ColorRojo;
        CancelVoiceButton.TextColor = Colors.White;
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
        OrderEditor.IsEnabled = !_ejecutandoOrden;
        VoiceStateBorder.BackgroundColor = ColorFondoReposo;
        VoiceStateBorder.Stroke =
            new SolidColorBrush(ColorBordeReposo);
        VoiceIdleIndicator.BackgroundColor = ColorAzul;
        VoiceActivity.Color = ColorAzul;
        VoiceButton.BackgroundColor = ColorAzul;
        VoiceButton.TextColor = Colors.White;
        VoiceModeButton.BackgroundColor = ColorSecundario;
        SendButton.BackgroundColor = ColorSecundario;
        SendButton.Text = "Enviar mensaje escrito";
        CancelVoiceButton.BackgroundColor = ColorSecundario;
        CancelVoiceButton.TextColor = ColorNormal;
        VoiceStateTitle.Text = string.IsNullOrWhiteSpace(
                _ordenPendienteConfirmacion)
            ? string.IsNullOrWhiteSpace(_preguntaPendiente)
                ? "Preparado para escucharte"
                : "Necesito un dato"
            : "Necesito tu confirmación";
        ActualizarModoVoz();
    }

    private void ActualizarModoVoz()
    {
        VoiceModeButton.Text = _modoBloqueado
            ? "Modo: tocar para hablar"
            : "Modo: mantener pulsado";
        VoiceModeHelpLabel.Text = _modoBloqueado
            ? "Toca una vez para empezar. Habla todo lo que necesites y toca de nuevo para enviar."
            : "Manten pulsado mientras hablas y suelta para transcribir y enviar.";

        if (_sesionVoz is null && !_iniciandoVoz)
        {
            VoiceButton.Text = _modoBloqueado
                ? "Toca para empezar a escuchar"
                : "Manten pulsado para hablar";
            SemanticProperties.SetHint(
                VoiceButton,
                _modoBloqueado
                    ? "Toca para empezar a escuchar; toca otra vez para enviar"
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

        MensajeConversacion[] contexto =
            _conversacion.TakeLast(12).ToArray();

        _ejecutandoOrden = true;
        BloquearControlesDuranteOrden(
            "Llama está decidiendo qué hacer…",
            $"«{orden}»");
        MostrarMensaje(
            ControlStatusLabel,
            "Llama esta interpretando la orden y decidiendo los comandos…");

        try
        {
            ResultadoOrden resultado =
                await _api.EnviarOrdenAsync(orden, contexto);

            AgregarAlContexto("user", orden);
            AgregarAlContexto(
                "assistant",
                resultado.Mensaje ?? "No he podido preparar una respuesta.");

            if (resultado.Estado?.Equals(
                    "requiere_confirmacion",
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                _ordenPendienteConfirmacion = orden;
                _preguntaPendiente = null;
                VoiceStateTitle.Text = "Necesito tu confirmación";
                VoiceTranscriptLabel.Text =
                    (resultado.Mensaje ?? "¿Quieres que continúe?")
                    + " Responde sí o no.";
                VoiceTranscriptLabel.TextColor = ColorAviso;
            }
            else if (resultado.Estado?.Equals(
                         "requiere_aclaracion",
                         StringComparison.OrdinalIgnoreCase) == true)
            {
                _ordenPendienteConfirmacion = null;
                _preguntaPendiente =
                    resultado.Mensaje ?? "¿Qué dato falta para continuar?";
                VoiceStateTitle.Text = "Necesito un dato";
                VoiceTranscriptLabel.Text = _preguntaPendiente;
                VoiceTranscriptLabel.TextColor = ColorAviso;
            }
            else
            {
                _ordenPendienteConfirmacion = null;
                _preguntaPendiente = null;
            }

            MostrarResultado(ControlStatusLabel, resultado);
            AgregarIntercambio(orden, resultado);
            OrderEditor.Text = string.Empty;
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
        BloquearControlesDuranteOrden(
            "Enviando la señal de encendido…",
            "El móvil está enviando Wake-on-LAN al ordenador.");

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

    private void BloquearControlesDuranteOrden(
        string titulo,
        string detalle)
    {
        VoiceButton.IsEnabled = false;
        VoiceModeButton.IsEnabled = false;
        SendButton.IsEnabled = false;
        OrderEditor.IsEnabled = false;
        CancelVoiceButton.IsVisible = false;
        VoiceActivity.IsVisible = true;
        VoiceActivity.IsRunning = true;
        VoiceActivity.Color = ColorVioleta;
        VoiceIdleIndicator.IsVisible = false;
        VoiceDurationLabel.IsVisible = false;
        VoiceStateBorder.BackgroundColor = ColorFondoProceso;
        VoiceStateBorder.Stroke =
            new SolidColorBrush(ColorVioleta);
        VoiceStateTitle.Text = titulo;
        VoiceTranscriptLabel.Text = detalle;
        VoiceTranscriptLabel.TextColor = ColorNormal;
        VoiceButton.BackgroundColor = ColorVioleta;
        VoiceButton.TextColor = Colors.White;
        VoiceButton.Text = "Procesando · espera un momento";
        VoiceModeButton.BackgroundColor = ColorVioleta;
        SendButton.BackgroundColor = ColorVioleta;
        SendButton.Text = "Esperando respuesta de la IA…";
    }

    private void AplicarAspectoEscuchando()
    {
        VoiceStateBorder.BackgroundColor = ColorFondoEscucha;
        VoiceStateBorder.Stroke =
            new SolidColorBrush(ColorVerde);
        VoiceIdleIndicator.BackgroundColor = ColorVerde;
        VoiceActivity.Color = ColorVerde;
        VoiceButton.BackgroundColor = ColorVerde;
        VoiceButton.TextColor = Colors.White;
        CancelVoiceButton.BackgroundColor = ColorRojo;
        CancelVoiceButton.TextColor = Colors.White;
    }

    private void AplicarAspectoTranscribiendo()
    {
        VoiceStateBorder.BackgroundColor =
            Color.FromArgb("#302008");
        VoiceStateBorder.Stroke =
            new SolidColorBrush(ColorAmbar);
        VoiceActivity.Color = ColorAmbar;
        VoiceButton.BackgroundColor = ColorAmbar;
        VoiceButton.TextColor = Colors.White;
        VoiceButton.Text = "Transcribiendo · espera";
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
        string confirmacion = resultado.Estado?.Equals(
                "requiere_confirmacion",
                StringComparison.OrdinalIgnoreCase) == true
            ? " Responde sí o no."
            : string.Empty;
        bool aclaracion = resultado.Estado?.Equals(
                "requiere_aclaracion",
                StringComparison.OrdinalIgnoreCase) == true;

        MostrarMensaje(
            etiqueta,
            (resultado.Mensaje ?? "Orden terminada.")
            + detalle
            + aprendido
            + confirmacion,
            correcto: resultado.Completado,
            error: !resultado.Completado
                   && confirmacion.Length == 0
                   && !aclaracion);

        if (aclaracion)
        {
            etiqueta.TextColor = ColorAviso;
        }
    }

    private void AgregarIntercambio(
        string orden,
        ResultadoOrden resultado)
    {
        EmptyHistoryLabel.IsVisible = false;

        HistoryStack.Children.Add(
            CrearBurbujaChat(
                "Tú",
                orden,
                Color.FromArgb("#14325A"),
                Color.FromArgb("#3B82F6")));
        HistoryStack.Children.Add(
            CrearBurbujaChat(
                "IA",
                resultado.Mensaje ?? "No he podido preparar una respuesta.",
                Color.FromArgb("#102A25"),
                resultado.Estado?.Equals(
                    "requiere_confirmacion",
                    StringComparison.OrdinalIgnoreCase) == true
                || resultado.Estado?.Equals(
                    "requiere_aclaracion",
                    StringComparison.OrdinalIgnoreCase) == true
                    ? ColorAviso
                    : resultado.Completado
                        ? ColorCorrecto
                        : ColorError));

        while (HistoryStack.Children.Count > 21)
        {
            HistoryStack.Children.RemoveAt(1);
        }
    }

    private static Border CrearBurbujaChat(
        string autor,
        string texto,
        Color fondo,
        Color colorAutor)
    {
        var contenido = new VerticalStackLayout
        {
            Spacing = 4
        };
        contenido.Children.Add(
            new Label
            {
                Text = autor,
                FontAttributes = FontAttributes.Bold,
                FontSize = 12,
                TextColor = colorAutor
            });
        contenido.Children.Add(
            new Label
            {
                Text = texto,
                FontSize = 14,
                TextColor = Color.FromArgb("#E8F2FF"),
                LineBreakMode = LineBreakMode.WordWrap
            });

        return new Border
        {
            BackgroundColor = fondo,
            Stroke = Color.FromArgb("#29425E"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = 14
            },
            Padding = 12,
            Content = contenido
        };
    }

    private void AgregarAlContexto(string rol, string texto)
    {
        string limpio = texto.Trim();

        if (limpio.Length == 0)
        {
            return;
        }

        if (limpio.Length > 800)
        {
            limpio = limpio[..800].Trim();
        }

        _conversacion.Add(new MensajeConversacion(rol, limpio));

        while (_conversacion.Count > 12)
        {
            _conversacion.RemoveAt(0);
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
