namespace ControlPCIA.Mobile.Platforms.Android;

internal sealed class EscuchaTeclaRepetida(
    Action<bool> alCambiar)
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
                vista.Pressed = true;
                alCambiar(true);
                return true;

            case global::Android.Views.MotionEventActions.Up:
                vista.Pressed = false;
                alCambiar(false);
                vista.PerformClick();
                return true;

            case global::Android.Views.MotionEventActions.Cancel:
                vista.Pressed = false;
                alCambiar(false);
                return true;

            default:
                return true;
        }
    }
}
