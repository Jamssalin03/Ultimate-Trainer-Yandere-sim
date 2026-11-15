// Inspector de compatibilidad: compara lo que usa el complemento con el Assembly-CSharp de una build.
//
//   YandereUltimate.Inspector <carpeta del juego> [YandereUltimate.Bridge.dll] [--json salida.json]
//
// 1. Calcula el SHA-256 de Assembly-CSharp.dll y lo busca en compatibilidad.json.
// 2. Lista cada tipo, campo y método del juego referenciado por el IL del complemento y lo resuelve
//    contra la build indicada (Mono.Cecil). Un miembro ausente = MissingFieldException en el juego.
// 3. Comprueba que cada referencia esté declarada en el catálogo de funciones (Comun/Catalogo.cs),
//    porque el modo "build no verificada" sólo protege lo que el catálogo declara.
// 4. Evalúa el catálogo contra la build (igual que el complemento con AccessTools) y muestra qué
//    funciones quedarían activas o desactivadas y por qué.
// Código de salida: 0 todo correcto; 1 error de uso; 2 referencias ausentes; 3 catálogo incompleto.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mono.Cecil;
using YandereUltimate.Comun;

namespace YandereUltimate.Inspector
{
    public static class Programa
    {
        /// <summary>Ensamblados cuyo contenido cambia con cada build del juego (lo que el catálogo debe cubrir).</summary>
        public static readonly string[] EnsambladosDelJuego = { "Assembly-CSharp", "Assembly-CSharp-firstpass", "AstarPathfindingProject" };

        public static int Main(string[] args)
        {
            var posicionales = new List<string>();
            string salidaJson = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--json" && i + 1 < args.Length) salidaJson = args[++i];
                else posicionales.Add(args[i]);
            }
            if (posicionales.Count < 1)
            {
                Console.Error.WriteLine("Uso: YandereUltimate.Inspector <carpeta del juego o de Managed> [YandereUltimate.Bridge.dll] [--json salida.json]");
                return 1;
            }
            string managed = Managed(posicionales[0]);
            if (managed == null) { Console.Error.WriteLine("No se encontró Assembly-CSharp.dll en " + posicionales[0]); return 1; }
            string puente = posicionales.Count > 1 ? posicionales[1] : null;

            var informe = Inspeccionar(managed, puente);
            Imprimir(informe);
            if (salidaJson != null) File.WriteAllText(salidaJson, JsonSerializer.Serialize(informe, new JsonSerializerOptions { WriteIndented = true }));
            if (informe.Ausentes.Count > 0) return 2;
            if (informe.FueraDeCatalogo.Count > 0) return 3;
            return 0;
        }

        public static string Managed(string ruta)
        {
            foreach (var c in new[] { ruta, Path.Combine(ruta, "Managed"), Path.Combine(ruta, "YandereSimulator_Data", "Managed") })
                if (File.Exists(Path.Combine(c, "Assembly-CSharp.dll"))) return Path.GetFullPath(c);
            return null;
        }

        public sealed class Informe
        {
            public string Sha256 { get; set; }
            public string Build { get; set; }
            public string Fecha { get; set; }
            public bool Verificada { get; set; }
            public List<string> Referencias { get; set; } = new();
            public List<string> Ausentes { get; set; } = new();
            public List<string> FueraDeCatalogo { get; set; } = new();
            public List<FuncionInforme> Funciones { get; set; } = new();
        }
        public sealed class FuncionInforme
        {
            public string Id { get; set; }
            public string Nombre { get; set; }
            public bool Visible { get; set; }
            public bool Disponible { get; set; }
            public string Motivo { get; set; }
        }

        public static Informe Inspeccionar(string managed, string puente)
        {
            var informe = new Informe();
            var ensamblado = Path.Combine(managed, "Assembly-CSharp.dll");
            informe.Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ensamblado)));
            foreach (var b in BuildsConocidas())
                if (string.Equals((string)b["sha256_Assembly_CSharp"], informe.Sha256, StringComparison.OrdinalIgnoreCase))
                { informe.Verificada = true; informe.Build = (string)b["build"]; informe.Fecha = (string)b["fecha"]; }

            if (puente != null)
            {
                var (referencias, ausentes) = Referencias(puente, managed);
                informe.Referencias = referencias;
                informe.Ausentes = ausentes;
                var declarados = new HashSet<string>(Catalogo.Funciones.SelectMany(f => f.Requisitos));
                informe.FueraDeCatalogo = referencias.Where(r => !declarados.Contains(r)).ToList();
            }

            foreach (var e in EvaluarCatalogo(managed))
                informe.Funciones.Add(new FuncionInforme { Id = e.Id, Nombre = e.Nombre, Visible = e.Def.Visible, Disponible = e.Disponible, Motivo = e.Motivo });
            return informe;
        }

        public static IEnumerable<JsonObject> BuildsConocidas()
        {
            using var s = typeof(Programa).Assembly.GetManifestResourceStream("compatibilidad.json");
            var raiz = JsonNode.Parse(s!)!;
            return raiz["builds_conocidas"]!.AsArray().Select(n => n!.AsObject()).ToList();
        }

        /// <summary>
        /// Referencias del IL del complemento hacia ensamblados del juego, en el formato del catálogo,
        /// y cuáles de todas (juego y UnityEngine) no se resuelven en la build indicada.
        /// </summary>
        public static (List<string> Juego, List<string> Ausentes) Referencias(string puente, string managed)
        {
            var resolutor = new DefaultAssemblyResolver();
            resolutor.AddSearchDirectory(managed);
            var bepinex = Path.Combine(AppContext.BaseDirectory, "BepInEx");
            if (Directory.Exists(bepinex)) resolutor.AddSearchDirectory(bepinex);
            using var modulo = ModuleDefinition.ReadModule(puente, new ReaderParameters { AssemblyResolver = resolutor });

            var juego = new SortedSet<string>(StringComparer.Ordinal);
            var ausentes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var t in modulo.GetTypeReferences())
            {
                var ambito = Ambito(t);
                if (!Relevante(ambito)) continue;
                var texto = "T:" + Nombre(t);
                if (EsDelJuego(ambito)) juego.Add(texto);
                if (Resolver(() => t.Resolve()) == null) ausentes.Add(texto);
            }
            foreach (var m in modulo.GetMemberReferences())
            {
                var ambito = Ambito(m.DeclaringType);
                if (!Relevante(ambito)) continue;
                string texto = m switch
                {
                    FieldReference f => "F:" + Nombre(f.DeclaringType) + "." + f.Name + ":" + Nombre(f.FieldType),
                    MethodReference mr => "M:" + Nombre(mr.DeclaringType) + "." + mr.Name + "(" + string.Join(",", mr.Parameters.Select(p => Nombre(p.ParameterType))) + "):" + Nombre(mr.ReturnType),
                    _ => null
                };
                if (texto == null) continue;
                if (EsDelJuego(ambito)) juego.Add(texto);
                if (Resolver(() => m.Resolve()) == null) ausentes.Add(texto);
            }
            return (juego.ToList(), ausentes.ToList());
        }

        static object Resolver(Func<object> f) { try { return f(); } catch (AssemblyResolutionException) { return null; } }

        static string Ambito(TypeReference t)
        {
            while (t is TypeSpecification s) t = s.ElementType;
            while (t.DeclaringType != null) t = t.DeclaringType;
            return t.Scope switch
            {
                AssemblyNameReference a => a.Name,
                ModuleDefinition md => md.Assembly.Name.Name,
                _ => t.Scope?.Name ?? ""
            };
        }
        static bool EsDelJuego(string ambito) => EnsambladosDelJuego.Contains(ambito);
        static bool Relevante(string ambito) => EsDelJuego(ambito) || ambito.StartsWith("UnityEngine", StringComparison.Ordinal);

        /// <summary>Mismo formato que Sondeo.NombreTipo.</summary>
        public static string Nombre(TypeReference t)
        {
            switch (t)
            {
                case ByReferenceType b: return Nombre(b.ElementType) + "&";
                case ArrayType a: return Nombre(a.ElementType) + "[" + new string(',', a.Rank - 1) + "]";
                case PointerType p: return Nombre(p.ElementType) + "*";
                case GenericParameter g: return g.Name;
                case GenericInstanceType gi: return Nombre(gi.ElementType) + "<" + string.Join(",", gi.GenericArguments.Select(Nombre)) + ">";
                case RequiredModifierType rq: return Nombre(rq.ElementType);
                case OptionalModifierType op: return Nombre(op.ElementType);
            }
            if (t.DeclaringType != null) return Nombre(t.DeclaringType) + "/" + t.Name;
            return string.IsNullOrEmpty(t.Namespace) ? t.Name : t.Namespace + "." + t.Name;
        }

        /// <summary>Evalúa el catálogo como lo hace el complemento, pero sobre metadatos (sin ejecutar Unity).</summary>
        /// <param name="reemplazos">Rutas de DLL que reemplazan a las homónimas de <paramref name="managed"/> (para pruebas).</param>
        public static List<EstadoFuncion> EvaluarCatalogo(string managed, params string[] reemplazos)
        {
            var archivos = Directory.GetFiles(managed, "*.dll")
                .Where(f => !reemplazos.Any(r => Path.GetFileName(r).Equals(Path.GetFileName(f), StringComparison.OrdinalIgnoreCase)))
                .Concat(reemplazos).ToList();
            var nucleo = archivos.FirstOrDefault(f => Path.GetFileName(f).Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase)) ?? typeof(object).Assembly.Location;
            if (!archivos.Contains(nucleo)) archivos.Add(nucleo);
            using var contexto = new MetadataLoadContext(new PathAssemblyResolver(archivos), Path.GetFileNameWithoutExtension(nucleo));
            var ensamblados = new List<Assembly>();
            foreach (var f in archivos)
                try { ensamblados.Add(contexto.LoadFromAssemblyPath(f)); } catch (BadImageFormatException) { /* DLL nativa */ } catch (FileLoadException) { }
            Type Buscar(string nombre)
            {
                foreach (var a in ensamblados) { var t = a.GetType(nombre, false); if (t != null) return t; }
                return null;
            }
            return Sondeo.Evaluar(Catalogo.Funciones, new ResolutorReflexion(Buscar));
        }

        static void Imprimir(Informe i)
        {
            Console.WriteLine("SHA-256 de Assembly-CSharp.dll: " + i.Sha256);
            Console.WriteLine(i.Verificada ? $"Build conocida: {i.Build} ({i.Fecha}) · VERIFICADA" : "Build desconocida: NO VERIFICADA (no figura en compatibilidad.json)");
            if (i.Referencias.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"Referencias del complemento al juego: {i.Referencias.Count}");
                foreach (var r in i.Referencias) Console.WriteLine("  " + (i.Ausentes.Contains(r) ? "AUSENTE  " : "ok       ") + r);
                foreach (var r in i.Ausentes.Where(a => !i.Referencias.Contains(a))) Console.WriteLine("  AUSENTE  " + r);
                if (i.FueraDeCatalogo.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Referencias sin declarar en Comun/Catalogo.cs (el modo no verificado no las protegería):");
                    foreach (var r in i.FueraDeCatalogo) Console.WriteLine("  " + r);
                }
            }
            Console.WriteLine();
            var visibles = i.Funciones.Where(f => f.Visible).ToList();
            Console.WriteLine($"Funciones disponibles: {visibles.Count(f => f.Disponible)} de {visibles.Count}");
            foreach (var f in i.Funciones.Where(f => !f.Disponible)) Console.WriteLine($"  DESACTIVADA {f.Nombre} ({f.Id}): {f.Motivo}");
        }
    }
}
