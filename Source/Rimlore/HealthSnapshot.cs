using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyChronicle
{
    public static class HealthSnapshot
    {
        public static Dictionary<string, object> Of(Pawn p, float step)
        {
            var d = new Dictionary<string, object>
            {
                ["summary"] = Round(p.health.summaryHealth.SummaryHealthPercent, step),
                ["pain"] = Round(p.health.hediffSet.PainTotal, step),
                ["bleed"] = Round(p.health.hediffSet.BleedRateTotal, step),
            };

            var capacities = new List<object>();
            foreach (PawnCapacityDef def in DefDatabase<PawnCapacityDef>.AllDefsListForReading)
            {
                if (!Shown(def, p)) continue;
                try
                {
                    capacities.Add(new Dictionary<string, object>
                    {
                        ["name"] = RichText.Strip(def.GetLabelFor(p)).CapitalizeFirst(),
                        ["level"] = Round(p.health.capacities.GetLevel(def), step),
                    });
                }
                catch (Exception) { }
            }
            d["capacities"] = capacities;

            var conditions = new List<object>();
            foreach (Hediff h in p.health.hediffSet.hediffs)
            {
                if (!h.Visible) continue;
                try
                {
                    conditions.Add(Condition(h, step));
                }
                catch (Exception) { }
            }
            d["conditions"] = conditions;

            try
            {
                d["parts"] = DamagedParts(p);
            }
            catch (Exception) { }
            return d;
        }

        private static List<object> DamagedParts(Pawn p)
        {
            HediffSet set = p.health.hediffSet;
            var withConditions = new HashSet<BodyPartRecord>(set.hediffs.Where(h => h.Visible && h.Part != null).Select(h => h.Part));
            var parts = new List<object>();
            foreach (BodyPartRecord part in p.RaceProps.body.AllParts)
            {
                bool missing = set.PartIsMissing(part);
                if (missing && part.parent != null && set.PartIsMissing(part.parent)) continue;
                float max = part.def.GetMaxHealth(p);
                float hp = missing ? 0f : set.GetPartHealth(part);
                if (!missing && hp >= max - 0.01f && !withConditions.Contains(part)) continue;
                var entry = new Dictionary<string, object>
                {
                    ["label"] = RichText.Strip(part.LabelCap),
                    ["def"] = part.def.defName,
                    ["hp"] = (float)Math.Round(hp),
                    ["max"] = (float)Math.Round(max),
                };
                if (missing) entry["missing"] = true;
                parts.Add(entry);
            }
            return parts;
        }

        private static Dictionary<string, object> Condition(Hediff h, float step)
        {
            string kind = Kind(h);
            var c = new Dictionary<string, object>
            {
                ["label"] = RichText.Strip(h.LabelBaseCap),
                ["kind"] = kind,
            };
            if (h.Part != null) c["part"] = RichText.Strip(h.Part.LabelCap);
            string note = RichText.Strip(h.LabelInBrackets ?? "");
            if (note.Length > 0 && kind != "disease") c["note"] = note;
            if (h.Bleeding) c["bleeding"] = true;
            if (h.def.tendable && kind != "scar" && kind != "missing") c["tended"] = h.IsTended();
            if (kind == "injury" || kind == "scar" || kind == "missing")
            {
                string from = !h.sourceLabel.NullOrEmpty() ? h.sourceLabel : h.sourceDef?.label;
                if (!from.NullOrEmpty()) c["from"] = RichText.Strip(from);
            }
            if (kind == "disease")
            {
                var immunizable = (h as HediffWithComps)?.TryGetComp<HediffComp_Immunizable>();
                float lethal = h.def.lethalSeverity > 0f ? h.def.lethalSeverity : h.def.maxSeverity;
                if (lethal > 0f) c["severity"] = Round(Math.Min(1f, h.Severity / lethal), step);
                if (immunizable != null) c["immunity"] = Round(immunizable.Immunity, step);
                if (h.def.lethalSeverity > 0f) c["lethal"] = true;
            }
            return c;
        }

        private static string Kind(Hediff h)
        {
            if (h is Hediff_MissingPart) return "missing";
            if (h is Hediff_AddedPart || h is Hediff_Implant || h.def.countsAsAddedPartOrImplant) return "implant";
            if (h is Hediff_Injury) return h.IsPermanent() ? "scar" : "injury";
            if ((h as HediffWithComps)?.TryGetComp<HediffComp_Immunizable>() != null) return "disease";
            if (h is Hediff_Addiction) return "addiction";
            if (h.def.chronic) return "chronic";
            return h.def.isBad ? "condition" : "effect";
        }

        private static bool Shown(PawnCapacityDef def, Pawn p)
        {
            if (p.RaceProps.Humanlike) return def.showOnHumanlikes;
            if (p.RaceProps.Animal) return def.showOnAnimals;
            if (p.RaceProps.IsMechanoid) return def.showOnMechanoids;
            return def.showOnHumanlikes;
        }

        private static float Round(float value, float step) => (float)Math.Round(value / step) * step;
    }
}
