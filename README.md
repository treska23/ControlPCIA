# ControlPCIA

ControlPCIA permite enviar una orden hablada o escrita desde una aplicación móvil para que un modelo local de Ollama decida cómo realizarla en Windows mediante PowerShell. También conserva una PWA como acceso de respaldo.

No hay un traductor intermedio ni una función programada para cada acción. El único flujo principal es:

```text
móvil → texto → Llama en Ollama → PowerShell → validador local → Windows
```

Llama puede razonar sobre aplicaciones, ventanas, audio, multimedia, pantallas y la interfaz de Windows. También puede crear y abrir documentos o proyectos, guardar, copiar y pegar a destinos nuevos e instalar programas. Antes de ejecutar nada, un validador independiente analiza el AST oficial de PowerShell y bloquea borrado o corte de archivos, sobrescrituras, operaciones destructivas de disco, credenciales y mecanismos de evasión.

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

La publicación actual es la versión 1.3 (código 4), firmada con los esquemas APK v1, v2 y v3. Su SHA-256 es `86070A0D5CDD62C27E8458E733D9F2441690F508401DF3C5BC1F3F4F9953A819`.

Para instalarlo, copia el APK al teléfono, ábrelo y permite la instalación desde esa fuente cuando Android lo solicite. Después abre ControlPCIA en el PC, pulsa **Buscar mi PC** en el móvil y escribe el código de seis cifras.

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

La página incluye manifiesto, iconos, modo `standalone` y un service worker que guarda únicamente la interfaz. Nunca almacena respuestas de `/api/`, órdenes ni resultados. La dirección actual de este PC es:

```text
http://192.168.1.15:5187
```

La IP puede cambiar si el router no la reserva. Además, los navegadores exigen HTTPS para la instalación PWA completa fuera de `localhost`; por eso, sobre HTTP en la LAN puede aparecer solo **Añadir a pantalla de inicio**. La app Android evita esa limitación y queda como opción principal.

## Control de aplicaciones por voz

Las órdenes no se resuelven con capturas, OCR ni reconocimiento gráfico. El flujo es:

    móvil → Llama propone un comando literal → validador local → proceso PowerShell → salida/código real → Llama responde

Llama no ejecuta acciones ni afirma resultados por su cuenta. ControlPCIA valida cada comando, lo ejecuta en un proceso externo de PowerShell y devuelve a Llama stdout, stderr y el código de salida. Si falla, el móvil recibe el error y puede continuar la conversación para aclarar la petición.

La consulta de ventanas abiertas se hace con comandos de consola (Get-Process y títulos de ventana). El aprendizaje sólo guarda secuencias que hayan terminado con una observación válida y vuelve a pasar cada receta por el validador antes de reutilizarla.

La política permite controlar aplicaciones, audio, multimedia, pantallas y ajustes normales, pero bloquea borrar o cortar/mover archivos, sobrescribir destinos, credenciales, consolas, seguridad y operaciones destructivas de discos. «No guardar/Descartar» sólo se habilita ante una confirmación inequívoca y contextual.

## Agente residente de Windows

Al iniciar el servidor por primera vez, ControlPCIA registra su propio arranque para el usuario actual. En los siguientes inicios de sesión se ejecuta con `--servidor --oculto`: no deja una consola abierta, mantiene el servidor móvil y el descubrimiento UDP activos y muestra únicamente un icono en la bandeja del sistema.

Desde ese icono se puede ver el código para emparejar un móvil nuevo, abrir la página local, mostrar u ocultar la consola, activar o desactivar «Iniciar con Windows» y cerrar el agente. También existen estas opciones explícitas:

```powershell
ControlPCIA.exe --activar-inicio
ControlPCIA.exe --desactivar-inicio
```

Esta configuración la realiza código de confianza de ControlPCIA en `HKCU`, sin administrador y sólo para la sesión del usuario. Llama no puede ejecutar estas opciones: el validador continúa bloqueando registro, arranque y configuración sensible.

## Aprendizaje local

Cuando una petición termina correctamente, la aplicación guarda una receta formada por:

- La intención normalizada.
- Los comandos que se ejecutaron correctamente.
- El número y la fecha de los éxitos.

No guarda stdout, stderr, contenido de archivos ni datos obtenidos del PC. Las recetas viven en:

```text
%LOCALAPPDATA%\ControlPCIA\recetas-v1.json
```

Ante una orden parecida, Llama recibe las recetas relacionadas como referencias. No se ejecutan directamente: la IA revisa el contexto, puede adaptarlas y cada comando vuelve a pasar por el validador actual. Una receta que deje de cumplir la política queda descartada automáticamente.

## Seguridad

La barrera de seguridad se aplica después del modelo y no depende de que Llama obedezca el prompt. Entre otras restricciones:

- Nunca se permite borrar archivos o carpetas, cortar/moverlos ni sobrescribir un destino existente.
- La creación y la copia directas sólo admiten rutas locales, absolutas y literales y destinos nuevos.
- Se bloquean desinstalación, operaciones destructivas sobre discos o particiones, credenciales, permisos, cuentas, Defender y arranque.
- `winget` sólo puede consultar el catálogo oficial o instalar para el usuario actual mediante un identificador literal; no acepta manifiestos, URLs, anulaciones ni desinstalación.
- Se bloquean intérpretes anidados, ejecución dinámica, reflexión peligrosa, exfiltración y clientes de red arbitrarios.
- `Start-Process` puede abrir aplicaciones registradas y rutas literales de documentos o proyectos, pero nunca ejecutables, scripts, instaladores o ubicaciones de red.
- Los programas nativos reciben argumentos literales validados.
- COM queda limitado al mecanismo de interfaz `WScript.Shell`, y `SendKeys` no admite texto libre.
- Las aplicaciones se controlan únicamente con comandos de consola de Windows o con la CLI/PowerShell documentada por cada aplicación. No se usan capturas, OCR, UI Automation ni cuadros de texto.
- Después de ejecutar un comando, Llama recibe stdout, stderr y el código de salida reales. Sólo puede afirmar que terminó cuando esa salida demuestra el resultado.
- El control queda limitado a las aplicaciones mencionadas por el usuario; una orden para Calculadora no debe actuar sobre ChatGPT u otra aplicación ajena.
- Una petición ambigua puede devolver una pregunta de confirmación. Confirmar no habilita borrado/corte de archivos ni daños de disco; únicamente una confirmación contextual explícita puede habilitar el control nativo «No guardar/Descartar».
- Cada proceso PowerShell tiene 20 segundos de límite, salida acotada y terminación del árbol completo si vence el tiempo.

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

La batería cubre comandos permitidos, operaciones restringidas, evasiones conocidas, ejecución con tiempo y salida limitados, red local, memoria persistente y revalidación de recetas.

## Componentes principales

- `ControlWindows.cs`: conversación genérica con Llama y bucle de pasos.
- `ClienteOllama.cs`: conexión exclusivamente local con Ollama.
- `ValidadorPowerShell.cs`: análisis estructural y política de denegación.
- `EjecutorPowerShell.cs`: ejecución acotada después de validar.
- `MemoriaRecetas.cs`: aprendizaje local persistente y recuperación por similitud.
- `ServidorMovil.cs`: servidor privado, emparejado y web adaptable para móvil.
- `ObservadorWindows.cs`: contexto de ventanas visibles que recibe la IA.
