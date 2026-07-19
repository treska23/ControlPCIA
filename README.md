# ControlPCIA

ControlPCIA permite controlar desde un móvil un conjunto reducido y estable de
funciones de un PC Windows. La versión actual conserva únicamente capacidades
que ya están comprobadas:

- Encender el PC por voz mediante Wake-on-LAN.
- Abrir aplicaciones instaladas.
- Consultar qué aplicaciones tienen una ventana abierta.
- Abrir páginas y realizar búsquedas en el navegador predeterminado.
- Consultar y cambiar la configuración de las pantallas.
- Traer, maximizar, minimizar, restaurar, colocar o cerrar ventanas superiores.
- Consultar y controlar sesiones de reproducción multimedia.

La aplicación Android existente se mantiene sin cambios. El agente de Windows
se ejecuta en segundo plano, recibe las peticiones de la APK y utiliza
PowerShell y comandos propios para las funciones del PC. Los comandos de
pantallas y multimedia usan directamente las API oficiales de Windows.

## Funcionamiento actual

### Traducción local y conversación

ControlPCIA intenta primero el núcleo determinista, que responde en milisegundos
a las funciones conocidas. Si no puede traducir una petición, realiza una sola
llamada al modelo local gratuito `qwen3.5:9b` mediante Ollama.

El modelo únicamente elige entre responder, preguntar o proponer un comando.
Nunca ejecuta nada. ControlPCIA comprueba la sintaxis, la existencia de los
comandos y las tres prohibiciones antes de iniciar PowerShell. No hay bucles de
verificación gráfica ni un límite artificial de dos pasos.

La APK envía las últimas intervenciones como contexto, por lo que el usuario
puede aclarar una petición o responder una pregunta del asistente. Cuando un
comando propuesto termina correctamente, se guarda localmente. La misma
petición exacta reutiliza después esa traducción sin volver a consultar a Qwen;
las coincidencias aproximadas sólo se entregan al modelo como referencias.

El agente precarga en segundo plano tanto el inventario de aplicaciones como el
modelo local al iniciar Windows.

### Abrir una aplicación

Ejemplos admitidos:

```text
abre Cubase
abre la calculadora
quiero que abras el bloc de notas
inicia Microsoft Edge
```

ControlPCIA consulta `Get-StartApps`, selecciona una aplicación instalada y
envía a Windows un único comando `Start-Process` con su AppID real.

Después de enviarlo:

- Si PowerShell devuelve un error, ese error se entrega al móvil.
- Si PowerShell acepta el comando, ControlPCIA informa de que la orden fue
  enviada.
- No inspecciona la pantalla, no busca la ventana y no comprueba posteriormente
  si la aplicación está abierta.

Una petición puede contener varias acciones claras, por ejemplo:

```text
abre la calculadora y luego abre YouTube
pon la pantalla 3 como principal y cambia su resolución a 1920 por 1080
```

ControlPCIA prepara primero todas las acciones. Si alguna no se puede traducir,
no ejecuta ninguna. Después las ejecuta en orden y se detiene en el primer error
real de Windows. No divide expresiones que sólo contienen una conjunción, como
`busca rock y metal en YouTube`.

### Abrir páginas y buscar en Internet

Ejemplos admitidos:

```text
abre YouTube
abre la página de Wikipedia
entra en youtube.com
abre https://openai.com/research/
busca baterías electrónicas por internet
busca vídeos drumless en YouTube
```

ControlPCIA construye una URL `http` o `https` y la entrega a Windows mediante
un único `Start-Process`. Windows utiliza el navegador predeterminado.

- Los nombres conocidos, como YouTube, se abren directamente.
- Los dominios y URL se abren directamente.
- Una página desconocida solicitada expresamente se busca en Google.
- Las búsquedas normales usan Google.
- «Busca … en YouTube» abre los resultados de YouTube.
- No se aceptan esquemas locales como `file:`.
- No se inspecciona ni se comprueba posteriormente el navegador.

### Consultar aplicaciones abiertas

Ejemplos admitidos:

```text
qué programas tengo abiertos
dime qué aplicaciones hay en ejecución
qué ventanas tengo abiertas
```

La respuesta procede directamente de `Get-Process`, filtrando los procesos con
una ventana superior visible. No se utilizan capturas, OCR, ratón, teclado
simulado ni reconocimiento gráfico.

### Controlar ventanas

Ejemplos admitidos:

```text
trae Microsoft Edge al frente
maximiza Visual Studio
minimiza Cubase
restaura la calculadora
coloca Edge en x 0 y 20 ancho 1920 alto 1060
cierra el bloc de notas
```

`ControlPCIA.exe window` busca ventanas superiores por el título o el nombre
del proceso y utiliza las API Win32. Puede consultar, activar, maximizar,
minimizar, restaurar, mover, redimensionar y solicitar un cierre normal. No
inspecciona el contenido interno, no usa OCR, ratón, teclado ni UI Automation.

Al cerrar, la aplicación conserva la oportunidad de mostrar su aviso de trabajo
sin guardar. Si la ventana sigue abierta, ControlPCIA devuelve esa situación al
móvil en vez de afirmar que se ha cerrado.

### Configurar pantallas

Ejemplos admitidos:

```text
qué pantallas tengo conectadas
dime qué resoluciones soporta el monitor 2
pon la pantalla 2 como principal
quiero que la pantalla número tres sea la principal
cambia la resolución del monitor 2 a 1920 por 1080
pon la pantalla principal en 4K a 60 Hz
pon la escala del monitor 3 al 150 por ciento
desactiva la pantalla 3
activa el monitor 2
duplica las pantallas
extiende el escritorio entre los monitores
usa solo la pantalla del PC
pon solo la segunda pantalla
gira el monitor 2 en vertical
gira el monitor número tres 270 grados
coloca la pantalla 2 a la derecha de la pantalla 1
```

El comando de consola subyacente es `ControlPCIA.exe display`. Usa las API
Win32 de visualización, sin abrir Configuración y sin simular ratón o teclado.
Permite:

- listar pantallas activas e inactivas y sus modos compatibles;
- elegir la pantalla principal;
- cambiar resolución, frecuencia y escala por monitor;
- activar o desactivar una salida;
- extender, duplicar, usar sólo la pantalla interna o sólo la externa;
- cambiar orientación;
- cambiar coordenadas o colocar una pantalla a la izquierda, derecha, encima o
  debajo de otra.

Windows valida los modos anunciados por el controlador antes de aplicarlos. El
resultado que vuelve al móvil es la aceptación o el error devuelto por la API,
sin reconocimiento gráfico posterior.

### Controlar reproducción

Ejemplos admitidos:

```text
pausa la reproducción
para el vídeo que estoy viendo por internet
haz play en Spotify
reanuda el vídeo de YouTube
pon la siguiente canción en Spotify
vuelve a la canción anterior
adelanta el vídeo de internet 30 segundos
retrocede la reproducción 15 segundos
qué canción se está reproduciendo en Spotify
activa el modo aleatorio en Spotify
repite esta canción
pon el vídeo de YouTube en pantalla completa
sal de la pantalla completa del vídeo
```

`ControlPCIA.exe media` usa el transporte multimedia global de Windows. Puede
seleccionar la sesión que Windows considera actual, una sesión de Spotify, la
de un navegador concreto o la sesión publicada por otra aplicación conocida.
Las operaciones disponibles dependen de lo que esa aplicación publique:
reproducir, pausar, alternar, detener, anterior, siguiente, avanzar,
retroceder, cambiar posición, velocidad, aleatorio y repetición.

Para poner el vídeo interno de un navegador en pantalla completa,
`ControlPCIA.exe media fullscreen --app browser` activa una ventana del
navegador y envía la tecla `F` mediante la API `SendInput` de Windows. Para
salir envía `Escape`. Esta excepción está limitada a esos dos comandos: no
expone escritura de teclas arbitrarias.

### Encender el PC por voz

La APK reconoce localmente frases como:

```text
enciende el ordenador
arranca el PC
despierta el equipo
```

El teléfono envía el paquete mágico Wake-on-LAN aunque el ordenador esté
apagado. Para guardar la dirección MAC y el broadcast, el móvil debe haberse
conectado correctamente al agente al menos una vez.

Wake-on-LAN también requiere que la BIOS/UEFI, la tarjeta de red y el estado de
apagado del equipo permitan el encendido remoto.

## Aplicación Android

La APK estable es la versión **1.5.5** (código 15). Incluye:

- Descubrimiento automático del PC en la red local.
- Dirección manual como alternativa.
- Emparejado mediante código de seis cifras.
- Token de sesión almacenado con `SecureStorage`.
- Dictado de voz nativo de Android.
- Botón circular: mantener pulsado, soltar para enviar y arrastrar hasta el
  candado para dejar el micrófono anclado.
- Cancelación de una escucha sin enviar la orden ni cerrar la aplicación.
- Envío de órdenes escritas.
- Encendido por Wake-on-LAN desde el mismo control de voz.

La APK se conserva como artefacto firmado en:

```text
mobile\ControlPCIA.Mobile\artifacts\release\com.treska.controlpcia-Signed.apk
```

SHA-256:

```text
F7EEA61ED2E2E0EB4D89C3AA33296B13D0B9522806407CA9239BD5D1CEF96198
```

El agente residente permite descargar exactamente ese archivo desde:

```text
http://DIRECCION-DEL-PC:5187/app-android.apk
```

## Agente residente de Windows

ControlPCIA escucha en el puerto `5187`, se registra para el usuario actual y
arranca con:

```text
ControlPCIA.exe --servidor --oculto
```

Un mutex impide que existan dos servidores. El icono de la bandeja permite:

- Ver el código para emparejar un móvil.
- Abrir la página local de respaldo.
- Mostrar u ocultar la consola.
- Activar o desactivar el inicio con Windows.
- Cerrar el agente.

Las sesiones móviles se conservan fuera de la carpeta del ejecutable, por lo
que actualizar el agente no obliga a emparejar de nuevo la APK.

## Inicio desde el repositorio

Requisitos:

- Windows 10 u 11.
- .NET 10.
- Ollama con el modelo gratuito `qwen3.5:9b` para las órdenes que no cubre el
  núcleo inmediato.

Sin Ollama siguen funcionando las rutas deterministas: Wake-on-LAN,
aplicaciones, web, ventanas, pantallas y multimedia.

```powershell
dotnet restore
dotnet run --project ControlPCIA\ControlPCIA.csproj -- --servidor
```

El PC mostrará sus direcciones locales y un código de seis cifras. El móvil debe
estar en la misma red privada.

Para publicar el agente:

```powershell
dotnet publish ControlPCIA\ControlPCIA.csproj `
  --configuration Release `
  --output artifacts\windows-agent
```

## Diagnóstico sin ejecutar

Se puede comprobar cómo se resolverá una apertura sin iniciar la aplicación:

```powershell
ControlPCIA.exe --traducir-sin-ejecutar "abre Cubase"
```

La salida JSON debe contener un solo comando y `ejecutado: false`.

Para consultar directamente las aplicaciones abiertas:

```powershell
ControlPCIA.exe "qué programas tengo abiertos"
```

## Red y seguridad

- El servidor sólo acepta conexiones loopback o de redes privadas.
- El emparejado utiliza un código temporal de seis cifras.
- Las sesiones usan tokens aleatorios; el PC conserva únicamente su hash.
- Sólo se procesa una petición cada vez.
- La APK usa HTTP exclusivamente dentro de la red local de confianza.
- El móvil no envía PowerShell arbitrario. Envía lenguaje natural; el núcleo o
  Qwen proponen un comando y el agente lo analiza antes de ejecutarlo.
- La validación de comandos sólo prohíbe tres clases destructivas: eliminar,
  mover o cortar, y formatear, limpiar o reinicializar unidades. Abrir y crear
  no están prohibidos.
- Wake-on-LAN se ejecuta localmente en el teléfono.

## Alcance deliberadamente no incluido

Estas funciones no forman parte de la versión estable actual:

- Control interno de aplicaciones que no publiquen una interfaz de consola.
- Ratón táctil y teclado virtual desde la APK.

El traductor iterativo experimental anterior se conserva en el repositorio,
pero no está conectado al servidor ni a la consola principal.

## Pruebas

```powershell
dotnet test tests\ControlPCIA.Tests\ControlPCIA.Tests.csproj `
  --configuration Release
```

La batería actual contiene **386 pruebas**. Cubre el controlador básico,
inventario de aplicaciones, errores de PowerShell, Wake-on-LAN, reconocimiento
de la orden de encendido, gesto de voz, cancelación, emparejado, sesiones,
red privada, servidor, validador y el código experimental conservado. Incluye
una regresión específica para impedir que «Explorador de Windows» vuelva a
resolverse como Click to Do, además de páginas, dominios, búsquedas normales y
búsquedas en YouTube. Las pruebas nuevas cubren la traducción y validación de
órdenes compuestas, traductor local de una llamada, memoria, conversación,
pantallas, ventanas y multimedia sin aplicar cambios reales al escritorio ni a
la reproducción.

## Componentes

- `ControlBasico.cs`: dirige las funciones estables del PC.
- `AsistenteControl.cs`: prioriza el núcleo rápido y activa el traductor local
  sólo como respaldo.
- `TraductorLocalRapido.cs`: pide una traducción estructurada de una sola
  llamada y entrega el comando al programa.
- `MemoriaTraducciones.cs`: conserva traducciones que terminaron correctamente.
- `ControlWebBasico.cs`: crea URL seguras y las entrega al navegador
  predeterminado sin inspeccionarlo.
- `ControlPantallasBasico.cs`: traduce lenguaje natural de pantallas.
- `ComandoPantallas.cs`: consulta y configura pantallas mediante Win32.
- `ControlVentanasBasico.cs`: traduce lenguaje natural de ventanas superiores.
- `ComandoVentanas.cs`: consulta y controla ventanas superiores mediante Win32.
- `ControlMultimediaBasico.cs`: traduce las órdenes de reproducción.
- `ComandoMultimedia.cs`: usa las sesiones multimedia globales de Windows.
- `InventarioAplicaciones.cs`: obtiene aplicaciones reales con
  `Get-StartApps`.
- `EjecutorPowerShell.cs`: ejecuta el comando y devuelve stdout, stderr y el
  código de salida.
- `ServidorMovil.cs`: API privada, emparejado, PWA y descarga de la APK.
- `InformacionWakeOnLan.cs`: entrega a la APK los datos de red necesarios.
- `GestorInicioWindows.cs`: inicio oculto para el usuario actual.
- `AgenteBandeja.cs`: icono y controles del agente residente.
- `mobile/ControlPCIA.Mobile`: aplicación Android estable, congelada en la
  versión 1.5.5.
