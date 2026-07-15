# ControlPCIA — Estado del proyecto

Última actualización: 15 de julio de 2026

## Objetivo

Crear una aplicación para controlar Windows mediante lenguaje natural. El usuario podrá dar órdenes habladas o escritas y un modelo local ejecutado con Ollama interpretará la intención y actuará sobre el PC.

Ejemplos de órdenes objetivo:

- "Abre Spotify y busca Radiohead"
- "Baja el volumen al 30 %"
- "Cierra esa ventana"
- "Pon el vídeo a pantalla completa"

## Decisión arquitectónica actual

La frase natural del usuario es la orden.

La dirección actual del proyecto cambia respecto a la arquitectura anterior basada principalmente en tool calling y UI Automation.

La nueva ruta principal será:

```text
Entrada de voz o texto
→ frase natural
→ Ollama / Qwen interpreta la intención
→ genera uno o varios comandos reales de PowerShell
→ ValidadorPowerShell local y determinista
→ si el comando es seguro, EjecutorPowerShell lo ejecuta
→ se recoge stdout / stderr / código de salida
→ solo se vuelve a consultar a la IA si hace falta continuar
```

La validación de seguridad NO debe usar otra IA ni llamadas externas. Debe ejecutarse localmente y ser prácticamente instantánea.

## Seguridad

PowerShell se usará como vía principal de actuación, pero Qwen no tendrá acceso libre al sistema.

Antes de ejecutar cualquier comando:

1. Se analiza localmente.
2. Se extraen los cmdlets/comandos utilizados.
3. Se resuelven alias cuando sea necesario.
4. Se validan comandos y argumentos contra una política de seguridad.
5. Se bloquea cualquier operación sensible.

La política debe ser principalmente de lista blanca, complementada con bloqueos explícitos.

### Debe bloquearse

- Lectura, escritura, creación, copia, movimiento, renombrado o borrado de archivos.
- Acceso arbitrario a rutas locales.
- Registro de Windows.
- Descargas y acceso de red no autorizado.
- Instalación de programas.
- Cambios de permisos.
- Elevación a administrador.
- Ejecución de scripts arbitrarios.
- Invoke-Expression o ejecución dinámica equivalente.
- Lanzar PowerShell o CMD anidados para saltarse el validador.
- Cualquier otro mecanismo que permita escapar de la política.

### Puede permitirse gradualmente

- Consulta y control seguro de procesos.
- Abrir aplicaciones bajo condiciones validadas.
- Activar, restaurar, minimizar o maximizar ventanas.
- Control de audio y multimedia.
- Otras operaciones de Windows que no impliquen archivos ni zonas sensibles.

## Componentes que ya existen y se conservan

- `Program.cs`: entrada actual por texto.
- `ControlWindows.cs`: orquestación con Ollama; debe migrarse desde tool calling hacia generación de comandos.
- `ObservadorWindows.cs`: enumera ventanas visibles y detecta la ventana activa.
- `ObservadorUIWindows.cs`: inspección mediante Windows UI Automation.
- `AplicacionesWindows.cs`: abre aplicaciones instaladas y trae al frente una ya abierta.
- `EjecutorWindows.cs`: capa actual de herramientas; se irá sustituyendo o integrando con la nueva vía de PowerShell.
- `EjecutorInterfazWindows.cs`: teclado como mecanismo de respaldo.
- `InteraccionUIWindows.cs`: si existe en local, debe conservarse como fallback para acciones que PowerShell no pueda resolver bien.

UI Automation, teclado y ratón pasan a ser mecanismos secundarios o de respaldo, no la vía principal.

## Estado probado

Ya se ha validado que:

- Ollama recibe una orden natural.
- Qwen interpreta correctamente órdenes como `abre spotify`.
- `AplicacionesWindows` puede abrir Spotify directamente sin mostrar el menú Inicio.
- Si Spotify ya está abierto, puede restaurarlo y traerlo al frente.
- `ObservadorWindows` enumera ventanas visibles.
- `ObservadorUIWindows` puede inspeccionar controles accesibles de Spotify.

## Nueva necesidad: memoria local de comandos / recetas

La vía de PowerShell no garantiza que el modelo conozca de inmediato la mejor forma de realizar cualquier acción sobre cualquier aplicación o parte de Windows.

Para evitar que el usuario tenga que esperar cada vez a que la IA vuelva a descubrir cómo hacer una operación, ControlPCIA debe tener una memoria local persistente de comandos o recetas ya resueltas y validadas.

La idea es:

```text
Petición del usuario
→ ¿existe una receta conocida y validada para esta intención/contexto?
    ├─ SÍ → reutilizarla inmediatamente
    └─ NO → pedir a la IA que proponga la solución
             → validar
             → ejecutar
             → si funciona, guardar la receta para futuras veces
```

Esta memoria debe almacenar solo soluciones que hayan sido validadas por la política de seguridad.

No debe guardar comandos peligrosos ni saltarse nunca `ValidadorPowerShell`.

Una receta guardada también debe volver a pasar por validación antes de ejecutarse, para que cambios futuros en la política de seguridad se apliquen automáticamente.

### Posibles datos a guardar por receta

- Intención normalizada.
- Contexto o aplicación objetivo.
- Comando o secuencia de comandos.
- Fecha de última validación.
- Número de ejecuciones correctas.
- Último resultado conocido.
- Versión o huella del entorno relevante si hiciera falta.

### Implementación sugerida

Empezar con una base local sencilla, probablemente SQLite, para poder buscar recetas rápidamente sin cargar archivos completos ni consultar a la IA.

También conviene mantener una caché en memoria de las recetas más usadas durante la ejecución de la aplicación.

La prioridad será siempre:

```text
receta local validada y conocida
→ comando PowerShell directo y seguro
→ UI Automation
→ teclado/ratón como último recurso
```

## Siguiente plan de trabajo

1. Asegurar que todos los últimos cambios locales están realmente subidos a GitHub.
2. Crear `ValidadorPowerShell.cs`.
3. Crear `EjecutorPowerShell.cs`.
4. Modificar `ControlWindows.cs` para que Qwen genere comandos PowerShell en lugar de tool calls.
5. Probar una lista inicial muy pequeña de comandos seguros.
6. Verificar que comandos de archivos, registro, red y administración se bloquean de forma instantánea.
7. Añadir la memoria local de recetas/comandos validados.
8. Mantener UI Automation y entrada de teclado como fallback.
9. Cuando el control por texto sea sólido, añadir entrada por voz.

## Regla de continuidad

Este archivo es la referencia principal de decisiones y estado del proyecto.

Si se abre un chat nuevo, empezar por:

> Lee `ESTADO_PROYECTO.md` del repositorio `treska23/ControlPCIA` y seguimos desde ahí.

El código del repositorio y este archivo son la fuente de verdad del proyecto, no el historial de una conversación concreta.
