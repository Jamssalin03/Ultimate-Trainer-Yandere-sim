// Archivo compartido por el complemento (net472), la aplicación de escritorio (net8) y las pruebas.
// Sin dependencias: sólo tipos de System. Es la lista blanca del protocolo local.
using System;
using System.Collections.Generic;

namespace YandereUltimate.Comun
{
    public sealed class DefValor
    {
        public DefValor(string id, double min, double max, bool entero) { Id = id; Min = min; Max = max; Entero = entero; }
        public string Id { get; }
        public double Min { get; }
        public double Max { get; }
        public bool Entero { get; }

        /// <summary>Devuelve null si el valor es aceptable; si no, el motivo en español.</summary>
        public string Validar(double valor)
        {
            if (double.IsNaN(valor) || double.IsInfinity(valor)) return "El valor debe ser un número finito.";
            if (valor < Min || valor > Max) return "El valor de " + Id + " debe estar entre " + Min + " y " + Max + ".";
            if (Entero && Math.Abs(valor - Math.Round(valor)) > 1e-9) return "El valor de " + Id + " debe ser un número entero.";
            return null;
        }
    }

    public static class Definiciones
    {
        /// <summary>Versión del protocolo entre la aplicación y el complemento.</summary>
        public const int Protocolo = 2;

        public const string OpSnapshot = "snapshot", OpRestore = "restore", OpSet = "set", OpRestoreValue = "restoreValue", OpToggle = "toggle", OpNpc = "npc";
        public static readonly string[] Operaciones = { OpSnapshot, OpRestore, OpSet, OpRestoreValue, OpToggle, OpNpc };

        /// <summary>Campos permitidos en una solicitud. Cualquier otro se rechaza.</summary>
        public static readonly string[] Campos = { "token", "op", "id", "value", "locked", "enabled", "npc" };

        public static readonly string[] Materias = { "Biology", "Chemistry", "Language", "Physical", "Psychology" };

        static readonly DefValor[] valores =
        {
            new DefValor("reputation", -100, 100, false),
            new DefValor("permanentRep", -100, 100, false),
            new DefValor("study", 0, 10000, true),
            new DefValor("info", 0, 100000, true),
            new DefValor("money", 0, 1000000, false),
            new DefValor("seduction", 0, 5, true),
            new DefValor("enlightenment", 0, 5, true),
            new DefValor("numbness", 0, 5, true),
            new DefValor("sanity", 0, 100, false),
            new DefValor("run", 1, 20, false),
            new DefValor("walk", 0.5, 10, false),
            new DefValor("health", 1, 100, true),
            new DefValor("hour", 7, 17.9, false),
            new DefValor("timeSpeed", 0, 600, false),
            new DefValor("atmosphere", 0, 1, false),
            new DefValor("Biology", 0, 100, true), new DefValor("BiologyGrade", 0, 5, true),
            new DefValor("Chemistry", 0, 100, true), new DefValor("ChemistryGrade", 0, 5, true),
            new DefValor("Language", 0, 100, true), new DefValor("LanguageGrade", 0, 5, true),
            new DefValor("Physical", 0, 100, true), new DefValor("PhysicalGrade", 0, 5, true),
            new DefValor("Psychology", 0, 100, true), new DefValor("PsychologyGrade", 0, 5, true),
        };
        public static IReadOnlyList<DefValor> Valores => valores;

        public static readonly string[] Interruptores =
        {
            "stupid", "vision", "hearing", "witness", "teachers", "council", "senpai", "reports",
            "freezeAll", "freezeStudents", "freezeTeachers", "freezeCouncil", "freezeClock", "sanity", "noPenalty"
        };

        public const string NpcIgnorar = "ignore", NpcAmistad = "friendship", NpcReputacion = "reputation", NpcVelocidad = "speed", NpcADestino = "toDestination", NpcProtagonista = "playerToNpc";
        public static readonly string[] AccionesNpc = { NpcIgnorar, NpcAmistad, NpcReputacion, NpcVelocidad, NpcADestino, NpcProtagonista };

        static readonly DefValor[] valoresNpc =
        {
            new DefValor(NpcAmistad, 0, 100, true),
            new DefValor(NpcReputacion, -100, 100, true),
            new DefValor(NpcVelocidad, 0.1, 8, false),
        };

        /// <summary>Rango aceptado para el identificador de un NPC (StudentID).</summary>
        public const int NpcMinimo = 0, NpcMaximo = 1000;

        public static DefValor Valor(string id)
        {
            foreach (var v in valores) if (v.Id == id) return v;
            return null;
        }

        /// <summary>Definición del valor de una acción de NPC, o null si la acción no lleva valor.</summary>
        public static DefValor ValorNpc(string accion)
        {
            foreach (var v in valoresNpc) if (v.Id == accion) return v;
            return null;
        }

        public static bool Contiene(string[] lista, string valor) => valor != null && Array.IndexOf(lista, valor) >= 0;
    }
}
