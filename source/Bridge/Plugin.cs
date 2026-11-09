using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObj = UnityEngine.Object;

namespace YandereUltimate
{
    [BepInPlugin("local.yandere.ultimate", "Yandere Simulator Ultimate Trainer", "1.0.0")]
    [BepInProcess("YandereSimulator.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string ExpectedHash = "62926A9CC29156037C340E7BD177D88D760D03618FCDFF0C2CD66CCAB78CED09";
        internal static Plugin Instance;
        internal readonly HashSet<string> Toggles = new HashSet<string>();
        internal readonly HashSet<int> Ignored = new HashSet<int>();
        readonly Dictionary<string, double> Locks = new Dictionary<string, double>();
        readonly Dictionary<string, double> Originals = new Dictionary<string, double>();
        readonly ConcurrentQueue<Job> jobs = new ConcurrentQueue<Job>();
        readonly Dictionary<int, FrozenNpc> frozen = new Dictionary<int, FrozenNpc>();
        readonly Dictionary<int, float> npcSpeeds = new Dictionary<int, float>();
        readonly Dictionary<string, int> relations = new Dictionary<string, int>();
        ClockScript frozenClock;
        float originalClockSpeed;
        NamedPipeServerStream server;
        Thread worker;
        volatile bool running;
        string token, pipe, endpoint;
        Harmony harmony;
        YandereScript yan;
        ClassScript academic;
        ReputationScript reputation;
        ClockScript clock;
        StudentManagerScript manager;
        StudentScript[] students = new StudentScript[0];
        float nextRefresh;
        bool compatible;
        string hash;
        bool originalBackground;
        DateTime lastClient = DateTime.UtcNow;

        sealed class Job
        {
            public JObject Request;
            public ManualResetEventSlim Done = new ManualResetEventSlim();
            public string Response;
            public volatile bool Expired;
        }
        sealed class FrozenNpc
        {
            public StudentScript Npc;
            public bool Enabled, CanMove, CanSearch;
            public Vector3 Position;
            public readonly Dictionary<AnimationState, float> Speeds = new Dictionary<AnimationState, float>();
        }

        void Awake()
        {
            Instance = this;
            originalBackground = Application.runInBackground;
            Application.runInBackground = true;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(typeof(YandereScript).Assembly.Location))).Replace("-", "");
            compatible = hash == ExpectedHash;
            if (compatible)
            {
                harmony = new Harmony("local.yandere.ultimate");
                harmony.PatchAll(typeof(Plugin).Assembly);
            }
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YandereUltimate");
            Directory.CreateDirectory(folder);
            endpoint = Path.Combine(folder, "endpoint.json");
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            token = Convert.ToBase64String(bytes);
            pipe = "YandereUltimate-" + System.Diagnostics.Process.GetCurrentProcess().Id + "-" + Guid.NewGuid().ToString("N");
            // Unity's Mono does not implement WindowsIdentity.User. Native ACLs avoid that API.
            Native.WritePrivate(endpoint, JsonConvert.SerializeObject(new { pipe, token, pid = System.Diagnostics.Process.GetCurrentProcess().Id }));
            running = true;
            worker = new Thread(Serve) { IsBackground = true, Name = "YandereUltimate.LocalPipe" };
            worker.Start();
            SceneManager.sceneLoaded += SceneLoaded;
            Logger.LogInfo("Puente local listo. Compatibilidad: " + compatible + "; SHA256 " + hash);
        }

        void SceneLoaded(Scene s, LoadSceneMode mode)
        {
            // Restore only surviving objects; destroyed scene references must never be reused.
            UnfreezeAll();
            Originals.Clear();
            Ignored.Clear();
            npcSpeeds.Clear(); relations.Clear(); frozenClock = null;
            nextRefresh = 0;
            Refresh();
        }

        void Serve()
        {
            while (running)
            {
                try
                {
                    using (server = Native.CreatePipe(pipe))
                    {
                        server.WaitForConnection();
                        using (var reader = new StreamReader(server))
                        using (var writer = new StreamWriter(server) { AutoFlush = true })
                        {
                            while (running && server.IsConnected)
                            {
                                var chars = new List<char>();
                                int c;
                                while ((c = reader.Read()) >= 0 && c != '\n') { if (chars.Count >= 65536) throw new IOException("Solicitud demasiado larga"); chars.Add((char)c); }
                                if (c < 0) break;
                                try
                                {
                                    var request = JObject.Parse(new string(chars.ToArray()));
                                    if ((string)request["token"] != token) { writer.WriteLine("{\"ok\":false,\"error\":\"Autenticación rechazada\"}"); break; }
                                    var job = new Job { Request = request };
                                    jobs.Enqueue(job);
                                    if (job.Done.Wait(5000)) writer.WriteLine(job.Response);
                                    else { job.Expired = true; writer.WriteLine("{\"ok\":false,\"error\":\"El juego no responde; operación cancelada\"}"); }
                                }
                                catch (JsonException) { writer.WriteLine("{\"ok\":false,\"error\":\"Solicitud inválida\"}"); }
                            }
                        }
                    }
                }
                catch (IOException) { /* A client may close immediately after its one response. */ }
                catch (Exception e) { if (running) { Logger.LogWarning("Comunicación: " + e.Message); Thread.Sleep(250); } }
            }
        }

        void Update()
        {
            Application.runInBackground = true;
            if (!compatible) { Drain(); return; }
            if (Time.realtimeSinceStartup >= nextRefresh) { Refresh(); nextRefresh = Time.realtimeSinceStartup + 2f; }
            Drain();
            // A crashed or closed client cannot leave NPCs frozen indefinitely.
            if ((DateTime.UtcNow - lastClient).TotalSeconds > 12 && (Toggles.Count > 0 || Locks.Count > 0 || Originals.Count > 0 || relations.Count > 0 || npcSpeeds.Count > 0)) Restore();
            foreach (var pair in Locks.ToArray())
                try { var value = Read(pair.Key); if (value < pair.Value) Write(pair.Key, pair.Value); }
                catch { Locks.Remove(pair.Key); }
            if (yan != null && Toggles.Contains("sanity")) yan.Sanity = 100f;
            if (reputation != null && Toggles.Contains("noPenalty") && reputation.PendingRep < 0) { reputation.PendingRep = 0; reputation.UpdatePendingRepLabel(); }
            if(Toggles.Contains("freezeClock") && clock != null) { if(frozenClock != clock) { frozenClock=clock; originalClockSpeed=clock.TimeSpeed; } clock.TimeSpeed=0; }
            else if(frozenClock != null) { frozenClock.TimeSpeed=originalClockSpeed; frozenClock=null; }
            FreezeUpdate();
        }

        void Drain()
        {
            for (int i = 0; i < 8 && jobs.TryDequeue(out var job); i++)
            {
                if (job.Expired) { job.Done.Set(); continue; }
                try
                {
                    lastClient = DateTime.UtcNow;
                    var result = Execute(job.Request);
                    job.Response = JsonConvert.SerializeObject(new { ok = true, data = result });
                }
                catch (Exception e) { Logger.LogWarning(e.Message); job.Response = JsonConvert.SerializeObject(new { ok = false, error = e.Message }); }
                finally { job.Done.Set(); }
            }
        }

        object Execute(JObject request)
        {
            var op = (string)request["op"];
            if (op == "snapshot") return Snapshot();
            if (!compatible) throw new InvalidOperationException("Versión incompatible: el ensamblado no coincide con la build verificada.");
            if (op == "restore") { Restore(); return Snapshot(); }
            var id = (string)request["id"];
            var backupFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"YandereUltimate","Cambios");
            Directory.CreateDirectory(backupFolder);
            File.WriteAllText(Path.Combine(backupFolder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".json"),JsonConvert.SerializeObject(new { operation=op, id, before=Snapshot() }));
            if (op == "set")
            {
                var value = (double)request["value"];
                if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("Valor inválido");
                if (!Originals.ContainsKey(id)) Originals[id] = Read(id);
                Write(id, value);
                if ((bool?)request["locked"] == true) Locks[id] = value; else Locks.Remove(id);
            }
            else if (op == "restoreValue")
            {
                Locks.Remove(id);
                if (Originals.TryGetValue(id, out var original)) { Write(id, original); Originals.Remove(id); }
            }
            else if (op == "toggle")
            {
                var allowed = new[] { "stupid", "vision", "hearing", "witness", "teachers", "council", "senpai", "reports", "freezeAll", "freezeStudents", "freezeTeachers", "freezeCouncil", "freezeClock", "sanity", "noPenalty" };
                if (!allowed.Contains(id)) throw new ArgumentException("Función no permitida");
                if ((bool)request["enabled"]) Toggles.Add(id); else Toggles.Remove(id);
            }
            else if (op == "npc")
            {
                int npcId = (int)request["npc"];
                var npc = students.FirstOrDefault(n => n != null && n.StudentID == npcId);
                if (npc == null) throw new InvalidOperationException("El personaje ya no está cargado.");
                switch (id)
                {
                    case "ignore": if ((bool)request["enabled"]) Ignored.Add(npcId); else Ignored.Remove(npcId); break;
                    case "friendship": if(!relations.ContainsKey("friendship:"+npcId)) relations["friendship:"+npcId]=StudentGlobals.GetStudentFriendship(npcId); StudentGlobals.SetStudentFriendship(npcId, ClampInt((double)request["value"], 0, 100)); break;
                    case "reputation": if(!relations.ContainsKey("reputation:"+npcId)) relations["reputation:"+npcId]=StudentGlobals.GetStudentReputation(npcId); StudentGlobals.SetStudentReputation(npcId, ClampInt((double)request["value"], -100, 100)); break;
                    case "speed": if(!npcSpeeds.ContainsKey(npcId)) npcSpeeds[npcId]=npc.WalkSpeed; npc.WalkSpeed=Clamp((double)request["value"],.1f,8f); if(npc.Pathfinding!=null) npc.Pathfinding.maxSpeed=npc.WalkSpeed; break;
                    case "toDestination": if (npc.Dying || !npc.Alive) throw new InvalidOperationException("Personaje no disponible"); npc.TeleportToDestination(); break;
                    case "playerToNpc":
                        if (yan == null || !yan.CanMove || yan.Attacking || yan.Struggling) throw new InvalidOperationException("La protagonista está ocupada");
                        var point = npc.transform.position - npc.transform.forward * 2;
                        if (!Physics.Raycast(point + Vector3.up * 2, Vector3.down, out var hit, 5f)) throw new InvalidOperationException("No se encontró suelo válido");
                        bool was = yan.MyController.enabled; yan.MyController.enabled = false; yan.transform.position = hit.point + Vector3.up * .1f; yan.MyController.enabled = was; break;
                    default: throw new ArgumentException("Acción no permitida");
                }
            }
            else throw new ArgumentException("Operación desconocida");
            return Snapshot();
        }

        void Refresh()
        {
            yan = Find<YandereScript>();
            academic = yan != null && yan.Class != null ? yan.Class : Find<ClassScript>();
            reputation = Find<ReputationScript>();
            clock = Find<ClockScript>();
            manager = Find<StudentManagerScript>();
            students = manager != null && manager.Students != null ? manager.Students.Where(n => n != null && n.gameObject.activeInHierarchy).Distinct().ToArray() : new StudentScript[0];
        }
        static T Find<T>() where T : Component => Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(x => x.gameObject.scene.IsValid() && x.gameObject.scene.isLoaded);
        static int ClampInt(double v, int min, int max) => (int)Math.Max(min, Math.Min(max, Math.Round(v)));
        static float Clamp(double v, float min, float max) => (float)Math.Max(min, Math.Min(max, v));

        static readonly string[] Subjects = { "Biology", "Chemistry", "Language", "Physical", "Psychology" };
        static readonly string[] Values = { "reputation", "permanentRep", "study", "info", "money", "seduction", "enlightenment", "numbness", "sanity", "run", "walk", "health", "hour", "timeSpeed", "atmosphere", "Biology", "BiologyGrade", "Chemistry", "ChemistryGrade", "Language", "LanguageGrade", "Physical", "PhysicalGrade", "Psychology", "PsychologyGrade" };
        double Read(string id)
        {
            switch (id)
            {
                case "reputation": if (reputation != null) return reputation.Reputation; break;
                case "permanentRep": return PlayerGlobals.Reputation;
                case "study": if (academic != null) return academic.StudyPoints; break;
                case "info": if (!GameGlobals.Eighties && yan != null && yan.Inventory != null) return yan.Inventory.PantyShots; break;
                case "money": if (yan != null && yan.Inventory != null) return yan.Inventory.Money; break;
                case "seduction": return PlayerGlobals.Seduction;
                case "enlightenment": return PlayerGlobals.Enlightenment;
                case "numbness": return PlayerGlobals.Numbness;
                case "sanity": if (yan != null) return yan.Sanity; break;
                case "run": if (yan != null) return yan.RunSpeed; break;
                case "walk": if (yan != null) return yan.WalkSpeed; break;
                case "health": if (yan != null) return yan.Health; break;
                case "hour": if (clock != null) return clock.PresentTime / 60f; break;
                case "timeSpeed": if (clock != null) return clock.TimeSpeed; break;
                case "atmosphere": return SchoolGlobals.SchoolAtmosphere;
                default:
                    if (Values.Contains(id) && academic != null) return Convert.ToDouble(typeof(ClassScript).GetField(id).GetValue(academic));
                    break;
            }
            throw new InvalidOperationException("No disponible en esta escena o modo");
        }

        void Write(string id, double value)
        {
            Read(id); // availability check before any mutation
            switch (id)
            {
                case "reputation": reputation.Reputation = Clamp(value, -100, 100); reputation.LerpTimer = 0; reputation.UpdatePendingRepLabel(); break;
                case "permanentRep": PlayerGlobals.Reputation = Clamp(value, -100, 100); break;
                case "study": academic.StudyPoints = ClampInt(value, 0, 10000); academic.StartingPoints = Math.Max(academic.StartingPoints, academic.StudyPoints); academic.UpdateLabel(); break;
                case "info": yan.Inventory.PantyShots = ClampInt(value, 0, 100000); PlayerGlobals.PantyShots = yan.Inventory.PantyShots; foreach(var s in Resources.FindObjectsOfTypeAll<ServicesScript>().Where(x => x.gameObject.scene.IsValid() && x.Inventory == yan.Inventory)) { s.UpdatePantyCount(); if (s.gameObject.activeInHierarchy) s.UpdateDesc(); } break;
                case "money": yan.Inventory.Money = Clamp(value, 0, 1000000); PlayerGlobals.Money = yan.Inventory.Money; yan.Inventory.UpdateMoney(); break;
                case "seduction": PlayerGlobals.Seduction = ClampInt(value, 0, 5); if(academic != null) academic.Seduction = PlayerGlobals.Seduction; break;
                case "enlightenment": PlayerGlobals.Enlightenment = ClampInt(value, 0, 5); if(academic != null) academic.Enlightenment = PlayerGlobals.Enlightenment; break;
                case "numbness": PlayerGlobals.Numbness = ClampInt(value, 0, 5); if(academic != null) academic.Numbness = PlayerGlobals.Numbness; break;
                case "sanity": yan.Sanity = Clamp(value, 0, 100); break;
                case "run": yan.RunSpeed = Clamp(value, 1, 20); break;
                case "walk": yan.WalkSpeed = Clamp(value, .5f, 10); break;
                case "health": yan.Health = ClampInt(value, 1, 100); break;
                case "hour": if(value / 1f < clock.PresentTime / 60f) throw new InvalidOperationException("Retroceder la hora puede romper eventos; sólo se permite avanzar."); clock.PresentTime = Clamp(value, 7, 17.9f) * 60; break;
                case "timeSpeed": clock.TimeSpeed = Clamp(value, 0, 600); break;
                case "atmosphere": SchoolGlobals.SchoolAtmosphere = Clamp(value, 0, 1); if(manager != null) manager.Atmosphere = SchoolGlobals.SchoolAtmosphere; break;
                default:
                    string subject = Subjects.FirstOrDefault(s => id == s || id == s + "Grade");
                    if (subject == null) throw new ArgumentException("Estadística desconocida");
                    int index = Array.IndexOf(Subjects, subject) + 1;
                    int points = id.EndsWith("Grade") ? ClampInt(value, 0, 5) * 20 : ClampInt(value, 0, 100);
                    int grade = points / 20;
                    typeof(ClassScript).GetField(subject).SetValue(academic, points);
                    typeof(ClassScript).GetField(subject + "Grade").SetValue(academic, grade);
                    typeof(ClassGlobals).GetProperty(subject).SetValue(null, points, null);
                    typeof(ClassGlobals).GetProperty(subject + "Grade").SetValue(null, grade, null);
                    if(academic.Subject != null && academic.Subject.Length > index) academic.Subject[index] = points;
                    if(academic.SubjectTemp != null && academic.SubjectTemp.Length > index) academic.SubjectTemp[index] = 0;
                    if(subject == "Physical" && yan != null) yan.PhysicalGrade = grade;
                    AccessTools.Method(typeof(ClassScript), "UpdateBars").Invoke(academic, null);
                    break;
            }
        }

        object Snapshot()
        {
            var values = new Dictionary<string, object>();
            if (compatible) foreach (var id in Values) try { values[id] = Read(id); } catch { values[id] = null; }
            return new {
                compatible, hash, build = "530", unity = Application.unityVersion, scene = SceneManager.GetActiveScene().name,
                mode = compatible ? (GameGlobals.Eighties ? "1980" : "202X") : "Desconocido",
                profile = compatible ? GameGlobals.Profile : 0, values,
                toggles = Toggles.ToArray(), locks = Locks.Keys.ToArray(),
                npcs = students.Where(n => n != null).Select(n => new { id = n.StudentID, name = n.Name, teacher = n.Teacher, council = n.Club == ClubType.Council,
                    alive = n.Alive, alarm = n.Alarm, chasing = n.Chasing, frozen = frozen.ContainsKey(n.GetInstanceID()), ignored = Ignored.Contains(n.StudentID),
                    friendship = StudentGlobals.GetStudentFriendship(n.StudentID), reputation = StudentGlobals.GetStudentReputation(n.StudentID), speed=n.WalkSpeed,
                    x = n.transform.position.x, y = n.transform.position.y, z = n.transform.position.z }).ToArray()
            };
        }

        void FreezeUpdate()
        {
            foreach(var npc in students)
            {
                if (npc == null) continue;
                bool freeze = Toggles.Contains("freezeAll") || (npc.Teacher && Toggles.Contains("freezeTeachers")) || (npc.Club == ClubType.Council && Toggles.Contains("freezeCouncil")) || (!npc.Teacher && npc.Club != ClubType.Council && Toggles.Contains("freezeStudents"));
                int key = npc.GetInstanceID();
                if(freeze && !frozen.ContainsKey(key))
                {
                    var f = new FrozenNpc { Npc = npc, Enabled = npc.enabled, Position = npc.transform.position, CanMove = npc.Pathfinding != null && npc.Pathfinding.canMove, CanSearch = npc.Pathfinding != null && npc.Pathfinding.canSearch };
                    if(npc.CharacterAnimation != null) foreach(AnimationState state in npc.CharacterAnimation) { f.Speeds[state] = state.speed; state.speed = 0; }
                    frozen[key] = f;
                }
                if(freeze) { npc.enabled = false; if(npc.Pathfinding != null) { npc.Pathfinding.canMove = false; npc.Pathfinding.canSearch = false; } npc.transform.position = frozen[key].Position; }
                else if(frozen.TryGetValue(key, out var old)) { Unfreeze(old); frozen.Remove(key); }
            }
            foreach(var k in frozen.Where(x => x.Value.Npc == null).Select(x => x.Key).ToArray()) frozen.Remove(k);
        }
        void LateUpdate() { foreach(var f in frozen.Values) if(f.Npc != null) f.Npc.transform.position = f.Position; }
        void Unfreeze(FrozenNpc f) { if(f.Npc == null) return; f.Npc.enabled = f.Enabled; if(f.Npc.Pathfinding != null) { f.Npc.Pathfinding.canMove = f.CanMove; f.Npc.Pathfinding.canSearch = f.CanSearch; } foreach(var state in f.Speeds) if(state.Key != null) state.Key.speed = state.Value; }
        void UnfreezeAll() { foreach(var f in frozen.Values) Unfreeze(f); frozen.Clear(); }
        void Restore()
        {
            Toggles.Clear(); Locks.Clear(); Ignored.Clear(); UnfreezeAll();
            if(frozenClock!=null) { frozenClock.TimeSpeed=originalClockSpeed; frozenClock=null; }
            foreach(var n in students) if(n!=null && npcSpeeds.TryGetValue(n.StudentID,out var speed)) { n.WalkSpeed=speed; if(n.Pathfinding!=null) n.Pathfinding.maxSpeed=speed; }
            npcSpeeds.Clear();
            foreach(var r in relations) { var parts=r.Key.Split(':'); int id=int.Parse(parts[1]); if(parts[0]=="friendship") StudentGlobals.SetStudentFriendship(id,r.Value); else StudentGlobals.SetStudentReputation(id,r.Value); }
            relations.Clear();
            foreach(var pair in Originals.ToArray()) try { Write(pair.Key, pair.Value); } catch(Exception e) { Logger.LogWarning("No se pudo restaurar " + pair.Key + ": " + e.Message); }
            Originals.Clear();
        }
        void OnDestroy()
        {
            running = false;
            SceneManager.sceneLoaded -= SceneLoaded;
            if(compatible) Restore();
            harmony?.UnpatchSelf();
            // A synchronous pipe read may block Dispose on Unity's main thread.
            // Do not join or dispose that worker from the engine shutdown callback.
            var closingServer=server;
            ThreadPool.QueueUserWorkItem(_=> { try { closingServer?.Dispose(); } catch { } });
            try { if(File.Exists(endpoint) && File.ReadAllText(endpoint).Contains(pipe)) File.Delete(endpoint); } catch { }
            Instance = null;
            Application.runInBackground = originalBackground;
        }
        internal bool Ignore(StudentScript npc) => Toggles.Contains("stupid") || Ignored.Contains(npc.StudentID) || (npc.Teacher && Toggles.Contains("teachers")) || (npc.Club == ClubType.Council && Toggles.Contains("council")) || (npc.StudentID == 1 && Toggles.Contains("senpai"));
        internal bool IsLocked(string id) => Locks.ContainsKey(id);
    }

    [HarmonyPatch(typeof(YandereScript), "Start")]
    static class BackgroundPatch
    {
        static void Postfix() { Application.runInBackground=true; }
    }

    [HarmonyPatch(typeof(StudentScript), "Update")]
    static class ProximityPatch
    {
        static void Prefix(StudentScript __instance, ref bool? __state)
        {
            var p=Plugin.Instance;
            if(p!=null && p.Ignore(__instance) && __instance.Yandere!=null) { __state=__instance.Yandere.Invisible; __instance.Yandere.Invisible=true; }
        }
        static void Finalizer(StudentScript __instance, bool? __state) { if(__state.HasValue && __instance.Yandere!=null) __instance.Yandere.Invisible=__state.Value; }
    }

    [HarmonyPatch(typeof(ServicesScript), "Purchase")]
    static class PurchasePatch
    {
        static void Prefix(ServicesScript __instance, ref int? __state) { if(Plugin.Instance!=null && Plugin.Instance.IsLocked("info")) __state=__instance.Inventory.PantyShots; }
        static void Postfix(ServicesScript __instance, int? __state)
        {
            if(!__state.HasValue) return;
            __instance.Inventory.PantyShots=Math.Max(__state.Value,__instance.Inventory.PantyShots);
            PlayerGlobals.PantyShots=__instance.Inventory.PantyShots;
            __instance.UpdatePantyCount();
        }
    }

    [HarmonyPatch]
    static class VisionPatch
    {
        static IEnumerable<MethodBase> TargetMethods() => typeof(StudentScript).GetMethods().Where(m => m.Name == "CanSeeObject" && m.ReturnType == typeof(bool));
        static bool Prefix(StudentScript __instance, ref bool __result)
        {
            var p = Plugin.Instance;
            if(p != null && (p.Toggles.Contains("vision") || p.Ignore(__instance))) { __result = false; return false; }
            return true;
        }
    }
    [HarmonyPatch]
    static class ReactionPatch
    {
        static readonly string[] names = { "UpdateVision", "BecomeAlarmed", "WitnessMurder", "UpdateWitnessedMurder", "DetermineWhatWasWitnessed", "PersonaReaction", "ChaseYandere", "SenpaiNoticed", "DetermineSenpaiReaction", "Shove", "Spray", "PushYandereAway", "AttackReaction" };
        static IEnumerable<MethodBase> TargetMethods() => names.Select(n => (MethodBase)AccessTools.Method(typeof(StudentScript), n)).Where(m => m != null);
        static bool Prefix(StudentScript __instance, MethodBase __originalMethod)
        {
            var p = Plugin.Instance;
            if(p == null) return true;
            if(p.Ignore(__instance)) return false;
            if(p.Toggles.Contains("witness") && new[] { "WitnessMurder", "UpdateWitnessedMurder", "DetermineWhatWasWitnessed", "PersonaReaction", "BecomeAlarmed" }.Contains(__originalMethod.Name)) return false;
            if(p.Toggles.Contains("reports") && new[] { "BecomeAlarmed", "ChaseYandere", "PersonaReaction" }.Contains(__originalMethod.Name)) return false;
            return true;
        }
    }
    [HarmonyPatch]
    static class HearingPatch
    {
        static IEnumerable<MethodBase> TargetMethods() => new[] { AccessTools.Method(typeof(AlarmDiscScript), "OnTriggerEnter"), AccessTools.Method(typeof(GiggleScript), "OnTriggerEnter") };
        static bool Prefix(Collider other)
        {
            var p = Plugin.Instance;
            if(p == null) return true;
            if(p.Toggles.Contains("hearing") || p.Toggles.Contains("stupid")) return false;
            var n = other.GetComponent<StudentScript>();
            return n == null || !p.Ignore(n);
        }
    }
}
