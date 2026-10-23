using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ColonyChronicle
{
    public class CombatLog
    {
        private readonly Dictionary<Battle, string> battleNames = new Dictionary<Battle, string>();

        public int Poll(ChronicleWriter writer, int lastLogId, int now, Func<Pawn, bool> isOurs)
        {
            BattleLog log = Find.BattleLog;
            if (log == null) return lastLogId;

            var fresh = new List<KeyValuePair<LogEntry, Battle>>();
            int maxId = lastLogId;
            foreach (Battle battle in log.Battles)
            {
                foreach (LogEntry entry in battle.Entries)
                {
                    if (entry.LogID <= lastLogId) continue;
                    fresh.Add(new KeyValuePair<LogEntry, Battle>(entry, battle));
                    if (entry.LogID > maxId) maxId = entry.LogID;
                }
            }
            fresh.Sort((a, b) => a.Key.LogID.CompareTo(b.Key.LogID));

            foreach (var kv in fresh)
            {
                try
                {
                    Write(writer, kv.Key, kv.Value, now, isOurs);
                }
                catch (Exception e)
                {
                    if (Prefs.DevMode) Log.Warning($"[Rimlore] Skipped combat log entry {kv.Key.LogID}: {e.Message}");
                }
            }

            WriteMerges(writer, now);
            var live = new HashSet<Battle>(log.Battles);
            foreach (Battle gone in battleNames.Keys.Where(b => !live.Contains(b)).ToList()) battleNames.Remove(gone);
            return maxId;
        }

        private void Write(ChronicleWriter writer, LogEntry entry, Battle battle, int now, Func<Pawn, bool> isOurs)
        {
            var pawns = entry.GetConcerns().OfType<Pawn>().Distinct().ToList();
            Pawn pov = pawns.FirstOrDefault(isOurs);
            if (pov == null) return;

            string text = RichText.Strip(entry.ToGameStringFromPOV(pov, false));
            if (string.IsNullOrWhiteSpace(text)) return;

            string kind = "miss";
            string by = null;
            foreach (Pawn p in pawns)
            {
                Texture2D icon = entry.IconFromPOV(p);
                if (icon == null) continue;
                if (icon == LogEntry.Blood || icon == LogEntry.BloodTarget) kind = "hit";
                else if (icon == LogEntry.Downed || icon == LogEntry.DownedTarget) kind = "down";
                else if (icon == LogEntry.Skull || icon == LogEntry.SkullTarget) kind = "death";
                else if (kind == "miss") kind = "other";
                if (icon == LogEntry.BloodTarget || icon == LogEntry.DownedTarget || icon == LogEntry.SkullTarget) by = p.ThingID;
            }

            string battleId = battle.GetUniqueLoadID();
            string name = RichText.Strip(battle.GetName());
            if (!battleNames.TryGetValue(battle, out string known) || known != name)
            {
                battleNames[battle] = name;
                writer.Write(new Dictionary<string, object>
                {
                    ["t"] = "battle",
                    ["id"] = battleId,
                    ["name"] = name,
                    ["tick"] = now,
                });
            }

            int tick = now - entry.Age;
            writer.Write(new Dictionary<string, object>
            {
                ["t"] = "combat",
                ["battle"] = battleId,
                ["log"] = entry.LogID,
                ["tick"] = tick,
                ["date"] = Dates.At(tick),
                ["kind"] = kind,
                ["by"] = by,
                ["text"] = text,
                ["pawns"] = pawns.Select(p => p.ThingID).ToList(),
                ["names"] = pawns.Select(p => p.LabelShort).ToList(),
                ["animals"] = pawns.Where(p => p.RaceProps.Animal).Select(p => p.ThingID).ToList(),
            });
        }

        private void WriteMerges(ChronicleWriter writer, int now)
        {
            foreach (Battle battle in battleNames.Keys.Where(b => b.AbsorbedBy != null).ToList())
            {
                writer.Write(new Dictionary<string, object>
                {
                    ["t"] = "battle",
                    ["id"] = battle.GetUniqueLoadID(),
                    ["into"] = battle.AbsorbedBy.GetUniqueLoadID(),
                    ["tick"] = now,
                });
                battleNames.Remove(battle);
            }
        }
    }
}
