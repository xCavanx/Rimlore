using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyChronicle
{
    public static class DeathCause
    {
        private const int RecentWoundTicks = 2 * GenDate.TicksPerDay;
        private const int CombatWindowTicks = GenDate.TicksPerDay;

        public static string FatalCondition(Pawn p)
        {
            try
            {
                Hediff fatal = p.health.hediffSet.hediffs.FirstOrDefault(h => h.CauseDeathNow());
                return fatal == null ? null : RichText.Strip(fatal.def.label);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static List<Hediff> RecentWounds(Pawn p)
        {
            try
            {
                return p.health.hediffSet.hediffs
                    .Where(h => (h is Hediff_Injury || h is Hediff_MissingPart) && h.ageTicks <= RecentWoundTicks
                             && (h.combatLogEntry?.Target != null || !h.combatLogText.NullOrEmpty() || h.sourceDef != null))
                    .OrderBy(h => h.ageTicks)
                    .ToList();
            }
            catch (Exception)
            {
                return new List<Hediff>();
            }
        }

        public static Pawn Attacker(Hediff wound, Pawn victim)
        {
            try
            {
                return wound.combatLogEntry?.Target?.GetConcerns().OfType<Pawn>().FirstOrDefault(x => x != victim);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool WasHurtBy(Pawn victim, Pawn attacker)
        {
            return RecentWounds(victim).Any(w => Attacker(w, victim) == attacker
                || (w.combatLogEntry?.Target == null && w.sourceDef != null && w.sourceDef == attacker.def));
        }

        public static List<string> Attackers(Pawn victim, List<Hediff> wounds)
        {
            var names = new List<string>();
            foreach (Hediff w in wounds)
            {
                Pawn a = Attacker(w, victim);
                string name = a != null ? Name(a)
                            : w.combatLogEntry?.Target == null && w.sourceDef?.race != null ? Indefinite(w.sourceDef.label)
                            : null;
                if (name != null && !names.Contains(name)) names.Add(name);
            }
            return names.Take(3).ToList();
        }

        public static List<string> Blows(List<Hediff> wounds)
        {
            var blows = new List<string>();
            foreach (Hediff w in wounds)
            {
                string text = null;
                try { text = w.combatLogText; } catch (Exception) { }
                text = RichText.Strip(text ?? "").Trim();
                if (text.Length > 0 && !blows.Contains(text)) blows.Add(text);
            }
            return blows.Take(3).ToList();
        }

        public static string Weapon(List<Hediff> wounds)
        {
            ThingDef weapon = wounds.Select(w => w.sourceDef).FirstOrDefault(d => d != null && d.race == null);
            return weapon == null ? null : Indefinite(weapon.label);
        }

        public static string LastBlow(Pawn p)
        {
            try
            {
                LogEntry best = null;
                foreach (Battle battle in Find.BattleLog.Battles)
                    foreach (LogEntry entry in battle.Entries)
                        if (entry is LogEntry_DamageResult && entry.Age <= CombatWindowTicks && entry.Concerns(p) && (best == null || entry.Age < best.Age))
                            best = entry;
                if (best == null) return null;
                string text = RichText.Strip(best.ToGameStringFromPOV(p, false));
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string Name(Pawn p)
        {
            if (p.RaceProps.Humanlike || (p.Name != null && !p.Name.Numerical)) return p.LabelShort;
            return Indefinite(p.KindLabel);
        }

        public static string Indefinite(string label)
        {
            if (label.NullOrEmpty()) return label;
            try { return Find.ActiveLanguageWorker.WithIndefiniteArticle(label, false, false); }
            catch (Exception) { return label; }
        }

        public static string JoinNames(List<string> names)
        {
            if (names.Count <= 1) return names.FirstOrDefault();
            return string.Join(", ", names.Take(names.Count - 1)) + " and " + names.Last();
        }
    }
}
