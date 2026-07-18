# ControlPCIA — estado y tareas

Última actualización: 19 de julio de 2026

## Objetivo vigente

Controlar Windows mediante órdenes habladas o escritas desde el móvil. La IA
local investiga y propone comandos; el programa los valida, ejecuta y devuelve
la evidencia real.

```text
móvil
→ voz o texto
→ Ollama / qwen3:8b
→ tareas + selección semántica de receta conocida
→ plantilla conocida o comando investigado
→ validador local + comprobación de correspondencia
→ proceso PowerShell
→ stdout, stderr y código de salida
→ siguiente paso o respuesta al móvil
```

No existe una función ni una comparación literal por cada frase. Llama traduce
distintas formas de pedir lo mismo y selecciona recetas de comandos
reutilizables. Tampoco se usan capturas, OCR, reconocimiento gráfico, TextBox,
ratón, teclado simulado ni UI Automation.

## Política definitiva

`ValidadorPowerShell` fue reescrito como una deny-list mínima. Sólo hay tres
categorías prohibidas:

1. Eliminar elementos o contenido.
2. Mover o cortar elementos.
3. Formatear, limpiar o reinicializar discos y unidades.

Todo lo demás invocable por consola está permitido: leer, buscar, crear, copiar,
sobrescribir, renombrar, abrir, guardar, descargar, instalar, configurar Windows, usar el
registro, servicios, red, módulos, ejecutables, intérpretes, APIs .NET y COM de
aplicaciones.

La política analiza el AST completo, incluidos bloques anidados. Los mecanismos
codificados o dinámicos se rechazan sólo cuando impedirían comprobar las tres
prohibiciones. También se rechazan `SendKeys`, `ControlPCIA.exe ui` y UI
Automation porque esa arquitectura fue retirada. `AppActivate` y las APIs
Win32 de estado de ventanas superiores están permitidas para activar, traer al
frente, maximizar, restaurar, minimizar, mover y redimensionar.

Se eliminaron los bloqueos heredados que exigían:

- carpetas personales o rutas literales;
- filtros y un máximo de 20 resultados para toda búsqueda;
- salida obligatoria con un formato concreto;
- que un ejecutable ya existiera antes de intentarlo;
- sólo metadatos y nunca contenido;
- no descargar, instalar, usar registro, servicios, seguridad o intérpretes;
- destinos siempre nuevos para `New-Item` y `Copy-Item`;
- una lista blanca de comandos, parámetros, COM o programas nativos.

La auditoría final eliminó además falsos positivos de nombres genéricos:
`Move-Window`, métodos `.Replace()` de texto y eliminaciones temporales de la
sesión como `Remove-Variable` o `Remove-Job` están permitidos. Los movimientos
reales de archivos, registro y contenido persistente continúan bloqueados.

## Aprendizaje local

`MemoriaRecetas` guarda en
`%LOCALAPPDATA%\ControlPCIA\recetas-v1.json`:

- intención normalizada;
- comandos comprobados;
- conteo y fechas de éxito.

No guarda stdout, stderr ni contenido leído. Antes de investigar, ControlPCIA
busca recetas relacionadas, extrae recursos reutilizables como AppID, fabricante,
ejecutable y plantilla, comprueba que sigan existiendo y se los entrega a Llama.
Si ya existe una plantilla aprendida, otra búsqueda `Get-ChildItem` se rechaza
como investigación repetida; sólo se adaptan los datos variables.

Prueba real verificada:

- «crea un proyecto nuevo en Cubase llamado ControlPCIA IA 20260718 Q»:
  localizó `Plantilla General-01.cpr`, creó el archivo con el nombre exacto,
  lo abrió mediante su asociación de Windows y aprendió la receta.
- Petición posterior con nombre `R`: volvió a completarse.
- Petición posterior con nombre `S`: reutilizó directamente la plantilla
  aprendida, sin ejecutar otra búsqueda de plantillas.

El programa conserva literalmente nombres pronunciados aunque el plan de Llama
omita accidentalmente un sufijo. Una copia con otro nombre se rechaza y se pide
al modelo que repita `Copy-Item` con el destino exacto. Una creación por
plantilla queda comprobada cuando el destino existe y se abre correctamente;
Llama ya no puede contradecir después ese resultado.

El planificador selecciona además conocimientos integrados por significado:
`ventanas.estado`, `aplicaciones.abrir`, `aplicaciones.inventario`,
`archivos.buscar` y `archivos.abrir`. Los cinco están conectados al camino corto
real. Una petición conocida usa traducir → adaptar receta → ejecutar → verificar.
Las consultas iniciales de procesos, aplicaciones y archivos son plantillas
fijas. Para activar, maximizar, minimizar o restaurar una ventana se selecciona
únicamente un `PROCESS_NAME` aparecido en stdout y ControlPCIA construye el
bloque Win32; Llama no redacta ese bloque. Una desconocida investiga por consola
y, si funciona, se convierte en memoria reutilizable.

La procedencia también se valida: un AppID sólo se abre si apareció en
`Get-StartApps` o en una receta comprobada, y un archivo sólo se abre desde un
`FULL_NAME` observado. El agente general pasa además por
`RevisorAlineacionComandoIA`: una propuesta que cierre, abra o modifique un
objetivo ajeno a las tareas pendientes no se ejecuta. Esto corrige traducciones
equivocadas sin convertir la correspondencia semántica en otra prohibición.

Existe `--traducir-sin-ejecutar`, que devuelve JSON con el plan, las recetas, el
primer comando, su validación, duración y `ejecutado: false`. Se ha usado para
probar formulaciones distintas sin manipular el escritorio.

## Aplicación móvil

La app .NET MAUI para Android es la experiencia principal. Incluye:

- descubrimiento UDP del PC y dirección manual como alternativa;
- emparejado por código de seis cifras y token en `SecureStorage`;
- un único control de voz para Wake-on-LAN y órdenes normales;
- modo tocar–hablar–tocar y modo mantener pulsado;
- envío automático al finalizar la voz;
- estados de color y texto: verde al escuchar, ámbar al transcribir y violeta
  mientras Llama decide, además de contador, texto parcial y respuesta háptica;
- Cancelar descarta la escucha sin enviar y absorbe errores tardíos del
  reconocedor nativo en vez de cerrar la aplicación;
- entrada de texto y conversación temporal con contexto;
- preguntas de aclaración cuando falta una decisión personal;
- errores reales de PowerShell visibles en el móvil.

Wake-on-LAN se resuelve localmente en el teléfono porque el PC apagado no puede
consultar a Ollama. No existe otro catálogo local de acciones. La PWA se mantiene
como respaldo y no cachea órdenes ni respuestas de la API.

## Agente residente

ControlPCIA configura el inicio por usuario y después arranca con
`--servidor --oculto`. Un mutex evita servidores duplicados. El icono de bandeja
permite ver el emparejado, abrir la web, mostrar la consola, cambiar el inicio
automático o salir.

## Verificaciones actuales

- 286 pruebas automatizadas correctas en Release.
- Matriz positiva explícita para lectura, escritura, creación, copia,
  sobrescritura, descargas, instalación, registro, servicios, red, Defender,
  apagado/reinicio, WMI/CIM, intérpretes, .NET y COM.
- Pruebas negativas para eliminación, movimiento/corte, formato y
  evasiones anidadas.
- Creación real de proyectos Cubase comprobada sólo por consola.
- Reutilización real de la memoria comprobada sin repetir descubrimiento.
- Instancias de Cubase abiertas durante las pruebas cerradas al terminar.
- APK Android 1.4.2 (código 8) publicado, SHA-256
  `0968785F200FE07B2D201ADED0F3EF98683DCE9F397FF9B8E714DAC33110961A`
  y firma v1, v2 y v3 verificada.
- Descarga del APK publicada por el propio agente en
  `http://192.168.1.15:5187/app-android.apk`: HTTP 200, 29.964.392 bytes,
  tipo Android correcto y hash idéntico al APK firmado. El service worker no
  intercepta ni cachea esta ruta.
- Aplicación Android 1.3.1 probada previamente en Samsung SM-S928B con
  descubrimiento, emparejado, ambos modos de micrófono y Wake-on-LAN por voz.
- Aplicación Android 1.4.2 instalada encima en el mismo Samsung mediante ADB
  `install -r`: conserva la fecha de primera instalación y los datos, declara
  código 8, alcanza `192.168.1.15:5187` desde Android y arranca sin excepciones
  fatales.
- El agente corregido está publicado e instalado en
  `%LOCALAPPDATA%\ControlPCIA\App`; el ejecutable, la DLL y el APK coinciden
  byte por byte con la publicación. SHA-256 de la DLL:
  `98B53CE562E52E3A1EFB75627294D3439056C8AC711CB2F27B02CEE8FD2E5A5D`.
  Conserva 16 recetas y el inicio oculto registrado. El proceso residente sigue
  detenido intencionadamente; ninguna prueba nueva ejecuta órdenes sobre
  aplicaciones reales.

## Tareas pendientes

- [x] Ejecutar pruebas y compilación Release completas tras estos cambios.
- [x] Añadir traducción diagnóstica sin ejecución y probar variaciones naturales.
- [x] Impedir que una orden de ventana cierre Edge o abra propiedades del sistema.
- [x] Publicar e instalar los binarios de esta corrección.
- [ ] Reactivar el agente residente sólo después de la revisión final.
- [x] Instalar APK 1.4.2 en el móvil conservando datos.
- [ ] Verificar manualmente en Android los estados de escucha/proceso y que
      Cancelar no envía ni cierra la aplicación.
- [ ] Verificar desde el móvil una conversación con error real y continuación.
- [ ] Probar Wake-on-LAN con el PC realmente apagado.
- [ ] Crear un instalador firmado para Windows.
- [ ] Valorar HTTPS local o un túnel autenticado antes de uso fuera de la LAN.

## Regla de continuidad

Para continuar en otra tarea:

> Lee `README.md` y `ESTADO_PROYECTO.md` del repositorio
> `treska23/ControlPCIA` y sigue desde la primera tarea sin marcar.
