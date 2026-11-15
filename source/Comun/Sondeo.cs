#nullable disable
// Comprobación de requisitos de cada función contra el ensamblado del juego cargado.
// Compartido: el complemento usa un resolutor basado en AccessTools; las pruebas y el inspector,
// uno basado en reflexión de metadatos. Sólo depende de System.Reflection.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace YandereUltimate.Comun
{
    public interface IResolutor
    {
        Type Tipo(string nombre);
        FieldInfo Campo(Type tipo, string nombre);
        IEnumerable<MethodBase> Metodos(Type tipo, string nombre);
    }

    public sealed class EstadoFuncion
    {
        public EstadoFuncion(DefFuncion def) { Def = def; }
        public DefFuncion Def { get; }
        public string Id => Def.Id;
        public string Nombre => Def.Nombre;
        public bool Disponible => Motivo == null;
        /// <summary>null si está disponible; si no, por qué se desactivó.</summary>
        public string Motivo { get; internal set; }
        /// <summary>Motivo propio (requisito ausente o fallo en ejecución), sin contar dependencias.</summary>
        internal string MotivoPropio { get; set; }
    }

    /// <summary>
    /// Formato de requisitos:
    ///   T:Tipo
    ///   F:Tipo.Campo:TipoDelCampo
    ///   M:Tipo.Metodo(TipoParam1,TipoParam2):TipoDeRetorno   ("(*)" acepta cualquier sobrecarga)
    /// Los nombres de tipo son completos ("System.Single", "Pathfinding.AIPath", "StudentScript[]").
    /// </summary>
    public static class Sondeo
    {
        public static List<EstadoFuncion> Evaluar(IEnumerable<DefFuncion> funciones, IResolutor r)
        {
            var estados = funciones.Select(f => new EstadoFuncion(f)).ToList();
            foreach (var e in estados)
            {
                var faltan = e.Def.Requisitos.Select(q => Verificar(q, r)).Where(m => m != null).ToList();
                if (faltan.Count > 0) e.MotivoPropio = string.Join("; ", faltan.Take(3)) + (faltan.Count > 3 ? " (y " + (faltan.Count - 3) + " más)" : "");
            }
            Propagar(estados);
            return estados;
        }

        /// <summary>Recalcula los motivos considerando dependencias (en el orden del catálogo).</summary>
        public static void Propagar(List<EstadoFuncion> estados)
        {
            var porId = estados.ToDictionary(e => e.Id);
            foreach (var e in estados)
            {
                e.Motivo = e.MotivoPropio;
                if (e.Motivo != null) continue;
                foreach (var d in e.Def.Depende)
                {
                    if (!porId.TryGetValue(d, out var dep)) { e.Motivo = "Dependencia desconocida: " + d; break; }
                    if (!dep.Disponible) { e.Motivo = "Depende de «" + dep.Nombre + "»: " + dep.Motivo; break; }
                }
            }
        }

        public static void Desactivar(List<EstadoFuncion> estados, string id, string motivo)
        {
            var e = estados.FirstOrDefault(x => x.Id == id);
            if (e == null || e.MotivoPropio != null) return;
            e.MotivoPropio = motivo;
            Propagar(estados);
        }

        /// <summary>Devuelve null si el requisito existe; si no, una descripción en español de lo que falta.</summary>
        public static string Verificar(string requisito, IResolutor r)
        {
            try
            {
                char clase = requisito[0];
                string cuerpo = requisito.Substring(2);
                if (clase == 'T')
                    return r.Tipo(cuerpo) != null ? null : "falta el tipo " + cuerpo;

                if (clase == 'F')
                {
                    int dos = cuerpo.LastIndexOf(':');
                    string esperado = cuerpo.Substring(dos + 1);
                    Separar(cuerpo.Substring(0, dos), out var tipo, out var miembro);
                    var t = r.Tipo(tipo);
                    if (t == null) return "falta el tipo " + tipo;
                    var f = r.Campo(t, miembro);
                    if (f == null) return "falta el campo " + tipo + "." + miembro;
                    var real = NombreTipo(f.FieldType);
                    return real == esperado ? null : "el campo " + tipo + "." + miembro + " cambió de tipo (" + esperado + " → " + real + ")";
                }

                if (clase == 'M')
                {
                    int abre = cuerpo.IndexOf('('), cierra = cuerpo.LastIndexOf(')');
                    string retorno = cuerpo.Substring(cierra + 2);
                    string lista = cuerpo.Substring(abre + 1, cierra - abre - 1);
                    Separar(cuerpo.Substring(0, abre), out var tipo, out var miembro);
                    var t = r.Tipo(tipo);
                    if (t == null) return "falta el tipo " + tipo;
                    var candidatos = r.Metodos(t, miembro).ToList();
                    if (candidatos.Count == 0) return "falta el método " + tipo + "." + miembro;
                    if (lista == "*") return candidatos.Any(m => Retorno(m) == retorno) ? null : "el método " + tipo + "." + miembro + " cambió de firma";
                    var parametros = lista.Length == 0 ? new string[0] : lista.Split(',');
                    foreach (var m in candidatos)
                    {
                        var ps = m.GetParameters().Select(p => NombreTipo(p.ParameterType)).ToArray();
                        if (ps.SequenceEqual(parametros) && Retorno(m) == retorno) return null;
                    }
                    return "el método " + tipo + "." + miembro + " cambió de firma";
                }
                return "requisito con formato desconocido: " + requisito;
            }
            catch (Exception e)
            {
                // Un tipo de parámetro o de campo que no se puede cargar también cuenta como ausente.
                return "no se pudo comprobar " + requisito.Substring(2) + " (" + e.GetType().Name + ")";
            }
        }

        static string Retorno(MethodBase m) => m is MethodInfo mi ? NombreTipo(mi.ReturnType) : "System.Void";

        static void Separar(string completo, out string tipo, out string miembro)
        {
            // Admite constructores: "Tipo..ctor"
            int punto = completo.EndsWith("..ctor") ? completo.Length - 6 : completo.LastIndexOf('.');
            tipo = completo.Substring(0, punto);
            miembro = completo.Substring(punto + 1);
        }

        /// <summary>Nombre estable de un tipo, igual al que produce el inspector a partir de los metadatos.</summary>
        public static string NombreTipo(Type t)
        {
            if (t.IsByRef) return NombreTipo(t.GetElementType()) + "&";
            if (t.IsArray) return NombreTipo(t.GetElementType()) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
            if (t.IsPointer) return NombreTipo(t.GetElementType()) + "*";
            if (t.IsGenericParameter) return t.Name;
            string nombre = t.IsNested ? NombreTipo(t.DeclaringType) + "/" + t.Name : (string.IsNullOrEmpty(t.Namespace) ? t.Name : t.Namespace + "." + t.Name);
            if (t.IsGenericType && !t.IsGenericTypeDefinition)
                nombre += "<" + string.Join(",", t.GetGenericArguments().Select(NombreTipo)) + ">";
            return nombre;
        }
    }

    /// <summary>Resolutor por reflexión estándar (busca miembros declarados y heredados, públicos o no).</summary>
    public class ResolutorReflexion : IResolutor
    {
        const BindingFlags Todo = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        readonly Func<string, Type> tipos;
        public ResolutorReflexion(Func<string, Type> tipos) { this.tipos = tipos; }
        public virtual Type Tipo(string nombre) => tipos(nombre.Replace('/', '+'));
        public virtual FieldInfo Campo(Type tipo, string nombre)
        {
            for (var t = tipo; t != null; t = t.BaseType) { var f = t.GetField(nombre, Todo); if (f != null) return f; }
            return null;
        }
        public virtual IEnumerable<MethodBase> Metodos(Type tipo, string nombre)
        {
            if (nombre == ".ctor") return tipo.GetConstructors(Todo).Cast<MethodBase>().ToList();
            var lista = new List<MethodBase>();
            for (var t = tipo; t != null; t = t.BaseType) lista.AddRange(t.GetMethods(Todo).Where(m => m.Name == nombre));
            return lista;
        }
    }
}
