using System.IO;
using Xunit;

namespace ControlPCIA.Tests;

public sealed class MemoriaRecetasTests
{
    [Fact]
    public async Task Guarda_y_recupera_una_receta_revalidada()
    {
        await ConMemoriaTemporalAsync(
            async memoria =>
            {
                bool aprendida = await memoria.AprenderAsync(
                    "Ábreme el Bloc de notas",
                    ["Start-Process notepad"],
                    TestContext.Current.CancellationToken);

                IReadOnlyList<RecetaReferencia> recetas =
                    await memoria.BuscarAsync(
                        "abreme el bloc de notas",
                        cancellationToken:
                            TestContext.Current.CancellationToken);

                Assert.True(aprendida);
                RecetaReferencia receta = Assert.Single(recetas);
                Assert.Equal("abreme el bloc de notas", receta.Intencion);
                Assert.Equal("Start-Process notepad", Assert.Single(receta.Comandos));
                Assert.Equal(1, receta.Exitos);
            });
    }

    [Fact]
    public async Task No_guarda_comandos_bloqueados()
    {
        await ConMemoriaTemporalAsync(
            async memoria =>
            {
                bool aprendida = await memoria.AprenderAsync(
                    "borra una prueba",
                    ["Remove-Item C:\\prueba.txt"],
                    TestContext.Current.CancellationToken);

                Assert.False(aprendida);
                Assert.Equal(
                    0,
                    await memoria.ContarAsync(
                        TestContext.Current.CancellationToken));
            });
    }

    [Fact]
    public async Task Persiste_y_encuentra_intenciones_parecidas()
    {
        string carpeta = CrearCarpetaTemporal();
        string ruta = Path.Combine(carpeta, "recetas.json");

        try
        {
            var primeraInstancia = new MemoriaRecetas(ruta);

            Assert.True(
                await primeraInstancia.AprenderAsync(
                    "abre spotify",
                    ["Start-Process spotify"],
                    TestContext.Current.CancellationToken));

            var segundaInstancia = new MemoriaRecetas(ruta);

            IReadOnlyList<RecetaReferencia> recetas =
                await segundaInstancia.BuscarAsync(
                    "inicia spotify",
                    cancellationToken:
                        TestContext.Current.CancellationToken);

            Assert.Single(recetas);
            Assert.True(recetas[0].Similitud >= 0.25);
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public async Task Una_receta_corregida_sustituye_la_secuencia_incompleta()
    {
        await ConMemoriaTemporalAsync(
            async memoria =>
            {
                Assert.True(
                    await memoria.AprenderAsync(
                        "abre la calculadora",
                        ["Start-Process calc.exe"],
                        TestContext.Current.CancellationToken));

                Assert.True(
                    await memoria.AprenderAsync(
                        "abre la calculadora",
                        [
                            "Start-Process calc.exe",
                            "ControlPCIA.exe ui focus \"Calculadora\""
                        ],
                        TestContext.Current.CancellationToken));

                RecetaReferencia receta = Assert.Single(
                    await memoria.BuscarAsync(
                        "abre la calculadora",
                        cancellationToken:
                            TestContext.Current.CancellationToken));

                Assert.Equal(2, receta.Comandos.Count);
                Assert.Equal(
                    "ControlPCIA.exe ui focus \"Calculadora\"",
                    receta.Comandos[1]);
            });
    }

    [Theory]
    [InlineData("  Pon MÚSICA, por favor.  ", "pon musica por favor")]
    [InlineData("Abre   Spotify", "abre spotify")]
    public void Normaliza_intenciones_sin_guardar_contenido_adicional(
        string original,
        string esperado)
    {
        Assert.Equal(esperado, MemoriaRecetas.Normalizar(original));
    }

    private static async Task ConMemoriaTemporalAsync(
        Func<MemoriaRecetas, Task> prueba)
    {
        string carpeta = CrearCarpetaTemporal();

        try
        {
            await prueba(
                new MemoriaRecetas(
                    Path.Combine(carpeta, "recetas.json")));
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    private static string CrearCarpetaTemporal()
    {
        string carpeta = Path.Combine(
            Path.GetTempPath(),
            "ControlPCIA.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(carpeta);
        return carpeta;
    }
}
