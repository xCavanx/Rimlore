using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyChronicle
{
    public class MainTabWindow_Chronicle : MainTabWindow
    {
        private enum Filter { All, Lives, Fights, Faith, Letters, Messages, Tales, Notes, Selected }

        private static readonly HashSet<string> CombatTales = new HashSet<string>
        {
            "Wounded", "Downed", "Hunted", "SocialFight", "DefeatedHostileFactionLeader", "CaravanAmbushedByHumanlike",
            "CaravanAmbushDefeated", "CaravanAssaultSuccessful", "CaravanFled",
        };

        private static bool IsCombatTale(ChronicleEntry e) =>
            e.category == "tale" && e.subtype != null && (CombatTales.Contains(e.subtype) || e.subtype.StartsWith("Killed"));

        private static readonly Dictionary<Filter, HashSet<string>> FilterCategories = new Dictionary<Filter, HashSet<string>>
        {
            [Filter.Lives] = new HashSet<string> { "colony", "founded", "joined", "born", "tamed", "captured", "recruited", "died", "left", "status" },
            [Filter.Faith] = new HashSet<string> { "faith" },
            [Filter.Letters] = new HashSet<string> { "letter" },
            [Filter.Messages] = new HashSet<string> { "message" },
            [Filter.Tales] = new HashSet<string> { "tale" },
            [Filter.Notes] = new HashSet<string> { "note" },
        };

        private const float RowHeight = 26f;
        private Filter filter = Filter.All;
        private string search = "";
        private bool newestFirst = true;
        private Vector2 scroll;

        public override Vector2 RequestedTabSize => new Vector2(1010f, 640f);

        public override void DoWindowContents(Rect inRect)
        {
            ChronicleComponent comp = ChronicleComponent.Instance;
            if (comp == null) return;
            comp.EnsureInitialized();

            var bar = new Rect(inRect.x, inRect.y, inRect.width, 30f);
            float x = bar.x;
            Text.Font = GameFont.Small;

            if (Widgets.ButtonText(new Rect(x, bar.y, 180f, 30f), "Show: " + FilterLabel(filter)))
            {
                Find.WindowStack.Add(new FloatMenu(System.Enum.GetValues(typeof(Filter)).Cast<Filter>()
                    .Select(f => new FloatMenuOption(FilterLabel(f), () => { filter = f; scroll = Vector2.zero; }))
                    .ToList()));
            }
            x += 188f;

            if (Widgets.ButtonText(new Rect(x, bar.y, 120f, 30f), newestFirst ? "Newest first" : "Oldest first"))
                newestFirst = !newestFirst;
            x += 128f;

            search = Widgets.TextField(new Rect(x, bar.y + 2f, 200f, 26f), search);
            if (search.NullOrEmpty())
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(x + 6f, bar.y + 5f, 190f, 26f), "Search...");
                GUI.color = Color.white;
            }
            x += 208f;

            float right = bar.xMax;
            if (Widgets.ButtonText(new Rect(right - 130f, bar.y, 130f, 30f), "Open viewer"))
                comp.Writer?.OpenViewer();
            TooltipHandler.TipRegion(new Rect(right - 130f, bar.y, 130f, 30f),
                "Open your colony's full history (timeline, pawn biographies, animal history) in your web browser.\n\nFolder: " + comp.Writer?.Folder);
            if (Widgets.ButtonText(new Rect(right - 268f, bar.y, 130f, 30f), "Add note"))
                Find.WindowStack.Add(new Dialog_AddNote(Find.Selector.SelectedPawns.ToList()));

            var visible = Visible(comp.Entries);
            var listRect = new Rect(inRect.x, bar.yMax + 8f, inRect.width, inRect.height - bar.height - 8f - 22f);
            var viewRect = new Rect(0f, 0f, listRect.width - 16f, visible.Count * RowHeight);

            Widgets.BeginScrollView(listRect, ref scroll, viewRect);
            int first = Mathf.Max(0, (int)(scroll.y / RowHeight));
            int last = Mathf.Min(visible.Count, first + (int)(listRect.height / RowHeight) + 2);
            for (int i = first; i < last; i++)
                DrawRow(new Rect(0f, i * RowHeight, viewRect.width, RowHeight), visible[i], i);
            Widgets.EndScrollView();

            GUI.color = Color.gray;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inRect.x, inRect.yMax - 18f, inRect.width, 18f),
                (ChronicleMod.Version.NullOrEmpty() ? "" : $"Rimlore v{ChronicleMod.Version} · ")
                + $"{visible.Count} of {comp.Entries.Count} entries. Hover a row for details, click to jump to the pawn. "
                + "The save keeps the newest messages and tales; the viewer keeps everything.");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private static string FilterLabel(Filter f)
        {
            switch (f)
            {
                case Filter.Lives: return "Arrivals & departures";
                case Filter.Selected: return "Selected pawns";
                default: return f.ToString();
            }
        }

        private List<ChronicleEntry> Visible(List<ChronicleEntry> all)
        {
            IEnumerable<ChronicleEntry> q = all;
            if (filter == Filter.Selected)
            {
                var ids = new HashSet<string>(Find.Selector.SelectedPawns.Select(p => p.ThingID));
                q = q.Where(e => e.pawnIds.Any(ids.Contains));
            }
            else if (filter == Filter.Fights)
            {
                q = q.Where(IsCombatTale);
            }
            else if (filter == Filter.Tales)
            {
                q = q.Where(e => e.category == "tale" && !IsCombatTale(e));
            }
            else if (filter != Filter.All)
            {
                q = q.Where(e => FilterCategories[filter].Contains(e.category));
            }
            if (!search.NullOrEmpty())
            {
                string s = search.ToLowerInvariant();
                q = q.Where(e => (e.label?.ToLowerInvariant().Contains(s) ?? false)
                              || (e.text?.ToLowerInvariant().Contains(s) ?? false)
                              || e.pawnNames.Any(n => n.ToLowerInvariant().Contains(s)));
            }
            var list = q.ToList();
            if (newestFirst) list.Reverse();
            return list;
        }

        private void DrawRow(Rect row, ChronicleEntry e, int index)
        {
            if (index % 2 == 1) Widgets.DrawLightHighlight(row);
            Widgets.DrawHighlightIfMouseover(row);

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(row.x + 4f, row.y, 210f, row.height), e.date);

            GUI.color = CategoryColor(e);
            Widgets.Label(new Rect(row.x + 218f, row.y, 90f, row.height), CategoryLabel(e));

            GUI.color = Color.white;
            Widgets.Label(new Rect(row.x + 312f, row.y, row.width - 316f, row.height), RowLabel(e).Truncate(row.width - 316f));
            Text.Anchor = TextAnchor.UpperLeft;

            string tip = e.label;
            if (!e.text.NullOrEmpty() && e.text != e.label) tip += "\n\n" + e.text;
            if (e.pawnNames.Count > 0) tip += "\n\nInvolves: " + string.Join(", ", e.pawnNames);
            TooltipHandler.TipRegion(row, tip);

            if (Widgets.ButtonInvisible(row)) JumpTo(e);
        }

        private static string RowLabel(ChronicleEntry e)
        {
            var missing = e.pawnNames.Where(n => !n.NullOrEmpty() && !(e.label ?? "").Contains(n)).Distinct().ToList();
            return missing.Count == 0 ? e.label : $"{e.label}  ({string.Join(", ", missing)})";
        }

        private static void JumpTo(ChronicleEntry e)
        {
            foreach (string id in e.pawnIds)
            {
                Pawn p = FindPawn(id);
                if (p == null) continue;
                if (p.Dead && p.Corpse != null && p.Corpse.Spawned) { CameraJumper.TryJumpAndSelect(p.Corpse); return; }
                if (!p.Dead) { CameraJumper.TryJumpAndSelect(p); return; }
            }
        }

        private static Pawn FindPawn(string id)
        {
            foreach (Map map in Find.Maps)
                foreach (Pawn p in map.mapPawns.AllPawns)
                    if (p.ThingID == id) return p;
            foreach (Pawn p in Find.WorldPawns.AllPawnsAliveOrDead)
                if (p.ThingID == id) return p;
            return null;
        }

        private static string CategoryLabel(ChronicleEntry e)
        {
            switch (e.category)
            {
                case "letter": return e.subtype switch
                {
                    "ThreatBig" => "Major threat",
                    "ThreatSmall" => "Threat",
                    "Death" => "Death",
                    "NegativeEvent" => "Bad event",
                    "PositiveEvent" => "Good event",
                    _ => "Letter",
                };
                case "message": return "Message";
                case "faith": return e.subtype == "converted" ? "Converted" : "Faith";
                case "tale": return IsCombatTale(e) ? "Fight" : "Tale";
                case "note": return e.subtype == "player" ? "Your note" : "Note";
                case "joined" when e.subtype == "resurrected": return "Resurrected";
                case "joined" when e.subtype == "returned": case "tamed" when e.subtype == "returned": return "Returned";
                default: return e.category.CapitalizeFirst();
            }
        }

        private static Color CategoryColor(ChronicleEntry e)
        {
            switch (e.category)
            {
                case "died": return new Color(0.9f, 0.35f, 0.35f);
                case "born": case "joined": case "recruited": case "tamed": case "founded": case "colony": return new Color(0.45f, 0.85f, 0.5f);
                case "left": case "captured": case "status": return new Color(0.9f, 0.75f, 0.4f);
                case "note": return new Color(0.55f, 0.75f, 1f);
                case "tale": return IsCombatTale(e) ? new Color(0.95f, 0.45f, 0.4f) : new Color(0.8f, 0.65f, 0.95f);
                case "faith": return new Color(0.95f, 0.8f, 0.45f);
                case "letter":
                    if (e.subtype == "ThreatBig" || e.subtype == "ThreatSmall" || e.subtype == "Death") return new Color(0.95f, 0.45f, 0.4f);
                    if (e.subtype == "PositiveEvent") return new Color(0.45f, 0.85f, 0.5f);
                    return new Color(0.85f, 0.85f, 0.85f);
                default: return Color.gray;
            }
        }
    }

    public class Dialog_AddNote : Window
    {
        private readonly List<Pawn> pawns;
        private string text = "";

        public override Vector2 InitialSize => new Vector2(560f, 380f);

        public Dialog_AddNote(List<Pawn> pawns)
        {
            this.pawns = pawns;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "Add a note to your colony's lore");
            Text.Font = GameFont.Small;

            string about = pawns.Count > 0 ? "About: " + string.Join(", ", pawns.Select(p => p.LabelShort))
                                           : "Tip: select pawns before opening this to attach the note to them.";
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inRect.x, inRect.y + 36f, inRect.width, 24f), about);
            GUI.color = Color.white;

            text = Widgets.TextArea(new Rect(inRect.x, inRect.y + 64f, inRect.width, inRect.height - 64f - 42f), text);

            if (Widgets.ButtonText(new Rect(inRect.xMax - 140f, inRect.yMax - 34f, 140f, 34f), "Save note") && !text.Trim().NullOrEmpty())
            {
                ChronicleComponent.Instance?.AddNote(text.Trim(), pawns);
                Close();
            }
        }
    }
}
