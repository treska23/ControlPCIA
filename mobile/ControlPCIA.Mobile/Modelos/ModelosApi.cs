namespace ControlPCIA.Mobile.Modelos;

public sealed record PcDescubierto(
    string Nombre,
    string Direccion)
{
    public string Descripcion => $"{Nombre} · {Direccion}";
}

public sealed record EstadoPc(
    bool Disponible,
    bool Ocupado,
    int RecetasAprendidas,
    string? Modelo,
    string? Mensaje,
    bool ModoPrueba,
    IReadOnlyList<DestinoWakeOnLan>? WakeOnLan);

public sealed record DestinoWakeOnLan(
    string Nombre,
    string Mac,
    int Puerto,
    IReadOnlyList<string>? DireccionesBroadcast);

public sealed record PasoOrden(
    int Numero,
    string? Comando,
    bool Ejecutado,
    int CodigoSalida,
    string? Salida,
    string? Error);

public sealed record MensajeConversacion(
    string Rol,
    string Texto);

public sealed record ResultadoOrden(
    bool Completado,
    string? Estado,
    string? Mensaje,
    IReadOnlyList<PasoOrden>? Pasos,
    bool Aprendido);
