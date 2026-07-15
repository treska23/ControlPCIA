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

### Lo que ya funciona o está implementado parcialmente

- Comunicación con Ollama mediante `http://localhost:11434/api/chat`.
- Uso actual del modelo `qwen3:8b`.
- Lectura de ventanas visibles mediante `ObservadorWindows`.
- Envío de entradas de teclado con `SendInput` en `EjecutorInterfazWindows`.
- Infraestructura inicial para que la IA reciba una instrucción y conozca parte del estado de Windows.

### Problema actual

El código conserva una arquitectura anterior que ya hemos decidido abandonar.

Actualmente existen restos como:

- `EjecutarAccion(JsonElement accion)`.
- Un `switch` con tipos como `abrir_aplicacion`, `cerrar_aplicacion` y `establecer_volumen`.
- `AbrirAplicacion` con casos concretos escritos a mano para Bloc de notas y Spotify.
- Un paso intermedio donde la IA devuelve JSON con `completado` y `siguiente_paso`.
- Otro método que vuelve a pedir a la IA un JSON con una secuencia de teclas.

Esto contradice la arquitectura deseada porque obliga a definir comandos y estructuras rígidas en lugar de dejar que el modelo interprete una frase natural usando capacidades genéricas.

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
si la aplicación contiene "Spotify", abre spotify:
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

El código se ha subido por primera vez al repositorio `treska23/ControlPCIA`.

`EjecutorWindows.EjecutarAsync` todavía no ejecuta acciones reales: actualmente solo imprime el siguiente paso recibido.

`ControlWindows.cs` contiene gran parte de la lógica antigua basada en JSON de comandos y debe simplificarse.

## Siguiente paso

1. Refactorizar `ControlWindows.cs` para eliminar la arquitectura basada en tipos de comando como `abrir_aplicacion`.
2. Eliminar el doble procesamiento innecesario de la misma intención por la IA.
3. Convertir `EjecutorWindows` en la capa real de capacidades genéricas de control.
4. Mantener `ObservadorWindows` como fuente inicial de percepción del sistema.
5. Diseñar el bucle del agente: observar → decidir → actuar → volver a observar → comprobar si la petición está completada.

## Regla de continuidad del proyecto

Este archivo debe actualizarse después de cada decisión arquitectónica importante o después de completar una fase relevante.

Si se abre un chat nuevo, el punto de partida debe ser:

> Lee `ESTADO_PROYECTO.md` del repositorio `treska23/ControlPCIA` y seguimos desde ahí.

El código del repositorio y este archivo son la fuente de verdad del estado del proyecto, no el historial de una conversación concreta.
