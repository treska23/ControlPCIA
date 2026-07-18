namespace ControlPCIA;

/// <summary>
/// Referencia legible de la política de consola. El análisis efectivo se hace
/// sobre el AST en <see cref="ValidadorPowerShell"/> para que alias, tuberías y
/// bloques anidados no puedan ocultar una operación prohibida.
/// </summary>
internal static class BaseRestriccionesPowerShell
{
    public const string Datos = """
        # POLÍTICA MÍNIMA DE CONTROLPCIA
        #
        # Sólo hay tres prohibiciones sobre el equipo:
        # 1. Eliminar elementos o contenido.
        # 2. Mover o cortar elementos.
        # 3. Formatear, limpiar o reinicializar discos y unidades.
        #
        # Todo lo demás que pueda invocarse por consola está permitido:
        # leer, buscar, crear, copiar, sobrescribir, abrir, guardar, descargar,
        # instalar, configurar Windows, usar registro, servicios, red, módulos,
        # ejecutables, APIs y automatización propia de las aplicaciones.
        #
        # La automatización gráfica antigua (ControlPCIA ui, SendKeys,
        # AppActivate y UIAutomation) no forma parte del producto: no es una
        # cuarta restricción de seguridad, sino una interfaz retirada.

        CMD|Remove-Item
        CMD|Clear-Content
        CMD|Move-Item
        CMD|Format-Volume
        CMD|Clear-Disk
        CMD|Initialize-Disk
        CMD|diskpart
        CMD|format
        """;
}
