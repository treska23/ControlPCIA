using System.Text;
using System.Windows.Automation;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ControlPCIA
{
    internal static class ObservadorUIWindows
    {
        public static string ObtenerControles(
            string nombreVentana)
        {
            if (string.IsNullOrWhiteSpace(nombreVentana))
            {
                return "No se indicó ninguna ventana.";
            }

            AutomationElement raiz =
                AutomationElement.RootElement;

            AutomationElementCollection ventanas =
                raiz.FindAll(
                    TreeScope.Children,
                    Condition.TrueCondition);

            AutomationElement? ventanaObjetivo = null;

            foreach (AutomationElement ventana
                     in ventanas)
            {
                try
                {
                    string titulo =
                        ventana.Current.Name ?? "";

                    if (titulo.Contains(
                            nombreVentana,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        ventanaObjetivo = ventana;
                        break;
                    }
                }
                catch
                {
                    // El elemento puede haber desaparecido
                    // mientras se estaba inspeccionando.
                }
            }

            if (ventanaObjetivo == null)
            {
                return
                    $"No se encontró una ventana " +
                    $"que coincida con '{nombreVentana}'.";
            }

            AutomationElementCollection controles =
                ventanaObjetivo.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement
                            .IsControlElementProperty,
                        true));

            var resultado =
                new StringBuilder();

            resultado.AppendLine(
                $"Controles encontrados en '{nombreVentana}':");

            int contador = 0;

            var controlesVistos = new HashSet<string>();

            foreach (AutomationElement control in controles)
            {
                if (contador >= 80)
                {
                    resultado.AppendLine(
                        "- Se ha alcanzado el límite de 80 controles relevantes.");

                    break;
                }

                try
                {
                    bool estaFueraDePantalla =
    control.Current.IsOffscreen;

                    if (estaFueraDePantalla)
                    {
                        continue;
                    }

                    string nombre =
                        control.Current.Name ?? "";

                    string automationId =
                        control.Current.AutomationId ?? "";

                    ControlType tipoControl =
                        control.Current.ControlType;

                    // Solo nos interesan controles con los que
                    // realmente tenga sentido interactuar.
                    bool tipoInteresante =
                        tipoControl == ControlType.Button ||
                        tipoControl == ControlType.Edit ||
                        tipoControl == ControlType.ComboBox ||
                        tipoControl == ControlType.Hyperlink ||
                        tipoControl == ControlType.ListItem ||
                        tipoControl == ControlType.CheckBox ||
                        tipoControl == ControlType.RadioButton ||
                        tipoControl == ControlType.TabItem;

                    if (!tipoInteresante)
                        continue;

                    if (string.IsNullOrWhiteSpace(nombre)
                        &&
                        string.IsNullOrWhiteSpace(automationId))
                    {
                        continue;
                    }

                    var patrones = new List<string>();

                    if (control.TryGetCurrentPattern(
                            ValuePattern.Pattern,
                            out _))
                    {
                        patrones.Add("Value");
                    }

                    if (control.TryGetCurrentPattern(
                            InvokePattern.Pattern,
                            out _))
                    {
                        patrones.Add("Invoke");
                    }

                    if (control.TryGetCurrentPattern(
                            SelectionItemPattern.Pattern,
                            out _))
                    {
                        patrones.Add("SelectionItem");
                    }

                    if (control.TryGetCurrentPattern(
                            TogglePattern.Pattern,
                            out _))
                    {
                        patrones.Add("Toggle");
                    }

                    if (control.TryGetCurrentPattern(
                            ExpandCollapsePattern.Pattern,
                            out _))
                    {
                        patrones.Add("ExpandCollapse");
                    }

                    if (patrones.Count == 0)
                        continue;

                    string patronesTexto =
                        string.Join(", ", patrones);

                    string tipo =
                        control.Current.LocalizedControlType ?? "";

                    // Evitamos repetir el mismo control.
                    string clave =
                        $"{tipo}|{nombre}|{automationId}|{patronesTexto}";

                    if (!controlesVistos.Add(clave))
                        continue;

                    resultado.AppendLine(
                        $"- Tipo: {tipo} | " +
                        $"Nombre: {nombre} | " +
                        $"Id: {automationId} | " +
                        $"Patrones: {patronesTexto}");

                    contador++;
                }
                catch
                {
                    // Puede desaparecer mientras lo inspeccionamos.
                }
            }

            if (contador == 0)
            {
                resultado.AppendLine(
                    "- No se encontraron controles " +
                    "accesibles mediante UI Automation.");
            }

            return resultado.ToString();
        }
    }
}