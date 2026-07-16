using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;

namespace ControlPCIA.Tests;

public sealed class AutomatizadorAplicacionesTests
{
    [Fact]
    public async Task Inspecciona_escribe_e_invoca_una_interfaz_real()
    {
        string titulo =
            "ControlPCIA UI Test " + Guid.NewGuid().ToString("N");
        var preparada =
            new TaskCompletionSource<Dispatcher>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var invocada =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var atajoRecibido =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        bool sensibleInvocada = false;
        Window? ventana = null;
        TextBox? busqueda = null;

        var hilo = new Thread(() =>
        {
            try
            {
                busqueda = new TextBox();
                AutomationProperties.SetName(
                    busqueda,
                    "Buscar plugin");
                AutomationProperties.SetAutomationId(
                    busqueda,
                    "PluginSearch");

                var boton = new Button
                {
                    Content = "Añadir plugin"
                };
                AutomationProperties.SetName(
                    boton,
                    "Añadir plugin");
                AutomationProperties.SetAutomationId(
                    boton,
                    "PluginButton");
                boton.Click += (_, _) =>
                    invocada.TrySetResult(true);

                var botonSensible = new Button
                {
                    Content = "Guardar como"
                };
                AutomationProperties.SetName(
                    botonSensible,
                    "Guardar como");
                AutomationProperties.SetAutomationId(
                    botonSensible,
                    "InnocentButton42");
                botonSensible.Click += (_, _) =>
                    sensibleInvocada = true;

                ventana = new Window
                {
                    Title = titulo,
                    Width = 420,
                    Height = 180,
                    WindowStartupLocation =
                        WindowStartupLocation.CenterScreen,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(16),
                        Children =
                        {
                            busqueda,
                            boton,
                            botonSensible
                        }
                    }
                };
                ventana.PreviewKeyDown += (_, e) =>
                {
                    if (e.Key == Key.T
                        && Keyboard.Modifiers.HasFlag(
                            ModifierKeys.Control))
                    {
                        atajoRecibido.TrySetResult(true);
                    }
                };

                ventana.Show();
                preparada.TrySetResult(ventana.Dispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                preparada.TrySetException(ex);
            }
        });

        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();

        Dispatcher dispatcher =
            await preparada.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

        try
        {
            await Task.Delay(
                150,
                TestContext.Current.CancellationToken);

            ResultadoAutomatizacionAplicacion inspeccion =
                AutomatizadorAplicaciones.Ejecutar(
                    ["ui", "inspect", titulo, "4"]);

            Assert.Equal(0, inspeccion.CodigoSalida);
            Assert.Contains(
                "name=Buscar plugin",
                inspeccion.Salida,
                StringComparison.Ordinal);
            Assert.Contains(
                "id=PluginButton",
                inspeccion.Salida,
                StringComparison.Ordinal);
            Assert.Contains(
                "name=[control protegido]",
                inspeccion.Salida,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "InnocentButton42",
                inspeccion.Salida,
                StringComparison.Ordinal);

            ResultadoAutomatizacionAplicacion texto =
                AutomatizadorAplicaciones.Ejecutar(
                    ["ui", "text", titulo, "id:PluginSearch", "Kontakt 7"]);

            Assert.Equal(0, texto.CodigoSalida);
            Assert.Equal(
                "Kontakt 7",
                await dispatcher.InvokeAsync(() => busqueda!.Text));

            ResultadoAutomatizacionAplicacion invocacion =
                AutomatizadorAplicaciones.Ejecutar(
                    ["ui", "invoke", titulo, "id:PluginButton", "Button"]);

            Assert.Equal(0, invocacion.CodigoSalida);
            Assert.True(
                await invocada.Task.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));

            ResultadoAutomatizacionAplicacion atajo =
                AutomatizadorAplicaciones.Ejecutar(
                    ["ui", "shortcut", titulo, "CTRL+T"]);

            Assert.Equal(0, atajo.CodigoSalida);
            Assert.True(
                await atajoRecibido.Task.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));

            ResultadoAutomatizacionAplicacion sensible =
                AutomatizadorAplicaciones.Ejecutar(
                    ["ui", "invoke", titulo, "id:InnocentButton42", "Button"]);

            Assert.NotEqual(0, sensible.CodigoSalida);
            Assert.False(
                await dispatcher.InvokeAsync(() => sensibleInvocada));
        }
        finally
        {
            await dispatcher.InvokeAsync(() => ventana?.Close());
            dispatcher.InvokeShutdown();
            hilo.Join(TimeSpan.FromSeconds(5));
        }
    }
}
