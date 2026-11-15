// Parches Harmony. Se aplican uno por uno (no PatchAll) y sólo si el catálogo confirmó con AccessTools
// que existen el método original y todos los miembros que usa el parche. Si un parche falla, se retira
// lo aplicado de ese grupo y se desactivan las funciones que dependen de él.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace YandereUltimate
{
    internal static class Parches
    {
        static readonly HashSet<string> Testigos = new HashSet<string> { "WitnessMurder", "UpdateWitnessedMurder", "DetermineWhatWasWitnessed", "PersonaReaction", "BecomeAlarmed" };
        static readonly HashSet<string> Reportes = new HashSet<string> { "BecomeAlarmed", "ChaseYandere", "PersonaReaction" };

        public static void Aplicar(Harmony h, Compatibilidad c, Action<string> advertir)
        {
            Grupo(h, c, advertir, "parche.fondo", () => new[] { (Original("YandereScript", "Start"), (HarmonyMethod)null, Hm(typeof(ParcheFondo), "Postfix"), (HarmonyMethod)null) });
            Grupo(h, c, advertir, "parche.proximidad", () => new[] { (Original("StudentScript", "Update"), Hm(typeof(ParcheProximidad), "Prefix"), (HarmonyMethod)null, Hm(typeof(ParcheProximidad), "Finalizer")) });
            Grupo(h, c, advertir, "parche.compra", () => new[] { (Original("ServicesScript", "Purchase"), Hm(typeof(ParcheCompra), "Prefix"), Hm(typeof(ParcheCompra), "Postfix"), (HarmonyMethod)null) });
            Grupo(h, c, advertir, "parche.vision", () =>
                AccessTools.GetDeclaredMethods(AccessTools.TypeByName("StudentScript")).Where(m => m.Name == "CanSeeObject" && m.ReturnType == typeof(bool))
                    .Select(m => ((MethodBase)m, Hm(typeof(ParcheVision), "Prefix"), (HarmonyMethod)null, (HarmonyMethod)null)).ToArray());
            Grupo(h, c, advertir, "parche.reacciones", () =>
                Comun.Catalogo.Parches().Select(n => (Original("StudentScript", n), Hm(typeof(ParcheReacciones), "Prefix"), (HarmonyMethod)null, (HarmonyMethod)null)).ToArray());
            Grupo(h, c, advertir, "parche.audicion", () => new[]
            {
                (Original("AlarmDiscScript", "OnTriggerEnter"), Hm(typeof(ParcheAudicion), "Prefix"), (HarmonyMethod)null, (HarmonyMethod)null),
                (Original("GiggleScript", "OnTriggerEnter"), Hm(typeof(ParcheAudicion), "Prefix"), (HarmonyMethod)null, (HarmonyMethod)null),
            });
        }

        static MethodBase Original(string tipo, string metodo) =>
            AccessTools.Method(AccessTools.TypeByName(tipo) ?? throw new MissingMemberException(tipo), metodo) ?? throw new MissingMemberException(tipo, metodo);
        static HarmonyMethod Hm(Type t, string metodo) => new HarmonyMethod(AccessTools.Method(t, metodo));

        static void Grupo(Harmony h, Compatibilidad c, Action<string> advertir, string funcion, Func<(MethodBase Original, HarmonyMethod Prefijo, HarmonyMethod Postfijo, HarmonyMethod Finalizador)[]> objetivos)
        {
            if (!c.Disponible(funcion)) return;
            var aplicados = new List<(MethodBase, HarmonyMethod[])>();
            try
            {
                var lista = objetivos();
                if (lista.Length == 0) throw new MissingMemberException("No hay métodos que parchear.");
                foreach (var o in lista)
                {
                    h.Patch(o.Original, prefix: o.Prefijo, postfix: o.Postfijo, finalizer: o.Finalizador);
                    aplicados.Add((o.Original, new[] { o.Prefijo, o.Postfijo, o.Finalizador }));
                }
            }
            catch (Exception e)
            {
                foreach (var (original, metodos) in aplicados)
                    foreach (var m in metodos.Where(x => x != null))
                        try { h.Unpatch(original, m.method); } catch { /* se informa abajo */ }
                var motivo = "No se pudo aplicar el parche: " + (e.InnerException ?? e).Message;
                c.Desactivar(funcion, motivo);
                advertir(funcion + ": " + motivo);
            }
        }

        internal static bool EsTestigo(string metodo) => Testigos.Contains(metodo);
        internal static bool EsReporte(string metodo) => Reportes.Contains(metodo);
    }

    static class ParcheFondo
    {
        // YandereScript.Start desactiva runInBackground; se reactiva una sola vez tras ese Start.
        static void Postfix() { Application.runInBackground = true; }
    }

    static class ParcheProximidad
    {
        static void Prefix(StudentScript __instance, ref bool? __state)
        {
            var p = Plugin.Instance;
            if (p != null && Juego.Ignora(__instance) && __instance.Yandere != null) { __state = __instance.Yandere.Invisible; __instance.Yandere.Invisible = true; }
        }
        static Exception Finalizer(StudentScript __instance, bool? __state, Exception __exception)
        {
            if (__state.HasValue && __instance.Yandere != null) __instance.Yandere.Invisible = __state.Value;
            return __exception;
        }
    }

    static class ParcheCompra
    {
        static void Prefix(ServicesScript __instance, ref int? __state)
        {
            if (Plugin.Instance != null && Plugin.Instance.EstaBloqueado("info")) __state = __instance.Inventory.PantyShots;
        }
        static void Postfix(ServicesScript __instance, int? __state)
        {
            if (!__state.HasValue) return;
            __instance.Inventory.PantyShots = Math.Max(__state.Value, __instance.Inventory.PantyShots);
            PlayerGlobals.PantyShots = __instance.Inventory.PantyShots;
            __instance.UpdatePantyCount();
        }
    }

    static class ParcheVision
    {
        static bool Prefix(StudentScript __instance, ref bool __result)
        {
            var p = Plugin.Instance;
            if (p != null && (p.Activo("vision") || Juego.Ignora(__instance))) { __result = false; return false; }
            return true;
        }
    }

    static class ParcheReacciones
    {
        static bool Prefix(StudentScript __instance, MethodBase __originalMethod)
        {
            var p = Plugin.Instance;
            if (p == null) return true;
            if (Juego.Ignora(__instance)) return false;
            if (p.Activo("witness") && Parches.EsTestigo(__originalMethod.Name)) return false;
            if (p.Activo("reports") && Parches.EsReporte(__originalMethod.Name)) return false;
            return true;
        }
    }

    static class ParcheAudicion
    {
        static bool Prefix(Collider other)
        {
            var p = Plugin.Instance;
            if (p == null) return true;
            if (p.Activo("hearing") || p.Activo("stupid")) return false;
            var n = other != null ? other.GetComponent<StudentScript>() : null;
            return n == null || !Juego.Ignora(n);
        }
    }
}
