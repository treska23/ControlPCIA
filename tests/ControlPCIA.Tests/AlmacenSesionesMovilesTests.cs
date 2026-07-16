using Microsoft.AspNetCore.Http;
using System.IO;
using Xunit;

namespace ControlPCIA.Tests;

public sealed class AlmacenSesionesMovilesTests
{
    [Fact]
    public void Conserva_el_emparejado_sin_guardar_el_token_real()
    {
        string carpeta = Path.Combine(
            Path.GetTempPath(),
            "ControlPCIA.Tests",
            Guid.NewGuid().ToString("N"));
        string ruta = Path.Combine(carpeta, "sesiones-v1.json");

        try
        {
            var primerAlmacen = new AlmacenSesionesMoviles(ruta);
            var primeraInstancia =
                new ServidorMovil.SeguridadMovil(primerAlmacen);
            ServidorMovil.ResultadoEmparejado emparejado =
                primeraInstancia.Emparejar(
                    "127.0.0.1",
                    primeraInstancia.Codigo);

            Assert.Equal(
                ServidorMovil.EstadoEmparejado.Correcto,
                emparejado.Estado);
            string token = Assert.IsType<string>(emparejado.Token);
            Assert.True(Autorizar(primeraInstancia, token));

            var segundaInstancia =
                new ServidorMovil.SeguridadMovil(
                    new AlmacenSesionesMoviles(ruta));

            Assert.True(Autorizar(segundaInstancia, token));

            string contenido = File.ReadAllText(ruta);
            Assert.DoesNotContain(
                token,
                contenido,
                StringComparison.Ordinal);
            Assert.Contains(
                "caducidadUtc",
                contenido,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(carpeta))
            {
                Directory.Delete(carpeta, recursive: true);
            }
        }
    }

    private static bool Autorizar(
        ServidorMovil.SeguridadMovil seguridad,
        string token)
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Headers.Authorization = "Bearer " + token;
        return seguridad.Autorizar(contexto);
    }
}
