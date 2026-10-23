using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ColonyChronicle
{
    public static class PawnSnapshot
    {
        private static readonly string[] RecordNames =
        {
            "Kills", "KillsHumanlikes", "KillsAnimals", "KillsMechanoids", "DamageTaken", "DamageDealt",
            "TimesInMentalState", "OperationsReceived", "TimeAsColonistOrColonyAnimal", "ThingsConstructed",
            "PlantsHarvested", "MealsCooked", "AnimalsTamed", "PrisonersRecruited", "TimesTendedOther",
        };

        public static string Kind(Pawn p)
        {
            if (p.IsPrisonerOfColony) return "prisoner";
            if (p.IsSlaveOfColony) return "slave";
            if (p.RaceProps.Animal) return "animal";
            if (p.RaceProps.IsMechanoid) return "mech";
            if (p.RaceProps.Humanlike) return "colonist";
            return "other";
        }

        public static bool IdeologyInPlay => ModsConfig.IdeologyActive && Find.IdeoManager != null && !Find.IdeoManager.classicMode;

        public static Dictionary<string, object> Stable(Pawn p)
        {
            var d = new Dictionary<string, object>
            {
                ["id"] = p.ThingID,
                ["name"] = RichText.Strip(p.Name?.ToStringFull ?? p.LabelCap.ToString()),
                ["short"] = RichText.Strip(p.LabelShort),
                ["kind"] = Kind(p),
                ["race"] = p.def.label,
                ["gender"] = p.gender.ToString(),
                ["age"] = p.ageTracker.AgeBiologicalYears,
                ["ageChrono"] = p.ageTracker.AgeChronologicalYears,
            };

            if (p.story != null)
            {
                Try(() => d["childhood"] = RichText.Strip(p.story.Childhood?.TitleCapFor(p.gender).ToString()));
                Try(() => d["adulthood"] = RichText.Strip(p.story.Adulthood?.TitleCapFor(p.gender).ToString()));
                Try(() => d["traits"] = p.story.traits.allTraits.Select(t => RichText.Strip(t.LabelCap.ToString())).ToList());
            }

            if (p.skills != null)
            {
                Try(() => d["skills"] = p.skills.skills.Select(s => (object)new Dictionary<string, object>
                {
                    ["name"] = s.def.skillLabel.CapitalizeFirst(),
                    ["level"] = s.TotallyDisabled ? -1 : s.Level,
                    ["passion"] = s.passion.ToString(),
                }).ToList());
            }

            if (p.RaceProps.Humanlike)
            {
                Try(() =>
                {
                    WorkTags disabled = p.CombinedDisabledWorkTags;
                    d["incapable"] = Enum.GetValues(typeof(WorkTags)).Cast<WorkTags>()
                        .Where(t => t != WorkTags.None && ((int)t & ((int)t - 1)) == 0 && (disabled & t) == t)
                        .Select(t => t.LabelTranslated().CapitalizeFirst())
                        .Distinct()
                        .ToList();
                });
            }

            if (ModsConfig.BiotechActive && p.genes != null)
                Try(() => d["xenotype"] = RichText.Strip(p.genes.XenotypeLabelCap));
            if (IdeologyInPlay && p.Ideo != null)
            {
                Try(() =>
                {
                    d["ideo"] = RichText.Strip(p.Ideo.name);
                    d["ideoId"] = p.Ideo.id;
                    var previous = p.ideo?.PreviousIdeos;
                    if (previous != null && previous.Count > 0)
                        d["prevIdeos"] = previous.Where(i => i != null).Select(i => RichText.Strip(i.name)).ToList();
                });
            }
            if (ModsConfig.RoyaltyActive)
                Try(() => d["title"] = RichText.Strip(p.royalty?.MostSeniorTitle?.def?.LabelCap.ToString()));
            if (p.Faction != null && p.Faction != Faction.OfPlayer)
                d["faction"] = RichText.Strip(p.Faction.Name);

            Try(() => d["hediffs"] = p.health.hediffSet.hediffs
                .Where(h => h.Visible)
                .Select(h => RichText.Strip(h.Part != null ? $"{h.LabelCap} ({h.Part.LabelCap})" : h.LabelCap))
                .Distinct()
                .ToList());

            Try(() =>
            {
                if (p.relations == null) return;
                d["relations"] = p.relations.RelatedPawns
                    .Where(o => o != null && o != p)
                    .Distinct()
                    .SelectMany(o => p.GetRelations(o).Distinct().Select(def => (object)new Dictionary<string, object>
                    {
                        ["rel"] = RichText.Strip(def.GetGenderSpecificLabelCap(o)),
                        ["id"] = o.ThingID,
                        ["name"] = RichText.Strip(o.Name?.ToStringFull ?? o.LabelShort),
                        ["dead"] = o.Dead,
                    }))
                    .Take(60)
                    .ToList();
                d["relAll"] = true;
            });

            if (p.RaceProps.Animal)
            {
                Try(() => d["master"] = RichText.Strip(p.playerSettings?.Master?.LabelShort));
                Try(() =>
                {
                    if (p.training == null) return;
                    d["training"] = DefDatabase<TrainableDef>.AllDefsListForReading
                        .Where(td => p.training.HasLearned(td))
                        .Select(td => RichText.Strip(td.LabelCap.ToString()))
                        .ToList();
                });
            }

            return d;
        }

        public static void AddVolatile(Pawn p, Dictionary<string, object> d, List<Pawn> colonyHumans)
        {
            Try(() => d["health"] = p.health.summaryHealth.SummaryHealthPercent);
            Try(() => { if (p.needs?.mood != null) d["mood"] = p.needs.mood.CurLevelPercentage; });
            if (d.ContainsKey("ideo")) Try(() => d["certainty"] = p.ideo.Certainty);
            d["location"] = Location(p);

            if (p.records != null)
            {
                var rec = new Dictionary<string, object>();
                foreach (string name in RecordNames)
                {
                    RecordDef def = DefDatabase<RecordDef>.GetNamedSilentFail(name);
                    if (def == null) continue;
                    Try(() =>
                    {
                        float v = p.records.GetValue(def);
                        if (v != 0f) rec[def.LabelCap.ToString()] = def.type == RecordType.Time ? v / GenDate.TicksPerDay : v;
                    });
                }
                d["records"] = rec;
            }

            if (p.RaceProps.Humanlike && p.relations != null && colonyHumans != null)
            {
                Try(() => d["opinions"] = colonyHumans
                    .Where(o => o != p)
                    .Select(o => new { o, op = p.relations.OpinionOf(o) })
                    .Where(x => x.op != 0)
                    .Select(x => (object)new Dictionary<string, object>
                    {
                        ["id"] = x.o.ThingID,
                        ["name"] = RichText.Strip(x.o.LabelShort),
                        ["op"] = x.op,
                    }).ToList());
            }
        }

        public static Dictionary<string, object> Ideo(Ideo ideo, bool primary)
        {
            var d = new Dictionary<string, object>
            {
                ["id"] = ideo.id,
                ["name"] = RichText.Strip(ideo.name),
                ["primary"] = primary,
            };
            Try(() => d["member"] = RichText.Strip(ideo.memberName));
            Try(() => d["culture"] = RichText.Strip(ideo.culture?.LabelCap.ToString()));
            Try(() => d["memes"] = ideo.memes.Where(m => m != null).Select(m => RichText.Strip(m.LabelCap.ToString())).ToList());
            Try(() => d["precepts"] = ideo.PreceptsListForReading
                .Where(pr => pr?.def != null && pr.def.visible && pr.def.issue != null)
                .Select(pr => RichText.Strip($"{pr.def.issue.LabelCap}: {pr.def.LabelCap}"))
                .Distinct()
                .ToList());
            Try(() => d["description"] = RichText.Strip(ideo.description));
            return d;
        }

        public static string Location(Pawn p)
        {
            try
            {
                if (p.MapHeld != null) return RichText.Strip(p.MapHeld.Parent?.LabelCap) ?? "map";
                Caravan c = p.GetCaravan();
                if (c != null) return "Caravan: " + RichText.Strip(c.LabelCap);
                if (p.IsWorldPawn()) return "World";
            }
            catch (Exception) { }
            return "Unknown";
        }

        private static void Try(Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                if (Prefs.DevMode) Log.Warning("[Rimlore] snapshot field failed: " + e.Message);
            }
        }
    }
}
