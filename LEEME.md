# Yandere Simulator Ultimate Trainer 1.1.0

Aplicación para Windows x64, en C# / WPF y .NET 8, con un complemento
BepInEx / Harmony dentro del juego. El paquete de Release incluye el ejecutable
autónomo, el complemento compilado, el cargador y `SHA256SUMS.txt`; el código
fuente completo está en este repositorio.

## Abrir y usar

1. Ejecutá `YandereSimulatorUltimateTrainer.exe`. No requiere Visual Studio,
   compilar nada ni instalar .NET.
2. En **Ajustes**, comprobá la carpeta del juego (se detecta sola en las
   ubicaciones habituales del launcher; si no, **Buscar…**). Con el juego
   cerrado, pulsá **Instalar complemento** (o **Actualizar complemento** si
   venís de la 1.0.0). Antes de instalar se respaldan tus partidas.
3. Abrí el juego. La cabecera muestra siempre el estado:
   - **Desconectado**: pulsaste Desconectar; **Conectar** (F5) vuelve a buscar.
   - **Conectando…**: el juego está cerrado o el complemento aún no respondió
     (se reintenta cada 900 ms; el motivo aparece debajo).
   - **Conectado**: indica la build, si está verificada, el modo y la escena.
   - **Incompatible**: ninguna función es compatible o el complemento
     instalado es de otra versión; las modificaciones quedan bloqueadas.
4. Usá las pestañas. Cada estadística muestra el valor que devuelve el juego;
   editá el campo o el deslizador y pulsá **Aplicar** (o Enter). **Máximo**
   sólo llena el campo.
5. Para recursos, marcá **No disminuir** y aplicá una cantidad suficiente.
   Los botones 100, 500 y 1000 de puntos de información sí aplican la cantidad.
6. Los interruptores cambian la función inmediatamente. Las opciones sin
   referencias válidas aparecen deshabilitadas, con el motivo.
7. **Restaurar** recupera el valor registrado antes de editar esa estadística.
   **Restaurar todo** (siempre visible, Ctrl+R) desactiva bloqueos e
   interruptores, descongela NPC y restaura los valores registrados.

### Pestañas

| Pestaña | Contenido |
|---|---|
| Estadísticas | Reputación actual y permanente, puntos de estudio, materias (progreso y nivel), seducción, iluminación, insensibilidad, estado mental, salud y velocidades de la protagonista |
| Recursos | Dinero y puntos de información (con bloqueo) |
| NPC | Buscador, estado del personaje, ignorar a la protagonista, destino, teletransporte, amistad, reputación y velocidad; congelar por grupo |
| Comportamiento | Personajes estúpidos, visión, audición, testigos, profesores, consejo, Senpai, reportes, estado mental al máximo, sin penalizaciones de reputación |
| Tiempo | Congelar el reloj, hora, velocidad del reloj y atmósfera escolar |
| Perfiles | Guardar, cargar, aplicar y eliminar perfiles locales |
| Ajustes | Carpeta del juego, instalar/actualizar/desinstalar, respaldos, tema y compatibilidad |
| Acerca de | Versión, repositorio, donaciones y avisos |

### Atajos de teclado

| Tecla | Acción |
|---|---|
| Ctrl+1 … Ctrl+8 | Cambiar de pestaña |
| Ctrl+R | Restaurar todo |
| F5 / Ctrl+D | Conectar / desconectar |
| Ctrl+F | Buscar personaje |
| Ctrl+T | Tema oscuro / claro |
| Enter | Aplicar el valor del campo |

El complemento fuerza la ejecución en segundo plano (tras `YandereScript.Start`,
que la desactiva) para que cambiar de ventana no interrumpa los comandos.
No utiliza memoria de otros procesos.

## Compatibilidad y límites

`compatibilidad.json` lista las builds verificadas (530 del 15/10/2026 y 532
del 31/10/2026; Unity 6000.2.8f1, Mono x64). Si la huella SHA-256 de
`Assembly-CSharp.dll` no figura, el complemento entra en **modo build no
verificada**: comprueba con AccessTools cada referencia que declara
`source/Comun/Catalogo.cs` para cada función y activa sólo las disponibles.
Las desactivadas y su motivo se ven en **Ajustes › Compatibilidad** y en el
registro de BepInEx. Si una función falla durante la partida, se desactiva y
se registra con `LogWarning`. No usa direcciones de memoria fijas.

- La disponibilidad de los valores depende de las clases cargadas en cada
  escena; no se muestran cifras simuladas cuando no existen referencias.
- Los niveles de materias se limitan a 0–5 y el progreso a 0–100, conforme
  a los umbrales de `ClassScript`; ambos se sincronizan. Los puntos de estudio
  se aplican al contador de la sesión de clase, no a una clave de Registro.
- Al cambiar de escena se descartan las referencias y los valores originales de
  objetos de la escena anterior; los valores globales (reputación permanente,
  seducción, iluminación, insensibilidad, atmósfera) y la amistad / reputación
  de NPC se siguen pudiendo restaurar. Los interruptores se conservan; un
  bloqueo cuyo objetivo no exista en la nueva escena se quita (con aviso).
- «Personajes estúpidos» debe activarse antes de ser descubierta. Suprimir
  los métodos identificados no revierte una derrota ya iniciada, una llamada
  policial anterior ni todos los eventos especiales de una rival.
- El botón de reportes bloquea las rutas identificadas en StudentScript;
  no representa una anulación global de todos los sistemas policiales.
- Teletransportar a un destino usa `StudentScript.TeleportToDestination`.
  Mover a la protagonista comprueba disponibilidad y suelo con raycast; no es
  una garantía de navegación perfecta en toda la geometría de la escuela.
- El tiempo sólo puede avanzar dentro de 7:00–17:54. No se permite retroceder
  ni cambiar el día en vivo; restaurar una hora anterior también se rechaza.
- El bloqueo de recursos mantiene el mínimo aplicado; una compra requiere
  suficiente saldo inicial para que el juego la admita.
- No se implementaron popularidad independiente, resistencia general,
  atravesar obstáculos ni anulación general de derrotas.
- El modo 1980 comparte las clases comprobadas, pero su funcionamiento jugable
  completo debe verificarse por separado. Los puntos de información se
  deshabilitan en 1980.
- Al cerrar el entrenador o desconectar se pide restaurar todo. Si el cliente
  desaparece, el complemento restaura la sesión tras 12 segundos sin
  solicitudes, siempre que el motor siga ejecutando callbacks. Si el juego se
  cierra abruptamente, no hay garantía de ejecutar código de restauración.

## Seguridad del canal local

- `endpoint.json` contiene el nombre del canal y un token aleatorio de 256 bits;
  se crea con ACL restringida al usuario que ejecuta el juego. No lo compartas.
- El token se compara en tiempo constante. Cada solicitud se valida contra una
  lista blanca (`source/Comun/Definiciones.cs`): operación, identificador,
  campos permitidos, tipos estrictos y rangos por estadística.
- El canal rechaza clientes remotos y admite una sola instancia.
- Una solicitud que vence antes de que el juego la procese nunca se ejecuta; una
  que ya empezó nunca se informa como cancelada (estado atómico).

## Respaldos, datos y desinstalación

Los ajustes, perfiles, errores y respaldos están en
`%LOCALAPPDATA%\YandereUltimate`:

- `config.json`: campos, perfiles, tema y ruta de instalación.
- `Respaldos`: antes de instalar (y con **Ajustes › Respaldar partidas**) se
  copian `SaveData` y `SaveFiles` del juego, PlayerPrefs exportado a `.reg`
  (si existe) y el ensamblado original. Importar el `.reg` restaura las
  preferencias de aquella fecha y puede sobrescribir avances posteriores:
  usalo deliberadamente y con el juego cerrado.
- `Cambios`: instantáneas de valores previos a los comandos de modificación.
  Como máximo una por segundo y las 50 más recientes; se escriben en un hilo
  propio del complemento. No equivalen a una copia completa de las partidas.
- `errores.log`: errores de la aplicación. El complemento registra en
  `BepInEx\LogOutput.log`, dentro del juego.

El instalador añade BepInEx 5.4.23.5 y `winhttp.dll` / configuración de
Doorstop, o, si ya hay un **BepInEx 5** (con otros mods), pregunta y agrega
sólo el complemento sin sobrescribir nada. Rechaza otros cargadores. Si la
copia falla, se revierte. El manifiesto `YandereUltimate.install.json` guarda
el hash de cada archivo añadido y las carpetas creadas.

Para desinstalar, cerrá el juego y elegí **Desinstalar complemento**. Se
retiran únicamente los archivos añadidos que todavía coincidan con sus hashes
y las carpetas propias que quedaron vacías. Si algún archivo cambió, no se
borra nada y se informa el conflicto. Se conservan otros mods, los archivos
generados por BepInEx (registros, configuración) y los respaldos.
La app no solicita administrador ni cambia antivirus o ajustes de seguridad.

## Código y ampliación

Abrí `source\YandereUltimate.sln` con Visual Studio y soporte de escritorio
.NET, o usá el SDK .NET 8. Se necesita acceso a NuGet.

| Proyecto | Función |
|---|---|
| `Desktop` | WPF: interfaz, perfiles, instalación y cliente del canal local |
| `Bridge` | Complemento: servidor del canal, cola al hilo principal, acceso al juego (`Juego.cs`), parches (`Parches.cs`), compatibilidad (`Compatibilidad.cs`) y Win32 (`Native.cs`) |
| `Comun` | Lista blanca del protocolo, catálogo de funciones y sondeo de requisitos (compartido) |
| `Inspector` | Informe de referencias del complemento contra una build (Mono.Cecil) |
| `Pruebas` | Pruebas automáticas (xUnit); las que usan el juego leen `YS_JUEGO` y `YS_PUENTE` |
| `Simulador` | Proceso que imita al juego con el complemento, para probar la interfaz sin el juego. No se distribuye |

Para compilar, ejecutá `Compilar.ps1` (detecta el juego o admite
`-Juego "ruta"`; `-Paquete` genera el `.zip`). También:

```powershell
dotnet build .\source\YandereUltimate.sln -c Release -p:GameDir="C:\ruta\al\juego"
dotnet publish .\source\Desktop\Desktop.csproj -c Release -o .\publicado
```

### Agregar una build nueva

1. Actualizá el juego con el launcher.
2. Ejecutá `Compilar.ps1 -Juego "ruta"`. El inspector imprime el SHA-256 y cada
   referencia del complemento al juego; si alguna falta, la compilación o el
   informe lo indican.
3. Corregí `Juego.cs` / `Parches.cs` y los requisitos de `Comun/Catalogo.cs`.
4. Probá en el juego y agregá la build a `builds_conocidas` en
   `compatibilidad.json` (build, fecha, Unity, SHA-256). No basta con agregar
   la huella sin revisar.
