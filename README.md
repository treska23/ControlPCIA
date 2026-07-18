# ControlPCIA

ControlPCIA permite enviar una orden hablada o escrita desde una aplicación móvil para que un modelo local de Ollama decida cómo realizarla en Windows mediante PowerShell. También conserva una PWA como acceso de respaldo.

No hay un traductor intermedio ni una función programada para cada acción. El único flujo principal es:

```text
móvil → texto → Llama propone comando → validador local → PowerShell → Windows
```

Llama puede razonar sobre aplicaciones, ventanas, audio, multimedia, pantallas, archivos e información del sistema. Antes de ejecutar nada, un validador independiente analiza el AST oficial de PowerShell. La política es deliberadamente mínima: permite cualquier operación invocable por consola salvo eliminar, mover/cortar y formatear o reinicializar unidades.

## Requisitos

- Windows 10 u 11.
- [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).
- [Ollama](https://ollama.com/) en el mismo PC.
- El modelo `qwen3:8b` instalado:

```powershell
ollama pull qwen3:8b
```

Ollama debe estar iniciado. Se puede usar la aplicación de Ollama o ejecutar:

```powershell
ollama serve
```

## Iniciar el control móvil

Desde la raíz del repositorio:

```powershell
dotnet restore
dotnet run --project ControlPCIA\ControlPCIA.csproj
```

ControlPCIA muestra las direcciones de red local y un código de seis cifras. En el móvil:

1. Conéctalo a la misma red privada que el PC.
2. Abre una de las direcciones mostradas, por ejemplo `http://192.168.1.15:5187`.
3. Escribe el código que aparece en el PC.
4. Dicta o escribe la orden y envíala a la IA.

Si Windows muestra un aviso del firewall, se debe permitir únicamente en redes privadas. El servidor no modifica el firewall por su cuenta.

El botón de dictado usa el reconocimiento de voz del navegador cuando está disponible. Si el navegador no lo ofrece en una página HTTP local, se puede usar el micrófono del propio teclado de Android o iOS dentro del cuadro de texto.

## Aplicación Android

La experiencia principal está en `mobile/ControlPCIA.Mobile`. La aplicación:

- Busca automáticamente el PC en la red local mediante UDP, sin pedir su IP a un usuario normal.
- Permite introducir la dirección manual como alternativa.
- Guarda el token de emparejado en el almacén seguro del móvil.
- Usa un único micrófono para encender el PC mediante Wake-on-LAN o enviar cualquier otra petición directamente a Llama.
- El modo predeterminado se inicia con un toque y termina con otro; al terminar envía la transcripción automáticamente. Como alternativa se puede mantener pulsado y soltar para enviar.
- Muestra de forma visible cuándo prepara el micrófono, escucha, transcribe y ejecuta, con contador, texto parcial y respuesta háptica.
- Si no entiende la voz lo indica y permite repetirla. Si Llama necesita confirmar una acción permitida pero ambigua, pregunta y acepta una respuesta posterior de sí o no.
- Muestra una conversación temporal con la IA, conserva un contexto acotado para respuestas posteriores y no persiste salidas sensibles.

APK Android generado para instalación manual:

```text
mobile\ControlPCIA.Mobile\bin\Release\net10.0-android\publish\com.treska.controlpcia-Signed.apk
```

La publicación actual es la versión 1.4.1 (código 7), firmada con los esquemas APK v1, v2 y v3. Su SHA-256 es `BA1C6C62A3F93FA5CE32AEE74A3B0BB07F11D1C28812BB2382ED5693C12A5D42`.

Con el agente del PC encendido, el móvil puede descargar siempre la compilación
firmada más reciente desde:

```text
http://192.168.1.15:5187/app-android.apk
```

También aparece el botón **Descargar app Android** en la página principal. Abre
el APK descargado y permite la instalación desde esa fuente cuando Android lo
solicite. Una actualización conserva la configuración de la app porque usa la
misma firma. Después abre ControlPCIA en el móvil, pulsa **Buscar mi PC** y
escribe el código de seis cifras.

El APK actual tiene una firma local de desarrollo. Es instalable manualmente, pero para Google Play será necesario crear y proteger una clave de publicación definitiva. El mismo código está preparado para iPhone, aunque compilar y firmar la versión iOS requiere un Mac y una cuenta de Apple Developer.

Para volver a generar el APK:

```powershell
dotnet publish mobile\ControlPCIA.Mobile\ControlPCIA.Mobile.csproj -f net10.0-android -c Release -p:AndroidPackageFormat=apk
```

## Encendido por voz con Wake-on-LAN

Durante el primer emparejado, con el PC encendido, la aplicación aprende localmente la dirección MAC, el broadcast de su tarjeta activa y el puerto UDP 9. Desde entonces la pantalla principal continúa disponible aunque el PC no responda. El mismo botón de voz reconoce «enciende el ordenador», «arranca el PC» o «despierta el equipo» y envía el paquete Wake-on-LAN aunque Llama no esté disponible porque el PC esté apagado.

Es la única orden que se resuelve en el teléfono. Todas las demás se transcriben y se envían automáticamente por el flujo móvil → Llama → PowerShell. El modo predeterminado se toca una vez para empezar y otra para detener y enviar; el modo alternativo mantiene pulsado mientras se habla y envía al soltar. El cuadro inferior sólo sirve para escribir una orden manual. Android exige una acción del usuario para activar el micrófono y la aplicación no mantiene una escucha permanente en segundo plano.

Wake-on-LAN necesita:

- Que móvil y PC estén en la misma LAN.
- Wake-on-LAN habilitado en BIOS/UEFI y en la tarjeta de red.
- Que el adaptador y el estado de apagado del equipo admitan el encendido remoto.

ControlPCIA no cambia automáticamente BIOS, controladores ni ajustes de energía. El agente ya configura su inicio oculto por usuario para poder recibir órdenes después de arrancar Windows; sigue pendiente empaquetarlo en un instalador firmado.

## PWA de respaldo

La página incluye manifiesto, iconos, modo `standalone` y un service worker que guarda únicamente la interfaz. Nunca almacena respuestas de `/api/`, órdenes, resultados ni el APK; cada descarga de Android sale directamente del agente residente. La dirección actual de este PC es:

```text
http://192.168.1.15:5187
```

La IP puede cambiar si el router no la reserva. Además, los navegadores exigen HTTPS para la instalación PWA completa fuera de `localhost`; por eso, sobre HTTP en la LAN puede aparecer solo **Añadir a pantalla de inicio**. La app Android evita esa limitación y queda como opción principal.

## Control de aplicaciones por voz

Las órdenes no se resuelven con capturas, OCR ni reconocimiento gráfico. El flujo es:

    móvil → Llama propone un comando literal → validador local → proceso PowerShell → salida/código real → Llama responde

Llama no ejecuta acciones ni afirma resultados por su cuenta. ControlPCIA valida cada comando, lo ejecuta en un proceso externo de PowerShell y devuelve a Llama stdout, stderr y el código de salida. Si falla, el móvil recibe el error y puede continuar la conversación para aclarar la petición.

La consulta de ventanas abiertas se hace con comandos de consola (`Get-Process` y títulos de ventana). Los archivos pueden localizarse, leerse, crearse, copiarse, sobrescribirse y abrirse mediante PowerShell, programas nativos o las APIs de cada aplicación. Cuando una consulta ya produjo evidencia válida, ControlPCIA compone la respuesta directamente con el `stdout` real para que el modelo no pueda reinterpretarlo o contradecirlo.

La política permite controlar aplicaciones, audio, multimedia, pantallas, archivos, instalaciones y configuración de Windows cuando exista un comando, CLI, cmdlet, API o protocolo URI real. Las únicas prohibiciones sobre el equipo son eliminar, mover/cortar y formatear unidades. Renombrar está permitido. La automatización gráfica antigua sigue retirada: no se usan capturas, OCR, `SendKeys`, `AppActivate`, ratón ni teclado simulado.

## Agente residente de Windows

Al iniciar el servidor por primera vez, ControlPCIA registra su propio arranque para el usuario actual. En los siguientes inicios de sesión se ejecuta con `--servidor --oculto`: no deja una consola abierta, mantiene el servidor móvil y el descubrimiento UDP activos y muestra únicamente un icono en la bandeja del sistema.

Desde ese icono se puede ver el código para emparejar un móvil nuevo, abrir la página local, mostrar u ocultar la consola, activar o desactivar «Iniciar con Windows» y cerrar el agente. También existen estas opciones explícitas:

```powershell
ControlPCIA.exe --activar-inicio
ControlPCIA.exe --desactivar-inicio
```

Esta configuración la realiza ControlPCIA en `HKCU`, sin administrador y sólo para la sesión del usuario. El agente de consola también puede usar el registro y otras interfaces de Windows cuando una petición lo requiera; el subcomando gráfico antiguo de ControlPCIA no se expone a Llama.

## Aprendizaje local

Cuando una petición termina correctamente, la aplicación guarda una receta formada por:

- La intención normalizada.
- Los comandos que se ejecutaron correctamente.
- El número y la fecha de los éxitos.

No guarda stdout, stderr, contenido de archivos ni datos obtenidos del PC. Las recetas viven en:

```text
%LOCALAPPDATA%\ControlPCIA\recetas-v1.json
```

Ante una orden parecida, Llama recibe primero las recetas relacionadas. La memoria extrae además recursos comprobados —por ejemplo AppID, ejecutables, plantillas y rutas— y evita repetir una investigación cuando el recurso sigue existiendo. La IA adapta los datos variables, como el nombre y destino de un proyecto nuevo, y cada comando vuelve a pasar por el validador actual. Una receta que deje de cumplir la política queda descartada automáticamente.

## Seguridad

La barrera se aplica después del modelo y no depende de que Llama obedezca el prompt. La política de acciones tiene exactamente tres categorías prohibidas:

- Eliminar elementos o contenido, también mediante alias, APIs, intérpretes, desinstaladores o comandos anidados.
- Mover o cortar elementos. Renombrar sin cambiar de ubicación está permitido.
- Formatear, limpiar o reinicializar discos y unidades.

Todo lo demás que pueda invocarse desde consola está permitido: leer, buscar, crear, copiar, sobrescribir, abrir, guardar, descargar, instalar, configurar Windows, usar registro, servicios, red, módulos, ejecutables, intérpretes, APIs .NET y COM de aplicaciones.

El validador rechaza código codificado o nombres de comando dinámicos únicamente cuando impedirían comprobar esas tres prohibiciones. `SendKeys`, `AppActivate`, UI Automation y `ControlPCIA.exe ui` permanecen fuera porque la arquitectura gráfica fue retirada, no porque formen una cuarta categoría de seguridad. Las aplicaciones se controlan mediante comandos, CLI, cmdlets, APIs o protocolos reales.

Los nombres `Move-*` no se bloquean de forma genérica: colocar una ventana u
otro objeto está permitido. Se bloquean los movimientos persistentes
comprobables (`Move-Item`, `Move-ItemProperty`, APIs de archivo, `robocopy
/MOVE`, etc.). Del mismo modo, reemplazar texto y eliminar objetos temporales
de la sesión de PowerShell está permitido; reemplazar, mover o eliminar archivos
y contenido persistente sigue bloqueado.

Después de ejecutar un comando, Llama recibe stdout, stderr y el código de salida reales. ControlPCIA conserva comprobaciones deterministas para no declarar éxito sin evidencia y puede pedir un dato personal imprescindible, como el nombre de un proyecto. Cada proceso PowerShell tiene un máximo operativo de diez minutos y salida acotada para que una orden bloqueada no deje colgado el agente residente. Las órdenes multitarea admiten hasta 24 resultados y el presupuesto crece hasta 96 pasos.

El acceso móvil añade código de emparejado, token aleatorio de sesión, caducidad, límite de intentos, restricción a direcciones privadas, cabeceras de seguridad y una sola orden simultánea. El móvil conserva el token real en `SecureStorage`; el PC guarda únicamente su hash y caducidad durante 90 días, renovables con el uso, para que el emparejado sobreviva a los reinicios. Ollama sólo escucha para ControlPCIA en `127.0.0.1`.

La conexión móvil actual usa HTTP dentro de la LAN. Debe utilizarse sólo en una red doméstica o privada de confianza y nunca exponerse directamente a Internet. Ninguna automatización de escritorio puede garantizar matemáticamente que una aplicación externa no tenga efectos indirectos; la política reduce las vías conocidas y debe seguir ampliándose cuando aparezcan casos nuevos.

## Otros modos

Comprobar Ollama y el modelo:

```powershell
dotnet run --project ControlPCIA\ControlPCIA.csproj -- --diagnostico
```

Introducir una orden en la consola:

```powershell
dotnet run --project ControlPCIA\ControlPCIA.csproj -- --consola
```

Enviar una orden directamente:

```powershell
dotnet run --project ControlPCIA\ControlPCIA.csproj -- "abre la calculadora"
```

Probar el validador con PowerShell, sólo para desarrollo:

```powershell
dotnet run --project ControlPCIA\ControlPCIA.csproj -- --comando-powershell "Get-Process"
```

Configuración opcional mediante variables de entorno:

- `CONTROLPCIA_OLLAMA_URL`: URL HTTP local de Ollama. Sólo se aceptan direcciones loopback.
- `CONTROLPCIA_OLLAMA_MODELO`: modelo; valor predeterminado `qwen3:8b`.
- `CONTROLPCIA_PUERTO`: puerto móvil entre 1024 y 65535; valor predeterminado `5187`.

## Pruebas

```powershell
dotnet test ControlPCIA.slnx
```

La batería cubre una matriz amplia de operaciones permitidas, las tres prohibiciones y sus evasiones, ejecución con tiempo y salida limitados, red local, conversación, memoria persistente y reutilización de recursos aprendidos.

## Componentes principales

- `ControlWindows.cs`: conversación genérica con Llama y bucle de pasos.
- `PlanificadorTareasIA.cs`: descomposición y auditoría de peticiones con una o varias tareas.
- `ClienteOllama.cs`: conexión exclusivamente local con Ollama.
- `ValidadorPowerShell.cs`: análisis estructural y política de denegación.
- `EjecutorPowerShell.cs`: ejecución acotada después de validar.
- `MemoriaRecetas.cs`: aprendizaje local persistente y recuperación por similitud.
- `ServidorMovil.cs`: servidor privado, emparejado y web adaptable para móvil.
