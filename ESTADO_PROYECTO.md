# ControlPCIA — Estado del proyecto

Última actualización: 15 de julio de 2026

## Objetivo

Crear una aplicación para controlar Windows mediante lenguaje natural, pensada para recibir órdenes habladas o escritas y permitir que un modelo local ejecutado con Ollama interprete la intención del usuario y actúe sobre el PC.

La idea central es que el usuario pueda decir frases naturales como:

- "Abre Spotify y pon Radiohead"
- "Baja un poco el volumen"
- "Cierra esa ventana"
- "Pon el vídeo a pantalla completa"

El sistema no debe depender de un catálogo cerrado de comandos del tipo `abrir_aplicacion`, `cerrar_aplicacion`, etc.

## Decisión arquitectónica principal

La frase del usuario es la orden.

El modelo de IA es el intérprete y debe decidir qué acciones necesita realizar para cumplirla.

No queremos mantener un JSON propio con una lista creciente de intenciones o comandos específicos para cada aplicación.

Ollama sí usa JSON internamente en su API HTTP y en posibles tool calls, pero eso se considera solo un protocolo técnico de comunicación, no el modelo conceptual de comandos de la aplicación.

## Arquitectura objetivo

Entrada de voz o texto
→ frase natural del usuario
→ agente de control
→ modelo local mediante Ollama
→ observación del estado de Windows
→ ejecución de acciones genéricas
→ nueva observación
→ continuar hasta completar la petición

Componentes conceptuales:

- **Modelo local (Ollama):** cerebro que interpreta la orden y decide qué hacer.
- **ObservadorWindows:** obtiene información sobre el estado visible del sistema, como ventanas abiertas.
- **EjecutorWindows:** capa que ejecutará acciones reales sobre Windows.
- **EjecutorInterfazWindows:** utilidad de bajo nivel para teclado y futura interacción de respaldo.

## Estado actual del código

El repositorio contiene actualmente:

- `Program.cs`
- `ControlWindows.cs`
- `ObservadorWindows.cs`
- `EjecutorWindows.cs`
- `EjecutorInterfazWindows.cs`

### Lo que ya funciona

- Comunicación con Ollama mediante `http://localhost:11434/api/chat`.
- Uso actual del modelo `qwen3:8b`.
- Lectura de ventanas visibles mediante `ObservadorWindows`.
- Envío de entradas de teclado con `SendInput` en `EjecutorInterfazWindows`.
- La orden natural del usuario llega correctamente a la IA.
- La IA interpreta frases libres como `abre spotify` y devuelve una decisión en lenguaje natural.
- `ControlWindows` ya no usa el antiguo catálogo de comandos del tipo `abrir_aplicacion` para esta ruta principal.
- `EjecutorWindows` recibe correctamente la decisión de la IA sin intentar parsearla como JSON.

### Prueba validada

Entrada:

```text
abre spotify
```

Resultado observado:

```text
LA IA HA DECIDIDO:
El PC debe abrir la aplicación Spotify.

EJECUTOR WINDOWS:
El PC debe abrir la aplicación Spotify.
```

La prueba confirma que la cadena actual funciona hasta `EjecutorWindows` sin excepciones.

## Qué NO queremos

No queremos una arquitectura basada en ampliar continuamente algo como:

```text
abrir_aplicacion
cerrar_aplicacion
establecer_volumen
minimizar_ventana
maximizar_ventana
...
```

Tampoco queremos programar casos específicos como:

```text
si la aplicación contiene "Spotify", abre spotify
```

La IA debe entender la intención y combinar capacidades genéricas del sistema.

## Qué sí queremos

Dar al agente capacidades suficientemente generales para controlar la parte interactiva de Windows, por ejemplo:

- Observar ventanas y aplicaciones visibles.
- Encontrar una ventana o control.
- Activar una ventana.
- Interactuar con controles mediante UI Automation cuando sea posible.
- Pulsar teclas.
- Escribir texto.
- Usar ratón cuando sea necesario.
- Usar APIs nativas de Windows para funciones concretas donde tenga sentido.
- Controlar multimedia y audio mediante APIs adecuadas.

La IA decidirá cómo utilizar esas capacidades según la frase del usuario.

## Restricciones decididas

El agente debe actuar únicamente sobre la parte interactiva permitida del sistema.

No debe realizar acciones adicionales que el usuario no haya pedido.

No debe manipular archivos, usar PowerShell o CMD, modificar el registro ni realizar tareas administrativas salvo que en el futuro se decida expresamente ampliar el alcance.

Debe intentar realizar el mínimo número de acciones necesarias para cumplir la petición.

## Punto exacto en el que estamos

La ruta principal ya es:

```text
frase natural del usuario
→ ControlWindows
→ Ollama / qwen3:8b
→ decisión en lenguaje natural
→ EjecutorWindows
```

`EjecutorWindows.EjecutarAsync` todavía no ejecuta acciones reales sobre Windows. Actualmente recibe la decisión y la muestra por consola.

El siguiente trabajo es convertir `EjecutorWindows` en la capa de actuación real.

## Siguiente paso

1. Dar a `EjecutorWindows` capacidades genéricas reales para actuar sobre Windows.
2. Priorizar mecanismos generales: UI Automation, APIs nativas y teclado/ratón como respaldo.
3. Evitar volver a introducir un catálogo de comandos específico por aplicación.
4. Después, crear el bucle completo del agente: observar → decidir → actuar → observar de nuevo → comprobar si la petición está completada.
5. Cuando la ejecución por texto funcione, añadir la entrada por voz.

## Regla de continuidad del proyecto

Este archivo debe actualizarse después de cada decisión arquitectónica importante o después de completar una fase relevante.

Si se abre un chat nuevo, el punto de partida debe ser:

> Lee `ESTADO_PROYECTO.md` del repositorio `treska23/ControlPCIA` y seguimos desde ahí.

El código del repositorio y este archivo son la fuente de verdad del estado del proyecto, no el historial de una conversación concreta.
