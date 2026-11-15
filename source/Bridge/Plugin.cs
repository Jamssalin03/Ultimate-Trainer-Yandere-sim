using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using YandereUltimate.Comun;

namespace YandereUltimate
{
    // Esta clase no declara campos ni firmas con tipos de Assembly-CSharp: si una build no verificada
    // cambia algo, el complemento igual carga y desactiva sólo las funciones afectadas (ver Juego.cs).
    [BepInPlugin("local.yandere.ultimate", "Yandere Simulator Ultimate Trainer", Version)]
    [BepInProcess("YandereSimulator.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Version = "1.1.0";
        static readonly HashSet<string> Globales = new HashSet<string> { "permanentRep", "seduction", "enlightenment", "numbness", "atmosphere" };
        const int EsperaRespuestaMs = 5000, EsperaExtraMs = 25000, InactividadSegundos = 12;

        internal static Plugin Instance;

        readonly HashSet<string> activos = new HashSet<string>();
        readonly HashSet<int> ignorados = new HashSet<int>();
        readonly Dictionary<string, double> bloqueos = new Dictionary<string, double>();
        readonly Dictionary<string, double> originales = new Dictionary<string, double>();
        readonly ConcurrentQueue<Trabajo> trabajos = new ConcurrentQueue<Trabajo>();
        readonly Dictionary<int, NpcCongelado> congelados = new Dictionary<int, NpcCongelado>();
        readonly Dictionary<int, float> velocidadesNpc = new Dictionary<int, float>();
        readonly Dictionary<int, int> amistades = new Dictionary<int, int>();
        readonly Dictionary<int, int> reputacionesNpc = new Dictionary<int, int>();
        readonly Escena escena = new Escena();

        Compatibilidad compat;
        Respaldos respaldos;
        Component relojCongelado;
        float velocidadOriginalReloj;
        System.IO.Pipes.NamedPipeServerStream servidor;
        Thread hilo;
        volatile bool ejecutando;
        string token, canal, endpoint;
        Harmony harmony;
        float proximoRefresco;
        bool fondoOriginal;
        DateTime ultimoCliente = DateTime.UtcNow;

        void Awake()
        {
            Instance = this;
            fondoOriginal = Application.runInBackground;
            Application.runInBackground = true;

            compat = Compatibilidad.Evaluar(typeof(Plugin).Assembly, Logger.LogWarning);
            if (compat.Estado != Compatibilidad.Incompatible)
            {
                harmony = new Harmony("local.yandere.ultimate");
                Parches.Aplicar(harmony, compat, Logger.LogWarning);
            }

            var carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YandereUltimate");
            Directory.CreateDirectory(carpeta);
            respaldos = new Respaldos(Path.Combine(carpeta, "Cambios"), 50, TimeSpan.FromSeconds(1), Logger.LogWarning);
            endpoint = Path.Combine(carpeta, "endpoint.json");
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            token = Convert.ToBase64String(bytes);
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            canal = "YandereUltimate-" + pid + "-" + Guid.NewGuid().ToString("N");
            // Mono de Unity no implementa WindowsIdentity.User: las ACL se crean con Win32 (Native.cs).
            Native.WritePrivate(endpoint, JsonConvert.SerializeObject(new { pipe = canal, token, pid, protocol = Definiciones.Protocolo }));
            ejecutando = true;
            hilo = new Thread(Servir) { IsBackground = true, Name = "YandereUltimate.LocalPipe" };
            hilo.Start();
            SceneManager.sceneLoaded += EscenaCargada;

            Logger.LogInfo("Puente local " + Version + " listo. Compatibilidad: " + compat.Estado + " · build " + (compat.Build ?? "desconocida") + " · SHA256 " + compat.Hash);
            foreach (var f in compat.Desactivadas()) Logger.LogWarning("Función desactivada «" + f.Nombre + "»: " + f.Motivo);
        }

        void EscenaCargada(Scene s, LoadSceneMode modo)
        {
            // Sólo se restauran objetos que sobreviven; nunca se reutilizan referencias de la escena anterior.
            DescongelarTodo();
            if (relojCongelado != null) { Intentar("interruptor.freezeClock", () => Juego.FijarVelocidadReloj(relojCongelado, velocidadOriginalReloj)); }
            relojCongelado = null;
            // Los valores globales (PlayerGlobals, SchoolGlobals) sobreviven al cambio de escena y se siguen
            // pudiendo restaurar; los de objetos de la escena anterior se descartan.
            foreach (var id in originales.Keys.Where(k => !Globales.Contains(k)).ToArray()) originales.Remove(id);
            ignorados.Clear(); velocidadesNpc.Clear();
            Application.runInBackground = true;
            proximoRefresco = 0;
            Refrescar();
        }

        // ---------- Canal local (hilo propio) ----------

        void Servir()
        {
            while (ejecutando)
            {
                try
                {
                    using (servidor = Native.CreatePipe(canal))
                    {
                        servidor.WaitForConnection();
                        using (var lector = new StreamReader(servidor))
                        using (var escritor = new StreamWriter(servidor) { AutoFlush = true })
                        {
                            while (ejecutando && servidor.IsConnected)
                            {
                                var linea = LeerLinea(lector);
                                if (linea == null) break;
                                escritor.WriteLine(Atender(linea, out bool cerrar));
                                if (cerrar) break;
                            }
                        }
                    }
                }
                catch (IOException) { /* El cliente puede cerrar justo después de su respuesta. */ }
                catch (Exception e) { if (ejecutando) { Logger.LogWarning("Comunicación: " + e.Message); Thread.Sleep(250); } }
            }
        }

        static string LeerLinea(StreamReader lector)
        {
            var texto = new System.Text.StringBuilder();
            int c;
            while ((c = lector.Read()) >= 0 && c != '\n')
            {
                if (texto.Length >= Protocolo.LongitudMaxima) throw new IOException("Solicitud demasiado larga");
                if (c != '\r') texto.Append((char)c);
            }
            return c < 0 && texto.Length == 0 ? null : texto.ToString();
        }

        string Atender(string linea, out bool cerrar)
        {
            cerrar = false;
            Solicitud solicitud;
            try
            {
                var json = Protocolo.Leer(linea);
                var recibido = json["token"];
                if (recibido == null || recibido.Type != Newtonsoft.Json.Linq.JTokenType.String || !Protocolo.TokenIgual((string)recibido, token))
                {
                    cerrar = true;
                    return Protocolo.Error("Autenticación rechazada");
                }
                solicitud = Protocolo.Validar(json);
            }
            catch (SolicitudInvalidaException e) { return Protocolo.Error(e.Message); }
            catch (JsonException) { return Protocolo.Error("Solicitud inválida"); }

            var trabajo = new Trabajo(solicitud);
            trabajos.Enqueue(trabajo);
            return trabajo.Esperar(EsperaRespuestaMs, EsperaExtraMs);
        }

        // ---------- Hilo principal ----------

        void Update()
        {
            if (compat.Estado != Compatibilidad.Incompatible && Time.realtimeSinceStartup >= proximoRefresco) { Refrescar(); proximoRefresco = Time.realtimeSinceStartup + 2f; }
            ProcesarTrabajos();
            if (compat.Estado == Compatibilidad.Incompatible) return;

            // Un cliente cerrado o colgado no puede dejar NPC congelados indefinidamente.
            if ((DateTime.UtcNow - ultimoCliente).TotalSeconds > InactividadSegundos && HayCambios()) { Logger.LogInfo("Sin contacto del entrenador: se restaura la sesión."); Restaurar(); }

            foreach (var par in bloqueos.ToArray())
            {
                try
                {
                    var actual = Juego.Valores[par.Key].Leer(escena);
                    if (actual == null) { Logger.LogWarning("Se quita el bloqueo de " + par.Key + ": no está disponible en esta escena o modo."); bloqueos.Remove(par.Key); continue; }
                    if (actual.Value < par.Value) Escribir(par.Key, par.Value);
                }
                catch (Exception e) { Logger.LogWarning("Se quita el bloqueo de " + par.Key + ": " + e.Message); bloqueos.Remove(par.Key); }
            }
            if (activos.Contains("sanity")) Intentar("interruptor.sanity", () => Juego.MantenerCordura(escena));
            if (activos.Contains("noPenalty")) Intentar("interruptor.noPenalty", () => Juego.SinPenalizacion(escena));
            ActualizarReloj();
            ActualizarCongelados();
        }

        void LateUpdate()
        {
            foreach (var f in congelados.Values) if (f.Npc != null) f.Npc.transform.position = f.Posicion;
        }

        /// <summary>Ejecuta un interruptor de cada frame; si falla, lo desactiva y lo registra una vez.</summary>
        void Intentar(string funcion, Action accion)
        {
            try { accion(); }
            catch (Exception e)
            {
                var id = funcion.Substring(funcion.IndexOf('.') + 1);
                Logger.LogWarning("Se desactiva " + funcion + ": " + e.Message);
                activos.Remove(id);
                compat.Desactivar(funcion, "Falló durante la partida: " + e.Message);
            }
        }

        bool HayCambios() => activos.Count > 0 || bloqueos.Count > 0 || originales.Count > 0 || amistades.Count > 0 || reputacionesNpc.Count > 0 || velocidadesNpc.Count > 0 || ignorados.Count > 0;

        void ProcesarTrabajos()
        {
            for (int i = 0; i < 8 && trabajos.TryDequeue(out var trabajo); i++)
            {
                if (!trabajo.Reclamar()) continue; // venció: el cliente ya recibió la cancelación
                string respuesta;
                try
                {
                    ultimoCliente = DateTime.UtcNow;
                    respuesta = Protocolo.Correcto(Ejecutar(trabajo.Solicitud));
                }
                catch (Exception e) { Logger.LogWarning(e.Message); respuesta = Protocolo.Error(e.Message); }
                trabajo.Completar(respuesta);
            }
        }

        object Ejecutar(Solicitud s)
        {
            if (s.Op == Definiciones.OpSnapshot) return Instantanea();
            if (compat.Estado == Compatibilidad.Incompatible) throw new InvalidOperationException("Versión incompatible: no se encontró ninguna función disponible en esta build.");
            if (s.Op == Definiciones.OpRestore) { Restaurar(); return Instantanea(); }

            string funcion = s.Op == Definiciones.OpToggle ? "interruptor." + s.Id : s.Op == Definiciones.OpNpc ? "npc." + s.Id : "valor." + s.Id;
            bool desactivando = s.Op == Definiciones.OpRestoreValue || (s.Op == Definiciones.OpToggle && !s.Activado) || (s.Op == Definiciones.OpNpc && s.Id == Definiciones.NpcIgnorar && !s.Activado);
            if (!desactivando && !compat.Disponible(funcion)) throw new InvalidOperationException("Función desactivada en esta build: " + compat.Motivo(funcion));

            // Respaldo del estado previo: como máximo uno por segundo y escrito fuera del hilo principal.
            respaldos.Encolar(DateTime.UtcNow, s.Op, s.Id, Instantanea);

            switch (s.Op)
            {
                case Definiciones.OpSet:
                    if (!originales.ContainsKey(s.Id)) originales[s.Id] = Leer(s.Id);
                    Escribir(s.Id, s.Valor.Value);
                    if (s.Bloqueado) bloqueos[s.Id] = s.Valor.Value; else bloqueos.Remove(s.Id);
                    break;
                case Definiciones.OpRestoreValue:
                    bloqueos.Remove(s.Id);
                    if (originales.TryGetValue(s.Id, out var original)) { Escribir(s.Id, original); originales.Remove(s.Id); }
                    break;
                case Definiciones.OpToggle:
                    if (s.Activado) activos.Add(s.Id); else activos.Remove(s.Id);
                    break;
                case Definiciones.OpNpc:
                    AccionNpc(s);
                    break;
            }
            return Instantanea();
        }

        void AccionNpc(Solicitud s)
        {
            var npc = escena.Estudiantes.FirstOrDefault(n => n != null && Juego.IdNpc(n) == s.Npc);
            if (npc == null) throw new InvalidOperationException("El personaje ya no está cargado.");
            switch (s.Id)
            {
                case Definiciones.NpcIgnorar:
                    if (s.Activado) ignorados.Add(s.Npc); else ignorados.Remove(s.Npc);
                    break;
                case Definiciones.NpcAmistad:
                    if (!amistades.ContainsKey(s.Npc)) amistades[s.Npc] = Juego.Amistad(s.Npc);
                    Juego.FijarAmistad(s.Npc, (int)Math.Round(s.Valor.Value));
                    break;
                case Definiciones.NpcReputacion:
                    if (!reputacionesNpc.ContainsKey(s.Npc)) reputacionesNpc[s.Npc] = Juego.ReputacionNpc(s.Npc);
                    Juego.FijarReputacionNpc(s.Npc, (int)Math.Round(s.Valor.Value));
                    break;
                case Definiciones.NpcVelocidad:
                    if (!velocidadesNpc.ContainsKey(s.Npc)) velocidadesNpc[s.Npc] = Juego.VelocidadNpc(npc);
                    Juego.FijarVelocidadNpc(npc, (float)s.Valor.Value);
                    break;
                case Definiciones.NpcADestino:
                    Juego.IrADestino(npc);
                    break;
                case Definiciones.NpcProtagonista:
                    Juego.ProtagonistaAlNpc(escena, npc);
                    break;
            }
        }

        void Refrescar() => Juego.Refrescar(escena, compat.Disponible);

        double Leer(string id)
        {
            if (!compat.Disponible("valor." + id)) throw new InvalidOperationException("Función desactivada en esta build: " + compat.Motivo("valor." + id));
            return Juego.Valores[id].Leer(escena) ?? throw new InvalidOperationException("No disponible en esta escena o modo.");
        }

        // Los valores del cliente ya llegan validados por Protocolo.Validar; aquí también se escriben
        // los originales al restaurar, que pueden estar fuera de los rangos editables.
        void Escribir(string id, double valor)
        {
            Leer(id); // comprobación de disponibilidad antes de modificar
            Juego.Valores[id].Escribir(escena, valor);
        }

        object Instantanea()
        {
            var valores = new Dictionary<string, object>();
            foreach (var def in Definiciones.Valores)
            {
                object v = null;
                if (compat.Disponible("valor." + def.Id)) try { v = Juego.Valores[def.Id].Leer(escena); } catch { v = null; }
                valores[def.Id] = v;
            }
            string modo = "Desconocido"; int perfil = 0;
            if (compat.Disponible("modo")) try { modo = Juego.Modo(); perfil = Juego.Perfil(); } catch { }
            bool relaciones = compat.Disponible("npc.relaciones");
            var npcs = new List<object>();
            if (compat.Disponible("npc.lista"))
                foreach (var n in escena.Estudiantes)
                {
                    if (n == null) continue;
                    try
                    {
                        int id = Juego.IdNpc(n);
                        npcs.Add(Juego.InfoNpc(n, congelados.ContainsKey(n.GetInstanceID()), ignorados.Contains(id), relaciones));
                    }
                    catch { /* un NPC destruido durante la lectura se omite */ }
                }
            return new
            {
                protocol = Definiciones.Protocolo, plugin = Version,
                compatibility = compat.Estado, compatible = compat.Estado != Compatibilidad.Incompatible, verified = compat.Verificada,
                hash = compat.Hash, build = compat.Build ?? "desconocida", buildDate = compat.Fecha,
                unity = Application.unityVersion, scene = SceneManager.GetActiveScene().name,
                mode = modo, profile = perfil, values = valores,
                toggles = activos.ToArray(), locks = bloqueos.Keys.ToArray(),
                disabled = compat.Desactivadas().Select(f => new { id = f.Id, name = f.Nombre, reason = f.Motivo }).ToArray(),
                npcs = npcs.ToArray()
            };
        }

        // ---------- Reloj y congelación ----------

        void ActualizarReloj()
        {
            if (activos.Contains("freezeClock") && escena.Reloj != null)
            {
                Intentar("interruptor.freezeClock", () =>
                {
                    if (relojCongelado != escena.Reloj)
                    {
                        if (relojCongelado != null) Juego.FijarVelocidadReloj(relojCongelado, velocidadOriginalReloj);
                        relojCongelado = escena.Reloj; velocidadOriginalReloj = Juego.VelocidadReloj(relojCongelado);
                    }
                    Juego.FijarVelocidadReloj(relojCongelado, 0);
                });
            }
            else if (relojCongelado != null)
            {
                var reloj = relojCongelado; relojCongelado = null;
                Intentar("interruptor.freezeClock", () => Juego.FijarVelocidadReloj(reloj, velocidadOriginalReloj));
            }
        }

        bool DebeCongelar(Component npc)
        {
            if (activos.Contains("freezeAll")) return true;
            bool profesor = Juego.EsProfesor(npc), consejo = Juego.EsConsejo(npc);
            return (profesor && activos.Contains("freezeTeachers")) || (consejo && activos.Contains("freezeCouncil")) || (!profesor && !consejo && activos.Contains("freezeStudents"));
        }

        void ActualizarCongelados()
        {
            bool alguno = activos.Contains("freezeAll") || activos.Contains("freezeStudents") || activos.Contains("freezeTeachers") || activos.Contains("freezeCouncil");
            if (!alguno && congelados.Count == 0) return;
            try
            {
                foreach (var npc in escena.Estudiantes)
                {
                    if (npc == null) continue;
                    int clave = npc.GetInstanceID();
                    if (DebeCongelar(npc))
                    {
                        if (!congelados.TryGetValue(clave, out var f)) congelados[clave] = f = Juego.Congelar(npc);
                        Juego.MantenerCongelado(f);
                    }
                    else if (congelados.TryGetValue(clave, out var anterior)) { Juego.Descongelar(anterior); congelados.Remove(clave); }
                }
                foreach (var k in congelados.Where(x => x.Value.Npc == null).Select(x => x.Key).ToArray()) congelados.Remove(k);
            }
            catch (Exception e)
            {
                Logger.LogWarning("Se desactiva la congelación de NPC: " + e.Message);
                foreach (var id in new[] { "freezeAll", "freezeStudents", "freezeTeachers", "freezeCouncil" }) activos.Remove(id);
                DescongelarTodo();
                compat.Desactivar("congelar", "Falló durante la partida: " + e.Message);
            }
        }

        void DescongelarTodo()
        {
            foreach (var f in congelados.Values)
                try { Juego.Descongelar(f); } catch (Exception e) { Logger.LogWarning("No se pudo descongelar un NPC: " + e.Message); }
            congelados.Clear();
        }

        // ---------- Restauración ----------

        void Restaurar()
        {
            activos.Clear(); bloqueos.Clear(); ignorados.Clear(); DescongelarTodo();
            if (relojCongelado != null) { var reloj = relojCongelado; relojCongelado = null; try { Juego.FijarVelocidadReloj(reloj, velocidadOriginalReloj); } catch (Exception e) { Logger.LogWarning("No se pudo restaurar el reloj: " + e.Message); } }
            foreach (var n in escena.Estudiantes)
            {
                if (n == null) continue;
                try { if (velocidadesNpc.TryGetValue(Juego.IdNpc(n), out var v)) Juego.FijarVelocidadNpc(n, v); }
                catch (Exception e) { Logger.LogWarning("No se pudo restaurar la velocidad de un NPC: " + e.Message); }
            }
            velocidadesNpc.Clear();
            foreach (var r in amistades) try { Juego.FijarAmistad(r.Key, r.Value); } catch (Exception e) { Logger.LogWarning("No se pudo restaurar la amistad de " + r.Key + ": " + e.Message); }
            foreach (var r in reputacionesNpc) try { Juego.FijarReputacionNpc(r.Key, r.Value); } catch (Exception e) { Logger.LogWarning("No se pudo restaurar la reputación de " + r.Key + ": " + e.Message); }
            amistades.Clear(); reputacionesNpc.Clear();
            foreach (var par in originales.ToArray())
                try { Escribir(par.Key, par.Value); } catch (Exception e) { Logger.LogWarning("No se pudo restaurar " + par.Key + ": " + e.Message); }
            originales.Clear();
        }

        void OnDestroy()
        {
            ejecutando = false;
            SceneManager.sceneLoaded -= EscenaCargada;
            if (compat != null && compat.Estado != Compatibilidad.Incompatible) Restaurar();
            harmony?.UnpatchSelf();
            respaldos?.Dispose();
            // Una lectura síncrona del canal puede bloquear Dispose en el hilo principal de Unity:
            // se libera desde el pool de hilos, sin esperar al hilo del canal.
            var cerrar = servidor;
            ThreadPool.QueueUserWorkItem(_ => { try { cerrar?.Dispose(); } catch { } });
            try { if (File.Exists(endpoint) && File.ReadAllText(endpoint).Contains(canal)) File.Delete(endpoint); } catch { }
            Instance = null;
            Application.runInBackground = fondoOriginal;
        }

        // ---------- Consultas usadas por los parches (sólo tipos primitivos en las firmas) ----------

        internal bool Activo(string id) => activos.Contains(id);
        internal bool EstaBloqueado(string id) => bloqueos.ContainsKey(id);
        internal bool Ignorando(int studentId, bool profesor, bool consejo) =>
            activos.Contains("stupid") || ignorados.Contains(studentId) || (profesor && activos.Contains("teachers")) || (consejo && activos.Contains("council")) || (studentId == 1 && activos.Contains("senpai"));
    }
}
