# ControlPCIA — Estado del proyecto

Última actualización: 15 de julio de 2026

## Objetivo

Crear una aplicación para controlar Windows mediante lenguaje natural, pensada para recibir órdenes habladas o escritas y permitir que un modelo local ejecutado con Ollama interprete la intención del usuario y actúe sobre el PC.

La idea central es que el usuario pueda decir frases naturales como:

- "Abre Spotify y pon Radiohead"
- "Baja un poco el volumen"
- "Cierra esa ventana"
- "Pon el vídeo a pantalla completa"

El sistema no debe depender de un catálogo cerrado de comandos específicos por aplicación.

## Decisión arquitectónica principal

La frase del usuario es la orden.

El modelo de IA es el intérprete y debe decidir qué capacidad segura necesita utilizar para cumplirla.

No queremos mantener un JSON propio con una lista creciente de intenciones específicas por aplicación. Ollama sí usa JSON internamente en su API HTTP y en tool calls, pero eso es solo el protocolo técnico de comunicación.

La IA no debe recibir una capacidad genérica para ejecutar comandos arbitrarios del sistema. El programa expone únicamente capacidades concretas y limitadas.

## Arquitectura objetivo

Entrada de voz o texto
→ frase natural del usuario
→ agente de control
→ modelo local mediante Ollama
→ observación del estado de Windows
→ selección de una capacidad segura
→ ejecución sobre Windows
→ nueva observación cuando sea necesario
→ continuar hasta completar la petición

Componentes conceptuales:

- **Modelo local (Ollama / qwen3:8b):** cerebro que interpreta la orden y decide qué capacidad utilizar.
- **ObservadorWindows:** obtiene información del estado visible del sistema, actualmente ventanas visibles y ventana activa.
- **AplicacionesWindows:** capa para localizar e iniciar aplicaciones instaladas mediante Windows sin abrir el menú Inicio ni usar rutas arbitrarias.
- **EjecutorWindows:** capa que valida y ejecuta las capacidades permitidas.
- **EjecutorInterfazWindows:** utilidad de bajo nivel para teclado como mecanismo de respaldo futuro.

## Estado actual del código

El proyecto local contiene actualmente:

- `Program.cs`
- `ControlWindows.cs`
- `ObservadorWindows.cs`
- `AplicacionesWindows.cs`
- `EjecutorWindows.cs`
- `EjecutorInterfazWindows.cs`

El proyecto está orientado exclusivamente a Windows y debe usar:

```xml
<TargetFramework>net10.0-windows</TargetFramework>
```

## Lo que ya funciona

- Comunicación con Ollama mediante `http://localhost:11434/api/chat`.
- Uso actual del modelo `qwen3:8b`.
- La orden natural del usuario llega correctamente a la IA.
- La IA usa tool calling para elegir capacidades expuestas por el programa.
- Lectura de ventanas visibles mediante `ObservadorWindows`.
- Detección de la ventana activa mediante `GetForegroundWindow`.
- Infraestructura de teclado mediante `SendInput` en `EjecutorInterfazWindows`.
- Inicio directo de aplicaciones instaladas mediante `AplicacionesWindows` y `shell:AppsFolder`.
- El inicio directo no abre el menú Inicio, no escribe en ninguna ventana y no usa PowerShell ni CMD.

## Prueba validada actual

Entrada:

```text
abre spotify
```

Resultado observado:

```text
CAPACIDAD ELEGIDA:
iniciar_aplicacion
La aplicación 'Spotify' se ha iniciado.
```

Spotify se inicia directamente desde Windows sin mostrar el menú Inicio.

Esto valida el flujo:

```text
frase natural
→ IA interpreta la intención
→ iniciar_aplicacion("Spotify")
→ Windows localiza la aplicación instalada
→ la aplicación se inicia directamente
```

La capacidad no contiene lógica específica para Spotify; recibe un nombre de aplicación y busca una coincidencia en el catálogo de aplicaciones de Windows.

## Problema detectado y corregido

Se probó temporalmente un enfoque donde la IA podía usar `pulsar_tecla` y `escribir_texto` en un bucle para abrir aplicaciones mediante el menú Inicio.

Ese enfoque falló porque el agente no podía observar suficientemente bien estados intermedios como el propio menú Inicio. Esto provocó bucles e incluso acciones no solicitadas.

Decisión: teclado y ratón no deben ser el mecanismo principal cuando existe una API o interfaz de Windows más directa y fiable. Deben quedar como respaldo para casos donde no haya una alternativa mejor.

## Qué NO queremos

No queremos programar casos específicos como:

```text
si la aplicación es Spotify, abre Spotify
```

Tampoco queremos dar a la IA una herramienta del tipo:

```text
ejecutar_comando("...")
```

ni permitirle:

- Ejecutar PowerShell.
- Ejecutar CMD.
- Ejecutar scripts arbitrarios.
- Pasar rutas de ejecutables arbitrarias.
- Manipular archivos.
- Modificar el registro.
- Realizar tareas administrativas no autorizadas.

## Qué sí queremos

Dar al agente capacidades generales pero delimitadas, por ejemplo:

- Observar ventanas visibles.
- Saber qué ventana tiene el foco.
- Iniciar una aplicación instalada por nombre.
- Activar o traer al frente una ventana existente.
- Interactuar con controles mediante Windows UI Automation.
- Controlar multimedia y audio mediante APIs adecuadas.
- Usar teclado y ratón solo como respaldo cuando sea necesario.

La IA decide qué capacidad necesita, pero el programa controla estrictamente qué capacidades existen y valida sus argumentos.

## Restricciones decididas

El agente debe actuar únicamente sobre la parte interactiva permitida del sistema.

No debe realizar acciones adicionales que el usuario no haya pedido.

No debe manipular archivos, usar PowerShell o CMD, modificar el registro ni realizar tareas administrativas salvo que en el futuro se amplíe expresamente el alcance.

Debe intentar realizar el mínimo número de acciones necesarias.

Cuando exista una forma directa e invisible de realizar una acción mediante Windows, se debe preferir frente a manipular visualmente la interfaz.

## Punto exacto en el que estamos

La primera capacidad real y fiable ya está implementada y validada:

```text
iniciar_aplicacion(nombre)
```

El siguiente trabajo debe ampliar el control de Windows manteniendo el mismo principio de seguridad y generalidad.

## Siguiente paso

1. Añadir una capacidad para detectar si una aplicación ya está abierta y traer su ventana al frente en lugar de intentar iniciar otra instancia cuando corresponda.
2. Ampliar `ObservadorWindows` con información más rica sobre las ventanas.
3. Incorporar Windows UI Automation para observar controles reales de una ventana y actuar sobre ellos.
4. Crear capacidades seguras para acciones de ventana: activar, minimizar, maximizar y cerrar la ventana adecuada.
5. Añadir después capacidades de multimedia y audio mediante APIs específicas.
6. Recuperar el bucle agente: observar → decidir → actuar → observar, únicamente cuando las capacidades y la percepción sean suficientemente fiables.
7. Cuando el control por texto sea sólido, añadir la entrada por voz.

## Regla de continuidad del proyecto

Este archivo debe actualizarse después de cada decisión arquitectónica importante o después de completar una fase relevante.

Si se abre un chat nuevo, el punto de partida debe ser:

> Lee `ESTADO_PROYECTO.md` del repositorio `treska23/ControlPCIA` y seguimos desde ahí.

El código del repositorio y este archivo son la fuente de verdad del estado del proyecto, no el historial de una conversación concreta.
