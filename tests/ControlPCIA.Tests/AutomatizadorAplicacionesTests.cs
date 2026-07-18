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
                    Content = "Don't Save"
                };
                AutomationProperties.SetName(
                    botonSensible,
                    "Don't Save");
                AutomationProperties.SetAutomationId(
                    botonSensible,
                    "InnocentButton42");
                botonSensible.Click += (_, _) =>
                    sensibleInvocada = true;

                var pestanas = new TabControl();
                pestanas.Items.Add(
                    new TabItem
                    {
                        Header = "Proyecto.cpr*",
                        Content = "Contenido de prueba"
                    });

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
                            pestanas,
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
                600,
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

            ResultadoAutomatizacionAplicacion estado =
                AutomatizadorAplicaciones.Ejecutar(
                    ["ui", "status", titulo]);

            Assert.Equal(0, estado.CodigoSalida);
            Assert.Contains(
                "modified=true",
                estado.Salida,
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

    [Fact]
    public async Task Solicita_cierre_nativo_y_confirma_que_la_ventana_desaparece()
    {
        string titulo =
            "ControlPCIA Close Test " + Guid.NewGuid().ToString("N");
        var preparada =
            new TaskCompletionSource<Dispatcher>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var cerrada =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        Window? ventana = null;

        var hilo = new Thread(() =>
        {
            ventana = new Window
            {
                Title = titulo,
                Width = 320,
                Height = 140,
                Content = new TextBlock
                {
                    Text = "Ventana de cierre"
                }
            };
            ventana.Closed += (_, _) =>
            {
                cerrada.TrySetResult(true);
                ventana.Dispatcher.BeginInvokeShutdown(
                    DispatcherPriority.Background);
            };
            ventana.Show();
            preparada.TrySetResult(ventana.Dispatcher);
            Dispatcher.Run();
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

            ResultadoAutomatizacionAplicacion cierre =
                AutomatizadorAplicaciones.Ejecutar(
                    ["ui", "close", titulo]);

            Assert.True(
                cierre.CodigoSalida == 0,
                cierre.Error);
            Assert.Contains(
                "still-visible=false",
                cierre.Salida,
                StringComparison.Ordinal);
            Assert.True(
                await cerrada.Task.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            if (!cerrada.Task.IsCompleted)
            {
                await dispatcher.InvokeAsync(() => ventana?.Close());
            }

            dispatcher.InvokeShutdown();
            hilo.Join(TimeSpan.FromSeconds(5));
        }
    }
}
