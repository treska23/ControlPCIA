using System.Text;

namespace ControlPCIA
{
    internal sealed record ResultadoValidacionPowerShell(
        bool Permitido,
        string Motivo);

    internal static class ValidadorPowerShell
    {
        private static readonly HashSet<string>
            ComandosBloqueados =
                new(StringComparer.OrdinalIgnoreCase);

        private static readonly List<string>
            PrefijosBloqueados =
                new();

        private static readonly List<ReglaArgumento>
            ArgumentosBloqueados =
                new();

        private static readonly List<string>
            DestinosBloqueados =
                new();

        private static readonly List<string>
            TextosBloqueados =
                new();

        private static readonly List<string>
            SintaxisBloqueada =
                new();

        static ValidadorPowerShell()
        {
            CargarRestricciones();
        }

        public static ResultadoValidacionPowerShell Validar(
            string comando)
        {
            if (string.IsNullOrWhiteSpace(comando))
            {
                return Bloquear(
                    "El comando está vacío.");
            }

            comando =
                comando.Trim();

            if (comando.Length > 20000)
            {
                return Bloquear(
                    "El comando supera la longitud máxima permitida.");
            }


            // ================================================
            // 1. FRAGMENTOS DE TEXTO PELIGROSOS
            // ================================================

            foreach (string texto
                     in TextosBloqueados)
            {
                if (comando.Contains(
                        texto,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Bloquear(
                        $"Se ha detectado una operación restringida: " +
                        $"'{texto}'.");
                }
            }


            // ================================================
            // 2. SINTAXIS BLOQUEADA
            // ================================================

            foreach (string sintaxis
                     in SintaxisBloqueada)
            {
                if (comando.Contains(
                        sintaxis,
                        StringComparison.Ordinal))
                {
                    return Bloquear(
                        $"Se ha detectado sintaxis restringida: " +
                        $"'{sintaxis}'.");
                }
            }


            // ================================================
            // 3. EXTRAER LOS COMANDOS QUE SE INTENTAN EJECUTAR
            // ================================================

            List<string> comandosEncontrados =
                ExtraerComandos(
                    comando);

            foreach (string nombreComando
                     in comandosEncontrados)
            {
                if (ComandosBloqueados.Contains(
                        nombreComando))
                {
                    return Bloquear(
                        $"El comando '{nombreComando}' " +
                        "está bloqueado por la política de seguridad.");
                }

                foreach (string prefijo
                         in PrefijosBloqueados)
                {
                    if (nombreComando.StartsWith(
                            prefijo,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return Bloquear(
                            $"El comando '{nombreComando}' pertenece " +
                            $"a una familia restringida: '{prefijo}'.");
                    }
                }
            }


            // ================================================
            // 4. ARGUMENTOS PROHIBIDOS EN COMANDOS CONCRETOS
            // ================================================

            foreach (ReglaArgumento regla
                     in ArgumentosBloqueados)
            {
                if (!ContieneComando(
                        comandosEncontrados,
                        regla.Comando))
                {
                    continue;
                }

                if (comando.Contains(
                        regla.Argumento,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Bloquear(
                        $"El argumento '{regla.Argumento}' " +
                        $"no está permitido con '{regla.Comando}'.");
                }
            }


            // ================================================
            // 5. DESTINOS PROHIBIDOS DE START-PROCESS
            // ================================================

            if (ContieneComando(
                    comandosEncontrados,
                    "Start-Process"))
            {
                string? destino =
                    ObtenerDestinoStartProcess(
                        comando);

                if (!string.IsNullOrWhiteSpace(
                        destino))
                {
                    foreach (string bloqueado
                             in DestinosBloqueados)
                    {
                        if (destino.Contains(
                                bloqueado,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return Bloquear(
                                $"No está permitido iniciar " +
                                $"el destino '{destino}'.");
                        }
                    }
                }
            }


            // ================================================
            // SI NO HA COINCIDIDO CON NINGUNA RESTRICCIÓN,
            // EL COMANDO SE PERMITE.
            // ================================================

            return new(
                true,
                "El comando no contiene operaciones restringidas.");
        }


        // ====================================================
        // CARGAR LA "BASE DE DATOS" DE RESTRICCIONES
        // ====================================================

        private static void CargarRestricciones()
        {
            string[] lineas =
                BaseRestriccionesPowerShell
                    .Datos
                    .Split(
                        new[]
                        {
                            "\r\n",
                            "\n"
                        },
                        StringSplitOptions.None);

            foreach (string lineaOriginal
                     in lineas)
            {
                string linea =
                    lineaOriginal.Trim();

                if (string.IsNullOrWhiteSpace(
                        linea))
                {
                    continue;
                }

                if (linea.StartsWith(
                        "#",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string[] partes =
                    linea.Split(
                        '|',
                        StringSplitOptions.None);

                if (partes.Length < 2)
                {
                    continue;
                }

                string tipo =
                    partes[0].Trim();

                string valor =
                    partes[1].Trim();

                switch (tipo.ToUpperInvariant())
                {
                    case "CMD":
                        if (!string.IsNullOrWhiteSpace(
                                valor))
                        {
                            ComandosBloqueados.Add(
                                valor);
                        }

                        break;


                    case "PREFIX":
                        if (!string.IsNullOrWhiteSpace(
                                valor))
                        {
                            PrefijosBloqueados.Add(
                                valor);
                        }

                        break;


                    case "TARGET":
                        if (!string.IsNullOrWhiteSpace(
                                valor))
                        {
                            DestinosBloqueados.Add(
                                valor);
                        }

                        break;


                    case "TEXT":
                        if (!string.IsNullOrWhiteSpace(
                                valor))
                        {
                            TextosBloqueados.Add(
                                valor);
                        }

                        break;


                    case "SYNTAX":
                        if (!string.IsNullOrWhiteSpace(
                                valor))
                        {
                            SintaxisBloqueada.Add(
                                valor);
                        }

                        break;


                    case "ARG":
                        if (partes.Length >= 3)
                        {
                            string comando =
                                partes[1].Trim();

                            string argumento =
                                string.Join(
                                    "|",
                                    partes.Skip(2))
                                    .Trim();

                            if (!string.IsNullOrWhiteSpace(
                                    comando)
                                &&
                                !string.IsNullOrWhiteSpace(
                                    argumento))
                            {
                                ArgumentosBloqueados.Add(
                                    new ReglaArgumento(
                                        comando,
                                        argumento));
                            }
                        }

                        break;
                }
            }
        }


        // ====================================================
        // EXTRAER COMANDOS DE UNA LÍNEA POWERSHELL
        //
        // Ejemplo:
        //
        // Get-Process | Stop-Process
        //
        // devuelve:
        //
        // Get-Process
        // Stop-Process
        //
        // Se respetan comillas para no separar dentro
        // de cadenas de texto.
        // ====================================================

        private static List<string> ExtraerComandos(
            string comando)
        {
            var resultado =
                new List<string>();

            List<string> segmentos =
                SepararSegmentos(
                    comando);

            foreach (string segmentoOriginal
                     in segmentos)
            {
                string segmento =
                    segmentoOriginal.Trim();

                if (string.IsNullOrWhiteSpace(
                        segmento))
                {
                    continue;
                }

                // Eliminamos caracteres estructurales
                // al principio.
                segmento =
                    segmento.TrimStart(
                        '(',
                        ')',
                        '{',
                        '}',
                        ' ',
                        '\t');

                if (string.IsNullOrWhiteSpace(
                        segmento))
                {
                    continue;
                }


                // Operador de llamada:
                //
                // & "comando"
                //
                if (segmento.StartsWith(
                        "&",
                        StringComparison.Ordinal))
                {
                    string despues =
                        segmento[1..]
                            .TrimStart();

                    string? comandoInvocado =
                        LeerPrimerElemento(
                            despues);

                    if (!string.IsNullOrWhiteSpace(
                            comandoInvocado))
                    {
                        resultado.Add(
                            comandoInvocado);
                    }

                    continue;
                }


                string? primerElemento =
                    LeerPrimerElemento(
                        segmento);

                if (!string.IsNullOrWhiteSpace(
                        primerElemento))
                {
                    resultado.Add(
                        primerElemento);
                }
            }

            return resultado;
        }


        private static List<string> SepararSegmentos(
            string texto)
        {
            var segmentos =
                new List<string>();

            var actual =
                new StringBuilder();

            bool dentroComillasDobles =
                false;

            bool dentroComillasSimples =
                false;

            for (int i = 0;
                 i < texto.Length;
                 i++)
            {
                char c =
                    texto[i];

                if (c == '"'
                    &&
                    !dentroComillasSimples)
                {
                    dentroComillasDobles =
                        !dentroComillasDobles;

                    actual.Append(c);

                    continue;
                }

                if (c == '\''
                    &&
                    !dentroComillasDobles)
                {
                    dentroComillasSimples =
                        !dentroComillasSimples;

                    actual.Append(c);

                    continue;
                }

                if (!dentroComillasDobles
                    &&
                    !dentroComillasSimples
                    &&
                    (
                        c == '|'
                        ||
                        c == ';'
                        ||
                        c == '\r'
                        ||
                        c == '\n'
                    ))
                {
                    AgregarSegmento(
                        segmentos,
                        actual);

                    continue;
                }

                actual.Append(c);
            }

            AgregarSegmento(
                segmentos,
                actual);

            return segmentos;
        }


        private static void AgregarSegmento(
            List<string> segmentos,
            StringBuilder actual)
        {
            string contenido =
                actual
                    .ToString()
                    .Trim();

            if (!string.IsNullOrWhiteSpace(
                    contenido))
            {
                segmentos.Add(
                    contenido);
            }

            actual.Clear();
        }


        private static string? LeerPrimerElemento(
            string texto)
        {
            texto =
                texto.TrimStart();

            if (string.IsNullOrWhiteSpace(
                    texto))
            {
                return null;
            }

            if (texto[0] == '"'
                ||
                texto[0] == '\'')
            {
                char comilla =
                    texto[0];

                int final =
                    texto.IndexOf(
                        comilla,
                        1);

                if (final > 1)
                {
                    return texto[
                        1..final];
                }

                return null;
            }

            int espacio =
                texto.IndexOfAny(
                    new[]
                    {
                        ' ',
                        '\t'
                    });

            if (espacio < 0)
            {
                return texto;
            }

            return texto[..espacio];
        }


        // ====================================================
        // START-PROCESS
        // ====================================================

        private static string? ObtenerDestinoStartProcess(
            string comando)
        {
            int posicion =
                comando.IndexOf(
                    "Start-Process",
                    StringComparison.OrdinalIgnoreCase);

            if (posicion < 0)
            {
                return null;
            }

            string resto =
                comando[
                    (posicion +
                     "Start-Process".Length)..]
                    .TrimStart();

            if (string.IsNullOrWhiteSpace(
                    resto))
            {
                return null;
            }

            // Permitimos también:
            //
            // Start-Process -FilePath notepad
            //
            if (resto.StartsWith(
                    "-FilePath",
                    StringComparison.OrdinalIgnoreCase))
            {
                resto =
                    resto[
                        "-FilePath".Length..]
                        .TrimStart();
            }

            return LeerPrimerElemento(
                resto);
        }


        private static bool ContieneComando(
            IEnumerable<string> comandos,
            string buscado)
        {
            return comandos.Any(
                comando =>
                    comando.Equals(
                        buscado,
                        StringComparison.OrdinalIgnoreCase));
        }


        private static ResultadoValidacionPowerShell
            Bloquear(
                string motivo)
        {
            return new(
                false,
                motivo);
        }


        private sealed record ReglaArgumento(
            string Comando,
            string Argumento);
    }
}