# Registro de cambios

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Versionado semántico.

## [1.1.0] - 2026-11-15

### Compatibilidad
- Compatible con la **build 532** (31/10/2026, Unity 6000.2.8f1, Mono x64,
  SHA-256 `E06B2209AA75DDA4EC6EE15E927566FD02F91B1BBC01A34908082D559CEB4F2C`).
  Se conserva la build 530.
- `compatibilidad.json` pasa de una huella única a una lista `builds_conocidas`
  (build, fecha, Unity, SHA-256). El complemento y la aplicación la incrustan al compilar.
- Nuevo **modo «build no verificada»**: si la huella no coincide, el complemento comprueba
  con AccessTools cada tipo, campo y método que necesita cada función y activa sólo las
  disponibles. El menú muestra cuáles quedaron desactivadas y por qué.
- Nuevo **inspector** (`source/Inspector`, Mono.Cecil): lista cada referencia del complemento
  al juego, la resuelve contra una build y comprueba que esté declarada en el catálogo.

### Seguridad y errores
- `Compilar.ps1` y `Directory.Build.props` sin rutas fijas de un usuario: parámetro `-Juego`
  o detección automática.
- Respaldos de `Cambios`: máximo 50 archivos, como mucho uno por segundo, escritos en un hilo
  propio (sin disco en el hilo principal del juego).
- Vencimiento de solicitudes con estado atómico (`Interlocked`): una operación vencida nunca se
  ejecuta y una ya iniciada nunca se informa como cancelada.
- Token del canal comparado en tiempo constante.
- Lista blanca de operaciones, identificadores y campos, con tipos estrictos y rangos por
  estadística; JSON con profundidad máxima y sin claves duplicadas.
- Bloqueos e interruptores que fallan se registran con `Logger.LogWarning` antes de quitarlos.
- Instalador: valida la ruta del juego, pregunta antes de usar un BepInEx existente (con otros
  mods) sin sobrescribir nada, rechaza otros cargadores, revierte si falla y desinstala sólo sus
  archivos y las carpetas que creó. Respalda partidas (`SaveData`, `SaveFiles`) y PlayerPrefs.
- Se quitó código redundante (`runInBackground` en cada frame, reflexión por nombre en materias,
  `PatchAll`, usings sin uso).
- La restauración conserva amistad/reputación de NPC y valores globales al cambiar de escena.

### Menú
- Pestañas: Estadísticas, Recursos, NPC, Comportamiento, Tiempo, Perfiles, Ajustes y Acerca de.
- Estado visible: Desconectado / Conectando… / Conectado (build verificada o no) / Incompatible.
- Buscador de NPC, deslizadores junto a los campos numéricos, tema oscuro y claro, atajos de
  teclado y botón «Restaurar todo» siempre visible.
- «Acerca de» con versión, repositorio y enlace de donaciones configurable (`donaciones` en
  `compatibilidad.json`).
- Ajustes: instalar, **actualizar** y desinstalar el complemento; respaldar partidas.

### Repositorio
- El `.rar` y la DLL compilada salen del repositorio (`.gitignore`); el paquete se publica como
  `.zip` en Releases con `SHA256SUMS.txt` (`Compilar.ps1 -Paquete`).
- Pruebas automáticas (`source/Pruebas`) y simulador del juego para probar la interfaz
  (`source/Simulador`).
- README: sección sobre falsos positivos del antivirus.

### Protocolo
- Versión de protocolo 2. La aplicación 1.1.0 informa «Incompatible» si el complemento instalado
  es 1.0.0: usá **Ajustes › Actualizar complemento** con el juego cerrado.

## [1.0.0] - 2026-10-17
- Primera versión: aplicación WPF y complemento BepInEx para la build 530.
