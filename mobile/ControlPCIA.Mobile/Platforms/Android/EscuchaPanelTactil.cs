namespace ControlPCIA.Mobile.Platforms.Android;

internal enum FasePanelTactil
{
    Pulsado,
    Movido,
    Soltado,
    Cancelado
}

internal sealed class EscuchaPanelTactil(
    Action<FasePanelTactil, float, float, long> alCambiar)
    : Java.Lang.Object, global::Android.Views.View.IOnTouchListener
{
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
                vista.Parent?.RequestDisallowInterceptTouchEvent(true);
                alCambiar(
                    FasePanelTactil.Pulsado,
                    evento.RawX,
                    evento.RawY,
                    evento.EventTime);
                return true;

            case global::Android.Views.MotionEventActions.Move:
                alCambiar(
                    FasePanelTactil.Movido,
                    evento.RawX,
                    evento.RawY,
                    evento.EventTime);
                return true;

            case global::Android.Views.MotionEventActions.Up:
                vista.Parent?.RequestDisallowInterceptTouchEvent(false);
                alCambiar(
                    FasePanelTactil.Soltado,
                    evento.RawX,
                    evento.RawY,
                    evento.EventTime);
                vista.PerformClick();
                return true;

            case global::Android.Views.MotionEventActions.Cancel:
                vista.Parent?.RequestDisallowInterceptTouchEvent(false);
                alCambiar(
                    FasePanelTactil.Cancelado,
                    evento.RawX,
                    evento.RawY,
                    evento.EventTime);
                return true;

            default:
                return true;
        }
    }
}
