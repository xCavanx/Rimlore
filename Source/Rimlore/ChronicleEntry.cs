using System.Collections.Generic;
using Verse;

namespace ColonyChronicle
{
    public class ChronicleEntry : IExposable
    {
        public int tick;
        public string category;
        public string subtype;
        public string label;
        public string text;
        public string date;
        public string cause;
        public List<string> pawnIds = new List<string>();
        public List<string> pawnNames = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref category, "category");
            Scribe_Values.Look(ref subtype, "subtype");
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref text, "text");
            Scribe_Values.Look(ref date, "date");
            Scribe_Values.Look(ref cause, "cause");
            Scribe_Collections.Look(ref pawnIds, "pawnIds", LookMode.Value);
            Scribe_Collections.Look(ref pawnNames, "pawnNames", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawnIds ??= new List<string>();
                pawnNames ??= new List<string>();
            }
        }

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>
            {
                ["t"] = "event",
                ["tick"] = tick,
                ["date"] = date,
                ["cat"] = category,
                ["sub"] = subtype,
                ["label"] = label,
                ["text"] = text,
                ["cause"] = cause,
                ["pawns"] = pawnIds,
                ["names"] = pawnNames,
            };
        }
    }
}
