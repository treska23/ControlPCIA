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

La política es de denegación de daños concretos, no una lista cerrada de acciones de escritorio. La IA puede crear documentos, archivos, carpetas y proyectos nuevos, abrir los ya existentes, guardar, copiar y pegar a destinos nuevos, importar, exportar, imprimir e instalar programas. La interfaz de las aplicaciones y los diálogos nativos de abrir/guardar están dentro del alcance.

Bloquea:

- Borrado de archivos o carpetas.
- Cortar o mover archivos y carpetas y cualquier copia que sobrescriba un destino existente.
- Desinstalación y operaciones destructivas sobre discos, particiones o volúmenes.
- Acceso a credenciales y cambios de Defender, cuentas, permisos, arranque u otras superficies de seguridad.
- Exfiltración, intérpretes anidados, alias evasivos, reflexión y ejecución dinámica.
- COM arbitrario y escritura de texto libre mediante `SendKeys`.

La creación y la copia directas por PowerShell se aceptan sólo con rutas locales, absolutas y literales, y únicamente si el destino todavía no existe. Las instalaciones se limitan a identificadores literales del catálogo `winget`; no se aceptan manifiestos, URLs o instaladores inventados. Las configuraciones normales de pantalla, audio, ventanas y aplicaciones están permitidas.

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

Las aplicaciones se controlan con comandos de consola de Windows o con la CLI/PowerShell documentada por la propia aplicación. Para consultar ventanas se usa, por ejemplo, `Get-Process | Where-Object MainWindowTitle | Select-Object ProcessName,MainWindowTitle`; no se inspeccionan píxeles, capturas, TextBox ni árboles gráficos. Si no existe un comando conocido, Llama responde pidiendo el dato necesario para investigarlo.

Cada paso devuelve al modelo stdout, stderr y código de salida. Un error, una salida vacía o un resultado que no demuestre la petición se comunica al móvil como respuesta conversacional para que el usuario pueda aclararlo. Las acciones de cierre que detecten trabajo sin guardar deben preguntar antes de guardar o descartar.

## Ejecución residente

El servidor configura una vez el inicio por usuario en `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` y conserva una preferencia separada para respetar una desactivación posterior. Windows lo inicia con `--servidor --oculto`. Un mutex impide servidores duplicados y la bandeja muestra el código de emparejado y permite abrir la página local, mostrar la consola, cambiar el inicio automático o salir. Estas operaciones pertenecen al código de confianza y no son invocables desde los comandos de Llama.

## Verificaciones realizadas

- Compilación Debug sin errores ni advertencias.
- 185 pruebas automatizadas correctas en Release.
- Diagnóstico real de Ollama y `qwen3:8b` correcto.
- Orden directa «abre la calculadora» → `Start-Process calc.exe` → stdout/código de PowerShell devueltos al modelo; no se usan acciones gráficas.
- Web móvil comprobada a 390 × 844 píxeles.
- Manifiesto, service worker y emparejado de la PWA comprobados en navegador.
- App Android compilada en Debug y publicada en Release sin advertencias de compilación.
- APK 1.2 (código 3) firmado y verificado con esquemas v1, v2 y v3; SHA-256 `66830AF49906B9E04A726982C603C10B1FF5677785855B63A9FE6A50FD3ACFA2`.
- Samsung SM-S928B real: instalación conservando datos, descubrimiento de `BARDO`, conexión por Wi-Fi, reconocimiento de voz en ambos modos y envío automático comprobados.
- `/api/escena` probado con 2 monitores y 13 ventanas reales.
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
- El modo alternativo **mantener pulsado** empieza al presionar y termina y envía al soltar. Ninguno mantiene escucha permanente en segundo plano.
- La transcripción de voz se muestra en la tarjeta del micrófono y no se copia al cuadro escrito ni requiere pulsar «Enviar mensaje escrito».
- Si no reconoce una frase muestra «No te he entendido»; Llama puede pedir confirmación de una acción permitida y la aplicación conserva la orden pendiente para recibir sí o no.
- La interfaz muestra preparación, escucha, texto parcial, duración, transcripción, ejecución, resultado y error, con respuesta háptica cuando el dispositivo la ofrece.
- Se ha retirado de la aplicación móvil la sección «Colocar ventanas» y su código específico.
- El fallo que rechazaba literalmente «enciende el ordenador» se corrigió añadiendo la forma verbal `enciend…` y pruebas de regresión.
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
- [x] Redefinir la permisibilidad: permitir crear, abrir, guardar, copiar/pegar a destinos nuevos e instalar; mantener bloqueados borrar, cortar/mover archivos, sobrescribir destinos, desinstalar y operar destructivamente sobre discos.
- [ ] Replantear la orden «suma dos más cinco en la calculadora» con el modelo comando–ejecutor: Llama sólo propone el comando PowerShell y ControlPCIA lo valida, lo ejecuta y devuelve el resultado. No añadir reconocimiento gráfico, búsqueda de TextBox ni lógica específica de Calculadora.
- [ ] Añadir una prueba real y pruebas de regresión para controlar una aplicación ya abierta mediante varios pasos seguros; cuando una aplicación sea abierta únicamente durante una prueba, cerrarla al terminar como limpieza. Esto no debe convertirse en un comportamiento automático del programa para las aplicaciones que abra el usuario.
- [x] Convertir el móvil en una conversación real con la IA, con respuestas informativas, contexto acotado y continuaciones.
- [x] Permitir mensajes complejos y referencias a respuestas anteriores, mostrando la salida, los errores reales, lo pendiente y las aclaraciones necesarias.
- [ ] Verificar en un dispositivo la conversación de error: si PowerShell no hace nada o devuelve error, el móvil debe mostrarlo y permitir que el usuario explique de nuevo la orden.
- [ ] Corregir el falso positivo observado con «cierra Visual Studio»: Visual Studio siguió abierto pero la aplicación móvil mostró la tarea como completada. Ninguna orden sobre aplicaciones puede terminar en `FIN` sólo porque el comando devolvió código cero; debe volver a observar el estado real y comprobar que la ventana o el proceso objetivo alcanzó el resultado pedido. Si no lo consiguió, debe reintentar con una estrategia segura o informar del fallo, nunca afirmar que se completó.
- [ ] Revisar específicamente el cierre de aplicaciones que pueden contener trabajo sin guardar, incluido Visual Studio. No debe quedar bloqueado sin explicación ni ejecutarse a ciegas: la IA debe detectar el posible riesgo, pedir confirmación cuando corresponda, cerrar sólo la aplicación solicitada y verificar después que realmente se cerró. Confirmar nunca autoriza descartar contenido sin guardar ni manipular archivos.
- [ ] Implementar el flujo conversacional concreto para cerrar Visual Studio con cambios sin guardar: la IA debe comprobar que Visual Studio está abierto, intentar detectar mediante su interfaz real si existen documentos o proyectos modificados y responder en el chat móvil «No has guardado el trabajo. ¿Quieres que lo guarde por ti?». La orden queda pendiente mientras espera una respuesta posterior de «sí» o «no».
- [ ] Si el usuario responde «sí», permitir como excepción estrecha y explícitamente confirmada únicamente la acción nativa **Guardar/Guardar todo** sobre documentos que ya están abiertos en la aplicación; después verificar que desaparece el estado modificado y cerrar Visual Studio, comprobando finalmente que ya no está abierto. Esta excepción no permite elegir rutas, usar «Guardar como», sobrescribir destinos mediante diálogos, crear proyectos, exportar, acceder a credenciales ni realizar otras manipulaciones de archivos.
- [ ] Si el usuario responde «no», no guardar ni descartar trabajo de forma implícita. La IA debe mantenerlo intacto y preguntar si quiere cancelar el cierre o **cerrar sin guardar**. Sólo una confirmación inequívoca de «cerrar sin guardar» puede autorizar el descarte mediante el diálogo normal de Visual Studio; después también debe verificar el resultado real.
- [ ] Mostrar toda esta conversación en la aplicación móvil —detección, pregunta, respuesta, guardado, cierre y verificación— y conservar temporalmente la orden pendiente para que «sí», «no» y «cierra sin guardar» se interpreten respecto a Visual Studio, sin perder el contexto ni aplicarse a otra aplicación por error.
- [ ] Añadir pruebas de regresión para consultas conversacionales sobre ventanas abiertas, referencias de seguimiento, cierre selectivo de varias aplicaciones, objetivo que ya estaba cerrado, fallo de cierre, aplicación con trabajo potencialmente sin guardar y prohibición de devolver «completada» mientras el objetivo siga visible.
- [ ] Probar Wake-on-LAN con el PC realmente apagado.
- [ ] Compilar, firmar y probar la aplicación iOS desde un Mac.
- [ ] Crear un instalador firmado; el inicio automático explícito y la bandeja ya están implementados.
- [ ] Evaluar otros modelos locales y órdenes complejas de varias aplicaciones.
- [ ] Mantener una revisión continua de nuevas vías de evasión del validador.
- [ ] Valorar HTTPS local o un túnel autenticado antes de permitir uso fuera de una LAN de confianza.

## Regla de continuidad

Este archivo registra las decisiones y el trabajo pendiente. Para continuar en otra tarea:

> Lee `README.md` y `ESTADO_PROYECTO.md` del repositorio `treska23/ControlPCIA` y sigue desde la primera tarea sin marcar.
