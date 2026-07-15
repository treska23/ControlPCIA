using System.Windows.Automation;

namespace ControlPCIA
{
    internal static class InteraccionUIWindows
    {
        public static async Task<string> Ejecutar(
            string nombreVentana,
            string nombreControl,
            string accion,
            string? valor)
        {
            try
            {
                AutomationElement? ventana =
                    BuscarVentana(nombreVentana);

                if (ventana == null)
                {
                    return
                        $"No se encontró la ventana '{nombreVentana}'.";
                }

                AutomationElement? control =
                    BuscarControl(
                        ventana,
                        nombreControl);

                if (control == null)
                {
                    return
                        $"No se encontró el control '{nombreControl}' " +
                        $"en la ventana '{nombreVentana}'.";
                }

                switch (accion.ToLowerInvariant())
                {
                    case "establecer_valor":
                        {
                            string texto =
                                valor ?? "";

                            if (control.TryGetCurrentPattern(
                                    ValuePattern.Pattern,
                                    out object patron))
                            {
                                var valuePattern =
                                    (ValuePattern)patron;

                                if (!valuePattern.Current.IsReadOnly)
                                {
                                    valuePattern.SetValue(texto);

                                    return
                                        $"Valor establecido en '{nombreControl}'.";
                                }
                            }

                            // Fallback:
                            // UI Automation ha encontrado el control,
                            // pero no permite modificarlo mediante ValuePattern.
                            // Le damos el foco y escribimos directamente en él.
                            control.SetFocus();

                            await Task.Delay(200);

                            EjecutorInterfazWindows.SeleccionarTodo();

                            await Task.Delay(100);

                            EjecutorInterfazWindows.EscribirTexto(texto);

                            await Task.Delay(200);

                            return
                                $"Texto escrito en el control '{nombreControl}' " +
                                "mediante entrada de teclado dirigida.";
                        }

                    case "invocar":
                        {
                            if (!control.TryGetCurrentPattern(
                                    InvokePattern.Pattern,
                                    out object patron))
                            {
                                return
                                    $"El control '{nombreControl}' " +
                                    "no admite InvokePattern.";
                            }

                            ((InvokePattern)patron).Invoke();

                            return
                                $"Control '{nombreControl}' invocado.";
                        }

                    case "seleccionar":
                        {
                            if (!control.TryGetCurrentPattern(
                                    SelectionItemPattern.Pattern,
                                    out object patron))
                            {
                                return
                                    $"El control '{nombreControl}' " +
                                    "no admite SelectionItemPattern.";
                            }

                            ((SelectionItemPattern)patron)
                                .Select();

                            return
                                $"Control '{nombreControl}' seleccionado.";
                        }

                    case "alternar":
                        {
                            if (!control.TryGetCurrentPattern(
                                    TogglePattern.Pattern,
                                    out object patron))
                            {
                                return
                                    $"El control '{nombreControl}' " +
                                    "no admite TogglePattern.";
                            }

                            ((TogglePattern)patron)
                                .Toggle();

                            return
                                $"Control '{nombreControl}' alternado.";
                        }

                    case "expandir":
                        {
                            if (!control.TryGetCurrentPattern(
                                    ExpandCollapsePattern.Pattern,
                                    out object patron))
                            {
                                return
                                    $"El control '{nombreControl}' " +
                                    "no admite ExpandCollapsePattern.";
                            }

                            ((ExpandCollapsePattern)patron)
                                .Expand();

                            return
                                $"Control '{nombreControl}' expandido.";
                        }

                    case "contraer":
                        {
                            if (!control.TryGetCurrentPattern(
                                    ExpandCollapsePattern.Pattern,
                                    out object patron))
                            {
                                return
                                    $"El control '{nombreControl}' " +
                                    "no admite ExpandCollapsePattern.";
                            }

                            ((ExpandCollapsePattern)patron)
                                .Collapse();

                            return
                                $"Control '{nombreControl}' contraído.";
                        }
                    case "confirmar":
                        {
                            // Si el control se puede invocar directamente,
                            // preferimos UI Automation.
                            if (control.TryGetCurrentPattern(
                                    InvokePattern.Pattern,
                                    out object patronInvoke))
                            {
                                ((InvokePattern)patronInvoke)
                                    .Invoke();

                                return
                                    $"Control '{nombreControl}' confirmado mediante Invoke.";
                            }

                            // Si admite foco de teclado,
                            // lo enfocamos y pulsamos Enter.
                            if (control.Current.IsKeyboardFocusable)
                            {
                                control.SetFocus();

                                await Task.Delay(150);

                                EjecutorInterfazWindows
                                    .PulsarEnter();

                                return
                                    $"Control '{nombreControl}' confirmado mediante Enter.";
                            }

                            return
                                $"El control '{nombreControl}' no puede confirmarse " +
                                "directamente mediante Invoke ni mediante foco de teclado.";
                        }

                    default:
                        return
                            $"Acción UI no permitida: {accion}";
                }
            }
            catch (Exception ex)
            {
                return
                    $"No se pudo interactuar con el control: " +
                    ex.Message;
            }
        }

        private static AutomationElement? BuscarVentana(
            string nombreVentana)
        {
            AutomationElementCollection ventanas =
                AutomationElement.RootElement.FindAll(
                    TreeScope.Children,
                    Condition.TrueCondition);

            foreach (AutomationElement ventana
                     in ventanas)
            {
                try
                {
                    string nombre =
                        ventana.Current.Name ?? "";

                    if (nombre.Contains(
                            nombreVentana,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return ventana;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static AutomationElement? BuscarControl(
            AutomationElement ventana,
            string nombreControl)
        {
            AutomationElementCollection controles =
                ventana.FindAll(
                    TreeScope.Descendants,
                    Condition.TrueCondition);

            // Primero buscamos coincidencia exacta.
            foreach (AutomationElement control
                     in controles)
            {
                try
                {
                    string nombre =
                        control.Current.Name ?? "";

                    string id =
                        control.Current.AutomationId ?? "";

                    if (nombre.Equals(
                            nombreControl,
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        id.Equals(
                            nombreControl,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return control;
                    }
                }
                catch
                {
                }
            }

            // Después coincidencia parcial.
            foreach (AutomationElement control
                     in controles)
            {
                try
                {
                    string nombre =
                        control.Current.Name ?? "";

                    if (nombre.Contains(
                            nombreControl,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return control;
                    }
                }
                catch
                {
                }
            }

            return null;
        }
    }
}