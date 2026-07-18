using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace ControlPCIA;

internal sealed record ResultadoAutomatizacionAplicacion(
    int CodigoSalida,
    string Salida,
    string Error);

internal static class AutomatizadorAplicaciones
{
    private const int MaximoElementosInspeccion = 250;

    private static readonly HashSet<string> ProcesosProtegidos =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "powershell", "pwsh", "cmd", "conhost", "WindowsTerminal",
            "wt", "regedit", "taskmgr", "mmc",
            "CredentialUIBroker", "consent", "SecurityHealthSystray",
        };

    private static readonly string[] TitulosProtegidos =
    [
        "credenciales", "credentials", "contraseña",
        "password", "control de cuentas de usuario", "user account control",
        "seguridad de windows", "windows security", "developer tools",
        "devtools", "herramientas de desarrollo"
    ];

    private static readonly Dictionary<string, ControlType> Tipos =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Button"] = ControlType.Button,
            ["MenuItem"] = ControlType.MenuItem,
            ["ListItem"] = ControlType.ListItem,
            ["TreeItem"] = ControlType.TreeItem,
            ["TabItem"] = ControlType.TabItem,
            ["CheckBox"] = ControlType.CheckBox,
            ["RadioButton"] = ControlType.RadioButton,
            ["ComboBox"] = ControlType.ComboBox,
            ["Edit"] = ControlType.Edit,
            ["Document"] = ControlType.Document,
            ["Hyperlink"] = ControlType.Hyperlink,
            ["DataItem"] = ControlType.DataItem,
            ["Custom"] = ControlType.Custom
        };

    public static ResultadoAutomatizacionAplicacion Ejecutar(
        IReadOnlyList<string> argumentos)
    {
        bool permitirDescarte =
            Environment.GetEnvironmentVariable(
                "CONTROLPCIA_PERMITIR_DESCARTE")
            == "1";
        ResultadoValidacionPowerShell validacion =
            ValidadorAutomatizacionAplicaciones.Validar(
                argumentos,
                permitirDescarte);

        if (!validacion.Permitido)
        {
            return Fallar(validacion.Motivo, 3);
        }

        try
        {
            string accion = argumentos[1].ToLowerInvariant();

            if (accion == "windows")
            {
                return ListarVentanas();
            }

            AutomationElement ventana = ObtenerVentanaConReintentos(argumentos[2]);
            ComprobarVentanaPermitida(ventana);

            return accion switch
            {
                "inspect" => Inspeccionar(
                    ventana,
                    argumentos.Count == 4
                        ? int.Parse(argumentos[3])
                        : 6,
                    permitirDescarte),
                "status" => ObtenerEstado(ventana),
                "focus" => Enfocar(ventana),
                "close" => Cerrar(ventana),
                "shortcut" => EnviarAtajo(ventana, argumentos[3]),
                "text" => EscribirTexto(
                    ventana,
                    argumentos[3],
                    argumentos[4],
                    permitirDescarte),
                _ => EjecutarAccionElemento(
                    ventana,
                    accion,
                    argumentos[3],
                    argumentos.Count == 5 ? argumentos[4] : null,
                    permitirDescarte)
            };
        }
        catch (ElementNotAvailableException)
        {
            return Fallar(
                "La interfaz cambió mientras se realizaba la acción. Inspecciona de nuevo la ventana.",
                4);
        }
        catch (InvalidOperationException ex)
        {
            return Fallar(ex.Message, 4);
        }
        catch (Exception ex)
        {
            return Fallar(
                "No se pudo controlar la aplicación: " + ex.Message,
                5);
        }
    }

    internal static ResultadoAutomatizacionAplicacion ListarVentanas()
    {
        AutomationElementCollection ventanas =
            AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                Condition.TrueCondition);
        var salida = new StringBuilder();
        var publicadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (AutomationElement ventana in ventanas)
        {
            string titulo = ObtenerPropiedad(
                ventana,
                AutomationElement.NameProperty);

            if (string.IsNullOrWhiteSpace(titulo))
            {
                continue;
            }

            try
            {
                ComprobarVentanaPermitida(ventana);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            string proceso = ObtenerNombreProceso(ventana);
            string clave = titulo + "\u001f" + proceso;

            if (!publicadas.Add(clave))
            {
                continue;
            }

            salida.Append("WINDOW|title=\"");
            salida.Append(EscaparCampo(titulo));
            salida.Append("\"|process=\"");
            salida.Append(EscaparCampo(proceso));
            salida.Append('"');
            salida.AppendLine();
        }

        return salida.Length == 0
            ? Fallar("No se encontraron ventanas de aplicaciones controlables.", 4)
            : Exito(salida.ToString().Trim());
    }

    private static ResultadoAutomatizacionAplicacion Inspeccionar(
        AutomationElement ventana,
        int profundidadMaxima,
        bool permitirDescarte)
    {
        var salida = new StringBuilder();
        salida.AppendLine(
            $"WINDOW|title=\"{EscaparCampo(ObtenerPropiedad(ventana, AutomationElement.NameProperty))}\"" +
            $"|process=\"{EscaparCampo(ObtenerNombreProceso(ventana))}\"");

        int elementos = 0;
        Recorrer(
            ventana,
            profundidad: 0,
            profundidadMaxima,
            ref elementos,
            salida,
            permitirDescarte);

        if (elementos >= MaximoElementosInspeccion)
        {
            salida.AppendLine(
                $"TRUNCATED|max={MaximoElementosInspeccion}");
        }

        return Exito(salida.ToString().Trim());
    }

    private static void Recorrer(
        AutomationElement elemento,
        int profundidad,
        int profundidadMaxima,
        ref int elementos,
        StringBuilder salida,
        bool permitirDescarte)
    {
        if (elementos >= MaximoElementosInspeccion)
        {
            return;
        }

        bool esContrasena = ObtenerBooleano(
            elemento,
            AutomationElement.IsPasswordProperty);
        string nombreReal = ObtenerPropiedad(
            elemento,
            AutomationElement.NameProperty);
        bool sensible =
            ValidadorAutomatizacionAplicaciones.EsControlSiempreProtegido(
                nombreReal,
                permitirDescarte);
        string nombre = esContrasena
            ? "[contenido protegido]"
            : sensible
                ? "[control protegido]"
                : nombreReal;
        string id = sensible
            ? string.Empty
            : ObtenerPropiedad(
                elemento,
                AutomationElement.AutomationIdProperty);
        string tipo = ObtenerTipo(elemento);
        string patrones = sensible
            ? string.Empty
            : ObtenerPatrones(elemento);

        if (profundidad == 0
            || !string.IsNullOrWhiteSpace(nombre)
            || !string.IsNullOrWhiteSpace(id))
        {
            salida.Append("UI|depth=");
            salida.Append(profundidad);
            salida.Append("|type=");
            salida.Append(Escapar(tipo));
            salida.Append("|name=");
            salida.Append(Escapar(nombre));
            salida.Append("|id=");
            salida.Append(Escapar(id));
            salida.Append("|enabled=");
            salida.Append(
                ObtenerBooleano(
                    elemento,
                    AutomationElement.IsEnabledProperty)
                    ? "true"
                    : "false");
            salida.Append("|patterns=");
            salida.Append(Escapar(patrones));
            salida.AppendLine();
            elementos++;
        }

        if (profundidad >= profundidadMaxima)
        {
            return;
        }

        TreeWalker navegador = TreeWalker.ControlViewWalker;
        AutomationElement? hijo;

        try
        {
            hijo = navegador.GetFirstChild(elemento);
        }
        catch (ElementNotAvailableException)
        {
            return;
        }

        while (hijo is not null
               && elementos < MaximoElementosInspeccion)
        {
            Recorrer(
                hijo,
                profundidad + 1,
                profundidadMaxima,
                ref elementos,
                salida,
                permitirDescarte);

            try
            {
                hijo = navegador.GetNextSibling(hijo);
            }
            catch (ElementNotAvailableException)
            {
                break;
            }
        }
    }

    private static ResultadoAutomatizacionAplicacion Enfocar(
        AutomationElement ventana)
    {
        ActivarVentana(ventana);
        return Exito(
            $"OK|focused={Escapar(ObtenerPropiedad(ventana, AutomationElement.NameProperty))}");
    }

    private static ResultadoAutomatizacionAplicacion ObtenerEstado(
        AutomationElement ventana)
    {
        string titulo = ObtenerPropiedad(
            ventana,
            AutomationElement.NameProperty);
        string proceso = ObtenerNombreProceso(ventana);
        int identificador = ObtenerIdentificadorVentana(ventana);
        EstadoModificado modificado = DetectarEstadoModificado(ventana);
        IntPtr manejador = new(identificador);

        return Exito(
            $"STATE|title=\"{EscaparCampo(titulo)}\"" +
            $"|process=\"{EscaparCampo(proceso)}\"" +
            $"|visible={IsWindowVisible(manejador).ToString().ToLowerInvariant()}" +
            $"|active={(GetForegroundWindow() == manejador).ToString().ToLowerInvariant()}" +
            $"|minimized={IsIconic(manejador).ToString().ToLowerInvariant()}" +
            $"|modified={modificado.ToString().ToLowerInvariant()}");
    }

    private static ResultadoAutomatizacionAplicacion Cerrar(
        AutomationElement ventana)
    {
        string titulo = ObtenerPropiedad(
            ventana,
            AutomationElement.NameProperty);
        int identificador = ObtenerIdentificadorVentana(ventana);
        int proceso = ObtenerIdentificadorProceso(ventana);
        IntPtr manejador = new(identificador);

        ActivarVentana(ventana);

        if (ventana.TryGetCurrentPattern(
                WindowPattern.Pattern,
                out object? patronVentana))
        {
            ((WindowPattern)patronVentana).Close();
        }
        else
        {
            PostMessage(
                manejador,
                0x0010,
                IntPtr.Zero,
                IntPtr.Zero);
        }

        Thread.Sleep(900);

        bool sigueVisible =
            IsWindow(manejador)
            && IsWindowVisible(manejador);
        string dialogo = sigueVisible
            ? ObtenerDialogoVisible(proceso, identificador)
            : string.Empty;

        return Exito(
            $"OK|close-requested=\"{EscaparCampo(titulo)}\"" +
            $"|still-visible={sigueVisible.ToString().ToLowerInvariant()}" +
            $"|dialog=\"{EscaparCampo(dialogo)}\"");
    }

    private static EstadoModificado DetectarEstadoModificado(
        AutomationElement ventana)
    {
        string titulo = ObtenerPropiedad(
            ventana,
            AutomationElement.NameProperty);

        if (TieneMarcaModificado(titulo))
        {
            return EstadoModificado.True;
        }

        AutomationElementCollection elementos = ventana.FindAll(
            TreeScope.Descendants,
            new OrCondition(
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.TabItem),
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Document)));
        bool encontroDocumento = false;

        foreach (AutomationElement elemento in elementos)
        {
            encontroDocumento = true;

            if (TieneMarcaModificado(
                    ObtenerPropiedad(
                        elemento,
                        AutomationElement.NameProperty)))
            {
                return EstadoModificado.True;
            }
        }

        return encontroDocumento
            ? EstadoModificado.False
            : EstadoModificado.Unknown;
    }

    private static bool TieneMarcaModificado(string texto)
    {
        string limpio = texto.Trim();
        return limpio.EndsWith('*')
               || limpio.Contains(" * - ", StringComparison.Ordinal)
               || limpio.Contains(
                   "modificado",
                   StringComparison.OrdinalIgnoreCase)
               || limpio.Contains(
                   "modified",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool EsSuperficieDeArchivos(AutomationElement ventana)
    {
        string proceso = ObtenerNombreProceso(ventana);
        string titulo = ValidadorAutomatizacionAplicaciones.Normalizar(
            ObtenerPropiedad(ventana, AutomationElement.NameProperty));
        string[] titulosDialogo =
        [
            "abrir", "open", "guardar como", "save as",
            "seleccionar archivo", "choose file", "seleccionar carpeta",
            "choose folder", "explorador de archivos", "file explorer"
        ];

        return proceso.Equals("explorer", StringComparison.OrdinalIgnoreCase)
               || titulosDialogo.Any(fragmento =>
                   titulo.Contains(
                       ValidadorAutomatizacionAplicaciones.Normalizar(fragmento),
                       StringComparison.Ordinal));
    }

    private static string ObtenerDialogoVisible(
        int proceso,
        int ventanaOriginal)
    {
        AutomationElementCollection ventanas =
            AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                Condition.TrueCondition);

        foreach (AutomationElement candidata in ventanas)
        {
            int identificador = ObtenerIdentificadorVentana(
                candidata,
                lanzarSiFalta: false);

            if (identificador == 0
                || identificador == ventanaOriginal
                || ObtenerIdentificadorProceso(candidata) != proceso)
            {
                continue;
            }

            string titulo = ObtenerPropiedad(
                candidata,
                AutomationElement.NameProperty);

            if (!string.IsNullOrWhiteSpace(titulo))
            {
                return titulo;
            }
        }

        return string.Empty;
    }

    private static ResultadoAutomatizacionAplicacion EnviarAtajo(
        AutomationElement ventana,
        string atajo)
    {
        if (!ValidadorAutomatizacionAplicaciones.EsAtajoPermitidoEnVentana(
                atajo,
                EsSuperficieDeArchivos(ventana)))
        {
            throw new InvalidOperationException(
                "Ese atajo podría cortar o eliminar archivos en la ventana actual.");
        }

        ActivarVentana(ventana);
        EntradaTecladoSegura.EnviarAtajo(atajo);

        return Exito(
            $"OK|shortcut={Escapar(atajo)}" +
            $"|window={Escapar(ObtenerPropiedad(ventana, AutomationElement.NameProperty))}");
    }

    private static ResultadoAutomatizacionAplicacion EscribirTexto(
        AutomationElement ventana,
        string selector,
        string texto,
        bool permitirDescarte)
    {
        AutomationElement elemento =
            BuscarElemento(ventana, selector, tipo: null);

        ComprobarElementoPermitido(
            ventana,
            elemento,
            permitirDescarte);

        if (ObtenerBooleano(elemento, AutomationElement.IsPasswordProperty))
        {
            throw new InvalidOperationException(
                "No se permite escribir en controles de contraseña.");
        }

        ControlType? tipo = ObtenerControlType(elemento);

        if (tipo != ControlType.Edit
            && tipo != ControlType.Document
            && tipo != ControlType.ComboBox)
        {
            throw new InvalidOperationException(
                "La acción de texto sólo admite cuadros de edición, documentos o listas desplegables.");
        }

        ActivarVentana(ventana);

        if (elemento.TryGetCurrentPattern(
                ValuePattern.Pattern,
                out object? patronValor))
        {
            var valor = (ValuePattern)patronValor;

            if (valor.Current.IsReadOnly)
            {
                throw new InvalidOperationException(
                    "El control de texto encontrado es de solo lectura.");
            }

            valor.SetValue(texto);
        }
        else
        {
            elemento.SetFocus();
            Thread.Sleep(80);
            EntradaTecladoSegura.ReemplazarTexto(texto);
        }

        return Exito(
            $"OK|text-set={Escapar(selector)}|characters={texto.Length}");
    }

    private static ResultadoAutomatizacionAplicacion EjecutarAccionElemento(
        AutomationElement ventana,
        string accion,
        string selector,
        string? tipo,
        bool permitirDescarte)
    {
        AutomationElement elemento =
            BuscarElemento(ventana, selector, tipo);

        ComprobarElementoPermitido(
            ventana,
            elemento,
            permitirDescarte);

        if (!ObtenerBooleano(elemento, AutomationElement.IsEnabledProperty))
        {
            throw new InvalidOperationException(
                $"El control '{selector}' está deshabilitado.");
        }

        ActivarVentana(ventana);

        switch (accion)
        {
            case "invoke":
                Invocar(elemento, selector);
                break;
            case "select":
                ObtenerPatron<SelectionItemPattern>(
                    elemento,
                    SelectionItemPattern.Pattern,
                    selector).Select();
                break;
            case "toggle":
                ObtenerPatron<TogglePattern>(
                    elemento,
                    TogglePattern.Pattern,
                    selector).Toggle();
                break;
            case "expand":
                ObtenerPatron<ExpandCollapsePattern>(
                    elemento,
                    ExpandCollapsePattern.Pattern,
                    selector).Expand();
                break;
            case "collapse":
                ObtenerPatron<ExpandCollapsePattern>(
                    elemento,
                    ExpandCollapsePattern.Pattern,
                    selector).Collapse();
                break;
            default:
                throw new InvalidOperationException(
                    $"La acción de interfaz '{accion}' no está implementada.");
        }

        return Exito(
            $"OK|action={accion}|element={Escapar(selector)}" +
            $"|type={Escapar(ObtenerTipo(elemento))}");
    }

    private static void Invocar(
        AutomationElement elemento,
        string selector)
    {
        if (elemento.TryGetCurrentPattern(
                InvokePattern.Pattern,
                out object? patronInvocacion))
        {
            ((InvokePattern)patronInvocacion).Invoke();
            return;
        }

        if (elemento.TryGetCurrentPattern(
                SelectionItemPattern.Pattern,
                out object? patronSeleccion))
        {
            ((SelectionItemPattern)patronSeleccion).Select();
            return;
        }

        throw new InvalidOperationException(
            $"El control '{selector}' no admite una acción de invocación segura.");
    }

    private static T ObtenerPatron<T>(
        AutomationElement elemento,
        AutomationPattern patron,
        string selector)
        where T : class
    {
        if (elemento.TryGetCurrentPattern(patron, out object? encontrado)
            && encontrado is T resultado)
        {
            return resultado;
        }

        throw new InvalidOperationException(
            $"El control '{selector}' no admite el patrón '{typeof(T).Name}'.");
    }

    private static AutomationElement BuscarElemento(
        AutomationElement ventana,
        string selector,
        string? tipo)
    {
        bool buscarId = selector.StartsWith(
            "id:",
            StringComparison.OrdinalIgnoreCase);
        string valor = buscarId ? selector[3..] : selector;
        AutomationProperty propiedad = buscarId
            ? AutomationElement.AutomationIdProperty
            : AutomationElement.NameProperty;
        Condition condicionTexto = new PropertyCondition(
            propiedad,
            valor,
            PropertyConditionFlags.IgnoreCase);
        Condition condicion = condicionTexto;

        if (!string.IsNullOrWhiteSpace(tipo))
        {
            condicion = new AndCondition(
                condicionTexto,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    Tipos[tipo]));
        }

        AutomationElement? exacto = ventana.FindFirst(
            TreeScope.Descendants,
            condicion);

        if (exacto is not null)
        {
            return exacto;
        }

        AutomationElementCollection candidatos = ventana.FindAll(
            TreeScope.Descendants,
            Condition.TrueCondition);
        var coincidencias = new List<AutomationElement>();

        foreach (AutomationElement candidato in candidatos)
        {
            if (!string.IsNullOrWhiteSpace(tipo)
                && ObtenerControlType(candidato) != Tipos[tipo])
            {
                continue;
            }

            string actual = ObtenerPropiedad(candidato, propiedad);

            if (actual.Contains(valor, StringComparison.OrdinalIgnoreCase))
            {
                coincidencias.Add(candidato);

                if (coincidencias.Count > 1)
                {
                    break;
                }
            }
        }

        return coincidencias.Count switch
        {
            1 => coincidencias[0],
            > 1 => throw new InvalidOperationException(
                $"El selector '{selector}' coincide con varios controles. Usa 'id:' o un nombre más preciso."),
            _ => throw new InvalidOperationException(
                $"No se encontró el control '{selector}'. Inspecciona de nuevo la ventana.")
        };
    }

    private static AutomationElement ObtenerVentana(string objetivo)
    {
        if (objetivo.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            IntPtr activa = GetForegroundWindow();

            if (activa == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Windows no devolvió una ventana activa.");
            }

            return AutomationElement.FromHandle(activa);
        }

        AutomationElementCollection ventanas =
            AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                Condition.TrueCondition);
        var exactas = new List<AutomationElement>();
        var parciales = new List<AutomationElement>();

        foreach (AutomationElement ventana in ventanas)
        {
            string titulo = ObtenerPropiedad(
                ventana,
                AutomationElement.NameProperty);

            if (titulo.Equals(objetivo, StringComparison.OrdinalIgnoreCase))
            {
                exactas.Add(ventana);
            }
            else if (titulo.Contains(
                         objetivo,
                         StringComparison.OrdinalIgnoreCase))
            {
                parciales.Add(ventana);
            }
        }

        IReadOnlyList<AutomationElement> candidatas =
            exactas.Count > 0 ? exactas : parciales;

        return candidatas.Count switch
        {
            1 => candidatas[0],
            // Windows puede exponer varias superficies del mismo proceso con el
            // mismo título (especialmente aplicaciones empaquetadas). La primera
            // de RootElement.Children es la situada más arriba en el orden Z.
            > 1 => candidatas[0],
            _ => throw new InvalidOperationException(
                $"No se encontró una ventana visible cuyo título contenga '{objetivo}'.")
        };
    }

    private static AutomationElement ObtenerVentanaConReintentos(string objetivo)
    {
        for (int intento = 0; intento < 12; intento++)
        {
            try
            {
                return ObtenerVentana(objetivo);
            }
            catch (InvalidOperationException ex)
                when (ex.Message.StartsWith(
                    "No se encontró una ventana visible",
                    StringComparison.Ordinal))
            {
                IntPtr identificador = FindWindow(null, objetivo);

                if (identificador != IntPtr.Zero)
                {
                    return AutomationElement.FromHandle(identificador);
                }

                if (intento == 11)
                {
                    throw;
                }

                Thread.Sleep(80);
            }
        }

        throw new InvalidOperationException(
            "No se pudo localizar la ventana solicitada.");
    }

    private static void ComprobarVentanaPermitida(AutomationElement ventana)
    {
        string proceso = ObtenerNombreProceso(ventana);
        string titulo = ValidadorAutomatizacionAplicaciones.Normalizar(
            ObtenerPropiedad(ventana, AutomationElement.NameProperty));

        if (ProcesosProtegidos.Contains(proceso)
            || TitulosProtegidos.Any(fragmento =>
                titulo.Contains(
                    ValidadorAutomatizacionAplicaciones.Normalizar(fragmento),
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "La ventana encontrada es una consola, un diálogo de credenciales o una superficie sensible.");
        }
    }

    private static void ComprobarElementoPermitido(
        AutomationElement ventana,
        AutomationElement elemento,
        bool permitirDescarte)
    {
        string nombre = ObtenerPropiedad(
            elemento,
            AutomationElement.NameProperty);
        string id = ObtenerPropiedad(
            elemento,
            AutomationElement.AutomationIdProperty);

        bool superficieArchivos = EsSuperficieDeArchivos(ventana);

        if (ObtenerBooleano(elemento, AutomationElement.IsPasswordProperty)
            || ValidadorAutomatizacionAplicaciones.EsControlSiempreProtegido(
                nombre,
                permitirDescarte)
            || ValidadorAutomatizacionAplicaciones.EsControlSiempreProtegido(
                id,
                permitirDescarte)
            || superficieArchivos
               && (ValidadorAutomatizacionAplicaciones
                       .EsAccionDestructivaDeArchivos(nombre)
                   || ValidadorAutomatizacionAplicaciones
                       .EsAccionDestructivaDeArchivos(id)))
        {
            throw new InvalidOperationException(
                "El control encontrado podría eliminar o cortar archivos, actuar sobre discos o acceder a credenciales.");
        }
    }

    private static void ActivarVentana(AutomationElement ventana)
    {
        int identificador = ObtenerIdentificadorVentana(ventana);

        IntPtr manejador = new(identificador);
        // Estas llamadas sólo restauran y cambian el orden Z de la ventana
        // objetivo; nunca minimizan, mueven ni redimensionan otras ventanas.
        ShowWindow(manejador, 9);
        SetWindowPos(
            manejador,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            0x0001 | 0x0002 | 0x0040);
        SetForegroundWindow(manejador);

        try
        {
            ventana.SetFocus();
        }
        catch (InvalidOperationException)
        {
            // Algunas ventanas sólo admiten la activación Win32.
        }

        for (int intento = 0; intento < 10; intento++)
        {
            if (GetForegroundWindow() == manejador)
            {
                Thread.Sleep(100);
                return;
            }

            Thread.Sleep(50);
        }

        throw new InvalidOperationException(
            "Windows no permitió traer la ventana al primer plano.");
    }

    private static int ObtenerIdentificadorVentana(
        AutomationElement ventana,
        bool lanzarSiFalta = true)
    {
        int identificador;

        try
        {
            identificador = (int)ventana.GetCurrentPropertyValue(
                AutomationElement.NativeWindowHandleProperty,
                ignoreDefaultValue: true);
        }
        catch (ElementNotAvailableException)
        {
            identificador = 0;
        }

        if (identificador == 0 && lanzarSiFalta)
        {
            throw new InvalidOperationException(
                "La ventana no expone un identificador que permita controlarla.");
        }

        return identificador;
    }

    private static int ObtenerIdentificadorProceso(
        AutomationElement elemento)
    {
        try
        {
            return elemento.Current.ProcessId;
        }
        catch (ElementNotAvailableException)
        {
            return 0;
        }
    }

    private static string ObtenerPatrones(AutomationElement elemento)
    {
        var patrones = new List<string>();
        AgregarPatron(elemento, InvokePattern.Pattern, "invoke", patrones);
        AgregarPatron(elemento, SelectionItemPattern.Pattern, "select", patrones);
        AgregarPatron(elemento, TogglePattern.Pattern, "toggle", patrones);
        AgregarPatron(elemento, ExpandCollapsePattern.Pattern, "expand", patrones);
        AgregarPatron(elemento, ValuePattern.Pattern, "text", patrones);

        return string.Join(',', patrones);
    }

    private static void AgregarPatron(
        AutomationElement elemento,
        AutomationPattern patron,
        string nombre,
        ICollection<string> patrones)
    {
        try
        {
            if (elemento.TryGetCurrentPattern(patron, out _))
            {
                patrones.Add(nombre);
            }
        }
        catch (ElementNotAvailableException)
        {
        }
    }

    private static string ObtenerNombreProceso(AutomationElement elemento)
    {
        try
        {
            int id = elemento.Current.ProcessId;
            return id <= 0
                ? "desconocido"
                : Process.GetProcessById(id).ProcessName;
        }
        catch (Exception ex) when (
            ex is ArgumentException
            or InvalidOperationException
            or ElementNotAvailableException)
        {
            return "desconocido";
        }
    }

    private static string ObtenerTipo(AutomationElement elemento)
    {
        ControlType? tipo = ObtenerControlType(elemento);
        return tipo?.ProgrammaticName.Replace(
                   "ControlType.",
                   string.Empty,
                   StringComparison.Ordinal)
               ?? "Unknown";
    }

    private static ControlType? ObtenerControlType(AutomationElement elemento)
    {
        try
        {
            return elemento.GetCurrentPropertyValue(
                AutomationElement.ControlTypeProperty,
                ignoreDefaultValue: true) as ControlType;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static string ObtenerPropiedad(
        AutomationElement elemento,
        AutomationProperty propiedad)
    {
        try
        {
            object valor = elemento.GetCurrentPropertyValue(
                propiedad,
                ignoreDefaultValue: true);
            return valor == AutomationElement.NotSupported
                ? string.Empty
                : valor?.ToString()?.Trim() ?? string.Empty;
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }

    private static bool ObtenerBooleano(
        AutomationElement elemento,
        AutomationProperty propiedad)
    {
        try
        {
            return elemento.GetCurrentPropertyValue(
                       propiedad,
                       ignoreDefaultValue: true) is true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static string Escapar(string texto)
    {
        return texto
            .Replace("|", "¦", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string EscaparCampo(string texto)
    {
        return Escapar(texto).Replace("\"", "'", StringComparison.Ordinal);
    }

    private static ResultadoAutomatizacionAplicacion Exito(string salida)
    {
        return new(0, salida, string.Empty);
    }

    private static ResultadoAutomatizacionAplicacion Fallar(
        string error,
        int codigo)
    {
        return new(codigo, string.Empty, error);
    }

    private enum EstadoModificado
    {
        Unknown,
        False,
        True
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(
        string? clase,
        string? titulo);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr ventana);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr ventana);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr ventana);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(
        IntPtr ventana,
        uint mensaje,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr ventana);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr ventana, int comando);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr ventana,
        IntPtr insertarDespues,
        int x,
        int y,
        int ancho,
        int alto,
        uint indicadores);
}
