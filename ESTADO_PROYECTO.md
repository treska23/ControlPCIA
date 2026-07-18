# ControlPCIA — estado y tareas

Última actualización: 18 de julio de 2026

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

La aplicación no contiene un catálogo cerrado de monitores, audio, ventanas o programas. Llama sólo propone comandos literales de PowerShell; ControlPCIA los valida, los ejecuta en un proceso externo y devuelve la salida real. No se usa reconocimiento gráfico ni UI Automation para decidir acciones.

## Decisiones de seguridad

La IA propone comandos, pero nunca decide si son seguros. `ValidadorPowerShell` usa el parser oficial de PowerShell para analizar todo el AST, incluidos pipelines y bloques anidados.

La política no es un catálogo cerrado de acciones, pero sí impone una frontera estricta sobre el disco. La IA puede consultar nombres, rutas, tamaño y fecha de archivos dentro de carpetas personales autorizadas, pero no abrir ni leer su contenido y no puede crear, copiar, mover, renombrar, sobrescribir o borrar archivos, carpetas, documentos o proyectos. Tampoco puede instalar, actualizar o desinstalar programas.

Bloquea:

- Apertura, lectura, creación, copia, movimiento, renombrado, sobrescritura o borrado de archivos y carpetas.
- Instalación, actualización y desinstalación de programas.
- Operaciones destructivas sobre discos, particiones o volúmenes.
- Acceso a credenciales y cambios de Defender, cuentas, permisos, arranque u otras superficies de seguridad.
- Exfiltración, intérpretes anidados, alias evasivos, reflexión y ejecución dinámica.
- COM de interfaz, `SendKeys`, `AppActivate`, atajos y simulación de ratón o teclado.

Las consultas de archivos requieren raíces personales literales autorizadas, un filtro de nombre, un máximo de 20 resultados y salida con rutas completas. `winget` sólo puede consultar mediante `search`, `show` o `list`. Las configuraciones normales de pantalla, audio, ventanas y aplicaciones están permitidas cuando existe una interfaz de consola real.

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
- Token de sesión aleatorio: el móvil conserva el valor real en `SecureStorage` y el PC sólo persiste su hash con 90 días de caducidad renovable.
- Bearer token; no se usan cookies ni CORS.
- Content Security Policy con nonce y demás cabeceras de seguridad.
- Una sola orden activa; una segunda recibe HTTP 409.
- Ollama permanece en loopback y no se expone al móvil.

La web permite texto, Web Speech API si existe y dictado del teclado móvil como alternativa. Ahora también es una PWA con manifiesto, iconos, interfaz `standalone` y service worker limitado al shell; `/api/` nunca entra en caché.

La aplicación nativa .NET MAUI para Android es ahora la experiencia principal. Implementa:

- Descubrimiento UDP automático del servidor en el puerto 5188 y dirección manual como alternativa.
- Emparejado y token en `SecureStorage`.
- Control de voz único para Wake-on-LAN y órdenes normales enviadas automáticamente a Llama.
- Modo predeterminado tocar–hablar–tocar y modo alternativo mantener pulsado, ambos con envío automático, estados visibles, contador, transcripción parcial y respuesta háptica.
- Respuesta clara cuando no entiende la voz y diálogo de confirmación por sí/no cuando Llama necesita aclarar una acción permitida.
- Entrada de texto, estado e historial no persistente como alternativa.
- APK Release firmado localmente para instalación manual.

Wake-on-LAN aprende MAC y broadcast durante el emparejado y conserva esos datos en el móvil. La frase de voz de arranque se reconoce localmente porque Llama no existe mientras el PC está apagado; no se ha añadido ningún otro catálogo local de acciones.

## Control genérico de aplicaciones

Las aplicaciones se controlan con comandos de consola de Windows o con la CLI/PowerShell documentada por la propia aplicación. Para consultar ventanas se usa `Get-Process` y se emiten pares `PROCESS_NAME`/`WINDOW_TITLE`; no se inspeccionan píxeles, capturas, TextBox ni árboles gráficos. Para localizar archivos se usa `Get-ChildItem` con filtro literal y se emite `FULL_NAME`, sin abrir ni leer contenido. Si no existe una interfaz de consola, ControlPCIA explica el límite.

Cada paso devuelve al modelo stdout, stderr y código de salida. Un error, una salida vacía o un resultado que no demuestre la petición se comunica al móvil como respuesta conversacional para que el usuario pueda aclararlo. Las consultas estructuradas ya demostradas se entregan al móvil directamente desde la salida real. El programa no inspecciona interfaces para detectar trabajo sin guardar y nunca guarda ni descarta archivos.

## Ejecución residente

El servidor configura una vez el inicio por usuario en `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` y conserva una preferencia separada para respetar una desactivación posterior. Windows lo inicia con `--servidor --oculto`. Un mutex impide servidores duplicados y la bandeja muestra el código de emparejado y permite abrir la página local, mostrar la consola, cambiar el inicio automático o salir. Estas operaciones pertenecen al código de confianza y no son invocables desde los comandos de Llama.

## Verificaciones realizadas

- Compilación Android Debug y publicación Release sin errores ni advertencias.
- 221 pruebas automatizadas correctas.
- Diagnóstico real de Ollama y `qwen3:8b` correcto.
- Orden directa «abre la calculadora» → `Start-Process calc.exe` → stdout/código de PowerShell devueltos al modelo; no se usan acciones gráficas.
- Web móvil comprobada a 390 × 844 píxeles.
- Manifiesto, service worker y emparejado de la PWA comprobados en navegador.
- App Android compilada en Debug y publicada en Release sin advertencias de compilación.
- APK 1.3.1 (código 5) firmado y verificado con esquemas v1, v2 y v3; SHA-256 `461052D286FBFEE8E1C995687F402CB3A50DF775CF41E58ECF575848496E464E`.
- Samsung SM-S928B real: la versión 1.3.1 se instaló conservando datos, emparejado y conexión por Wi-Fi. En el modo tocar–hablar–tocar se comprobó manualmente que «Detener y enviar» transcribe y envía la orden. Una prueba instrumentada mantuvo la sesión abierta durante más de un minuto, atravesó varios silencios y un resultado ambiental sin enviar nada. También se comprobó que el modo mantener pulsado inicia al presionar, detiene al soltar y muestra «No te he entendido» cuando no obtiene texto. Finalmente, el reconocimiento físico transcribió literalmente «enciende el ordenador», envió Wake-on-LAN a 2 destinos y no produjo ninguna petición a `/api/orden`.
- Consulta conjunta real verificada: programas con ventana abierta más búsqueda exacta de `README.md`; devolvió títulos y rutas completas sin abrir ni leer archivos.
- Wake-on-LAN detectó 1 adaptador válido, UDP 9 y su broadcast local sin exponer la MAC en la interfaz.
- Navegación web corregida: Llama puede abrir URL públicas literales `http/https` y búsquedas web, mientras se bloquean redes privadas, `file:` y descargas ejecutables o comprimidas.
- Se verificó que el ejecutor usa un proceso externo de PowerShell y devuelve stdout, stderr y código de salida al modelo.
- Modo residente probado en puerto 5190: proceso sin ventana, servidor HTTP 200 y cierre limpio del PID temporal sin registrar el inicio durante la prueba.
- Persistencia de emparejado comprobada creando una sesión, reiniciando `SeguridadMovil` y autorizando el mismo token; el archivo contiene sólo el hash y la caducidad, nunca el token real.

## Rediseño móvil completado

La entrada de voz nativa se ha simplificado de esta forma:

- Se ha eliminado la tarjeta separada «¿El PC está apagado?» y existe un único control de voz para todas las peticiones.
- Ese mismo control reconoce localmente una orden de encendido aunque el PC no esté disponible. Las demás frases se envían automáticamente a Llama.
- El modo predeterminado **tocar para hablar** empieza con un toque y termina y envía con otro, sin un límite corto impuesto por la aplicación.
- Si Android finaliza internamente un fragmento por silencio, la aplicación conserva la transcripción, abre otro fragmento y no envía nada hasta que el usuario pulsa **Detener y enviar**.
- El modo alternativo **mantener pulsado** empieza al presionar y termina y envía al soltar. Ninguno mantiene escucha permanente en segundo plano.
- La transcripción de voz se muestra en la tarjeta del micrófono y no se copia al cuadro escrito ni requiere pulsar «Enviar mensaje escrito».
- Si no reconoce una frase muestra «No te he entendido»; Llama puede pedir confirmación de una acción permitida y la aplicación conserva la orden pendiente para recibir sí o no.
- La interfaz muestra preparación, escucha, texto parcial, duración, transcripción, ejecución, resultado y error, con respuesta háptica cuando el dispositivo la ofrece.
- Se ha retirado de la aplicación móvil la sección «Colocar ventanas» y su código específico.
- El fallo que rechazaba literalmente «enciende el ordenador» se corrigió añadiendo la forma verbal `enciend…` y pruebas de regresión.
- Emparejado correcto y API sin token rechazada con HTTP 401.
- Orden móvil «abre el bloc de notas» → `Start-Process notepad.exe` → completada.
- Los cierres normales solicitan `CloseMainWindow()` y exigen después una consulta de proceso distinta que demuestre el resultado; no se usa cierre forzado de aplicaciones con ventana.
- Dos órdenes simultáneas → una HTTP 200 y otra HTTP 409.
- Aprendizaje real: «abre la calculadora» guardó la receta; «inicia la calculadora» encontró una receta relacionada y la reutilizó.
- Comandos de borrado, lectura de archivos, rutas nativas, alias evasivos, intérpretes, COM de interfaz, `SendKeys`, `AppActivate` y el subcomando antiguo `ui` bloqueados en pruebas.

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
- [x] Prototipar y después retirar el lienzo móvil de colocación de ventanas por resultar confuso.
- [x] Añadir Wake-on-LAN aprendido y la orden local «enciende el ordenador».
- [x] Mantener la web como PWA de respaldo sin cachear datos de la API.
- [x] Permitir navegación web pública literal sin habilitar descargas ni acceso a direcciones privadas.
- [x] Unificar Wake-on-LAN y órdenes normales en un único control de voz móvil.
- [x] Implementar pulsar para hablar, bloqueo hasta detener y estados visuales/hápticos de escucha.
- [x] Hacer predeterminado tocar–hablar–tocar, enviar automáticamente la voz y separar claramente el mensaje escrito.
- [x] Añadir aclaración/confirmación conversacional sin permitir que una confirmación eluda la política local.
- [x] Retirar de la interfaz móvil el editor de colocación de ventanas.
- [x] Añadir control de aplicaciones mediante comandos de consola validados y aprendizaje de secuencias.
- [x] Añadir agente residente con inicio por usuario, modo oculto, exclusión de duplicados y bandeja de sistema.
- [x] Probar físicamente en un teléfono Android ambos modos de micrófono, descubrimiento, emparejado y envío automático.
- [x] Terminar la prueba física de la APK 1.3.1 (código 5) en el Samsung: continuidad del modo fijo tras varias pausas, inicio/parada del modo mantener pulsado y frase literal «enciende el ordenador» resuelta localmente con envío Wake-on-LAN a 2 destinos sin pasar por `/api/orden`.
- [x] Redefinir la permisibilidad: sólo consultar rutas y metadatos; bloquear abrir/leer/crear/copiar/mover/renombrar/sobrescribir/borrar archivos y bloquear instalar, actualizar o desinstalar programas.
- [x] Resolver «suma dos más cinco»: PowerShell puede calcularlo y devolver `7` al móvil. La aplicación Calculadora de Windows no admite una expresión por CLI o protocolo URI, por lo que no se simulan teclas ni se añade lógica gráfica.
- [x] Probar una aplicación ya abierta mediante varios pasos de consola. Cubase se resolvió desde el inventario real de Windows y se verificó por proceso/título; las acciones internas quedan limitadas a su CLI/API real y no se simulan entradas.
- [x] Convertir el móvil en una conversación real con la IA, con respuestas informativas, contexto acotado y continuaciones.
- [x] Permitir mensajes complejos y referencias a respuestas anteriores, mostrando la salida, los errores reales, lo pendiente y las aclaraciones necesarias.
- [ ] Verificar en un dispositivo la conversación de error: si PowerShell no hace nada o devuelve error, el móvil debe mostrarlo y permitir que el usuario explique de nuevo la orden.
- [x] Corregir el falso positivo observado con «cierra Visual Studio»: un comando de cierre ya no se considera su propia verificación; hace falta una consulta posterior distinta que demuestre que el proceso o la ventana alcanzó el estado pedido.
- [x] Fijar la política para trabajo sin guardar: al no usar inspección gráfica ni manipular archivos, ControlPCIA no puede detectar de forma fiable el estado interno de documentos, guardarlos o descartarlos. Debe solicitar un cierre normal, verificar el proceso por consola y explicar la limitación si la aplicación presenta un diálogo.
- [x] Añadir consultas conversacionales de información del PC: programas con ventana abierta y localización exacta de archivos por nombre, con respuestas compuestas desde evidencia real y pruebas contra resultados parciales, nombres alterados, tablas truncadas y búsquedas sin límite.
- [ ] Ampliar las consultas informativas seguras sobre el PC (uso de CPU/memoria, audio, red, pantallas y aplicaciones instaladas) manteniendo la misma respuesta basada en evidencia.
- [ ] Añadir más pruebas de regresión para referencias de seguimiento, cierre selectivo de varias aplicaciones, objetivo que ya estaba cerrado y fallo de cierre.
- [ ] Probar Wake-on-LAN con el PC realmente apagado.
- [ ] Compilar, firmar y probar la aplicación iOS desde un Mac.
- [ ] Crear un instalador firmado; el inicio automático explícito y la bandeja ya están implementados.
- [ ] Evaluar otros modelos locales y órdenes complejas de varias aplicaciones.
- [ ] Mantener una revisión continua de nuevas vías de evasión del validador.
- [ ] Valorar HTTPS local o un túnel autenticado antes de permitir uso fuera de una LAN de confianza.

## Regla de continuidad

Este archivo registra las decisiones y el trabajo pendiente. Para continuar en otra tarea:

> Lee `README.md` y `ESTADO_PROYECTO.md` del repositorio `treska23/ControlPCIA` y sigue desde la primera tarea sin marcar.
