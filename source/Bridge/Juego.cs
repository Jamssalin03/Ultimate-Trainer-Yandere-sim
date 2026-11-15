// Acceso a las clases del juego. Regla: todo uso de un tipo o miembro de Assembly-CSharp vive en un
// método pequeño propio (o lambda) que sólo se invoca si la función correspondiente está disponible.
// Mono compila cada método por separado, así que un miembro ausente en una build no verificada sólo
// desactiva su función, nunca el complemento entero. Plugin.cs no declara campos de tipos del juego.
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using YandereUltimate.Comun;

namespace YandereUltimate
{
    /// <summary>Referencias de la escena cargada, guardadas como Component para no exigir los tipos del juego.</summary>
    internal sealed class Escena
    {
        public Component Yan, Clase, Reputacion, Reloj, Gestor;
        public Component[] Estudiantes = new Component[0];
    }

    internal sealed class AccesoValor
    {
        public AccesoValor(string id, Func<Escena, double?> leer, Action<Escena, double> escribir) { Id = id; Leer = leer; Escribir = escribir; }
        public string Id { get; }
        /// <summary>Devuelve null si el valor no existe en la escena o el modo cargados.</summary>
        public Func<Escena, double?> Leer { get; }
        public Action<Escena, double> Escribir { get; }
    }

    internal sealed class NpcCongelado
    {
        public Component Npc;
        public bool Habilitado, PodiaMoverse, PodiaBuscar;
        public Vector3 Posicion;
        public readonly Dictionary<AnimationState, float> Velocidades = new Dictionary<AnimationState, float>();
    }

    internal static class Juego
    {
        static float F(double v) => (float)v;
        static int I(double v) => (int)Math.Round(v);

        // ---------- Búsqueda de referencias ----------

        // AccessTools.TypeByName recorre todos los tipos cargados si el nombre no está calificado:
        // se resuelve una sola vez por tipo (los tipos no cambian durante la ejecución).
        static readonly Dictionary<string, Type> tipos = new Dictionary<string, Type>();
        internal static Type Tipo(string nombre)
        {
            lock (tipos)
            {
                if (!tipos.TryGetValue(nombre, out var t)) tipos[nombre] = t = AccessTools.TypeByName(nombre);
                return t;
            }
        }

        static Component Buscar(string tipo)
        {
            var t = Tipo(tipo);
            if (t == null) return null;
            foreach (var o in Resources.FindObjectsOfTypeAll(t))
                if (o is Component c && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded) return c;
            return null;
        }

        public static void Refrescar(Escena e, Func<string, bool> disponible)
        {
            e.Yan = Buscar("YandereScript");
            e.Clase = disponible("ref.claseProtagonista") && e.Yan != null ? ClaseDeProtagonista(e.Yan) ?? Buscar("ClassScript") : Buscar("ClassScript");
            e.Reputacion = Buscar("ReputationScript");
            e.Reloj = Buscar("ClockScript");
            e.Gestor = Buscar("StudentManagerScript");
            e.Estudiantes = disponible("ref.estudiantes") && e.Gestor != null ? EstudiantesActivos(e.Gestor) : new Component[0];
        }
        static Component ClaseDeProtagonista(Component yan) => ((YandereScript)yan).Class;
        static Component[] EstudiantesActivos(Component gestor)
        {
            var lista = ((StudentManagerScript)gestor).Students;
            if (lista == null) return new Component[0];
            return lista.Where(n => n != null && n.gameObject.activeInHierarchy).Distinct().Cast<Component>().ToArray();
        }

        // ---------- Valores numéricos ----------

        public static readonly Dictionary<string, AccesoValor> Valores = CrearValores();

        static Dictionary<string, AccesoValor> CrearValores()
        {
            var lista = new List<AccesoValor>
            {
                new AccesoValor("reputation", e => e.Reputacion == null ? (double?)null : ((ReputationScript)e.Reputacion).Reputation,
                    (e, v) => { var r = (ReputationScript)e.Reputacion; r.Reputation = F(v); r.LerpTimer = 0; r.UpdatePendingRepLabel(); }),
                new AccesoValor("permanentRep", e => PlayerGlobals.Reputation, (e, v) => PlayerGlobals.Reputation = F(v)),
                new AccesoValor("study", e => e.Clase == null ? (double?)null : ((ClassScript)e.Clase).StudyPoints,
                    (e, v) => { var c = (ClassScript)e.Clase; c.StudyPoints = I(v); c.StartingPoints = Math.Max(c.StartingPoints, c.StudyPoints); c.UpdateLabel(); }),
                new AccesoValor("info", LeerInfo, EscribirInfo),
                new AccesoValor("money", e => InventarioDe(e) == null ? (double?)null : ((InventoryScript)InventarioDe(e)).Money, EscribirDinero),
                new AccesoValor("seduction", e => PlayerGlobals.Seduction, (e, v) => { PlayerGlobals.Seduction = I(v); if (e.Clase != null) ((ClassScript)e.Clase).Seduction = PlayerGlobals.Seduction; }),
                new AccesoValor("enlightenment", e => PlayerGlobals.Enlightenment, (e, v) => { PlayerGlobals.Enlightenment = I(v); if (e.Clase != null) ((ClassScript)e.Clase).Enlightenment = PlayerGlobals.Enlightenment; }),
                new AccesoValor("numbness", e => PlayerGlobals.Numbness, (e, v) => { PlayerGlobals.Numbness = I(v); if (e.Clase != null) ((ClassScript)e.Clase).Numbness = PlayerGlobals.Numbness; }),
                new AccesoValor("sanity", e => e.Yan == null ? (double?)null : ((YandereScript)e.Yan).Sanity, (e, v) => ((YandereScript)e.Yan).Sanity = F(v)),
                new AccesoValor("run", e => e.Yan == null ? (double?)null : ((YandereScript)e.Yan).RunSpeed, (e, v) => ((YandereScript)e.Yan).RunSpeed = F(v)),
                new AccesoValor("walk", e => e.Yan == null ? (double?)null : ((YandereScript)e.Yan).WalkSpeed, (e, v) => ((YandereScript)e.Yan).WalkSpeed = F(v)),
                new AccesoValor("health", e => e.Yan == null ? (double?)null : ((YandereScript)e.Yan).Health, (e, v) => ((YandereScript)e.Yan).Health = I(v)),
                new AccesoValor("hour", e => e.Reloj == null ? (double?)null : ((ClockScript)e.Reloj).PresentTime / 60f, EscribirHora),
                new AccesoValor("timeSpeed", e => e.Reloj == null ? (double?)null : ((ClockScript)e.Reloj).TimeSpeed, (e, v) => ((ClockScript)e.Reloj).TimeSpeed = F(v)),
                new AccesoValor("atmosphere", e => SchoolGlobals.SchoolAtmosphere, (e, v) => { SchoolGlobals.SchoolAtmosphere = F(v); if (e.Gestor != null) ((StudentManagerScript)e.Gestor).Atmosphere = SchoolGlobals.SchoolAtmosphere; }),
            };
            foreach (var materia in Definiciones.Materias)
            {
                var m = materia;
                lista.Add(new AccesoValor(m, e => e.Clase == null ? (double?)null : Materia((ClassScript)e.Clase, m), (e, v) => EscribirMateria(e, m, I(v))));
                lista.Add(new AccesoValor(m + "Grade", e => e.Clase == null ? (double?)null : Grado((ClassScript)e.Clase, m), (e, v) => EscribirMateria(e, m, I(v) * 20)));
            }
            return lista.ToDictionary(a => a.Id);
        }

        static Component InventarioDe(Escena e) => e.Yan == null ? null : ((YandereScript)e.Yan).Inventory;
        static double? LeerInfo(Escena e)
        {
            if (GameGlobals.Eighties) return null; // Info-chan no existe en 1980
            var inv = InventarioDe(e);
            return inv == null ? (double?)null : ((InventoryScript)inv).PantyShots;
        }
        static void EscribirInfo(Escena e, double v)
        {
            var inv = (InventoryScript)InventarioDe(e);
            inv.PantyShots = I(v);
            PlayerGlobals.PantyShots = inv.PantyShots;
            foreach (var o in Resources.FindObjectsOfTypeAll<ServicesScript>())
            {
                if (!o.gameObject.scene.IsValid() || o.Inventory != inv) continue;
                o.UpdatePantyCount();
                if (o.gameObject.activeInHierarchy) o.UpdateDesc();
            }
        }
        static void EscribirDinero(Escena e, double v)
        {
            var inv = (InventoryScript)InventarioDe(e);
            inv.Money = F(v);
            PlayerGlobals.Money = inv.Money;
            inv.UpdateMoney();
        }
        static void EscribirHora(Escena e, double v)
        {
            var reloj = (ClockScript)e.Reloj;
            if (v < reloj.PresentTime / 60f - 1e-4) throw new InvalidOperationException("Retroceder la hora puede romper eventos; sólo se permite avanzar.");
            reloj.PresentTime = F(v) * 60f;
        }

        static int Materia(ClassScript c, string m)
        {
            switch (m)
            {
                case "Biology": return c.Biology;
                case "Chemistry": return c.Chemistry;
                case "Language": return c.Language;
                case "Physical": return c.Physical;
                default: return c.Psychology;
            }
        }
        static int Grado(ClassScript c, string m)
        {
            switch (m)
            {
                case "Biology": return c.BiologyGrade;
                case "Chemistry": return c.ChemistryGrade;
                case "Language": return c.LanguageGrade;
                case "Physical": return c.PhysicalGrade;
                default: return c.PsychologyGrade;
            }
        }
        static void EscribirMateria(Escena e, string m, int puntos)
        {
            var c = (ClassScript)e.Clase;
            int grado = puntos / 20;
            switch (m)
            {
                case "Biology": c.Biology = puntos; c.BiologyGrade = grado; ClassGlobals.Biology = puntos; ClassGlobals.BiologyGrade = grado; break;
                case "Chemistry": c.Chemistry = puntos; c.ChemistryGrade = grado; ClassGlobals.Chemistry = puntos; ClassGlobals.ChemistryGrade = grado; break;
                case "Language": c.Language = puntos; c.LanguageGrade = grado; ClassGlobals.Language = puntos; ClassGlobals.LanguageGrade = grado; break;
                case "Physical": c.Physical = puntos; c.PhysicalGrade = grado; ClassGlobals.Physical = puntos; ClassGlobals.PhysicalGrade = grado; if (e.Yan != null) ((YandereScript)e.Yan).PhysicalGrade = grado; break;
                default: c.Psychology = puntos; c.PsychologyGrade = grado; ClassGlobals.Psychology = puntos; ClassGlobals.PsychologyGrade = grado; break;
            }
            int indice = Array.IndexOf(Definiciones.Materias, m) + 1;
            if (c.Subject != null && c.Subject.Length > indice) c.Subject[indice] = puntos;
            if (c.SubjectTemp != null && c.SubjectTemp.Length > indice) c.SubjectTemp[indice] = 0;
            // UpdateBars es privado: se invoca por reflexión (requisito declarado en el catálogo).
            AccessTools.Method(typeof(ClassScript), "UpdateBars")?.Invoke(c, null);
        }

        // ---------- Interruptores aplicados en cada frame ----------

        public static void MantenerCordura(Escena e) { if (e.Yan != null) ((YandereScript)e.Yan).Sanity = 100f; }

        public static void SinPenalizacion(Escena e)
        {
            if (e.Reputacion == null) return;
            var r = (ReputationScript)e.Reputacion;
            if (r.PendingRep < 0) { r.PendingRep = 0; r.UpdatePendingRepLabel(); }
        }

        public static float VelocidadReloj(Component reloj) => ((ClockScript)reloj).TimeSpeed;
        public static void FijarVelocidadReloj(Component reloj, float v) => ((ClockScript)reloj).TimeSpeed = v;

        // ---------- NPC ----------

        /// <summary>Usado por los parches: ¿este NPC debe ignorar a la protagonista?</summary>
        public static bool Ignora(StudentScript n)
        {
            var p = Plugin.Instance;
            return p != null && p.Ignorando(n.StudentID, n.Teacher, n.Club == ClubType.Council);
        }

        public static int IdNpc(Component npc) => ((StudentScript)npc).StudentID;
        public static bool EsProfesor(Component npc) => ((StudentScript)npc).Teacher;
        public static bool EsConsejo(Component npc) => ((StudentScript)npc).Club == ClubType.Council;

        public static object InfoNpc(Component c, bool congelado, bool ignorado, bool relaciones)
        {
            var n = (StudentScript)c;
            var p = n.transform.position;
            return new
            {
                id = n.StudentID, name = n.Name, teacher = n.Teacher, council = n.Club == ClubType.Council,
                alive = n.Alive, alarm = n.Alarm, chasing = n.Chasing, frozen = congelado, ignored = ignorado,
                friendship = relaciones ? (int?)StudentGlobals.GetStudentFriendship(n.StudentID) : null,
                reputation = relaciones ? (int?)StudentGlobals.GetStudentReputation(n.StudentID) : null,
                speed = n.WalkSpeed, x = p.x, y = p.y, z = p.z
            };
        }

        public static NpcCongelado Congelar(Component c)
        {
            var n = (StudentScript)c;
            var f = new NpcCongelado { Npc = n, Habilitado = n.enabled, Posicion = n.transform.position };
            if (n.Pathfinding != null) { f.PodiaMoverse = n.Pathfinding.canMove; f.PodiaBuscar = n.Pathfinding.canSearch; }
            if (n.CharacterAnimation != null)
                foreach (AnimationState estado in n.CharacterAnimation) { f.Velocidades[estado] = estado.speed; estado.speed = 0; }
            return f;
        }

        public static void MantenerCongelado(NpcCongelado f)
        {
            var n = (StudentScript)f.Npc;
            n.enabled = false;
            if (n.Pathfinding != null) { n.Pathfinding.canMove = false; n.Pathfinding.canSearch = false; }
            n.transform.position = f.Posicion;
        }

        public static void Descongelar(NpcCongelado f)
        {
            if (f.Npc == null) return;
            var n = (StudentScript)f.Npc;
            n.enabled = f.Habilitado;
            if (n.Pathfinding != null) { n.Pathfinding.canMove = f.PodiaMoverse; n.Pathfinding.canSearch = f.PodiaBuscar; }
            foreach (var estado in f.Velocidades) if (estado.Key != null) estado.Key.speed = estado.Value;
        }

        public static int Amistad(int id) => StudentGlobals.GetStudentFriendship(id);
        public static void FijarAmistad(int id, int v) => StudentGlobals.SetStudentFriendship(id, v);
        public static int ReputacionNpc(int id) => StudentGlobals.GetStudentReputation(id);
        public static void FijarReputacionNpc(int id, int v) => StudentGlobals.SetStudentReputation(id, v);

        public static float VelocidadNpc(Component c) => ((StudentScript)c).WalkSpeed;
        public static void FijarVelocidadNpc(Component c, float v)
        {
            var n = (StudentScript)c;
            n.WalkSpeed = v;
            if (n.Pathfinding != null) n.Pathfinding.maxSpeed = v;
        }

        public static void IrADestino(Component c)
        {
            var n = (StudentScript)c;
            if (n.Dying || !n.Alive) throw new InvalidOperationException("Personaje no disponible.");
            n.TeleportToDestination();
        }

        public static void ProtagonistaAlNpc(Escena e, Component c)
        {
            var yan = (YandereScript)e.Yan;
            if (yan == null || !yan.CanMove || yan.Attacking || yan.Struggling) throw new InvalidOperationException("La protagonista está ocupada.");
            var n = (StudentScript)c;
            var punto = n.transform.position - n.transform.forward * 2;
            if (!Physics.Raycast(punto + Vector3.up * 2, Vector3.down, out var impacto, 5f)) throw new InvalidOperationException("No se encontró suelo válido cerca del personaje.");
            bool estaba = yan.MyController.enabled;
            yan.MyController.enabled = false;
            yan.transform.position = impacto.point + Vector3.up * .1f;
            yan.MyController.enabled = estaba;
        }

        // ---------- Modo de juego ----------

        public static string Modo() => GameGlobals.Eighties ? "1980" : "202X";
        public static int Perfil() => GameGlobals.Profile;
    }
}
