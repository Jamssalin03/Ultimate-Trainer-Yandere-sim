# Yandere Simulator Ultimate Trainer

Aplicación real para Windows x64, en C# / WPF y .NET 8, con un complemento
BepInEx / Harmony dentro del juego. La distribución incluye el ejecutable
autónomo, el complemento compilado, el cargador y el código fuente completo.

## Abrir y usar

1. Ejecutá `YandereSimulatorUltimateTrainer.exe` en esta carpeta. No requiere
   Visual Studio, compilar nada ni instalar .NET para usarlo.
2. El complemento ya se instaló en la copia del juego inspeccionada. En otra
   instalación, cerrá el juego, seleccioná su ejecutable con **Buscar instalación**
   y elegí **Instalar complemento**.
3. Abrí el juego. **Conectar** lee la versión y la escena; la conexión también
   se intenta automáticamente cada 900 ms. Entrá a una partida.
4. Usá el menú lateral. Cada estadística muestra el valor que devuelve el
   juego; editá el campo y pulsá **Aplicar**. **Máximo** sólo llena el campo,
   y requiere **Aplicar** para modificarlo.
5. Para recursos, marcá **No disminuir** y aplicá una cantidad suficiente.
   Los botones 100, 500 y 1000 de puntos de información sí aplican la cantidad.
6. Los interruptores cambian la función inmediatamente. Las opciones sin
   referencias válidas aparecen deshabilitadas, con el motivo.
7. **Restaurar** recupera el valor registrado antes de editar esa estadística
   en la escena actual. **Restaurar valores de sesión** desactiva los bloqueos,
   descongela NPC y restaura los valores registrados que sigan disponibles.

El juego debe abrirse una vez con el complemento para que exista el canal
local. El complemento fuerza la ejecución en segundo plano para que cambiar
de ventana no interrumpa los comandos. No utiliza memoria de otros procesos.

## Controles implementados

- Reputación actual y persistente, seducción, iluminación e insensibilidad.
- Puntos de estudio de la interfaz de clase y progreso / nivel individual
  de Biología, Química, Lenguaje, Educación Física y Psicología.
- Dinero y puntos de información, con edición, lectura, restauración y bloqueo.
  Los puntos de información se deshabilitan en 1980.
- Bloqueo de puntos durante `ServicesScript.Purchase`, manteniendo la compra
  y restaurando el contador; actualización de las etiquetas de servicios.
- «Personajes estúpidos» y controles separados de visión, audición, testigos,
  profesores, consejo, Senpai e inicio de alarmas / reportes.
- Congelación de todos, estudiantes comunes, profesores o consejo, con
  conservación de animaciones, estado de movimiento y posición para restaurar.
- NPC cargados con nombre, ID, alarma, persecución, posición y relaciones;
  ignorar a Ayano por NPC, amistad y reputación personalizadas, velocidad al
  caminar, teleport al destino del propio juego y Ayano cerca de un NPC.
- Estado mental, bloqueo al máximo, velocidades al caminar / correr, salud
  de combate y bloqueo de penalizaciones pendientes de reputación.
- Congelación y velocidad del reloj, avance de hora y atmósfera escolar.
- Perfiles locales, búsqueda, configuración automática, detección de cierre,
  conexión, reconexión, instalación y desinstalación desde la ventana.

Implementado no significa validado de extremo a extremo: consultá
`PRUEBAS.md` para distinguir los resultados efectivamente comprobados.

## Compatibilidad y límites

Sólo admite el ensamblado inspeccionado de la build 530 del 15/10/2026,
Unity 6000.2.8f1, Mono x64. Ver `compatibilidad.json`. La huella SHA-256 se
comprueba dentro del juego, y una huella diferente bloquea los cambios.
No usa direcciones de memoria fijas.

- La disponibilidad de los valores depende de las clases cargadas en cada
  escena; no se muestran cifras simuladas cuando no existen referencias.
- Los niveles de materias se limitan a 0–5 y el progreso a 0–100, conforme
  a los umbrales de `ClassScript`; ambos se sincronizan. Los puntos de estudio
  se aplican al contador de la sesión de clase, no a una clave de Registro.
- La configuración se aplica a la escena actual; cambiar de escena recupera
  referencias y descarta valores originales de objetos anteriores. Los
  interruptores se conservan; un bloqueo numérico puede desactivarse si su
  objetivo no está disponible en la nueva escena y deberá volver a aplicarse.
  Las selecciones individuales de NPC se descartan al cambiar
  de escena. No se garantiza restaurar consecuencias de eventos ya ejecutados.
- «Personajes estúpidos» debe activarse antes de ser descubierta. Suprimir
  los métodos identificados no revierte una derrota ya iniciada, una llamada
  policial anterior ni todos los eventos especiales de una rival.
- El botón de reportes bloquea las rutas identificadas en StudentScript;
  no representa una anulación global de todos los sistemas policiales.
- Teletransportar a un destino usa `StudentScript.TeleportToDestination`.
  Mover a Ayano comprueba disponibilidad y suelo con raycast; no es una
  garantía de navegación perfecta en toda geometría de la escuela.
- El tiempo sólo puede avanzar dentro de 7:00–17:54. No se permite retroceder
  ni cambiar el día en vivo; restaurar una hora anterior también se rechaza.
  Saltar horas puede omitir eventos. Restaurar la velocidad sí es reversible.
- El bloqueo de recursos mantiene el mínimo aplicado; una compra requiere
  suficiente saldo inicial para que el juego la admita.
- No se implementaron popularidad independiente, resistencia general,
  atravesar obstáculos ni anulación general de derrotas. Se informa en la app.
- El modo 1980 comparte las clases comprobadas, pero su funcionamiento jugable
  completo debe verificarse por separado. No se asume que tenga Info-chan.
- El cierre del entrenador solicita restauración. Si el cliente desaparece,
  el puente restaura la sesión tras 12 segundos sin solicitudes, siempre que
  el motor siga ejecutando callbacks. Si se cierra el juego abruptamente,
  no hay garantía de ejecutar código de restauración.

## Respaldos, datos y desinstalación

Los ajustes, perfiles, errores y respaldos están en
`%LOCALAPPDATA%\YandereUltimate`:

- `config.json`: campos, perfiles y ruta de instalación.
- `Respaldos`: PlayerPrefs exportado a `.reg`, ensamblado original y versión,
  creados antes de instalar. Importar el `.reg` restaura las preferencias de
  aquella fecha y puede sobrescribir avances posteriores: usalo deliberadamente
  y con el juego cerrado.
- `Cambios`: instantáneas de valores previos a cada comando de modificación
  en la versión final del complemento; no equivalen a una copia completa de
  todos los archivos de partida.
- `errores.log`: errores de la aplicación. El complemento también registra
  errores en `BepInEx\LogOutput.log`, dentro del juego.
- `endpoint.json`: credenciales efímeras de comunicación. Está restringido
  mediante permisos de Windows al usuario que ejecuta el juego. No compartirlo.

El instalador añade archivos de BepInEx y `winhttp.dll` / configuración de
Doorstop. Conserva un manifiesto con hashes de cada archivo añadido. No
reemplaza el ensamblado original y rechaza colisiones con otros archivos.

Para desinstalar, cerrá el juego y elegí **Desinstalar complemento**. Se
retiran únicamente archivos añadidos que todavía coincidan con sus hashes.
Si alguno cambió, se conserva y se informa el conflicto. Se mantienen los
respaldos, archivos generados de configuración y registros para recuperación.
La app no solicita administrador ni cambia antivirus o ajustes de seguridad.

## Código y ampliación

Abrí `source\YandereUltimate.sln` con Visual Studio y soporte de escritorio
.NET. Se necesita SDK .NET 8 o posterior y acceso NuGet para restaurar las
referencias de compilación de .NET Framework 4.7.2 del complemento.

`Desktop` implementa WPF, perfiles, instalación y el cliente de canal local.
`Bridge` implementa el servidor, cola al hilo principal, acceso tipado a las
clases del juego, validaciones y parches Harmony. `Native.cs` proporciona
permisos Windows y canal local sin APIs incompletas de Unity Mono.

Para compilar, ejecutá `Compilar.ps1`; admite `-Juego "ruta"`. También:

```powershell
dotnet build .\source\YandereUltimate.sln -c Release -p:GameDir="C:\ruta\al\juego"
dotnet publish .\source\Desktop\Desktop.csproj -c Release -o .\publicado
```

Los intermedios se guardan en `%TEMP%\ys-ultimate\build`. El cargador de
compilación se toma de `Loader`, junto al código fuente. Para nuevas builds,
primero hay que verificar clases y métodos, ajustar referencias y revisar
explícitamente la huella admitida; no basta con saltarse la comprobación.
