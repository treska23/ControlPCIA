# ControlPCIA

ControlPCIA permite enviar una orden hablada o escrita desde una aplicación móvil para que un modelo local de Ollama decida cómo realizarla en Windows mediante PowerShell. También conserva una PWA como acceso de respaldo.

No hay un traductor intermedio ni una función programada para cada acción. El único flujo principal es:

```text
móvil → texto → Llama en Ollama → PowerShell → validador local → Windows
```

Llama puede razonar sobre aplicaciones, ventanas, audio, multimedia, pantallas y la interfaz de Windows. Antes de ejecutar nada, un validador independiente analiza el AST oficial de PowerShell y bloquea acceso a archivos, configuración sensible y mecanismos de evasión.

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
- Ofrece dos formas de hablar: mantener pulsado y soltar para enviar, o escucha bloqueada hasta tocar detener.
- Muestra de forma visible cuándo prepara el micrófono, escucha, transcribe y ejecuta, con contador, texto parcial y respuesta háptica.
- Muestra estado, historial sencillo y recetas aprendidas sin conservar salidas sensibles.

APK Android generado para instalación manual:

```text
mobile\ControlPCIA.Mobile\bin\Release\net10.0-android\publish\com.treska.controlpcia-Signed.apk
```

Para instalarlo, copia el APK al teléfono, ábrelo y permite la instalación desde esa fuente cuando Android lo solicite. Después abre ControlPCIA en el PC, pulsa **Buscar mi PC** en el móvil y escribe el código de seis cifras.

El APK actual tiene una firma local de desarrollo. Es instalable manualmente, pero para Google Play será necesario crear y proteger una clave de publicación definitiva. El mismo código está preparado para iPhone, aunque compilar y firmar la versión iOS requiere un Mac y una cuenta de Apple Developer.

Para volver a generar el APK:

```powershell
dotnet publish mobile\ControlPCIA.Mobile\ControlPCIA.Mobile.csproj -f net10.0-android -c Release -p:AndroidPackageFormat=apk
```

## Encendido por voz con Wake-on-LAN

Durante el primer emparejado, con el PC encendido, la aplicación aprende localmente la dirección MAC, el broadcast de su tarjeta activa y el puerto UDP 9. Desde entonces la pantalla principal continúa disponible aunque el PC no responda. El mismo botón de voz reconoce «enciende el ordenador», «arranca el PC» o «despierta el equipo» y envía el paquete Wake-on-LAN aunque Llama no esté disponible porque el PC esté apagado.

Es la única orden que se resuelve en el teléfono. Todas las demás se transcriben y se envían automáticamente por el flujo móvil → Llama → PowerShell. En el modo normal se mantiene pulsado el botón mientras se habla; en el modo bloqueado se toca una vez para empezar y otra para detener. Android exige una acción del usuario para activar el micrófono y la aplicación no mantiene una escucha permanente en segundo plano.

Wake-on-LAN necesita:

- Que móvil y PC estén en la misma LAN.
- Wake-on-LAN habilitado en BIOS/UEFI y en la tarjeta de red.
- Que el adaptador y el estado de apagado del equipo admitan el encendido remoto.

ControlPCIA no cambia automáticamente BIOS, controladores ni ajustes de energía. Para controlar el PC después de arrancarlo también será necesario configurar de forma explícita el inicio automático del servidor; esa opción todavía está pendiente de un instalador seguro.

## PWA de respaldo

La página incluye manifiesto, iconos, modo `standalone` y un service worker que guarda únicamente la interfaz. Nunca almacena respuestas de `/api/`, órdenes ni resultados. La dirección actual de este PC es:

```text
http://192.168.1.15:5187
```

La IP puede cambiar si el router no la reserva. Además, los navegadores exigen HTTPS para la instalación PWA completa fuera de `localhost`; por eso, sobre HTTP en la LAN puede aparecer solo **Añadir a pantalla de inicio**. La app Android evita esa limitación y queda como opción principal.

## Control de aplicaciones por voz

Las órdenes de la aplicación móvil también pueden actuar dentro de programas abiertos. No existe una función específica para Cubase, Spotify o cada aplicación: Llama recibe las ventanas controlables y utiliza primitivas genéricas de UI Automation para:

- Enumerar ventanas e inspeccionar botones, menús, pestañas, listas, árboles y cuadros de edición.
- Enfocar una aplicación, invocar un control, seleccionar elementos, alternar opciones y expandir secciones.
- Escribir texto breve en controles identificados y enviar atajos de teclado validados.
- Volver a inspeccionar la interfaz después de abrir un menú o diálogo y encadenar hasta diez pasos.

Por ejemplo, para «crea una pista en Cubase» o «inserta Kontakt», Llama debe observar los nombres reales que expone Cubase, decidir la secuencia y ejecutarla. Cuando una secuencia termina bien, la memoria de recetas puede reutilizarla en peticiones parecidas. Si una aplicación dibuja una interfaz personalizada que no expone controles accesibles, sólo podrán utilizarse los atajos seguros que esa aplicación admita.

La capa de UI bloquea consolas, exploradores de archivos, credenciales, herramientas de desarrollo, campos de contraseña y controles relacionados con abrir, guardar, importar, exportar, descargar, instalar, imprimir, eliminar o descartar contenido. Los atajos de guardado, apertura, impresión, cierre, portapapeles y borrado también están bloqueados.

## Agente residente de Windows

Al iniciar el servidor por primera vez, ControlPCIA registra su propio arranque para el usuario actual. En los siguientes inicios de sesión se ejecuta con `--servidor --oculto`: no deja una consola abierta, mantiene el servidor móvil y el descubrimiento UDP activos y muestra únicamente un icono en la bandeja del sistema.

Desde ese icono se puede abrir la página local, mostrar u ocultar la consola, activar o desactivar «Iniciar con Windows» y cerrar el agente. También existen estas opciones explícitas:

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

- No se permite crear, leer, borrar, copiar, mover, renombrar ni escribir archivos o carpetas.
- Se bloquean registro, discos, particiones, permisos, usuarios, servicios, tareas, red, firewall, Defender y arranque.
- Se bloquean intérpretes anidados, ejecución dinámica, reflexión peligrosa, descargas y clientes de red capaces de transferir contenido.
- `Start-Process` requiere un destino literal sin rutas ni argumentos arbitrarios.
- Los programas nativos sólo reciben argumentos literales limitados y sin rutas.
- COM queda limitado al mecanismo de interfaz `WScript.Shell`, y `SendKeys` no admite texto libre.
- La automatización de aplicaciones sólo admite las primitivas `ControlPCIA.exe ui` validadas; comprueba tanto el selector pedido como el nombre real del control antes de actuar.
- Cada proceso PowerShell tiene 20 segundos de límite, salida acotada y terminación del árbol completo si vence el tiempo.

El acceso móvil añade código de emparejado, token aleatorio de sesión, caducidad, límite de intentos, restricción a direcciones privadas, cabeceras de seguridad y una sola orden simultánea. Ollama sólo escucha para ControlPCIA en `127.0.0.1`.

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
