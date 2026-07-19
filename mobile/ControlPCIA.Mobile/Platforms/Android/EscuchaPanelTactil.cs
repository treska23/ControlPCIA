using ControlPCIA.Mobile.Servicios;

namespace ControlPCIA.Mobile.Platforms.Android;

internal sealed class EscuchaPanelTactil(
    Action<
        FaseGestoTrackpad,
        int,
        float,
        float,
        long> alCambiar)
    : Java.Lang.Object, global::Android.Views.View.IOnTouchListener
{
    private bool _ignorarSiguienteSoltado;

    public bool OnTouch(
        global::Android.Views.View? vista,
        global::Android.Views.MotionEvent? evento)
    {
        if (vista is null || evento is null)
        {
            return false;
        }

        switch (evento.ActionMasked)
        {
            case global::Android.Views.MotionEventActions.Down:
                _ignorarSiguienteSoltado = false;
                vista.Parent?.RequestDisallowInterceptTouchEvent(true);
                alCambiar(
                    FaseGestoTrackpad.Pulsado,
                    1,
                    CentroX(evento),
                    CentroY(evento),
                    evento.EventTime);
                return true;

            case global::Android.Views.MotionEventActions.PointerDown:
                if (evento.PointerCount == 2)
                {
                    alCambiar(
                        FaseGestoTrackpad.Pulsado,
                        2,
                        CentroX(evento),
                        CentroY(evento),
                        evento.EventTime);
                }
                else
                {
                    alCambiar(
                        FaseGestoTrackpad.Cancelado,
                        evento.PointerCount,
                        CentroX(evento),
                        CentroY(evento),
                        evento.EventTime);
                }

                return true;

            case global::Android.Views.MotionEventActions.Move:
                alCambiar(
                    FaseGestoTrackpad.Movido,
                    evento.PointerCount,
                    CentroX(evento),
                    CentroY(evento),
                    evento.EventTime);
                return true;

            case global::Android.Views.MotionEventActions.PointerUp:
                if (evento.PointerCount == 2)
                {
                    alCambiar(
                        FaseGestoTrackpad.Soltado,
                        2,
                        CentroX(evento),
                        CentroY(evento),
                        evento.EventTime);
                    _ignorarSiguienteSoltado = true;
                }
                else
                {
                    alCambiar(
                        FaseGestoTrackpad.Cancelado,
                        evento.PointerCount,
                        CentroX(evento),
                        CentroY(evento),
                        evento.EventTime);
                }

                return true;

            case global::Android.Views.MotionEventActions.Up:
                vista.Parent?.RequestDisallowInterceptTouchEvent(false);

                if (_ignorarSiguienteSoltado)
                {
                    _ignorarSiguienteSoltado = false;
                }
                else
                {
                    alCambiar(
                        FaseGestoTrackpad.Soltado,
                        1,
                        CentroX(evento),
                        CentroY(evento),
                        evento.EventTime);
                }

                vista.PerformClick();
                return true;

            case global::Android.Views.MotionEventActions.Cancel:
                vista.Parent?.RequestDisallowInterceptTouchEvent(false);
                _ignorarSiguienteSoltado = false;
                alCambiar(
                    FaseGestoTrackpad.Cancelado,
                    evento.PointerCount,
                    CentroX(evento),
                    CentroY(evento),
                    evento.EventTime);
                return true;

            default:
                return true;
        }
    }

    private static float CentroX(
        global::Android.Views.MotionEvent evento)
    {
        float suma = 0;

        for (int indice = 0;
             indice < evento.PointerCount;
             indice++)
        {
            suma += evento.GetX(indice);
        }

        return suma / Math.Max(evento.PointerCount, 1);
    }

    private static float CentroY(
        global::Android.Views.MotionEvent evento)
    {
        float suma = 0;

        for (int indice = 0;
             indice < evento.PointerCount;
             indice++)
        {
            suma += evento.GetY(indice);
        }

        return suma / Math.Max(evento.PointerCount, 1);
    }
}
