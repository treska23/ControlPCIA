using System.Diagnostics;

namespace ControlPCIA
{
    internal sealed record ResultadoEjecucionPowerShell(
        bool Ejecutado,
        int CodigoSalida,
        string Salida,
        string Error);

    internal static class EjecutorPowerShell
    {
        public static async Task<ResultadoEjecucionPowerShell>
            EjecutarAsync(
                string comando)
        {
            ResultadoValidacionPowerShell validacion =
                ValidadorPowerShell.Validar(
                    comando);

            if (!validacion.Permitido)
            {
                return new(
                    false,
                    -1,
                    "",
                    "BLOQUEADO: " +
                    validacion.Motivo);
            }

            var inicio =
                new ProcessStartInfo
                {
                    FileName =
                        "powershell.exe",

                    UseShellExecute =
                        false,

                    RedirectStandardOutput =
                        true,

                    RedirectStandardError =
                        true,

                    CreateNoWindow =
                        true
                };

            inicio.ArgumentList.Add(
                "-NoLogo");

            inicio.ArgumentList.Add(
                "-NoProfile");

            inicio.ArgumentList.Add(
                "-NonInteractive");

            inicio.ArgumentList.Add(
                "-Command");

            inicio.ArgumentList.Add(
                comando);

            using var proceso =
                new Process
                {
                    StartInfo = inicio
                };

            try
            {
                if (!proceso.Start())
                {
                    return new(
                        false,
                        -1,
                        "",
                        "No se pudo iniciar PowerShell.");
                }

                Task<string> salidaTask =
                    proceso.StandardOutput
                        .ReadToEndAsync();

                Task<string> errorTask =
                    proceso.StandardError
                        .ReadToEndAsync();

                await proceso.WaitForExitAsync();

                string salida =
                    await salidaTask;

                string error =
                    await errorTask;

                return new(
                    true,
                    proceso.ExitCode,
                    salida.Trim(),
                    error.Trim());
            }
            catch (Exception ex)
            {
                return new(
                    false,
                    -1,
                    "",
                    ex.Message);
            }
        }
    }
}