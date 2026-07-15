namespace ControlPCIA
{
    internal static class EjecutorWindows
    {
        public static async Task EjecutarAsync(string siguientePaso)
        {
            Console.WriteLine();
            Console.WriteLine("EJECUTOR WINDOWS:");
            Console.WriteLine(siguientePaso);

            // Aquí intentaremos, por este orden:
            //
            // 1. APIs nativas de Windows
            // 2. UI Automation
            // 3. Control multimedia
            // 4. Teclado/ratón como último recurso

            await Task.CompletedTask;
        }
    }
}
