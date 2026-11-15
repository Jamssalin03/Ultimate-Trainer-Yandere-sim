#nullable disable
// Catálogo de funciones y de las referencias del juego que necesita cada una.
// Cada referencia del IL del complemento a Assembly-CSharp debe figurar aquí (lo comprueban las pruebas
// y el inspector); también las que se usan por reflexión o por nombre (parches Harmony, UpdateBars).
// El orden importa: una función sólo puede depender de funciones declaradas antes.
using System.Collections.Generic;
using System.Linq;

namespace YandereUltimate.Comun
{
    public sealed class DefFuncion
    {
        public DefFuncion(string id, string nombre, bool visible, string[] requisitos, params string[] depende)
        { Id = id; Nombre = nombre; Visible = visible; Requisitos = requisitos; Depende = depende; }
        public string Id { get; }
        /// <summary>Nombre en español que se muestra en el menú.</summary>
        public string Nombre { get; }
        /// <summary>Las funciones internas no se muestran: sólo propagan su estado a las visibles.</summary>
        public bool Visible { get; }
        public string[] Requisitos { get; }
        public string[] Depende { get; }
    }

    public static class Catalogo
    {
        const string Int = "System.Int32", Float = "System.Single", Bool = "System.Boolean", Void = "System.Void";

        static string[] R(params string[] r) => r;
        static string F(string tipo, string campo, string t) => "F:" + tipo + "." + campo + ":" + t;
        static string M(string tipo, string metodo, string retorno, params string[] parametros) => "M:" + tipo + "." + metodo + "(" + string.Join(",", parametros) + "):" + retorno;
        static string Get(string tipo, string prop, string t) => M(tipo, "get_" + prop, t);
        static string Set(string tipo, string prop, string t) => M(tipo, "set_" + prop, Void, t);
        static string Cualquiera(string tipo, string metodo, string retorno) => "M:" + tipo + "." + metodo + "(*):" + retorno;

        static readonly string[] IgnorarDepende = { "parche.proximidad", "parche.vision", "parche.reacciones", "parche.audicion" };

        static string[] Materia(string m)
        {
            var r = new List<string>
            {
                "T:ClassScript", "T:ClassGlobals", F("ClassScript", m, Int), F("ClassScript", m + "Grade", Int),
                Set("ClassGlobals", m, Int), Set("ClassGlobals", m + "Grade", Int),
                F("ClassScript", "Subject", Int + "[]"), F("ClassScript", "SubjectTemp", Int + "[]"),
                M("ClassScript", "UpdateBars", Void), // privado: se invoca por reflexión
            };
            if (m == "Physical") r.Add(F("YandereScript", "PhysicalGrade", Int));
            return r.ToArray();
        }

        static IEnumerable<DefFuncion> Crear()
        {
            // ----- Internas -----
            yield return new DefFuncion("ref.claseProtagonista", "Clase de la protagonista", false, R("T:YandereScript", "T:ClassScript", F("YandereScript", "Class", "ClassScript")));
            yield return new DefFuncion("ref.estudiantes", "Lista de estudiantes de la escena", false, R("T:StudentManagerScript", "T:StudentScript", F("StudentManagerScript", "Students", "StudentScript[]")));
            yield return new DefFuncion("modo", "Modo de juego y perfil", false, R("T:GameGlobals", Get("GameGlobals", "Eighties", Bool), Get("GameGlobals", "Profile", Int)));
            yield return new DefFuncion("npc.datos", "Datos básicos de NPC", false,
                R("T:StudentScript", "T:ClubType", F("StudentScript", "StudentID", Int), F("StudentScript", "Teacher", Bool), F("StudentScript", "Club", "ClubType")), "ref.estudiantes");
            yield return new DefFuncion("npc.relaciones", "Relaciones de NPC", false,
                R("T:StudentGlobals", M("StudentGlobals", "GetStudentFriendship", Int, Int), M("StudentGlobals", "SetStudentFriendship", Void, Int, Int),
                  M("StudentGlobals", "GetStudentReputation", Int, Int), M("StudentGlobals", "SetStudentReputation", Void, Int, Int)));
            yield return new DefFuncion("congelar", "Congelación de NPC", false,
                R("T:Pathfinding.AIPath", "T:Pathfinding.AIBase", F("StudentScript", "Pathfinding", "Pathfinding.AIPath"), F("Pathfinding.AIBase", "canMove", Bool),
                  F("Pathfinding.AIBase", "canSearch", Bool), F("StudentScript", "CharacterAnimation", "UnityEngine.Animation")), "npc.datos");

            yield return new DefFuncion("parche.fondo", "Ejecución en segundo plano", false, R("T:YandereScript", M("YandereScript", "Start", Void)));
            yield return new DefFuncion("parche.proximidad", "Parche de proximidad", false,
                R(M("StudentScript", "Update", Void), F("StudentScript", "Yandere", "YandereScript"), F("YandereScript", "Invisible", Bool)), "npc.datos");
            yield return new DefFuncion("parche.compra", "Parche de compras de Info-chan", false,
                R("T:ServicesScript", "T:InventoryScript", M("ServicesScript", "Purchase", Void), F("ServicesScript", "Inventory", "InventoryScript"),
                  F("InventoryScript", "PantyShots", Int), "T:PlayerGlobals", Set("PlayerGlobals", "PantyShots", Int), M("ServicesScript", "UpdatePantyCount", Void)));
            yield return new DefFuncion("parche.vision", "Parche de visión", false, R(Cualquiera("StudentScript", "CanSeeObject", Bool)), "npc.datos");
            yield return new DefFuncion("parche.reacciones", "Parche de reacciones", false,
                Parches().Select(n => Cualquiera("StudentScript", n, Void)).ToArray(), "npc.datos");
            yield return new DefFuncion("parche.audicion", "Parche de audición", false,
                R("T:AlarmDiscScript", "T:GiggleScript", M("AlarmDiscScript", "OnTriggerEnter", Void, "UnityEngine.Collider"), M("GiggleScript", "OnTriggerEnter", Void, "UnityEngine.Collider")), "npc.datos");

            // ----- Estadísticas y recursos -----
            yield return new DefFuncion("valor.reputation", "Reputación actual", true,
                R("T:ReputationScript", F("ReputationScript", "Reputation", Float), F("ReputationScript", "LerpTimer", Float), M("ReputationScript", "UpdatePendingRepLabel", Void)));
            yield return new DefFuncion("valor.permanentRep", "Reputación permanente", true, R("T:PlayerGlobals", Get("PlayerGlobals", "Reputation", Float), Set("PlayerGlobals", "Reputation", Float)));
            yield return new DefFuncion("valor.study", "Puntos de estudio", true,
                R("T:ClassScript", F("ClassScript", "StudyPoints", Int), F("ClassScript", "StartingPoints", Int), M("ClassScript", "UpdateLabel", Void)));
            yield return new DefFuncion("valor.info", "Puntos de información", true,
                R("T:YandereScript", "T:InventoryScript", "T:ServicesScript", F("YandereScript", "Inventory", "InventoryScript"), F("InventoryScript", "PantyShots", Int),
                  Set("PlayerGlobals", "PantyShots", Int), F("ServicesScript", "Inventory", "InventoryScript"), M("ServicesScript", "UpdatePantyCount", Void), M("ServicesScript", "UpdateDesc", Void)), "modo");
            yield return new DefFuncion("valor.money", "Dinero", true,
                R("T:YandereScript", "T:InventoryScript", F("YandereScript", "Inventory", "InventoryScript"), F("InventoryScript", "Money", Float), Set("PlayerGlobals", "Money", Float), M("InventoryScript", "UpdateMoney", Void)));
            foreach (var (id, prop, nombre) in new[] { ("seduction", "Seduction", "Seducción"), ("enlightenment", "Enlightenment", "Iluminación"), ("numbness", "Numbness", "Insensibilidad") })
                yield return new DefFuncion("valor." + id, nombre, true, R("T:PlayerGlobals", Get("PlayerGlobals", prop, Int), Set("PlayerGlobals", prop, Int), F("ClassScript", prop, Int)));
            yield return new DefFuncion("valor.sanity", "Estado mental", true, R("T:YandereScript", Get("YandereScript", "Sanity", Float), Set("YandereScript", "Sanity", Float)));
            yield return new DefFuncion("valor.run", "Velocidad al correr", true, R("T:YandereScript", F("YandereScript", "RunSpeed", Float)));
            yield return new DefFuncion("valor.walk", "Velocidad al caminar", true, R("T:YandereScript", F("YandereScript", "WalkSpeed", Float)));
            yield return new DefFuncion("valor.health", "Salud de combate", true, R("T:YandereScript", F("YandereScript", "Health", Int)));
            yield return new DefFuncion("valor.hour", "Hora del día", true, R("T:ClockScript", F("ClockScript", "PresentTime", Float)));
            yield return new DefFuncion("valor.timeSpeed", "Velocidad del reloj", true, R("T:ClockScript", F("ClockScript", "TimeSpeed", Float)));
            yield return new DefFuncion("valor.atmosphere", "Atmósfera de la escuela", true,
                R("T:SchoolGlobals", Get("SchoolGlobals", "SchoolAtmosphere", Float), Set("SchoolGlobals", "SchoolAtmosphere", Float), F("StudentManagerScript", "Atmosphere", Float)));
            foreach (var (m, nombre) in new[] { ("Biology", "Biología"), ("Chemistry", "Química"), ("Language", "Lenguaje"), ("Physical", "Educación Física"), ("Psychology", "Psicología") })
            {
                yield return new DefFuncion("valor." + m, nombre + ": progreso", true, Materia(m));
                yield return new DefFuncion("valor." + m + "Grade", nombre + ": nivel", true, Materia(m));
            }

            // ----- Comportamiento -----
            yield return new DefFuncion("interruptor.stupid", "Personajes estúpidos", true, R(), IgnorarDepende);
            yield return new DefFuncion("interruptor.vision", "Desactivar visión de los NPC", true, R(), "parche.vision");
            yield return new DefFuncion("interruptor.hearing", "Desactivar audición de los NPC", true, R(), "parche.audicion");
            yield return new DefFuncion("interruptor.witness", "Desactivar reacciones de testigos", true, R(), "parche.reacciones");
            yield return new DefFuncion("interruptor.teachers", "Desactivar intervención de profesores", true, R(), IgnorarDepende);
            yield return new DefFuncion("interruptor.council", "Desactivar intervención del consejo", true, R(), IgnorarDepende);
            yield return new DefFuncion("interruptor.senpai", "Desactivar detección de Senpai", true, R(), IgnorarDepende);
            yield return new DefFuncion("interruptor.reports", "Desactivar reportes y alarmas", true, R(), "parche.reacciones");
            yield return new DefFuncion("interruptor.sanity", "Mantener estado mental al máximo", true, R(Set("YandereScript", "Sanity", Float)));
            yield return new DefFuncion("interruptor.noPenalty", "Sin penalizaciones pendientes de reputación", true,
                R("T:ReputationScript", F("ReputationScript", "PendingRep", Float), M("ReputationScript", "UpdatePendingRepLabel", Void)));

            // ----- NPC -----
            yield return new DefFuncion("npc.lista", "Lista de personajes", true,
                R(F("StudentScript", "Name", "System.String"), Get("StudentScript", "Alive", Bool), F("StudentScript", "Alarm", Float), F("StudentScript", "Chasing", Bool), F("StudentScript", "WalkSpeed", Float)), "npc.datos");
            foreach (var (id, nombre) in new[] { ("freezeAll", "Congelar todos los NPC"), ("freezeStudents", "Congelar estudiantes comunes"), ("freezeTeachers", "Congelar profesores"), ("freezeCouncil", "Congelar consejo estudiantil") })
                yield return new DefFuncion("interruptor." + id, nombre, true, R(), "congelar");
            yield return new DefFuncion("npc.ignore", "Ignorar a la protagonista (por NPC)", true, R(), new[] { "npc.lista" }.Concat(IgnorarDepende).ToArray());
            yield return new DefFuncion("npc.friendship", "Amistad del NPC", true, R(), "npc.lista", "npc.relaciones");
            yield return new DefFuncion("npc.reputation", "Reputación del NPC", true, R(), "npc.lista", "npc.relaciones");
            yield return new DefFuncion("npc.speed", "Velocidad del NPC", true,
                R(F("StudentScript", "WalkSpeed", Float), F("StudentScript", "Pathfinding", "Pathfinding.AIPath"), "T:Pathfinding.AIBase", F("Pathfinding.AIBase", "maxSpeed", Float)), "npc.lista");
            yield return new DefFuncion("npc.toDestination", "Enviar a su destino", true,
                R(F("StudentScript", "Dying", Bool), Get("StudentScript", "Alive", Bool), M("StudentScript", "TeleportToDestination", Void)), "npc.lista");
            yield return new DefFuncion("npc.playerToNpc", "Llevar a la protagonista al NPC", true,
                R(F("YandereScript", "CanMove", Bool), F("YandereScript", "Attacking", Bool), F("YandereScript", "Struggling", Bool), F("YandereScript", "MyController", "UnityEngine.CharacterController")), "npc.lista");

            // ----- Tiempo -----
            yield return new DefFuncion("interruptor.freezeClock", "Congelar el reloj", true, R("T:ClockScript", F("ClockScript", "TimeSpeed", Float)));
        }

        /// <summary>Métodos de StudentScript que intercepta el parche de reacciones.</summary>
        public static string[] Parches() => new[] { "UpdateVision", "BecomeAlarmed", "WitnessMurder", "UpdateWitnessedMurder", "DetermineWhatWasWitnessed", "PersonaReaction", "ChaseYandere", "SenpaiNoticed", "DetermineSenpaiReaction", "Shove", "Spray", "PushYandereAway", "AttackReaction" };

        public static readonly DefFuncion[] Funciones = Crear().ToArray();

        public static DefFuncion Buscar(string id) => Funciones.FirstOrDefault(f => f.Id == id);
    }
}
