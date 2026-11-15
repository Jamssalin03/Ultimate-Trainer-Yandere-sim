using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using YandereUltimate.Comun;

namespace YandereUltimate
{
    /// <summary>
    /// Decide el modo de funcionamiento según la build instalada:
    /// "verificada" (huella en compatibilidad.json), "no_verificada" (huella desconocida, sólo funciones cuyos
    /// requisitos existen) o "incompatible" (ninguna función disponible).
    /// </summary>
    internal sealed class Compatibilidad
    {
        public const string ModoVerificada = "verificada", NoVerificada = "no_verificada", Incompatible = "incompatible";

        List<EstadoFuncion> funciones = new List<EstadoFuncion>();
        public string Hash { get; private set; }
        public string Build { get; private set; }
        public string Fecha { get; private set; }
        public bool Verificada { get; private set; }
        public string Estado { get; private set; } = Incompatible;

        sealed class ResolutorAccessTools : IResolutor
        {
            public Type Tipo(string nombre) => AccessTools.TypeByName(nombre.Replace('/', '+'));
            public FieldInfo Campo(Type tipo, string nombre) => AccessTools.Field(tipo, nombre);
            public IEnumerable<MethodBase> Metodos(Type tipo, string nombre)
            {
                if (nombre == ".ctor") return AccessTools.GetDeclaredConstructors(tipo).Cast<MethodBase>();
                var lista = new List<MethodBase>();
                for (var t = tipo; t != null; t = t.BaseType) lista.AddRange(AccessTools.GetDeclaredMethods(t).Where(m => m.Name == nombre));
                return lista;
            }
        }

        public static Compatibilidad Evaluar(Assembly complemento, Action<string> advertir)
        {
            var c = new Compatibilidad();
            try
            {
                var juego = AccessTools.TypeByName("YandereScript")?.Assembly ?? AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (juego == null) { c.Hash = "(sin Assembly-CSharp)"; advertir("No se encontró Assembly-CSharp."); return c; }
                using (var sha = SHA256.Create()) c.Hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(juego.Location))).Replace("-", "");

                foreach (var b in BuildsConocidas(complemento))
                    if (string.Equals((string)b["sha256_Assembly_CSharp"], c.Hash, StringComparison.OrdinalIgnoreCase))
                    { c.Verificada = true; c.Build = (string)b["build"]; c.Fecha = (string)b["fecha"]; }

                // También en builds verificadas: confirma con AccessTools cada referencia antes de usarla.
                c.funciones = Sondeo.Evaluar(Catalogo.Funciones, new ResolutorAccessTools());
            }
            catch (Exception e) { advertir("No se pudo evaluar la compatibilidad: " + e.Message); c.funciones = new List<EstadoFuncion>(); }
            c.Recalcular();
            return c;
        }

        static IEnumerable<JObject> BuildsConocidas(Assembly complemento)
        {
            using (var s = complemento.GetManifestResourceStream("compatibilidad.json"))
            {
                if (s == null) return Enumerable.Empty<JObject>();
                using (var r = new StreamReader(s)) return JObject.Parse(r.ReadToEnd())["builds_conocidas"]?.OfType<JObject>().ToList() ?? new List<JObject>();
            }
        }

        void Recalcular()
        {
            if (Verificada && funciones.Count > 0) Estado = ModoVerificada;
            else Estado = funciones.Any(f => f.Disponible && f.Def.Visible) ? NoVerificada : Incompatible;
        }

        public bool Disponible(string id) => funciones.Any(f => f.Id == id && f.Disponible);
        public string Motivo(string id) => funciones.FirstOrDefault(f => f.Id == id)?.Motivo ?? "función desconocida";

        public void Desactivar(string id, string motivo)
        {
            Sondeo.Desactivar(funciones, id, motivo);
            Recalcular();
        }

        /// <summary>Funciones visibles en el menú que quedaron desactivadas, con su motivo.</summary>
        public IEnumerable<EstadoFuncion> Desactivadas() => funciones.Where(f => !f.Disponible && f.Def.Visible);
    }
}
