using System.Text.Json;
using System.IO;

namespace ControlPCIA;

internal sealed class AlmacenSesionesMoviles
{
    private const int VersionActual = 1;
    private const int MaximoSesiones = 20;

    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _ruta;
    private readonly object _puerta = new();

    public static AlmacenSesionesMoviles Predeterminado { get; } =
        new(ObtenerRutaPredeterminada());

    internal AlmacenSesionesMoviles(string ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta))
        {
            throw new ArgumentException(
                "La ruta de sesiones móviles no puede estar vacía.",
                nameof(ruta));
        }

        _ruta = Path.GetFullPath(ruta);
    }

    public IReadOnlyDictionary<string, DateTimeOffset> Cargar()
    {
        lock (_puerta)
        {
            if (!File.Exists(_ruta))
            {
                return new Dictionary<string, DateTimeOffset>();
            }

            try
            {
                string json = File.ReadAllText(_ruta);
                AlmacenPersistido? almacen =
                    JsonSerializer.Deserialize<AlmacenPersistido>(
                        json,
                        OpcionesJson);

                if (almacen?.Version != VersionActual)
                {
                    return new Dictionary<string, DateTimeOffset>();
                }

                DateTimeOffset ahora = DateTimeOffset.UtcNow;

                return almacen.Sesiones
                    .Where(sesion =>
                        EsHashValido(sesion.Hash)
                        && sesion.CaducidadUtc > ahora)
                    .OrderByDescending(sesion => sesion.CaducidadUtc)
                    .Take(MaximoSesiones)
                    .ToDictionary(
                        sesion => sesion.Hash,
                        sesion => sesion.CaducidadUtc,
                        StringComparer.Ordinal);
            }
            catch (Exception ex) when (
                ex is IOException
                or UnauthorizedAccessException
                or JsonException)
            {
                return new Dictionary<string, DateTimeOffset>();
            }
        }
    }

    public void Guardar(
        IEnumerable<KeyValuePair<string, DateTimeOffset>> sesiones)
    {
        lock (_puerta)
        {
            DateTimeOffset ahora = DateTimeOffset.UtcNow;
            SesionPersistida[] validas = sesiones
                .Where(sesion =>
                    EsHashValido(sesion.Key)
                    && sesion.Value > ahora)
                .OrderByDescending(sesion => sesion.Value)
                .Take(MaximoSesiones)
                .Select(sesion => new SesionPersistida
                {
                    Hash = sesion.Key,
                    CaducidadUtc = sesion.Value
                })
                .ToArray();
            string? carpeta = Path.GetDirectoryName(_ruta);

            if (string.IsNullOrWhiteSpace(carpeta))
            {
                throw new InvalidOperationException(
                    "La memoria de sesiones no tiene una carpeta válida.");
            }

            Directory.CreateDirectory(carpeta);
            string temporal =
                Path.Combine(
                    carpeta,
                    $".{Path.GetFileName(_ruta)}.{Guid.NewGuid():N}.tmp");

            try
            {
                string json = JsonSerializer.Serialize(
                    new AlmacenPersistido
                    {
                        Version = VersionActual,
                        Sesiones = validas.ToList()
                    },
                    OpcionesJson);
                File.WriteAllText(temporal, json);
                File.Move(temporal, _ruta, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporal))
                {
                    File.Delete(temporal);
                }
            }
        }
    }

    private static bool EsHashValido(string? hash)
    {
        return hash is { Length: 64 }
               && hash.All(caracter =>
                   char.IsAsciiHexDigit(caracter));
    }

    private static string ObtenerRutaPredeterminada()
    {
        string carpeta = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(carpeta))
        {
            throw new InvalidOperationException(
                "Windows no devolvió la carpeta local para las sesiones móviles.");
        }

        return Path.Combine(
            carpeta,
            "ControlPCIA",
            "sesiones-v1.json");
    }

    private sealed class AlmacenPersistido
    {
        public int Version { get; set; } = VersionActual;
        public List<SesionPersistida> Sesiones { get; set; } = [];
    }

    private sealed class SesionPersistida
    {
        public string Hash { get; set; } = string.Empty;
        public DateTimeOffset CaducidadUtc { get; set; }
    }
}
