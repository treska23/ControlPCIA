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
→ conversación + comandos aprendidos relacionados
→ un comando PowerShell propuesto por Llama
→ validador local
→ proceso PowerShell
→ stdout, stderr y código de salida
→ siguiente paso o respuesta al móvil
```

No existe un planificador, un catálogo de acciones, una función por frase ni un
segundo revisor con IA. Sólo existe una llamada a Llama con la instrucción de
traducir la petición a PowerShell. Si necesita investigar, el programa ejecuta
la consulta validada y devuelve el resultado real al mismo modelo. Tampoco se
usan capturas, OCR, reconocimiento gráfico, TextBox, ratón, teclado simulado ni
UI Automation.

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
- gesto de voz tipo WhatsApp: mantener pulsado, soltar para enviar y arrastrar
  hacia arriba para dejar el micrófono anclado hasta una pulsación posterior;
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

- 199 pruebas automatizadas correctas en Release.
- Matriz positiva explícita para lectura, escritura, creación, copia,
  sobrescritura, descargas, instalación, registro, servicios, red, Defender,
  apagado/reinicio, WMI/CIM, intérpretes, .NET y COM.
- Pruebas negativas para eliminación, movimiento/corte, formato y
  evasiones anidadas.
- Creación real de proyectos Cubase comprobada sólo por consola.
- Reutilización real de la memoria comprobada sin repetir descubrimiento.
- Instancias de Cubase abiertas durante las pruebas cerradas al terminar.
- APK Android 1.5.0 (código 10) publicado, SHA-256
  `5C6C1664E976616FCB42954BAEB611569B974EFF6670D6342C8C7748074C4253`
  y firma v1, v2 y v3 verificada.
- Descarga del APK publicada por el propio agente en
  `http://192.168.1.15:5187/app-android.apk`: 22.880.376 bytes,
  tipo Android correcto y hash idéntico al APK firmado. El service worker no
  intercepta ni cachea esta ruta.
- Aplicación Android 1.3.1 probada previamente en Samsung SM-S928B con
  descubrimiento, emparejado, ambos modos de micrófono y Wake-on-LAN por voz.
- Aplicación Android 1.5.0 instalada encima en el mismo Samsung mediante ADB
  `install -r`: conserva la fecha de primera instalación y los datos, declara
  código 10; queda pendiente comprobar manualmente el gesto de voz y su
  conversación en el modo seguro sin ejecución.
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

- [x] Ejecutar pruebas y compilación Release completas tras estos cambios.
- [x] Añadir traducción diagnóstica sin ejecución y probar variaciones naturales.
- [x] Añadir un modo móvil de prueba que no pueda invocar el ejecutor.
- [x] Impedir que una orden de ventana cierre Edge o abra propiedades del sistema.
- [x] Publicar e instalar los binarios de esta corrección.
- [x] Reactivar el agente residente sólo después de la revisión final.
- [x] Instalar APK 1.5.0 en el móvil conservando datos.
- [ ] Verificar manualmente en Android el gesto de mantener, soltar y anclar,
      los estados de escucha/proceso y que Cancelar no envía ni cierra la
      aplicación.
- [ ] Verificar desde el móvil una conversación con error real y continuación.
- [ ] Probar Wake-on-LAN con el PC realmente apagado.
- [ ] Crear un instalador firmado para Windows.
- [ ] Valorar HTTPS local o un túnel autenticado antes de uso fuera de la LAN.

## Regla de continuidad

Para continuar en otra tarea:

> Lee `README.md` y `ESTADO_PROYECTO.md` del repositorio
> `treska23/ControlPCIA` y sigue desde la primera tarea sin marcar.
