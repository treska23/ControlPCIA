# ControlPCIA — estado estable

Última actualización: 19 de julio de 2026

## Decisión de producto

La aplicación queda terminada, por ahora, únicamente con las funciones que ya
han demostrado un comportamiento estable. No se borra el trabajo experimental,
pero tampoco se ejecuta desde la APK ni desde el flujo principal.

Flujo activo:

```text
APK Android 1.5.5
→ red local y emparejado
→ agente residente de Windows
→ ControlBasico
→ un comando PowerShell conocido
→ stdout, stderr y código de salida
→ respuesta al móvil
```

Ollama, Llama y Qwen no participan en este flujo.

## Funciones activas

1. Encender el ordenador por voz mediante Wake-on-LAN desde la APK.
2. Abrir una aplicación instalada, una aplicación por petición.
3. Consultar qué aplicaciones tienen una ventana abierta.
4. Dictar o escribir la petición desde la APK.
5. Descubrir y emparejar el PC en la red local.
6. Mantener el agente oculto e iniciado con Windows.
7. Usar la PWA como respaldo y descargar la APK desde el agente.

## Semántica de apertura

ControlPCIA obtiene el AppID real mediante `Get-StartApps` y envía un único
`Start-Process`.

No busca después procesos ni ventanas. No afirma haber comprobado la apertura.
Sólo comunica:

- el error devuelto por PowerShell; o
- que Windows aceptó el comando.

## Funciones conservadas pero inactivas

El repositorio mantiene el traductor local, memoria, control de ventanas,
validación avanzada y sus pruebas. No se han borrado. Permanecen desconectados
del servidor y de `Program.cs` hasta que se decida retomarlos individualmente.

## Evidencias actuales

- **245/245 pruebas Release correctas**.
- APK congelada: versión **1.5.5**, código **15**.
- SHA-256 de la APK:
  `F7EEA61ED2E2E0EB4D89C3AA33296B13D0B9522806407CA9239BD5D1CEF96198`.
- Agente instalado en:
  `%LOCALAPPDATA%\ControlPCIA\App`.
- SHA-256 de la DLL instalada:
  `B6534D9666F2127E312A6D28BF6C14835DEF0361EF79786BC00EE3B141E4894A`.
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

## Alcance cerrado de esta versión

No quedan tareas obligatorias dentro del alcance estable descrito. Las
siguientes capacidades pertenecen a versiones futuras y deberán incorporarse
de una en una, sin modificar lo que ya funciona:

- lenguaje natural general con o sin IA;
- multitareas;
- control interno de aplicaciones;
- aprendizaje de comandos;
- conversaciones complejas;
- operaciones sobre archivos o configuración;
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
