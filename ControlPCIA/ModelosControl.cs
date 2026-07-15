namespace ControlPCIA;

internal sealed record ResultadoPasoControl(
    int Numero,
    string Comando,
    bool Ejecutado,
    int CodigoSalida,
    string Salida,
    string Error);

internal sealed record EventoControl(
    string Tipo,
    string Mensaje,
    string? Comando = null,
    ResultadoPasoControl? Paso = null);

internal sealed record ResultadoControl(
    bool Completado,
    string Estado,
    string Mensaje,
    IReadOnlyList<ResultadoPasoControl> Pasos,
    bool Aprendido);
