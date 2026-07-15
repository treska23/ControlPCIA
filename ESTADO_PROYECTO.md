# ControlPCIA — estado y tareas

Última actualización: 15 de julio de 2026

## Objetivo vigente

Controlar un PC Windows mediante órdenes habladas o escritas desde el móvil. La IA local debe decidir los comandos de consola necesarios, sin que la aplicación contenga una función por cada acción.

```text
móvil
→ texto transcrito
→ Ollama / qwen3:8b
→ uno o varios pasos PowerShell
→ validación local determinista
→ ejecución en Windows
→ resultado devuelto a Llama
→ FIN
```

La aplicación no contiene un catálogo cerrado de monitores, audio, ventanas o programas. Las antiguas clases de tool calling y UI Automation por acciones fueron retiradas.

## Decisiones de seguridad

La IA propone comandos, pero nunca decide si son seguros. `ValidadorPowerShell` usa el parser oficial de PowerShell para analizar todo el AST, incluidos pipelines y bloques anidados.

La política es de denegación de capacidades peligrosas, no una lista de acciones de escritorio permitidas. Bloquea:

- Lectura, creación, modificación, movimiento y borrado de archivos o carpetas.
- Registro, discos, particiones, volúmenes, permisos, usuarios y grupos.
- Servicios, tareas, red, firewall, Defender, arranque y configuración crítica.
- Descargas, exfiltración, intérpretes anidados, alias evasivos, reflexión y ejecución dinámica.
- Rutas o argumentos dinámicos enviados a programas nativos.
- COM arbitrario y texto libre mediante `SendKeys`.

Los comandos guardados por el aprendizaje siempre se vuelven a validar. La memoria interna de ControlPCIA es la única excepción de escritura persistente: la IA no puede acceder a ella ni escribir otros archivos.

## Aprendizaje

`MemoriaRecetas` guarda recetas exitosas en `%LOCALAPPDATA%\ControlPCIA\recetas-v1.json`.

Se conserva únicamente:

- Intención normalizada.
- Secuencia de comandos con código de salida cero.
- Conteo y fechas de éxito.

No se guardan salidas, errores ni contenido del sistema. Al recibir una petición, se buscan hasta cinco recetas parecidas por tokens. Se entregan a Llama como referencias no confiables; Llama puede adaptarlas y el validador toma siempre la decisión final.

## Control móvil

El modo predeterminado inicia un servidor en el puerto 5187 y muestra las direcciones LAN y un código de emparejado.

Protecciones implementadas:

- Sólo loopback, redes RFC1918, link-local o IPv6 ULA.
- Código aleatorio de seis cifras y máximo de intentos por dirección.
- Token de sesión aleatorio almacenado en memoria como hash y con 12 horas de caducidad.
- Bearer token; no se usan cookies ni CORS.
- Content Security Policy con nonce y demás cabeceras de seguridad.
- Una sola orden activa; una segunda recibe HTTP 409.
- Ollama permanece en loopback y no se expone al móvil.

La web permite texto, Web Speech API si existe y dictado del teclado móvil como alternativa. Ahora también es una PWA con manifiesto, iconos, interfaz `standalone` y service worker limitado al shell; `/api/` nunca entra en caché.

La aplicación nativa .NET MAUI para Android es ahora la experiencia principal. Implementa:

- Descubrimiento UDP automático del servidor en el puerto 5188 y dirección manual como alternativa.
- Emparejado y token en `SecureStorage`.
- Dictado nativo Android, texto, estado e historial no persistente.
- Consulta de pantallas y geometría de ventanas mediante `/api/escena`.
- Lienzo arrastrable y ajustable que entrega a Llama la distribución deseada.
- APK Release firmado localmente para instalación manual.

Wake-on-LAN aprende MAC y broadcast durante el emparejado y conserva esos datos en el móvil. La frase de voz de arranque se reconoce localmente porque Llama no existe mientras el PC está apagado; no se ha añadido ningún otro catálogo local de acciones.

## Verificaciones realizadas

- Compilación Debug sin errores ni advertencias.
- 97 pruebas automatizadas correctas en Release.
- Diagnóstico real de Ollama y `qwen3:8b` correcto.
- Orden directa «abre la calculadora» → `Start-Process calc.exe` → código 0 → `FIN`.
- Web móvil comprobada a 390 × 844 píxeles.
- Manifiesto, service worker y emparejado de la PWA comprobados en navegador.
- App Android compilada en Debug y publicada en Release sin advertencias de compilación.
- APK firmado verificado con `jarsigner`; SHA-256 `11FF8DC31554E04E9C81D46A0912B456D4CD00781256E9693E7DCC0B24F55D7B`.
- `/api/escena` probado con 2 monitores y 13 ventanas reales.
- Wake-on-LAN detectó 1 adaptador válido, UDP 9 y su broadcast local sin exponer la MAC en la interfaz.
- Emparejado correcto y API sin token rechazada con HTTP 401.
- Orden móvil «abre el bloc de notas» → `Start-Process notepad.exe` → completada.
- Orden móvil «cierra el bloc de notas» → `Stop-Process -Name notepad -Force` → completada.
- Dos órdenes simultáneas → una HTTP 200 y otra HTTP 409.
- Aprendizaje real: «abre la calculadora» guardó la receta; «inicia la calculadora» encontró una receta relacionada y la reutilizó.
- Comandos de borrado, lectura de archivos, rutas nativas, alias evasivos, intérpretes, COM peligroso y texto libre por `SendKeys` bloqueados en pruebas.

## Tareas

- [x] Sustituir el catálogo de acciones por IA → PowerShell genérico.
- [x] Analizar comandos con el AST oficial de PowerShell.
- [x] Limitar tiempo, salida y árbol de procesos.
- [x] Añadir pruebas automatizadas de seguridad y evasiones.
- [x] Separar Ollama y resultados estructurados de la consola.
- [x] Añadir servidor móvil autenticado.
- [x] Añadir entrada escrita y dictado con fallback al teclado.
- [x] Impedir órdenes simultáneas.
- [x] Añadir aprendizaje persistente con revalidación.
- [x] Retirar la arquitectura antigua por herramientas específicas.
- [x] Crear una aplicación Android nativa con descubrimiento, voz, texto y estado.
- [x] Añadir un lienzo móvil para describir a Llama la colocación de ventanas.
- [x] Añadir Wake-on-LAN aprendido y la orden local «enciende el ordenador».
- [x] Mantener la web como PWA de respaldo sin cachear datos de la API.
- [ ] Probar físicamente en un teléfono Android el micrófono, descubrimiento, lienzo y Wake-on-LAN con el PC realmente apagado.
- [ ] Compilar, firmar y probar la aplicación iOS desde un Mac.
- [ ] Crear un instalador y una opción explícita de inicio automático.
- [ ] Evaluar otros modelos locales y órdenes complejas de varias aplicaciones.
- [ ] Mantener una revisión continua de nuevas vías de evasión del validador.
- [ ] Valorar HTTPS local o un túnel autenticado antes de permitir uso fuera de una LAN de confianza.

## Regla de continuidad

Este archivo registra las decisiones y el trabajo pendiente. Para continuar en otra tarea:

> Lee `README.md` y `ESTADO_PROYECTO.md` del repositorio `treska23/ControlPCIA` y sigue desde la primera tarea sin marcar.
