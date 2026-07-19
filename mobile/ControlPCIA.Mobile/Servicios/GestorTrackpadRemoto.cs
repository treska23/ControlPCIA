namespace ControlPCIA.Mobile.Servicios;

public enum FaseGestoTrackpad
{
    Pulsado,
    Movido,
    Soltado,
    Cancelado
}

public enum AccionGestoTrackpad
{
    Ninguna,
    ClicIzquierdo,
    ClicDerecho
}

public sealed record ResultadoGestoTrackpad(
    double DeltaX = 0,
    double DeltaY = 0,
    int Rueda = 0,
    AccionGestoTrackpad Accion =
        AccionGestoTrackpad.Ninguna);

/// <summary>
/// Convierte gestos táctiles en entrada de ratón sin depender de Android.
/// Las coordenadas que recibe deben estar expresadas en dp.
/// </summary>
public sealed class GestorTrackpadRemoto
{
    private const double RecorridoMaximoTapDp = 18;
    private const double UmbralArrastreRuedaDp = 4;
    private const long DuracionMaximaTapUnDedoMs = 550;
    private const long DuracionMaximaTapDosDedosMs = 750;
    private const double SensibilidadPuntero = 1.35;
    private const double SensibilidadRueda = 4;

    private int _dedos;
    private double _ultimaX;
    private double _ultimaY;
    private double _recorrido;
    private long _inicio;
    private bool _cancelado;
    private bool _ruedaActiva;

    public ResultadoGestoTrackpad Procesar(
        FaseGestoTrackpad fase,
        int dedos,
        double x,
        double y,
        long tiempo)
    {
        return fase switch
        {
            FaseGestoTrackpad.Pulsado =>
                Iniciar(dedos, x, y, tiempo),
            FaseGestoTrackpad.Movido =>
                Mover(dedos, x, y),
            FaseGestoTrackpad.Soltado =>
                Soltar(dedos, tiempo),
            _ => Cancelar()
        };
    }

    private ResultadoGestoTrackpad Iniciar(
        int dedos,
        double x,
        double y,
        long tiempo)
    {
        _dedos = dedos is 1 or 2
            ? dedos
            : 0;
        _ultimaX = x;
        _ultimaY = y;
        _recorrido = 0;
        _inicio = tiempo;
        _cancelado = _dedos == 0;
        _ruedaActiva = false;
        return new ResultadoGestoTrackpad();
    }

    private ResultadoGestoTrackpad Mover(
        int dedos,
        double x,
        double y)
    {
        if (_cancelado
            || _dedos == 0
            || dedos != _dedos)
        {
            _cancelado = true;
            return new ResultadoGestoTrackpad();
        }

        double deltaX =
            x - _ultimaX;
        double deltaY =
            y - _ultimaY;
        _ultimaX = x;
        _ultimaY = y;

        if (_dedos == 1)
        {
            _recorrido +=
                Math.Abs(deltaX)
                + Math.Abs(deltaY);
            return new ResultadoGestoTrackpad(
                deltaX * SensibilidadPuntero,
                deltaY * SensibilidadPuntero);
        }

        _recorrido +=
            Math.Abs(deltaX)
            + Math.Abs(deltaY);

        if (!_ruedaActiva
            && _recorrido <= UmbralArrastreRuedaDp)
        {
            return new ResultadoGestoTrackpad();
        }

        _ruedaActiva = true;
        int rueda =
            (int)Math.Round(
                -deltaY * SensibilidadRueda);
        return new ResultadoGestoTrackpad(
            Rueda: Math.Clamp(rueda, -2_400, 2_400));
    }

    private ResultadoGestoTrackpad Soltar(
        int dedos,
        long tiempo)
    {
        ResultadoGestoTrackpad resultado =
            new();

        if (!_cancelado
            && dedos == _dedos
            && !_ruedaActiva
            && _recorrido <= RecorridoMaximoTapDp)
        {
            long duracion =
                tiempo - _inicio;

            if (_dedos == 1
                && duracion <= DuracionMaximaTapUnDedoMs)
            {
                resultado =
                    new ResultadoGestoTrackpad(
                        Accion:
                            AccionGestoTrackpad
                                .ClicIzquierdo);
            }
            else if (_dedos == 2
                     && duracion
                     <= DuracionMaximaTapDosDedosMs)
            {
                resultado =
                    new ResultadoGestoTrackpad(
                        Accion:
                            AccionGestoTrackpad
                                .ClicDerecho);
            }
        }

        Restablecer();
        return resultado;
    }

    private ResultadoGestoTrackpad Cancelar()
    {
        Restablecer();
        return new ResultadoGestoTrackpad();
    }

    private void Restablecer()
    {
        _dedos = 0;
        _recorrido = 0;
        _inicio = 0;
        _cancelado = false;
        _ruedaActiva = false;
    }
}
