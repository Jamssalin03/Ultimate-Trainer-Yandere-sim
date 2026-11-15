// Simulador del juego para pruebas de la aplicación (no es el juego ni lo modifica).
//   YandereSimulator.exe [--no-verificada] [--incompatible] [--sin-npc] [--protocolo N]
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Newtonsoft.Json;
using YandereUltimate.Comun;

namespace YandereUltimate.Simulador
{
    public static class Programa
    {
        static readonly Dictionary<string, double> valores = new Dictionary<string, double>
        {
            ["reputation"] = -10, ["permanentRep"] = -10, ["study"] = 0, ["info"] = 0, ["money"] = 10, ["seduction"] = 0, ["enlightenment"] = 0,
            ["numbness"] = 0, ["sanity"] = 100, ["run"] = 5, ["walk"] = 1, ["health"] = 10, ["hour"] = 7.03, ["timeSpeed"] = 3.5, ["atmosphere"] = 1,
            ["Biology"] = 0, ["BiologyGrade"] = 0, ["Chemistry"] = 0, ["ChemistryGrade"] = 0, ["Language"] = 0, ["LanguageGrade"] = 0,
            ["Physical"] = 0, ["PhysicalGrade"] = 0, ["Psychology"] = 0, ["PsychologyGrade"] = 0,
        };
        sealed class Npc { public int Id; public string Nombre; public bool Profesor, Consejo; public int Amistad, Reputacion; public float Velocidad = 1, X, Y, Z; }
        static readonly List<Npc> npcs = new List<Npc>();
        static readonly HashSet<string> activos = new HashSet<string>(), desactivadasIds = new HashSet<string>();
        static readonly HashSet<int> ignorados = new HashSet<int>();
        static readonly Dictionary<string, double> bloqueos = new Dictionary<string, double>(), originales = new Dictionary<string, double>();
        static readonly ConcurrentQueue<Trabajo> trabajos = new ConcurrentQueue<Trabajo>();
        static readonly List<object> desactivadas = new List<object>();
        static string compatibilidad = "verificada";
        static int protocolo = Definiciones.Protocolo;
        static Respaldos respaldos;

        public static void Main(string[] args)
        {
            if (args.Contains("--no-verificada"))
            {
                compatibilidad = "no_verificada";
                foreach (var (id, nombre, motivo) in new[] { ("valor.timeSpeed", "Velocidad del reloj", "falta el campo ClockScript.TimeSpeed"), ("interruptor.freezeClock", "Congelar el reloj", "falta el campo ClockScript.TimeSpeed"), ("npc.speed", "Velocidad del NPC", "el campo Pathfinding.AIBase.maxSpeed cambió de tipo (System.Single → System.Double)") })
                { desactivadas.Add(new { id, name = nombre, reason = motivo }); desactivadasIds.Add(id); }
            }
            if (args.Contains("--incompatible")) compatibilidad = "incompatible";
            int i = Array.IndexOf(args, "--protocolo"); if (i >= 0) protocolo = int.Parse(args[i + 1]);
            if (!args.Contains("--sin-npc"))
            {
                var nombres = new[] { "Senpai", "Osana Najimi", "Raibaru Fumetsu", "Megami Saikou", "Kokona Haruka", "Musume Ronshaku", "Budo Masuta", "Info-chan", "Sakyu Basu", "Inkyu Basu", "Akane Toriyasu", "Kizana Sunobu" };
                for (int n = 0; n < nombres.Length; n++) npcs.Add(new Npc { Id = n + 1, Nombre = nombres[n], Consejo = n == 3, X = n * 2.5f, Y = 0, Z = n });
                npcs.Add(new Npc { Id = 90, Nombre = "Natsuki Anna", Profesor = true, X = 40, Z = 3 });
                npcs.Add(new Npc { Id = 97, Nombre = "Kunahito Sunobu", Profesor = true, X = 42, Z = 1 });
            }

            var carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YandereUltimate");
            Directory.CreateDirectory(carpeta);
            respaldos = new Respaldos(Path.Combine(carpeta, "Cambios"), 50, TimeSpan.FromSeconds(1), m => Console.WriteLine("[respaldo] " + m));
            var bytes = new byte[32]; RandomNumberGenerator.Fill(bytes);
            string token = Convert.ToBase64String(bytes), canal = "YandereUltimate-sim-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(carpeta, "endpoint.json"), JsonConvert.SerializeObject(new { pipe = canal, token, pid = Environment.ProcessId, protocol = protocolo }));
            Console.WriteLine("Simulador listo: " + compatibilidad + " · protocolo " + protocolo + " · " + carpeta);

            new Thread(() => Servir(canal, token)) { IsBackground = true }.Start();
            while (true) { Frame(); Thread.Sleep(16); }
        }

        static void Servir(string canal, string token)
        {
            while (true)
            {
                try
                {
                    using var servidor = new NamedPipeServerStream(canal, PipeDirection.InOut, 1, PipeTransmissionMode.Byte);
                    servidor.WaitForConnection();
                    using var lector = new StreamReader(servidor);
                    using var escritor = new StreamWriter(servidor) { AutoFlush = true };
                    string linea;
                    while (servidor.IsConnected && (linea = lector.ReadLine()) != null)
                    {
                        string respuesta;
                        try
                        {
                            var json = Protocolo.Leer(linea);
                            if (!Protocolo.TokenIgual((string)json["token"], token)) { escritor.WriteLine(Protocolo.Error("Autenticación rechazada")); break; }
                            var t = new Trabajo(Protocolo.Validar(json));
                            trabajos.Enqueue(t);
                            respuesta = t.Esperar(5000, 25000);
                        }
                        catch (SolicitudInvalidaException e) { respuesta = Protocolo.Error(e.Message); }
                        catch (JsonException) { respuesta = Protocolo.Error("Solicitud inválida"); }
                        Console.WriteLine("<- " + linea.Replace(token, "***") + "\n-> " + (respuesta.Length > 160 ? respuesta.Substring(0, 160) + "…" : respuesta));
                        escritor.WriteLine(respuesta);
                    }
                }
                catch (IOException) { }
            }
        }

        static void Frame()
        {
            while (trabajos.TryDequeue(out var t))
            {
                if (!t.Reclamar()) continue;
                string r;
                try { r = Protocolo.Correcto(Ejecutar(t.Solicitud)); } catch (Exception e) { r = Protocolo.Error(e.Message); }
                t.Completar(r);
            }
            foreach (var b in bloqueos) if (valores[b.Key] < b.Value) valores[b.Key] = b.Value;
            if (activos.Contains("sanity")) valores["sanity"] = 100;
            if (!activos.Contains("freezeClock")) valores["hour"] = Math.Min(17.9, valores["hour"] + valores["timeSpeed"] / 60 / 60 * 0.016);
            if (!activos.Overlaps(new[] { "freezeAll", "freezeStudents" })) foreach (var n in npcs.Where(n => !n.Profesor)) n.X += 0.002f * n.Velocidad;
        }

        static object Ejecutar(Solicitud s)
        {
            if (s.Op == Definiciones.OpSnapshot) return Instantanea();
            if (compatibilidad == "incompatible") throw new InvalidOperationException("Versión incompatible.");
            if (s.Op == Definiciones.OpRestore) { Restaurar(); return Instantanea(); }
            string funcion = s.Op == Definiciones.OpToggle ? "interruptor." + s.Id : s.Op == Definiciones.OpNpc ? "npc." + s.Id : "valor." + s.Id;
            if (desactivadasIds.Contains(funcion)) throw new InvalidOperationException("Función desactivada en esta build.");
            respaldos.Encolar(DateTime.UtcNow, s.Op, s.Id, Instantanea);
            switch (s.Op)
            {
                case Definiciones.OpSet:
                    if (s.Id == "hour" && s.Valor < valores["hour"]) throw new InvalidOperationException("Retroceder la hora puede romper eventos; sólo se permite avanzar.");
                    if (!originales.ContainsKey(s.Id)) originales[s.Id] = valores[s.Id];
                    valores[s.Id] = s.Valor.Value;
                    if (s.Id.EndsWith("Grade")) valores[s.Id.Replace("Grade", "")] = s.Valor.Value * 20;
                    else if (Definiciones.Materias.Contains(s.Id)) valores[s.Id + "Grade"] = (int)s.Valor.Value / 20;
                    if (s.Bloqueado) bloqueos[s.Id] = s.Valor.Value; else bloqueos.Remove(s.Id);
                    break;
                case Definiciones.OpRestoreValue:
                    bloqueos.Remove(s.Id);
                    if (originales.TryGetValue(s.Id, out var o)) { valores[s.Id] = o; originales.Remove(s.Id); }
                    break;
                case Definiciones.OpToggle: if (s.Activado) activos.Add(s.Id); else activos.Remove(s.Id); break;
                case Definiciones.OpNpc:
                    var n = npcs.FirstOrDefault(x => x.Id == s.Npc) ?? throw new InvalidOperationException("El personaje ya no está cargado.");
                    switch (s.Id)
                    {
                        case "ignore": if (s.Activado) ignorados.Add(n.Id); else ignorados.Remove(n.Id); break;
                        case "friendship": n.Amistad = (int)s.Valor.Value; break;
                        case "reputation": n.Reputacion = (int)s.Valor.Value; break;
                        case "speed": n.Velocidad = (float)s.Valor.Value; break;
                    }
                    break;
            }
            return Instantanea();
        }

        static void Restaurar()
        {
            activos.Clear(); bloqueos.Clear(); ignorados.Clear();
            foreach (var o in originales) valores[o.Key] = o.Value;
            originales.Clear();
            foreach (var n in npcs) { n.Amistad = 0; n.Reputacion = 0; n.Velocidad = 1; }
        }

        static bool Congelado(Npc n) => activos.Contains("freezeAll") || (n.Profesor && activos.Contains("freezeTeachers")) || (n.Consejo && activos.Contains("freezeCouncil")) || (!n.Profesor && !n.Consejo && activos.Contains("freezeStudents"));

        static object Instantanea() => new
        {
            protocol = protocolo, plugin = "1.1.0-sim", compatibility = compatibilidad, compatible = compatibilidad != "incompatible", verified = compatibilidad == "verificada",
            hash = compatibilidad == "verificada" ? "E06B2209AA75DDA4EC6EE15E927566FD02F91B1BBC01A34908082D559CEB4F2C" : "0000SIMULADA",
            build = compatibilidad == "verificada" ? "532" : "desconocida", buildDate = compatibilidad == "verificada" ? "2026-10-31" : null,
            unity = "6000.2.8f1", scene = "SchoolScene", mode = "202X", profile = 1,
            values = Definiciones.Valores.ToDictionary(d => d.Id, d => desactivadasIds.Contains("valor." + d.Id) ? (object)null : valores[d.Id]),
            toggles = activos.ToArray(), locks = bloqueos.Keys.ToArray(), disabled = desactivadas.ToArray(),
            npcs = npcs.Select(n => new { id = n.Id, name = n.Nombre, teacher = n.Profesor, council = n.Consejo, alive = true, alarm = 0f, chasing = false, frozen = Congelado(n), ignored = ignorados.Contains(n.Id), friendship = n.Amistad, reputation = n.Reputacion, speed = n.Velocidad, x = n.X, y = n.Y, z = n.Z }).ToArray()
        };
    }
}
