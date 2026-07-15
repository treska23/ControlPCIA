# ControlPCIA — Estado del proyecto

Última actualización: 15 de julio de 2026

## Objetivo

Crear una aplicación para controlar Windows mediante lenguaje natural. El usuario da una orden hablada o escrita y un modelo local ejecutado con Ollama interpreta la intención y actúa sobre el PC.

Ejemplos objetivo:

- "Abre Spotify y busca Radiohead"
- "Baja el volumen al 30 %"
- "Cierra esa ventana"
- "Pon el vídeo a pantalla completa"

## Decisión arquitectónica actual

La frase natural del usuario es la orden.

La ruta principal ha cambiado respecto al enfoque inicial basado en tool calling y UI Automation.

Flujo actual:

```text
Entrada de texto o voz
→ frase natural
→ Ollama / Qwen interpreta la intención
→ Qwen propone un comando PowerShell por paso
→ ValidadorPowerShell comprueba localmente las restricciones
→ si no coincide con una operación restringida, EjecutorPowerShell lo ejecuta
→ se recoge stdout / stderr / código de salida
→ Qwen recibe el resultado y decide si necesita otro paso
→ FIN cuando la petición está completada
```

La validación de seguridad es local, determinista y rápida. No se consulta otra IA para validar comandos.

Qwen NO está limitado por una lista blanca de comandos permitidos. Debe poder razonar libremente y elegir la forma adecuada de controlar Windows. La seguridad se aplica después mediante una base de restricciones.

## Seguridad: enfoque actual

Se eliminó el enfoque de `ComandosPermitidos`, porque impedía a Qwen realizar acciones normales como `Start-Process notepad`.

El principio actual es:

```text
Qwen propone libremente un comando
→ ¿coincide con una operación restringida?
    ├─ SÍ → bloquear, no ejecutar
    └─ NO → ejecutar
```

La política se guarda en `BaseRestriccionesPowerShell.cs` como una cadena de datos con reglas de varios tipos:

- `CMD`: comandos completamente bloqueados.
- `PREFIX`: familias de comandos bloqueadas.
- `ARG`: argumentos concretos prohibidos para determinados comandos.
- `TARGET`: destinos que no se pueden iniciar mediante `Start-Process`.
- `TEXT`: fragmentos de texto asociados a mecanismos sensibles.
- `SYNTAX`: sintaxis restringida, como redirecciones que puedan escribir archivos.

`ValidadorPowerShell.cs` carga esta base una sola vez y la transforma en colecciones en memoria para que cada comprobación sea rápida.

### Restricciones principales actuales

La intención es impedir principalmente:

- Borrar archivos o carpetas.
- Crear archivos o carpetas.
- Modificar, copiar, mover o renombrar archivos.
- Escribir contenido en archivos.
- Acceso destructivo a discos, particiones o volúmenes.
- Modificar el registro de Windows.
- Cambiar permisos y propiedad.
- Modificar usuarios y grupos del sistema.
- Modificar servicios o tareas programadas.
- Cambios sensibles de red, firewall o Defender.
- Cambios críticos de configuración del sistema.
- Ejecución dinámica destinada a saltarse el validador.
- Lanzar PowerShell, CMD u otros intérpretes anidados para evitar las restricciones.
- Elevación mediante mecanismos restringidos como `-Verb RunAs`.

La base incluye también aliases conocidos de operaciones peligrosas, por ejemplo variantes de `Remove-Item` como `rm`.

Esta barrera todavía debe seguir revisándose y ampliándose según aparezcan casos reales. Para una versión futura más robusta se contempla usar el parser oficial de PowerShell para analizar la estructura del comando antes de ejecutarlo.

## Componentes actuales

- `Program.cs`: entrada actual por texto.
- `ControlWindows.cs`: orquesta la conversación con Ollama/Qwen y el bucle de ejecución.
- `EjecutorPowerShell.cs`: ejecuta PowerShell sin ventana después de pasar siempre por `ValidadorPowerShell`.
- `ValidadorPowerShell.cs`: valida restricciones localmente antes de ejecutar.
- `BaseRestriccionesPowerShell.cs`: base de datos textual de operaciones restringidas.
- `ObservadorWindows.cs`: enumera ventanas visibles y detecta la ventana activa.
- `ObservadorUIWindows.cs`: inspecciona controles mediante Windows UI Automation.
- `AplicacionesWindows.cs`: capacidad anterior para abrir aplicaciones instaladas y traer al frente una ya abierta.
- `EjecutorWindows.cs`: capa anterior de tool calling; se conserva por ahora.
- `EjecutorInterfazWindows.cs`: entrada de teclado como mecanismo de respaldo.
- `InteraccionUIWindows.cs`: interacción genérica con controles mediante UI Automation y fallback de teclado.

UI Automation, teclado y ratón quedan como mecanismos secundarios o fallback cuando PowerShell no sea suficiente.

## Estado actual de ControlWindows

`ControlWindows` ya no obliga a Qwen a elegir entre una lista cerrada de comandos PowerShell.

Qwen puede proponer un comando por paso. Cada comando pasa obligatoriamente por `EjecutorPowerShell`, que a su vez llama a `ValidadorPowerShell` antes de ejecutarlo.

Si un comando es bloqueado, Qwen recibe el motivo y puede intentar otra estrategia segura.

Si un comando se ejecuta pero falla, Qwen recibe el código de salida y el error. Se añadieron instrucciones para que no invente rutas de instalación ni repita rutas de `Program Files` al azar, sino que cambie de estrategia o consulte Windows cuando necesite descubrir cómo está registrada una aplicación.

Existe un límite de pasos para evitar bucles indefinidos.

## Pruebas validadas

### 1. Abrir Bloc de notas

Entrada:

```text
abre el bloc de notas
```

Qwen propuso:

```powershell
Start-Process notepad
```

Resultado:

- El comando pasó el validador.
- PowerShell devolvió código 0.
- El Bloc de notas se abrió correctamente.
- La petición terminó con `FIN`.

Esto valida que Qwen puede traducir un nombre humano de aplicación a una forma ejecutable sin que el usuario conozca `notepad`.

### 2. Abrir Spotify

Inicialmente Qwen intentó rutas inventadas de `Program Files` mediante `-WorkingDirectory`, que fallaron.

Se corrigió el prompt para que:

- Nunca invente rutas de instalación.
- No pruebe carpetas al azar.
- Analice el error y cambie de estrategia.
- Consulte Windows cuando necesite descubrir cómo está registrada una aplicación.

Después de ese cambio, la orden para abrir Spotify quedó funcionando correctamente.

### 3. Bloquear borrado de archivos

Entrada de prueba:

```text
borra C:\prueba.txt
```

Qwen propuso una operación `Remove-Item`.

Resultado:

```text
BLOQUEADO: El comando 'Remove-Item' está bloqueado por la política de seguridad.
```

El archivo no se borra y el agente termina indicando que no encontró una forma permitida de realizar la petición.

Esto valida la idea central actual:

```text
la IA puede razonar libremente
→ la capa de restricciones impide operaciones prohibidas
```

## Memoria local de comandos / recetas

Sigue pendiente implementar una memoria local persistente para no obligar a Qwen a redescubrir soluciones que ya funcionaron.

Flujo previsto:

```text
Petición del usuario
→ ¿existe receta conocida para esta intención/contexto?
    ├─ SÍ → volver a validar → ejecutar directamente
    └─ NO → Qwen propone solución
             → validar
             → ejecutar
             → si funciona, guardar receta
```

Una receta guardada SIEMPRE debe volver a pasar por `ValidadorPowerShell` antes de ejecutarse, para que cambios futuros en la política de seguridad se apliquen también a recetas antiguas.

Implementación sugerida:

- SQLite como almacenamiento persistente.
- Caché en memoria para recetas frecuentes.
- Guardar intención normalizada, contexto/aplicación, comando o secuencia, número de ejecuciones correctas y último resultado conocido.

Prioridad prevista:

```text
receta local conocida
→ PowerShell validado
→ UI Automation
→ teclado/ratón como último recurso
```

## Siguiente punto de trabajo

Al retomar el proyecto:

1. Subir todos los cambios locales actuales a GitHub.
2. Revisar que `BaseRestriccionesPowerShell.cs`, `ValidadorPowerShell.cs`, `EjecutorPowerShell.cs` y el nuevo `ControlWindows.cs` estén en el repositorio.
3. Añadir la memoria local de recetas/comandos validados.
4. Probar varias aplicaciones además de Bloc de notas y Spotify.
5. Probar órdenes normales de Windows: ventanas, volumen y multimedia.
6. Revisar falsos positivos y posibles vías de evasión de la base de restricciones.
7. Más adelante sustituir o reforzar el análisis textual con el parser oficial de PowerShell.
8. Mantener UI Automation como fallback para operaciones que PowerShell no pueda resolver.
9. Cuando el control por texto sea sólido, añadir entrada por voz.

## Regla de continuidad

Este archivo es la referencia principal de decisiones y estado del proyecto.

Si se abre un chat nuevo, empezar por:

> Lee `ESTADO_PROYECTO.md` del repositorio `treska23/ControlPCIA` y seguimos desde ahí.

El código del repositorio y este archivo son la fuente de verdad del proyecto, no el historial de una conversación concreta.
