#nullable disable
// Lógica del protocolo sin dependencias de Unity: se compila en el complemento y en las pruebas.
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YandereUltimate.Comun;

namespace YandereUltimate
{
    internal sealed class SolicitudInvalidaException : Exception
    {
        public SolicitudInvalidaException(string mensaje) : base(mensaje) { }
    }

    /// <summary>Solicitud ya validada: tipos estrictos y valores dentro de la lista blanca.</summary>
    internal sealed class Solicitud
    {
        public string Op;
        public string Id;
        public double? Valor;
        public bool Bloqueado;
        public bool Activado;
        public int Npc;
    }

    internal static class Protocolo
    {
        public const int LongitudMaxima = 65536;

        static readonly JsonLoadSettings Lectura = new JsonLoadSettings
        {
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
            CommentHandling = CommentHandling.Ignore,
            LineInfoHandling = LineInfoHandling.Ignore,
        };

        public static JObject Leer(string linea)
        {
            using (var texto = new StringReader(linea))
            using (var lector = new JsonTextReader(texto) { MaxDepth = 4, DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
            {
                var token = JToken.ReadFrom(lector, Lectura);
                if (lector.Read()) throw new SolicitudInvalidaException("Solicitud inválida: contenido adicional.");
                if (!(token is JObject objeto)) throw new SolicitudInvalidaException("Solicitud inválida: se esperaba un objeto.");
                return objeto;
            }
        }

        /// <summary>Compara dos cadenas en tiempo constante respecto de su contenido.</summary>
        public static bool TokenIgual(string recibido, string esperado)
        {
            if (recibido == null || esperado == null) return false;
            var a = Encoding.UTF8.GetBytes(recibido);
            var b = Encoding.UTF8.GetBytes(esperado);
            int diferencia = a.Length ^ b.Length;
            for (int i = 0; i < b.Length; i++) diferencia |= (i < a.Length ? a[i] : 0) ^ b[i];
            return diferencia == 0;
        }

        public static Solicitud Validar(JObject r)
        {
            foreach (var p in r.Properties())
                if (!Definiciones.Contiene(Definiciones.Campos, p.Name)) throw new SolicitudInvalidaException("Campo no permitido: " + p.Name);

            var s = new Solicitud { Op = Texto(r, "op", true) };
            if (!Definiciones.Contiene(Definiciones.Operaciones, s.Op)) throw new SolicitudInvalidaException("Operación desconocida.");
            switch (s.Op)
            {
                case Definiciones.OpSnapshot:
                case Definiciones.OpRestore:
                    Prohibir(r, "id", "value", "locked", "enabled", "npc");
                    break;
                case Definiciones.OpSet:
                {
                    Prohibir(r, "enabled", "npc");
                    s.Id = Texto(r, "id", true);
                    var def = Definiciones.Valor(s.Id) ?? throw new SolicitudInvalidaException("Estadística no permitida.");
                    s.Valor = Numero(r, "value", true);
                    var error = def.Validar(s.Valor.Value);
                    if (error != null) throw new SolicitudInvalidaException(error);
                    s.Bloqueado = Logico(r, "locked", false) ?? false;
                    break;
                }
                case Definiciones.OpRestoreValue:
                    Prohibir(r, "value", "locked", "enabled", "npc");
                    s.Id = Texto(r, "id", true);
                    if (Definiciones.Valor(s.Id) == null) throw new SolicitudInvalidaException("Estadística no permitida.");
                    break;
                case Definiciones.OpToggle:
                    Prohibir(r, "value", "locked", "npc");
                    s.Id = Texto(r, "id", true);
                    if (!Definiciones.Contiene(Definiciones.Interruptores, s.Id)) throw new SolicitudInvalidaException("Función no permitida.");
                    s.Activado = Logico(r, "enabled", true).Value;
                    break;
                case Definiciones.OpNpc:
                {
                    Prohibir(r, "locked");
                    s.Id = Texto(r, "id", true);
                    if (!Definiciones.Contiene(Definiciones.AccionesNpc, s.Id)) throw new SolicitudInvalidaException("Acción no permitida.");
                    var npc = r["npc"];
                    if (npc == null || npc.Type != JTokenType.Integer) throw new SolicitudInvalidaException("El campo npc debe ser un número entero.");
                    long id = npc.Value<long>();
                    if (id < Definiciones.NpcMinimo || id > Definiciones.NpcMaximo) throw new SolicitudInvalidaException("Identificador de personaje fuera de rango.");
                    s.Npc = (int)id;
                    var def = Definiciones.ValorNpc(s.Id);
                    if (def != null)
                    {
                        Prohibir(r, "enabled");
                        s.Valor = Numero(r, "value", true);
                        var error = def.Validar(s.Valor.Value);
                        if (error != null) throw new SolicitudInvalidaException(error);
                    }
                    else if (s.Id == Definiciones.NpcIgnorar) { Prohibir(r, "value"); s.Activado = Logico(r, "enabled", true).Value; }
                    else Prohibir(r, "value", "enabled");
                    break;
                }
            }
            return s;
        }

        static void Prohibir(JObject r, params string[] campos)
        {
            foreach (var c in campos) if (r.Property(c) != null) throw new SolicitudInvalidaException("El campo " + c + " no corresponde a esta operación.");
        }
        static string Texto(JObject r, string campo, bool requerido)
        {
            var t = r[campo];
            if (t == null) { if (requerido) throw new SolicitudInvalidaException("Falta el campo " + campo + "."); return null; }
            if (t.Type != JTokenType.String) throw new SolicitudInvalidaException("El campo " + campo + " debe ser texto.");
            return t.Value<string>();
        }
        static double? Numero(JObject r, string campo, bool requerido)
        {
            var t = r[campo];
            if (t == null) { if (requerido) throw new SolicitudInvalidaException("Falta el campo " + campo + "."); return null; }
            if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float) throw new SolicitudInvalidaException("El campo " + campo + " debe ser numérico.");
            return t.Value<double>();
        }
        static bool? Logico(JObject r, string campo, bool requerido)
        {
            var t = r[campo];
            if (t == null) { if (requerido) throw new SolicitudInvalidaException("Falta el campo " + campo + "."); return null; }
            if (t.Type != JTokenType.Boolean) throw new SolicitudInvalidaException("El campo " + campo + " debe ser verdadero o falso.");
            return t.Value<bool>();
        }

        public static string Error(string mensaje) => JsonConvert.SerializeObject(new { ok = false, error = mensaje });
        public static string Correcto(object datos) => JsonConvert.SerializeObject(new { ok = true, data = datos });
    }

    /// <summary>
    /// Solicitud encolada para el hilo principal de Unity. El estado se cambia sólo con Interlocked,
    /// de modo que una operación vencida nunca se ejecuta y una operación ya iniciada nunca se informa como cancelada.
    /// </summary>
    internal sealed class Trabajo
    {
        const int Pendiente = 0, EnCurso = 1, Terminado = 2, Vencido = 3;
        int estado = Pendiente;
        readonly ManualResetEventSlim hecho = new ManualResetEventSlim(false);
        string respuesta;

        public Trabajo(Solicitud solicitud) { Solicitud = solicitud; }
        public Solicitud Solicitud { get; }
        public bool Vencio => Volatile.Read(ref estado) == Vencido;

        /// <summary>Hilo principal: reclama el trabajo. Devuelve false si ya venció.</summary>
        public bool Reclamar() => Interlocked.CompareExchange(ref estado, EnCurso, Pendiente) == Pendiente;

        /// <summary>Hilo principal: publica la respuesta de un trabajo reclamado.</summary>
        public void Completar(string r)
        {
            respuesta = r;
            if (Interlocked.CompareExchange(ref estado, Terminado, EnCurso) != EnCurso) throw new InvalidOperationException("Trabajo completado sin reclamar.");
            hecho.Set();
        }

        /// <summary>
        /// Hilo del canal: espera la respuesta. Si vence antes de que el juego la reclame, se marca como vencida
        /// y no se ejecutará. Si el juego ya la está ejecutando, se espera su resultado real.
        /// </summary>
        public string Esperar(int espera, int esperaExtra)
        {
            if (hecho.Wait(espera)) return respuesta;
            if (Interlocked.CompareExchange(ref estado, Vencido, Pendiente) == Pendiente)
                return Protocolo.Error("El juego no responde; la operación se canceló sin aplicarse.");
            if (hecho.Wait(esperaExtra)) return respuesta;
            return Protocolo.Error("El juego tardó demasiado en confirmar la operación; su resultado es desconocido. Actualizá el estado antes de reintentar.");
        }
    }

    /// <summary>
    /// Respaldos de "Cambios": como máximo uno por intervalo, como máximo <c>maximo</c> archivos,
    /// y toda la escritura de disco en un hilo propio (nunca en el hilo principal del juego).
    /// </summary>
    internal sealed class Respaldos : IDisposable
    {
        readonly string carpeta;
        readonly int maximo;
        readonly TimeSpan intervalo;
        readonly Action<string> registrar;
        readonly BlockingCollection<Tuple<string, object>> cola = new BlockingCollection<Tuple<string, object>>(4);
        readonly Thread hilo;
        DateTime ultimo = DateTime.MinValue;

        public Respaldos(string carpeta, int maximo, TimeSpan intervalo, Action<string> registrar)
        {
            this.carpeta = carpeta; this.maximo = maximo; this.intervalo = intervalo; this.registrar = registrar;
            hilo = new Thread(Bucle) { IsBackground = true, Name = "YandereUltimate.Respaldos" };
            hilo.Start();
        }

        /// <summary>Hilo principal: decide si corresponde un respaldo y, si es así, encola el contenido (sin tocar el disco).</summary>
        public bool Encolar(DateTime ahoraUtc, string operacion, string id, Func<object> contenido)
        {
            if (ahoraUtc - ultimo < intervalo || cola.IsAddingCompleted) return false;
            ultimo = ahoraUtc;
            var datos = new { operation = operacion, id, utc = ahoraUtc.ToString("o"), before = contenido() };
            try { return cola.TryAdd(Tuple.Create(ahoraUtc.ToString("yyyyMMdd-HHmmss-fff") + ".json", (object)datos)); }
            catch (InvalidOperationException) { return false; }
        }

        /// <summary>Espera a que se escriban los respaldos pendientes (para pruebas y cierre ordenado).</summary>
        public bool Vaciar(int esperaMs)
        {
            var limite = DateTime.UtcNow.AddMilliseconds(esperaMs);
            while (DateTime.UtcNow < limite) { if (cola.Count == 0 && Volatile.Read(ref escribiendo) == 0) return true; Thread.Sleep(10); }
            return false;
        }
        int escribiendo;

        void Bucle()
        {
            foreach (var item in cola.GetConsumingEnumerable())
            {
                Interlocked.Exchange(ref escribiendo, 1);
                try
                {
                    Directory.CreateDirectory(carpeta);
                    var destino = Path.Combine(carpeta, item.Item1);
                    var temporal = destino + ".tmp";
                    File.WriteAllText(temporal, JsonConvert.SerializeObject(item.Item2));
                    if (File.Exists(destino)) File.Delete(destino);
                    File.Move(temporal, destino);
                    Podar();
                }
                catch (Exception e) { registrar?.Invoke("No se pudo escribir el respaldo de cambios: " + e.Message); }
                finally { Interlocked.Exchange(ref escribiendo, 0); }
            }
        }

        void Podar()
        {
            var sobrantes = new DirectoryInfo(carpeta).GetFiles("*.json").OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(maximo).ToArray();
            foreach (var f in sobrantes)
                try { f.Delete(); } catch (Exception e) { registrar?.Invoke("No se pudo borrar un respaldo antiguo: " + e.Message); }
        }

        public void Dispose() { try { cola.CompleteAdding(); } catch (ObjectDisposedException) { } }
    }
}
