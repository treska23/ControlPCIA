# ControlPCIA — estado y tareas

Última actualización: 19 de julio de 2026

## Objetivo vigente

Controlar Windows mediante órdenes habladas o escritas desde el móvil. La IA
local investiga y propone comandos; el programa los valida, ejecuta y devuelve
la evidencia real.

```text
móvil
→ voz o texto
→ Ollama / qwen3.5:9b
→ conversación + aplicaciones reales + comandos aprendidos
→ consulta o comando PowerShell mediante herramientas estructuradas
→ validador local
→ proceso PowerShell
→ comprobación real, stdout, stderr y código de salida
→ siguiente paso o respuesta al móvil
```

No existe una función por frase ni un segundo revisor con IA. El modelo sólo
puede `proponer_consulta`, `proponer_comando` o `preguntar_usuario`; nunca
ejecuta. Si necesita investigar, ControlPCIA ejecuta la consulta de sólo lectura
y devuelve el resultado real al mismo modelo. Antes de una acción compuesta,
PowerShell confirma además que todos los nombres de comando existen, evitando
ejecuciones parciales causadas por comandos inventados. Tampoco se usan
capturas, OCR, reconocimiento gráfico, TextBox, ratón, teclado simulado ni UI
Automation.

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
`%LOCALAPPDATA%\ControlPCIA\recetas-v2.json`:

- intención normalizada;
- comandos comprobados;
- conteo y fechas de éxito.

No guarda stdout, stderr ni contenido leído. Antes de investigar, ControlPCIA
busca por similitud y entrega a Llama únicamente los comandos aprendidos
relacionados. Las recetas antiguas que contengan UI Automation o que ya no
cumplan el validador se ignoran automáticamente. No existe una regla especial
para AppID, fabricantes, plantillas ni ninguna aplicación concreta.

Prueba real verificada:

- «crea un proyecto nuevo en Cubase llamado ControlPCIA IA 20260718 Q»:
  localizó `Plantilla General-01.cpr`, creó el archivo con el nombre exacto,
  lo abrió mediante su asociación de Windows y aprendió la receta.
- Petición posterior con nombre `R`: volvió a completarse.
- Petición posterior con nombre `S`: reutilizó directamente la plantilla
  aprendida, sin ejecutar otra búsqueda de plantillas.

El controlador no impone procedencias, rutas, nombres de ejecutable ni
estrategias por aplicación. Una ruta equivocada simplemente produce el error
real de PowerShell; ese error vuelve a Llama para que pruebe otra consulta o
comando. Una acción sin stdout continúa hasta obtener una comprobación y no se
declara completada sólo por recibir código de salida cero.

Existe `--traducir-sin-ejecutar`, que devuelve JSON con el primer comando, su
validación, duración y `ejecutado: false`. Se ha usado para probar formulaciones
distintas sin manipular el escritorio.

El servidor admite además `--solo-traducir`. Está pensado para comprobar desde
Android el micrófono, Cancelar y la conversación completa sin tocar el
escritorio. El endpoint `/api/orden` usa el mismo `ControlarAsync` con ejecución
desactivada, devuelve el comando como no ejecutado y nunca afirma que la tarea
se haya completado. La aplicación muestra **Modo de prueba seguro**.

## Aplicación móvil

La app .NET MAUI para Android es la experiencia principal. Incluye:

- descubrimiento UDP del PC y dirección manual como alternativa;
- emparejado por código de seis cifras y token en `SecureStorage`;
- un único control de voz para Wake-on-LAN y órdenes normales;
- control circular de voz tipo WhatsApp con listener táctil nativo, carril
  vertical, candado, mantener pulsado, soltar para enviar y arrastrar hacia
  arriba para dejarlo anclado hasta una pulsación posterior;
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

- 231 pruebas automatizadas correctas en Debug; la última comprobación Release
  se ejecuta antes de publicar este estado.
- Matriz positiva explícita para lectura, escritura, creación, copia,
  sobrescritura, descargas, instalación, registro, servicios, red, Defender,
  apagado/reinicio, WMI/CIM, intérpretes, .NET y COM.
- Pruebas negativas para eliminación, movimiento/corte, formato y
  evasiones anidadas.
- Creación real de proyectos Cubase comprobada sólo por consola.
- Reutilización real de la memoria comprobada sin repetir descubrimiento.
- Instancias de Cubase abiertas durante las pruebas cerradas al terminar.
- APK Android 1.5.5 (código 15), SHA-256
  `F7EEA61ED2E2E0EB4D89C3AA33296B13D0B9522806407CA9239BD5D1CEF96198`.
- Descarga del APK publicada por el propio agente en
  `http://192.168.1.15:5187/app-android.apk`: 22.926.540 bytes,
  tipo Android correcto y hash idéntico al APK firmado. El service worker no
  intercepta ni cachea esta ruta.
- Aplicación Android 1.3.1 probada previamente en Samsung SM-S928B con
  descubrimiento, emparejado, ambos modos de micrófono y Wake-on-LAN por voz.
- Aplicación Android 1.5.5 instalada encima en el mismo Samsung mediante ADB
  `install -r`: conserva sus datos. Se comprobó físicamente que el círculo
  recorre el carril completo, permanece junto al candado tras una pausa y que
  Cancelar muestra «Escucha cancelada», no envía la orden y mantiene vivo el
  mismo proceso Android. El cierre anterior procedía de callbacks tardíos del
  reconocedor nativo y quedó drenado antes de destruirlo.
- El agente corregido está publicado e instalado en
  `%LOCALAPPDATA%\ControlPCIA\App`; el ejecutable, la DLL y el APK coinciden
  byte por byte con la publicación. SHA-256 de la DLL:
  `6B0344E477F58A5CDFB12E0819074003657F5EDB3FCAA1DB2B0468CCA102328F`.
  Conserva 16 recetas y el inicio oculto registrado.
- El agente instalado se comprobó primero en el puerto 5188 con
  `--solo-traducir`: `/api/estado` devolvió `modoPrueba: true` y una petición
  devolvió un comando validado con `ejecutado: false`.
- El agente residente definitivo está activo de forma oculta en el puerto
  5187, con Ollama disponible, `modoPrueba: false`, un destino Wake-on-LAN y
  la entrada de inicio de Windows apuntando al ejecutable instalado. No se
  enviaron órdenes de prueba al modo normal.

## Tareas pendientes

- [x] Corregir e instalar APK 1.5.5: carril completo y Cancelar sin cierre.
- [x] Sustituir texto libre del modelo por propuestas estructuradas.
- [x] Añadir inventario real de aplicaciones, memoria v2 y diagnóstico sin ejecución.
- [x] Añadir control genérico de ventanas superiores por consola, sin interfaz gráfica.
- [x] Comprobar con PowerShell los nombres de comando antes de ejecutar una multitarea.
- [ ] Exigir evidencia de todas las acciones de una petición multitarea; la
      traducción «abre calculadora y bloc de notas» ya propone ambos comandos,
      pero su primera verificación sólo comprueba la calculadora.
- [ ] Terminar la matriz de traducciones reales y reducir la latencia de
      `qwen3.5:9b`, incluido el precalentamiento al iniciar Windows.
- [ ] Verificar de extremo a extremo móvil → agente → PowerShell → móvil en modo
      normal, sin pruebas que manipulen gráficamente el escritorio.
- [ ] Publicar e instalar la nueva compilación del agente de Windows; la APK
      1.5.5 sí está instalada, pero el agente residente aún ejecuta la versión anterior.
- [ ] Retirar el modelo antiguo `qwen3:8b` cuando quede confirmada definitivamente
      la configuración `qwen3.5:9b`.
- [ ] Verificar desde el móvil una conversación con error real y continuación.
- [ ] Probar Wake-on-LAN con el PC realmente apagado.
- [ ] Crear un instalador firmado para Windows.
- [ ] Valorar HTTPS local o un túnel autenticado antes de uso fuera de la LAN.

## Regla de continuidad

Para continuar en otra tarea:

> Lee `README.md` y `ESTADO_PROYECTO.md` del repositorio
> `treska23/ControlPCIA` y sigue desde la primera tarea sin marcar.
