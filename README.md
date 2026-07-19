# ControlPCIA

ControlPCIA permite controlar desde un móvil un conjunto reducido y estable de
funciones de un PC Windows. La versión actual conserva únicamente capacidades
que ya están comprobadas:

- Encender el PC por voz mediante Wake-on-LAN.
- Abrir una aplicación instalada, una cada vez.
- Consultar qué aplicaciones tienen una ventana abierta.

La aplicación Android existente se mantiene sin cambios. El agente de Windows
se ejecuta en segundo plano, recibe las peticiones de la APK y utiliza
PowerShell para las dos funciones del PC.

## Funcionamiento actual

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

La versión estable admite una sola aplicación por petición. Una orden como
`abre la calculadora y el bloc de notas` se rechaza sin ejecutar nada.

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

No se necesita Ollama ni descargar un modelo para esta versión estable.

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
- El endpoint móvil no acepta PowerShell arbitrario: el controlador estable
  construye únicamente los comandos necesarios para abrir una aplicación o
  consultar ventanas abiertas.
- Wake-on-LAN se ejecuta localmente en el teléfono.

## Alcance deliberadamente no incluido

Estas funciones no forman parte de la versión estable actual:

- Traducción general mediante Llama, Qwen u otro modelo.
- Órdenes multitarea.
- Control interno de Cubase u otras aplicaciones.
- Conversación para resolver acciones complejas.
- Aprendizaje automático de nuevos comandos.
- Manipulación de ventanas, archivos o configuración desde el móvil.

El código experimental anterior se conserva en el repositorio para poder
retomarlo en el futuro, pero no está conectado al servidor ni a la consola
principal.

## Pruebas

```powershell
dotnet test tests\ControlPCIA.Tests\ControlPCIA.Tests.csproj `
  --configuration Release
```

La batería actual contiene **250 pruebas**. Cubre el controlador básico,
inventario de aplicaciones, errores de PowerShell, Wake-on-LAN, reconocimiento
de la orden de encendido, gesto de voz, cancelación, emparejado, sesiones,
red privada, servidor, validador y el código experimental conservado. Incluye
una regresión específica para impedir que «Explorador de Windows» vuelva a
resolverse como Click to Do.

## Componentes

- `ControlBasico.cs`: interpreta y ejecuta únicamente las dos funciones
  estables del PC.
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
