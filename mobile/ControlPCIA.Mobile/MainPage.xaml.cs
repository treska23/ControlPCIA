using ControlPCIA.Mobile.Controles;
using ControlPCIA.Mobile.Modelos;
using ControlPCIA.Mobile.Servicios;
using Microsoft.Maui.Graphics;

namespace ControlPCIA.Mobile;

public partial class MainPage : ContentPage
{
    private static readonly Color ColorNormal =
        Color.FromArgb("#AFC2D8");
    private static readonly Color ColorCorrecto =
        Color.FromArgb("#86EFAC");
    private static readonly Color ColorError =
        Color.FromArgb("#FCA5A5");

    private readonly ControlPciaApi _api = new();
    private readonly DescubrimientoPc _descubrimiento = new();
    private readonly WakeOnLan _wake = new();
    private readonly DistribucionDrawable _distribucion = new();
    private bool _inicializada;
    private bool _actualizandoTamano;

    public MainPage()
    {
        InitializeComponent();
        LayoutCanvas.Drawable = _distribucion;
        _wake.Cargar();
        ActualizarWakePanel();
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
            ActivarConexion(false);
            return;
        }

        ConnectionStatusLabel.Text = "Comprobando la conexion guardada…";

        try
        {
            EstadoPc estado = await _api.ObtenerEstadoAsync();
            ActivarConexion(true);
            MostrarEstadoPc(estado);
        }
        catch (Exception ex)
        {
            ActivarConexion(false);
            AddressEntry.Text = _api.Direccion;
            MostrarMensaje(
                ConnectionStatusLabel,
                MensajeAmigable(ex),
                error: true);
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
            ActivarConexion(true);
            MostrarEstadoPc(estado);
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

    private void OnForgetClicked(object? sender, EventArgs e)
    {
        _api.Olvidar();
        ActivarConexion(false);
        AddressEntry.Text = string.Empty;
        CodeEntry.Text = string.Empty;
        MostrarMensaje(
            ConnectionStatusLabel,
            "Busca el PC o escribe su direccion para conectarlo de nuevo.");
    }

    private async void OnWakeClicked(object? sender, EventArgs e)
    {
        WakeButton.IsEnabled = false;
        MostrarMensaje(
            WakeStatusLabel,
            "Escuchando… Di «enciende el ordenador». ");

        try
        {
            string texto = await ReconocimientoVoz.EscucharAsync();

            if (!WakeOnLan.EsOrdenEncender(texto))
            {
                MostrarMensaje(
                    WakeStatusLabel,
                    $"He oído «{texto}». Di «enciende el ordenador» para despertarlo.",
                    error: true);
                return;
            }

            await EnviarWakeOnLanAsync(WakeStatusLabel);
        }
        catch (Exception ex)
        {
            MostrarMensaje(
                WakeStatusLabel,
                MensajeAmigable(ex),
                error: true);
        }
        finally
        {
            WakeButton.IsEnabled = true;
        }
    }

    private void OnControlTabClicked(object? sender, EventArgs e)
    {
        MostrarPestana(control: true);
    }

    private void OnLayoutTabClicked(object? sender, EventArgs e)
    {
        MostrarPestana(control: false);
    }

    private async void OnVoiceClicked(object? sender, EventArgs e)
    {
        VoiceButton.IsEnabled = false;
        SendButton.IsEnabled = false;
        MostrarMensaje(ControlStatusLabel, "Escuchando… Habla ahora.");

        try
        {
            string texto = await ReconocimientoVoz.EscucharAsync();

            if (WakeOnLan.EsOrdenEncender(texto)
                &&
                _wake.EstaConfigurado)
            {
                await EnviarWakeOnLanAsync(ControlStatusLabel);
                return;
            }

            OrderEditor.Text = texto;
            MostrarMensaje(
                ControlStatusLabel,
                "He escrito la frase. Revisala y pulsa Enviar a la IA.",
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
            VoiceButton.IsEnabled = true;
            SendButton.IsEnabled = true;
        }
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        string orden = OrderEditor.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(orden))
        {
            MostrarMensaje(
                ControlStatusLabel,
                "Escribe o dicta primero lo que quieres que haga el PC.",
                error: true);
            return;
        }

        SendButton.IsEnabled = false;
        VoiceButton.IsEnabled = false;
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
            SendButton.IsEnabled = true;
            VoiceButton.IsEnabled = true;
        }
    }

    private async void OnRefreshSceneClicked(object? sender, EventArgs e)
    {
        RefreshSceneButton.IsEnabled = false;
        ApplyLayoutButton.IsEnabled = false;
        MostrarMensaje(
            LayoutStatusLabel,
            "Leyendo las pantallas y ventanas abiertas del PC…");

        try
        {
            EscenaPc escena = await _api.ObtenerEscenaAsync();
            _distribucion.Cargar(escena);
            LayoutCanvas.Invalidate();

            WindowPicker.ItemsSource = _distribucion.Ventanas.ToArray();
            WindowPicker.SelectedIndex =
                _distribucion.Ventanas.Count > 0 ? 0 : -1;

            ApplyLayoutButton.IsEnabled =
                _distribucion.Ventanas.Count > 0;
            ActualizarSeleccion();

            MostrarMensaje(
                LayoutStatusLabel,
                _distribucion.Ventanas.Count > 0
                    ? $"He cargado {_distribucion.Ventanas.Count} ventanas. Toca una y arrastrala."
                    : "No hay ventanas visibles para colocar.",
                correcto: _distribucion.Ventanas.Count > 0,
                error: _distribucion.Ventanas.Count == 0);
        }
        catch (Exception ex)
        {
            MostrarMensaje(
                LayoutStatusLabel,
                MensajeAmigable(ex),
                error: true);
            ComprobarSesion();
        }
        finally
        {
            RefreshSceneButton.IsEnabled = true;
        }
    }

    private void OnWindowSelected(object? sender, EventArgs e)
    {
        if (WindowPicker.SelectedIndex < 0)
        {
            return;
        }

        _distribucion.Seleccionar(WindowPicker.SelectedIndex);
        ActualizarSeleccion();
        LayoutCanvas.Invalidate();
    }

    private void OnCanvasTapped(object? sender, TappedEventArgs e)
    {
        Point? punto = e.GetPosition(LayoutCanvas);

        if (punto is null)
        {
            return;
        }

        int indice = _distribucion.Seleccionar(
            (float)punto.Value.X,
            (float)punto.Value.Y);

        if (indice >= 0)
        {
            WindowPicker.SelectedIndex = indice;
            ActualizarSeleccion();
            LayoutCanvas.Invalidate();
        }
    }

    private void OnCanvasPanned(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType == GestureStatus.Started)
        {
            _distribucion.IniciarArrastre();
            return;
        }

        if (e.StatusType == GestureStatus.Running)
        {
            _distribucion.Arrastrar(e.TotalX, e.TotalY);
            LayoutCanvas.Invalidate();
        }
    }

    private void OnSizeChanged(object? sender, ValueChangedEventArgs e)
    {
        if (_actualizandoTamano)
        {
            return;
        }

        _distribucion.AjustarTamano(
            (float)(WidthSlider.Value / 100),
            (float)(HeightSlider.Value / 100));
        LayoutCanvas.Invalidate();
    }

    private async void OnApplyLayoutClicked(object? sender, EventArgs e)
    {
        string orden = _distribucion.CrearOrden();

        if (string.IsNullOrWhiteSpace(orden))
        {
            MostrarMensaje(
                LayoutStatusLabel,
                "Carga primero las ventanas abiertas.",
                error: true);
            return;
        }

        ApplyLayoutButton.IsEnabled = false;
        RefreshSceneButton.IsEnabled = false;
        MostrarMensaje(
            LayoutStatusLabel,
            "Llama esta interpretando el dibujo y decidiendo los comandos…");

        try
        {
            ResultadoOrden resultado =
                await _api.EnviarOrdenAsync(orden);

            MostrarResultado(LayoutStatusLabel, resultado);
            AgregarHistorial("Distribucion dibujada de ventanas", resultado);
        }
        catch (Exception ex)
        {
            MostrarMensaje(
                LayoutStatusLabel,
                MensajeAmigable(ex),
                error: true);
            ComprobarSesion();
        }
        finally
        {
            ApplyLayoutButton.IsEnabled = _distribucion.Ventanas.Count > 0;
            RefreshSceneButton.IsEnabled = true;
        }
    }

    private void ActivarConexion(bool conectada)
    {
        ConnectionPanel.IsVisible = !conectada;
        ConnectedBar.IsVisible = conectada;
        NavigationBar.IsVisible = conectada;
        ConnectedAddressLabel.Text = conectada ? _api.Direccion : string.Empty;

        if (conectada)
        {
            MostrarPestana(control: true);
        }
        else
        {
            ControlPanel.IsVisible = false;
            LayoutPanel.IsVisible = false;
        }
    }

    private void MostrarPestana(bool control)
    {
        ControlPanel.IsVisible = control;
        LayoutPanel.IsVisible = !control;
        ControlTabButton.BackgroundColor =
            Color.FromArgb(control ? "#2563EB" : "#172A42");
        LayoutTabButton.BackgroundColor =
            Color.FromArgb(control ? "#172A42" : "#2563EB");
    }

    private void MostrarEstadoPc(EstadoPc estado)
    {
        _wake.Guardar(estado.WakeOnLan);
        ActualizarWakePanel();

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

    private void ActualizarWakePanel()
    {
        WakePanel.IsVisible = _wake.EstaConfigurado;
        WakeDescriptionLabel.Text =
            $"Toca Hablar y di «enciende el ordenador» · UDP {_wake.Puerto}.";
    }

    private async Task EnviarWakeOnLanAsync(Label estado)
    {
        MostrarMensaje(
            estado,
            $"Orden reconocida. Enviando Wake‑on‑LAN por UDP {_wake.Puerto}…");

        int destinos = await _wake.EncenderAsync();

        MostrarMensaje(
            estado,
            $"Señal de encendido enviada a {destinos} destinos. El PC puede tardar unos segundos en arrancar.",
            correcto: true);
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

    private void ActualizarSeleccion()
    {
        VentanaLienzo? ventana = _distribucion.VentanaSeleccionada;

        if (ventana is null)
        {
            SelectedWindowLabel.Text = "Carga las ventanas para empezar.";
            return;
        }

        SelectedWindowLabel.Text = "Seleccionada: " + ventana.Titulo;
        _actualizandoTamano = true;
        WidthSlider.Value = ventana.Ancho * 100;
        HeightSlider.Value = ventana.Alto * 100;
        _actualizandoTamano = false;
    }

    private void ComprobarSesion()
    {
        if (!_api.EstaConfigurada)
        {
            ActivarConexion(false);
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
                "No puedo contactar con el PC. Comprueba que ControlPCIA esta abierto y que ambos dispositivos usan la misma Wi‑Fi.",
            TaskCanceledException =>
                "El PC ha tardado demasiado en responder. Intentalo otra vez.",
            _ => ex.Message
        };
    }
}
