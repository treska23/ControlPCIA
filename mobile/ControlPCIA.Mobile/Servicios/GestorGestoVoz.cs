namespace ControlPCIA.Mobile.Servicios;

internal enum EstadoGestoVoz
{
    Reposo,
    Pulsando,
    Anclado
}

internal enum AccionGestoVoz
{
    Ninguna,
    IniciarEscucha,
    Anclar,
    Enviar,
    Cancelar
}

internal sealed class GestorGestoVoz(double umbralAnclajeDp)
{
    public EstadoGestoVoz Estado { get; private set; }

    public double CalcularProgreso(double distanciaDp)
    {
        return Math.Clamp(
            Math.Max(0, distanciaDp)
            / umbralAnclajeDp,
            0,
            1);
    }

    public AccionGestoVoz Pulsar()
    {
        if (Estado == EstadoGestoVoz.Anclado)
        {
            Estado = EstadoGestoVoz.Reposo;
            return AccionGestoVoz.Enviar;
        }

        if (Estado != EstadoGestoVoz.Reposo)
        {
            return AccionGestoVoz.Ninguna;
        }

        Estado = EstadoGestoVoz.Pulsando;
        return AccionGestoVoz.IniciarEscucha;
    }

    public AccionGestoVoz Mover(double distanciaDp)
    {
        if (Estado != EstadoGestoVoz.Pulsando
            || distanciaDp < umbralAnclajeDp)
        {
            return AccionGestoVoz.Ninguna;
        }

        Estado = EstadoGestoVoz.Anclado;
        return AccionGestoVoz.Anclar;
    }

    public AccionGestoVoz Soltar()
    {
        if (Estado != EstadoGestoVoz.Pulsando)
        {
            return AccionGestoVoz.Ninguna;
        }

        Estado = EstadoGestoVoz.Reposo;
        return AccionGestoVoz.Enviar;
    }

    public AccionGestoVoz Cancelar()
    {
        if (Estado != EstadoGestoVoz.Pulsando)
        {
            return AccionGestoVoz.Ninguna;
        }

        Estado = EstadoGestoVoz.Reposo;
        return AccionGestoVoz.Cancelar;
    }

    public void Restablecer()
    {
        Estado = EstadoGestoVoz.Reposo;
    }
}
