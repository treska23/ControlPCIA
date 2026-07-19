namespace ControlPCIA.Mobile.Platforms.Android;

internal enum FaseToqueVoz
{
    Pulsado,
    Movido,
    Soltado,
    Cancelado
}

internal sealed class EscuchaToqueVoz(
    Action<FaseToqueVoz, float> alCambiar)
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
                global::Android.Util.Log.Debug(
                    "ControlPCIA.Voz",
                    "Pulsado");
                alCambiar(FaseToqueVoz.Pulsado, evento.RawY);
                return true;

            case global::Android.Views.MotionEventActions.Move:
                alCambiar(FaseToqueVoz.Movido, evento.RawY);
                return true;

            case global::Android.Views.MotionEventActions.Up:
                vista.Parent?.RequestDisallowInterceptTouchEvent(false);
                global::Android.Util.Log.Debug(
                    "ControlPCIA.Voz",
                    "Soltado");
                alCambiar(FaseToqueVoz.Soltado, evento.RawY);
                vista.PerformClick();
                return true;

            case global::Android.Views.MotionEventActions.Cancel:
                vista.Parent?.RequestDisallowInterceptTouchEvent(false);
                global::Android.Util.Log.Debug(
                    "ControlPCIA.Voz",
                    "Cancelado");
                alCambiar(FaseToqueVoz.Cancelado, evento.RawY);
                return true;

            default:
                return true;
        }
    }
}
