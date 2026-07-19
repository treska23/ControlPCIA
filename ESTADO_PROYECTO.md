# ControlPCIA — estado estable

Última actualización: 19 de julio de 2026

## Decisión de producto

La aplicación mantiene activas todas las funciones que ya han demostrado un
comportamiento estable mientras se incorporan las siguientes capacidades de
forma incremental. El trabajo experimental no se borra, pero tampoco se conecta
a la APK hasta que su comportamiento esté validado.

Flujo activo:

```text
APK Android 1.5.5
→ red local y emparejado
→ agente residente de Windows
→ AsistenteControl
→ ControlBasico inmediato o una llamada a qwen3.5:9b
→ uno o varios comandos conocidos, validados antes de ejecutar
→ stdout, stderr y código de salida
→ respuesta al móvil
```

Qwen participa únicamente como traductor o interlocutor cuando el núcleo
determinista no reconoce la petición. ControlPCIA sigue siendo el único que
valida y ejecuta comandos.

## Funciones activas

1. Encender el ordenador por voz mediante Wake-on-LAN desde la APK.
2. Abrir aplicaciones instaladas.
3. Consultar qué aplicaciones tienen una ventana abierta.
4. Abrir una página en el navegador predeterminado.
5. Buscar en Internet, Google, Bing o YouTube.
6. Dictar o escribir la petición desde la APK.
7. Descubrir y emparejar el PC en la red local.
8. Mantener el agente oculto e iniciado con Windows.
9. Usar la PWA como respaldo y descargar la APK desde el agente.
10. Consultar y configurar pantallas mediante las API de Windows.
11. Controlar la reproducción de una sesión multimedia actual, de Spotify o
    de un navegador.
12. Consultar, traer, maximizar, minimizar, restaurar, colocar y cerrar ventanas
    superiores mediante las API de Windows.
13. Traducir primero una orden compuesta, ejecutar sus acciones en orden y
    detenerse ante el primer error.
14. Traducir órdenes generales con una sola llamada al modelo local
    `qwen3.5:9b`.
15. Mantener contexto conversacional y preguntar al usuario cuando falte una
    decisión.
16. Aprender comandos que terminaron correctamente y reutilizar coincidencias
    exactas sin volver a llamar al modelo.

## Semántica de apertura

ControlPCIA obtiene el AppID real mediante `Get-StartApps` y envía un único
`Start-Process`.

No busca después procesos ni ventanas. No afirma haber comprobado la apertura.
Sólo comunica:

- el error devuelto por PowerShell; o
- que Windows aceptó el comando.

## Implementación anterior conservada pero inactiva

El repositorio mantiene `ControlWindows`, el traductor iterativo anterior, y
sus pruebas. No se ha borrado, pero permanece desconectado: sus consultas,
verificaciones y ocho rondas fueron sustituidas en el flujo activo por
`TraductorLocalRapido`.

## Semántica web

ControlPCIA entrega una única URL `http` o `https` mediante `Start-Process`.
Windows decide qué navegador abrir. No se inspecciona la ventana ni se verifica
la página después. Un error de PowerShell se devuelve al móvil.

Los sitios conocidos y dominios se abren directamente. Las búsquedas generales
utilizan Google y las peticiones «busca … en YouTube» utilizan los resultados
de YouTube. Los esquemas locales como `file:` nunca se abren.

## Semántica de pantallas

El agente incorpora `ControlPCIA.exe display`, que usa
`EnumDisplayDevices`, `EnumDisplaySettings`, `ChangeDisplaySettingsEx`,
`QueryDisplayConfig`, `SetDisplayConfig` y `DisplayConfigSetDeviceInfo`. No
abre la interfaz de Configuración ni simula entrada.

Admite consulta de pantallas y modos; pantalla principal; resolución;
frecuencia; escala por monitor; activación y desactivación; topología
extendida, duplicada, interna o externa; orientación; coordenadas y colocación
relativa.

La selección de pantalla principal se ha validado en la topología real de tres
monitores: cualquier pantalla activa puede pasar a `(0,0)` de forma atómica y
la consulta posterior confirma cuál quedó como principal.

## Semántica de ventanas

El agente incorpora `ControlPCIA.exe window`. Consulta ventanas superiores por
título o proceso y puede traerlas al frente, maximizar, minimizar, restaurar,
mover, redimensionar y solicitar su cierre normal mediante Win32. No inspecciona
su contenido y no utiliza OCR, ratón, teclado ni UI Automation.

Después de un cierre espera la respuesta de la aplicación. Si la ventana sigue
abierta —por ejemplo, porque hay trabajo sin guardar— devuelve el error real al
móvil en vez de asegurar que la tarea se completó.

## Semántica multimedia

El agente incorpora `ControlPCIA.exe media`, basado en
`GlobalSystemMediaTransportControlsSessionManager`. Controla sólo las sesiones
que una aplicación haya publicado en Windows y devuelve si esa aplicación
aceptó o rechazó la orden.

Admite play, pausa, alternancia, stop, anterior, siguiente, avance, retroceso,
posición, velocidad, aleatorio y repetición. Puede elegir Spotify, un navegador
o la sesión multimedia actual. Para la pantalla completa interna del vídeo,
activa el navegador y envía únicamente `F`; para salir envía `Escape`.

## Evidencias actuales

- **386/386 pruebas Release correctas**.
- APK congelada: versión **1.5.5**, código **15**.
- SHA-256 de la APK:
  `F7EEA61ED2E2E0EB4D89C3AA33296B13D0B9522806407CA9239BD5D1CEF96198`.
- Agente instalado en:
  `%LOCALAPPDATA%\ControlPCIA\App`.
- SHA-256 de la DLL instalada:
  `D3F0759EBBBBC26C4ABA5BDF6D715AE32ABE6C7E14C6CF488CB4B501CD513D3D`.
- La DLL instalada coincide byte por byte con la publicación Release.
- La APK servida por el agente coincide byte por byte con el artefacto 1.5.5.
- Agente residente activo en `0.0.0.0:5187`.
- Inicio con Windows registrado con `--servidor --oculto`.
- Página principal: HTTP 200.
- Descarga `/app-android.apk`: HTTP 200,
  `application/vnd.android.package-archive`, 22.567.283 bytes.
- Emparejado real del API: correcto.
- `/api/estado`: `disponible: true`, modo `control-basico`,
  `modoPrueba: false`.
- El agente entrega a la APK un destino Wake-on-LAN válido.
- Consulta autenticada real de aplicaciones abiertas: completada mediante un
  solo paso de PowerShell.
- Petición no compatible: rechazada con estado `no_disponible` y cero pasos.
- `abre Cubase` en modo sin ejecución: AppID real de Cubase 15 y exactamente un
  comando, sin verificación posterior.
- `abre el explorador de Windows`: AppID exacto
  `Microsoft.Windows.Explorer`; el comparador ya no acepta coincidencias
  parciales producidas por palabras de dos letras como `do` en Click to Do.
- `abre YouTube`: un único comando con `https://www.youtube.com/`.
- `busca vídeos de batería en YouTube`: un único comando con la búsqueda de
  YouTube codificada.
- `busca ControlPCIA por internet`: un único comando con la búsqueda de Google
  codificada.
- Consulta Win32 real: tres pantallas activas de 3840x2160 a 60 Hz y una salida
  inactiva, sin aplicar ningún cambio.
- Consulta real de modos compatibles de la pantalla principal: correcta.
- Traducciones de pantalla principal, resolución, frecuencia, orientación,
  posición, topología y desactivación: un único comando y sin ejecución en la
  validación.
- Consulta Win32 real de ventanas de Edge: correcta, incluyendo estado,
  coordenadas y ventana en primer plano.
- Traducciones de primer plano, maximizado, minimizado, restauración, posición
  y cierre de ventanas: un único comando y sin ejecución en la validación.
- Traducción de órdenes compuestas: prepara todos los comandos antes de
  ejecutar, conserva el orden y no divide términos de búsqueda unidos por «y».
- Traducción real con `qwen3.5:9b`: una respuesta estructurada por petición,
  sin ejecutar durante la validación.
- La ruta determinista «pantalla número tres como principal» se traduce en
  menos de 100 ms; una respuesta conversacional caliente de Qwen tarda alrededor
  de 1,9 s en esta máquina.
- Conversación real: «cuánto es dos más cinco» devuelve «siete» sin comando.
- La ruta de Escritorio se resuelve con `Environment.GetFolderPath`; se bloquean
  rutas personales y archivos de proyecto inventados.
- Memoria real: `hostname` se aprendió después de una ejecución correcta y la
  segunda petición exacta evitó llamar al modelo.
- Consulta multimedia real: Windows publica correctamente la sesión de Edge,
  con metadatos, estado, posición y capacidades de control.
- Traducción de «pausa el vídeo que estoy viendo por internet»: una llamada a
  la sesión multimedia del navegador, sin ejecutar durante la validación.

## Siguientes capacidades

Las siguientes capacidades siguen pendientes y deberán incorporarse sin
modificar lo que ya funciona:

- control interno de aplicaciones;
- más configuraciones todavía no incorporadas al núcleo inmediato;
- ratón táctil y teclado virtual desde la APK;
- acceso fuera de la red local;
- instalador firmado para distribución pública.

## Regla de continuidad

Antes de añadir una capacidad:

1. No modificar la APK 1.5.5 salvo petición expresa.
2. No retirar Wake-on-LAN ni ninguna función estable.
3. Conservar el agente residente, emparejado e inicio con Windows.
4. Añadir una sola capacidad.
5. Probarla sin manipular el escritorio automáticamente.
6. Activarla sólo cuando el resultado real sea estable.
