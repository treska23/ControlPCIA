using System.Text;
using System.Windows.Automation;

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

            foreach (AutomationElement control
                     in controles)
            {
                if (contador >= 150)
                {
                    resultado.AppendLine(
                        "- Se ha alcanzado el límite " +
                        "de 150 controles.");

                    break;
                }

                try
                {
                    string nombre =
                        control.Current.Name ?? "";

                    string tipo =
                        control.Current
                            .LocalizedControlType ?? "";

                    string automationId =
                        control.Current
                            .AutomationId ?? "";

                    if (string.IsNullOrWhiteSpace(nombre)
                        &&
                        string.IsNullOrWhiteSpace(
                            automationId))
                    {
                        continue;
                    }

                    resultado.AppendLine(
                        $"- Tipo: {tipo} | " +
                        $"Nombre: {nombre} | " +
                        $"Id: {automationId}");

                    contador++;
                }
                catch
                {
                    // Ignoramos controles que hayan
                    // desaparecido durante la lectura.
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