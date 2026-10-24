using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyChronicle
{
    public class ChronicleSettings : ModSettings
    {
        public float snapshotIntervalDays = 1f;
        public int fullSnapshotIntervalDays = 15;
        public bool recordMessages = true;
        public bool recordTales = true;
        public bool recordCombat = true;
        public bool savePortraits = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref snapshotIntervalDays, "snapshotIntervalDays", 1f);
            Scribe_Values.Look(ref fullSnapshotIntervalDays, "fullSnapshotIntervalDays", 15);
            Scribe_Values.Look(ref recordMessages, "recordMessages", true);
            Scribe_Values.Look(ref recordTales, "recordTales", true);
            Scribe_Values.Look(ref recordCombat, "recordCombat", true);
            Scribe_Values.Look(ref savePortraits, "savePortraits", true);
        }
    }

    [StaticConstructorOnStartup]
    public static class TabPlacement
    {
        static TabPlacement()
        {
            MainButtonDef lore = DefDatabase<MainButtonDef>.GetNamedSilentFail("Rimlore");
            MainButtonDef history = DefDatabase<MainButtonDef>.GetNamedSilentFail("History");
            if (lore != null && history != null) lore.order = history.order - 1;
        }
    }

    public class ChronicleMod : Mod
    {
        public static ChronicleSettings Settings;
        public static string RootDir;
        public static string Version;

        public ChronicleMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<ChronicleSettings>();
            RootDir = content.RootDir;
            Version = content.ModMetaData?.ModVersion;
        }

        public override string SettingsCategory() => "Rimlore";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);

            if (!Version.NullOrEmpty())
            {
                GUI.color = Color.gray;
                list.Label("Rimlore v" + Version);
                GUI.color = Color.white;
                list.Gap(6f);
            }

            list.Label($"Check pawns for changes every {Settings.snapshotIntervalDays:0.##} day(s)",
                tooltip: "A pawn snapshot is only written when something meaningful changed (skills, traits, health conditions, relationships...).");
            Settings.snapshotIntervalDays = Mathf.Round(list.Slider(Settings.snapshotIntervalDays, 0.25f, 15f) * 4f) / 4f;

            list.Label($"Write a full snapshot of every pawn every {Settings.fullSnapshotIntervalDays} day(s)",
                tooltip: "Full snapshots also capture slow-moving numbers like kill counts and records.");
            Settings.fullSnapshotIntervalDays = (int)list.Slider(Settings.fullSnapshotIntervalDays, 1f, 60f);

            list.Gap();
            list.CheckboxLabeled("Record messages (the small top-left notices)", ref Settings.recordMessages);
            list.CheckboxLabeled("Record tales (the events art is made about)", ref Settings.recordTales);
            list.CheckboxLabeled("Record combat logs (every hit, miss and downing your pawns are part of)", ref Settings.recordCombat,
                "Shown in each pawn's story and the timeline's Fights filter in the viewer, like the game's own Log tab, but kept forever.");
            list.CheckboxLabeled("Save pawn portraits for the viewer (colonists, prisoners and slaves)", ref Settings.savePortraits,
                "A picture of each pawn is saved next to your history and refreshed about once a day, so their look in the viewer keeps up with new clothes and hair. Pawns who die or leave keep their last portrait.");

            list.Gap();
            list.Label("Rimlore files are kept in: " + ChronicleWriter.BaseFolder);
            list.End();
        }
    }
}
