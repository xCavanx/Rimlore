using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ColonyChronicle
{
    public class ChronicleComponent : GameComponent
    {
        private const int PollInterval = 250;
        private const int MaxStoredTextLength = 1200;
        private const int MaxStoredMessages = 1500;
        private const int MaxStoredTales = 1500;

        private string colonyId;
        private string folderName;
        private int lastArchiveTick = -1;
        private List<string> archiveSeenKeys = new List<string>();
        private int lastTaleId = -1;
        private int lastLogId = -1;
        private int lastSnapshotTick = -1;
        private int lastFullSnapshotTick = -1;
        private string lastColonyName;
        private List<ChronicleEntry> entries = new List<ChronicleEntry>();

        private ChronicleWriter writer;
        private bool initialized;
        private bool startedNewGame;
        private bool attachedToExistingSave;
        private readonly Dictionary<string, Pawn> tracked = new Dictionary<string, Pawn>();
        private readonly Dictionary<string, string> trackedKind = new Dictionary<string, string>();
        private readonly Dictionary<string, string> lastSignature = new Dictionary<string, string>();
        private readonly Dictionary<string, Ideo> trackedIdeo = new Dictionary<string, Ideo>();
        private readonly Dictionary<int, string> ideoSignature = new Dictionary<int, string>();
        private readonly Dictionary<int, Dictionary<string, object>> ideoLast = new Dictionary<int, Dictionary<string, object>>();
        private int primaryIdeoId = -1;
        private readonly Dictionary<string, ChronicleEntry> pendingDeaths = new Dictionary<string, ChronicleEntry>();
        private readonly CombatLog combatLog = new CombatLog();

        private static readonly HashSet<string> IgnoredMessageTypes = new HashSet<string> { "RejectInput", "SilentInput", "CautionInput" };

        public static ChronicleComponent Instance => Current.Game?.GetComponent<ChronicleComponent>();
        public List<ChronicleEntry> Entries => entries;
        public ChronicleWriter Writer => writer;

        public ChronicleComponent(Game game) { }

        public override void StartedNewGame() => startedNewGame = true;

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                CommitPendingDeaths(int.MaxValue);
                writer?.Flush();
                TrimStoredEntries();
            }

            Scribe_Values.Look(ref colonyId, "colonyId");
            Scribe_Values.Look(ref folderName, "folderName");
            Scribe_Values.Look(ref lastArchiveTick, "lastArchiveTick", -1);
            Scribe_Collections.Look(ref archiveSeenKeys, "archiveSeenKeys", LookMode.Value);
            Scribe_Values.Look(ref lastTaleId, "lastTaleId", -1);
            Scribe_Values.Look(ref lastLogId, "lastLogId", -1);
            Scribe_Values.Look(ref lastSnapshotTick, "lastSnapshotTick", -1);
            Scribe_Values.Look(ref lastFullSnapshotTick, "lastFullSnapshotTick", -1);
            Scribe_Values.Look(ref lastColonyName, "lastColonyName");
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                archiveSeenKeys ??= new List<string>();
                entries ??= new List<ChronicleEntry>();
                MergeStoredDeathLetters();
            }
        }

        private void MergeStoredDeathLetters()
        {
            var letters = entries.Where(e => e.category == "letter" && e.subtype == "Death" && e.pawnIds.Count > 0).ToList();
            foreach (ChronicleEntry letter in letters)
            {
                ChronicleEntry died = entries.FirstOrDefault(e => e.category == "died"
                    && Math.Abs(e.tick - letter.tick) <= GenDate.TicksPerDay
                    && e.pawnIds.Any(letter.pawnIds.Contains));
                if (died == null) continue;
                if (!letter.text.NullOrEmpty() && (died.text == null || !died.text.Contains(letter.text)))
                    died.text = (died.text.NullOrEmpty() ? "" : died.text + "\n\n") + letter.text;
                entries.Remove(letter);
            }
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (!initialized) Initialize();
            if (now % PollInterval != 0) return;
            try
            {
                Poll(now);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Rimlore] Poll failed: " + e, 0x43C4A2);
            }
        }

        public void EnsureInitialized()
        {
            if (!initialized) Initialize();
        }

        private void Initialize()
        {
            initialized = true;
            if (colonyId == null)
            {
                colonyId = Guid.NewGuid().ToString("N");
                attachedToExistingSave = !startedNewGame;
            }
            if (folderName == null)
                folderName = Sanitize(Faction.OfPlayer?.Name ?? "Colony") + "_" + colonyId.Substring(0, 8);

            writer = new ChronicleWriter(folderName);
            lastColonyName ??= Faction.OfPlayer?.Name;

            int now = Find.TickManager.TicksGame;
            string kind = startedNewGame ? "new" : attachedToExistingSave ? "attached" : "loaded";
            writer.Write(new Dictionary<string, object>
            {
                ["t"] = "session",
                ["kind"] = kind,
                ["tick"] = now,
                ["date"] = Dates.At(now),
                ["colony"] = Faction.OfPlayer?.Name,
                ["colonyId"] = colonyId,
                ["game"] = VersionControl.CurrentVersionStringWithRev,
                ["mod"] = ChronicleMod.Version,
                ["realTime"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            });

            foreach (Pawn p in ColonyPawns())
            {
                tracked[p.ThingID] = p;
                trackedKind[p.ThingID] = PawnSnapshot.Kind(p);
                if (PawnSnapshot.IdeologyInPlay && p.Ideo != null) trackedIdeo[p.ThingID] = p.Ideo;
            }

            if (startedNewGame)
            {
                var founders = tracked.Values.ToList();
                AddEntry(new ChronicleEntry
                {
                    tick = now,
                    category = "founded",
                    label = $"{Faction.OfPlayer?.Name ?? "The colony"} was founded",
                    text = "Founding members: " + string.Join(", ", founders.Select(Describe)),
                    pawnIds = founders.Select(p => p.ThingID).ToList(),
                    pawnNames = founders.Select(p => p.LabelShort).ToList(),
                });
            }
            else if (attachedToExistingSave)
            {
                AddEntry(new ChronicleEntry
                {
                    tick = now,
                    category = "note",
                    label = "Rimlore started recording",
                    text = "Earlier letters, messages and tales the game still remembered have been imported. "
                         + $"Current members: {tracked.Count}.",
                });
            }

            PollArchive();
            if (ChronicleMod.Settings.recordTales) PollTales();
            PollCombat(now);

            Snapshot(now, full: true);
            PollIdeos(now, force: true);
            writer.Flush();
        }

        private void Poll(int now)
        {
            CheckColonyName(now);
            ScanPawns(now);
            PollArchive();
            CommitPendingDeaths(now);
            if (ChronicleMod.Settings.recordTales) PollTales();
            PollCombat(now);

            int fullInterval = Math.Max(1, ChronicleMod.Settings.fullSnapshotIntervalDays) * GenDate.TicksPerDay;
            int interval = Math.Max(PollInterval, (int)(ChronicleMod.Settings.snapshotIntervalDays * GenDate.TicksPerDay));
            bool full = now - lastFullSnapshotTick >= fullInterval;
            if (full) Snapshot(now, full: true);
            else if (now - lastSnapshotTick >= interval) Snapshot(now, full: false);
            if (full || now % GenDate.TicksPerHour == 0) PollIdeos(now, force: full);

            writer.Flush();
        }

        private void CheckColonyName(int now)
        {
            string name = Faction.OfPlayer?.Name;
            if (name.NullOrEmpty() || name == lastColonyName) return;
            string previous = lastColonyName;
            lastColonyName = name;
            writer.Write(new Dictionary<string, object>
            {
                ["t"] = "session",
                ["kind"] = "renamed",
                ["tick"] = now,
                ["date"] = Dates.At(now),
                ["colony"] = name,
                ["colonyId"] = colonyId,
            });
            AddEntry(new ChronicleEntry
            {
                tick = now,
                category = "colony",
                label = $"The colony is now known as {name}",
                text = previous.NullOrEmpty() ? null : $"Previously: {previous}",
            });
        }

        private void PollCombat(int now)
        {
            if (!ChronicleMod.Settings.recordCombat) return;
            lastLogId = combatLog.Poll(writer, lastLogId, now, InvolvesColony);
        }

        private bool InvolvesColony(Pawn p)
        {
            return p != null && (p.Faction == Faction.OfPlayer || p.IsPrisonerOfColony || p.IsSlaveOfColony || tracked.ContainsKey(p.ThingID));
        }

        private static bool BelongsToColony(Pawn p)
        {
            return p != null && !p.Dead && !p.Destroyed
                && (p.Faction == Faction.OfPlayer || p.IsPrisonerOfColony || p.IsSlaveOfColony);
        }

        private static IEnumerable<Pawn> ColonyPawns()
        {
            var seen = new HashSet<Pawn>();
            foreach (Map map in Find.Maps)
                foreach (Pawn p in map.mapPawns.AllPawns)
                    if (BelongsToColony(p) && seen.Add(p)) yield return p;

            foreach (Caravan caravan in Find.WorldObjects.Caravans)
                foreach (Pawn p in caravan.PawnsListForReading)
                    if (BelongsToColony(p) && seen.Add(p)) yield return p;
        }

        private void ScanPawns(int now)
        {
            var current = new Dictionary<string, Pawn>();
            foreach (Pawn p in ColonyPawns()) current[p.ThingID] = p;

            foreach (var kv in current)
            {
                Pawn p = kv.Value;
                string kind = PawnSnapshot.Kind(p);
                if (!tracked.ContainsKey(kv.Key))
                {
                    tracked[kv.Key] = p;
                    trackedKind[kv.Key] = kind;
                    if (PawnSnapshot.IdeologyInPlay && p.Ideo != null) trackedIdeo[kv.Key] = p.Ideo;
                    OnArrived(p, kind, now);
                    SnapshotPawn(p, now, full: true, ColonyHumans());
                }
                else if (trackedKind[kv.Key] != kind)
                {
                    OnStatusChanged(p, trackedKind[kv.Key], kind, now);
                    trackedKind[kv.Key] = kind;
                    SnapshotPawn(p, now, full: true, ColonyHumans());
                }
                CheckConversion(p, now);
            }

            foreach (string id in tracked.Keys.Where(id => !current.ContainsKey(id)).ToList())
            {
                Pawn p = tracked[id];
                if (p.Dead)
                {
                    OnDied(p, trackedKind[id], now);
                }
                else if (p.Destroyed || p.Discarded ||
                         (p.Faction != Faction.OfPlayer && !p.IsPrisonerOfColony && !p.IsSlaveOfColony))
                {
                    OnLeft(p, trackedKind[id], now);
                }
                else
                {
                    continue;
                }
                tracked.Remove(id);
                trackedKind.Remove(id);
                lastSignature.Remove(id);
                trackedIdeo.Remove(id);
            }
        }

        private void OnArrived(Pawn p, string kind, int now)
        {
            bool newborn = p.ageTracker.AgeBiologicalTicks < 2 * GenDate.TicksPerDay;
            string category, label;
            string text = Describe(p);

            string history = LastLifeEvent(p.ThingID);
            if (history == "died" && kind != "prisoner")
            {
                AddEntry(MakeEntry(now, "joined", $"{p.LabelShort} was brought back to life", text, p, "resurrected"));
                return;
            }
            if (history == "left" && kind != "prisoner")
            {
                AddEntry(MakeEntry(now, kind == "animal" ? "tamed" : "joined", $"{p.LabelShort} returned to the colony", text, p, "returned"));
                return;
            }

            if (newborn && kind == "mech")
            {
                category = "born";
                label = $"{p.LabelShort} was gestated";
            }
            else if (newborn)
            {
                category = "born";
                Pawn mother = p.GetMother(), father = p.GetFather();
                label = $"{p.LabelShort} was born";
                if (mother != null || father != null)
                    text += ". Parents: " + string.Join(" & ", new[] { mother, father }.Where(x => x != null).Select(x => x.LabelShort));
            }
            else if (kind == "prisoner")
            {
                category = "captured";
                label = $"{p.LabelShort} was taken prisoner";
                if (p.Faction != null) text += $", of {p.Faction.Name}";
            }
            else if (kind == "animal")
            {
                category = "tamed";
                label = $"{p.LabelShort} ({p.def.label}) joined the colony";
            }
            else if (kind == "mech")
            {
                category = "joined";
                label = $"{p.LabelShort} joined the colony";
            }
            else
            {
                category = "joined";
                label = $"{p.LabelShort} joined the colony" + (kind == "slave" ? " as a slave" : "");
            }

            AddEntry(MakeEntry(now, category, label, text, p));
        }

        private void OnStatusChanged(Pawn p, string from, string to, int now)
        {
            string label =
                from == "prisoner" && to == "colonist" ? $"{p.LabelShort} was recruited" :
                from == "prisoner" && to == "slave" ? $"{p.LabelShort} was enslaved" :
                from == "slave" && to == "colonist" ? $"{p.LabelShort} was emancipated" :
                to == "prisoner" ? $"{p.LabelShort} was imprisoned" :
                $"{p.LabelShort} is now a {to} (was {from})";
            string category = from == "prisoner" && to == "colonist" ? "recruited" : "status";
            AddEntry(MakeEntry(now, category, label, Describe(p), p));
        }

        private void OnDied(Pawn p, string kind, int now)
        {
            string condition = DeathCause.FatalCondition(p);
            var wounds = DeathCause.RecentWounds(p);
            string by = DeathCause.JoinNames(DeathCause.Attackers(p, wounds));
            string weapon = by == null ? DeathCause.Weapon(wounds) : null;
            var blows = DeathCause.Blows(wounds);
            if (blows.Count == 0)
            {
                string blow = DeathCause.LastBlow(p);
                if (blow != null) blows.Add(blow);
            }

            string how = by != null ? $"after being attacked by {by}"
                       : weapon != null ? $"after being wounded by {weapon}"
                       : null;
            string label = condition != null ? $"{p.LabelShort} died of {condition}" + (how != null ? " " + how : "")
                         : by != null ? $"{p.LabelShort} was killed by {by}"
                         : weapon != null ? $"{p.LabelShort} was killed by {weapon}"
                         : $"{p.LabelShort} died";

            string text = Describe(p);
            if (blows.Count > 0) text += "\n\nWhat happened:\n" + string.Join("\n", blows);
            try
            {
                var conditions = p.health.hediffSet.hediffs.Where(h => h.Visible).Select(h => RichText.Strip(h.LabelCap)).Distinct().Take(8).ToList();
                if (conditions.Count > 0) text += "\n\nConditions at death: " + string.Join(", ", conditions);
            }
            catch (Exception) { }

            var entry = MakeEntry(now, "died", label, text, p);
            entry.cause = condition != null ? condition + (how != null ? " " + how : "")
                        : by != null ? $"killed by {by}"
                        : weapon != null ? $"killed by {weapon}"
                        : null;
            pendingDeaths[p.ThingID] = entry;
        }

        private void CommitPendingDeaths(int now)
        {
            if (pendingDeaths.Count == 0) return;
            foreach (var kv in pendingDeaths.Where(kv => now - kv.Value.tick >= 2 * PollInterval).ToList())
            {
                AddEntry(kv.Value);
                pendingDeaths.Remove(kv.Key);
            }
        }

        private bool MergeDeathLetter(Letter letter)
        {
            if (letter.def?.defName != "Death" || letter.lookTargets?.targets == null) return false;
            foreach (GlobalTargetInfo target in letter.lookTargets.targets)
            {
                if (!target.HasThing || !(target.Thing is Pawn p)) continue;
                if (pendingDeaths.TryGetValue(p.ThingID, out ChronicleEntry pending))
                {
                    string letterText = Clean(((IArchivable)letter).ArchivedTooltip);
                    if (!letterText.NullOrEmpty()) pending.text += "\n\n" + letterText;
                    return true;
                }
                int since = letter.arrivalTick - GenDate.TicksPerDay;
                if (entries.Any(e => e.category == "died" && e.tick >= since && e.pawnIds.Contains(p.ThingID))) return true;
            }
            return false;
        }

        private void OnLeft(Pawn p, string kind, int now)
        {
            string label = kind == "prisoner" ? $"{p.LabelShort} is no longer a prisoner (released, escaped or traded)"
                                              : $"{p.LabelShort} left the colony";
            string text = Describe(p);
            if (!p.Destroyed && p.Faction != null && p.Faction != Faction.OfPlayer) text += $". Now with {p.Faction.Name}";
            AddEntry(MakeEntry(now, "left", label, text, p));
        }

        private string LastLifeEvent(string pawnId)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                ChronicleEntry e = entries[i];
                switch (e.category)
                {
                    case "died": case "left": case "joined": case "born": case "tamed": case "captured": case "recruited": case "founded":
                        if (e.pawnIds.Contains(pawnId)) return e.category;
                        break;
                }
            }
            return null;
        }

        private static string Describe(Pawn p)
        {
            string who = p.RaceProps.Humanlike ? "" : p.def.label + ", ";
            return $"{p.Name?.ToStringFull ?? p.LabelShort} ({who}{p.gender.GetLabel()}, age {p.ageTracker.AgeBiologicalYears})";
        }

        private ChronicleEntry MakeEntry(int now, string category, string label, string text, Pawn p, string subtype = null)
        {
            return new ChronicleEntry
            {
                tick = now,
                category = category,
                subtype = subtype,
                label = label,
                text = text,
                pawnIds = new List<string> { p.ThingID },
                pawnNames = new List<string> { p.LabelShort },
            };
        }

        private void Snapshot(int now, bool full)
        {
            lastSnapshotTick = now;
            if (full) lastFullSnapshotTick = now;
            List<Pawn> humans = ColonyHumans();
            foreach (Pawn p in tracked.Values)
                if (BelongsToColony(p)) SnapshotPawn(p, now, full, humans);
        }

        private List<Pawn> ColonyHumans() => tracked.Values.Where(p => BelongsToColony(p) && p.RaceProps.Humanlike).ToList();

        private void SnapshotPawn(Pawn p, int now, bool full, List<Pawn> colonyHumans)
        {
            try
            {
                var data = PawnSnapshot.Stable(p);
                string sig = Json.Serialize(data);
                if (!full && lastSignature.TryGetValue(p.ThingID, out string prev) && prev == sig) return;
                lastSignature[p.ThingID] = sig;
                PawnSnapshot.AddVolatile(p, data, colonyHumans);
                writer.Write(new Dictionary<string, object>
                {
                    ["t"] = "snap",
                    ["tick"] = now,
                    ["date"] = Dates.At(now),
                    ["full"] = full,
                    ["pawn"] = data,
                });
            }
            catch (Exception e)
            {
                Log.WarningOnce($"[Rimlore] Couldn't snapshot {p}: {e.Message}", p.thingIDNumber ^ 0x43C4A3);
            }
        }

        private void CheckConversion(Pawn p, int now)
        {
            if (!PawnSnapshot.IdeologyInPlay || !p.RaceProps.Humanlike) return;
            Ideo current = p.Ideo;
            trackedIdeo.TryGetValue(p.ThingID, out Ideo previous);
            if (current == previous) return;
            trackedIdeo[p.ThingID] = current;
            if (previous == null || current == null) return;

            string from = RichText.Strip(previous.name), to = RichText.Strip(current.name);
            var entry = MakeEntry(now, "faith", $"{p.LabelShort} converted from {from} to {to}", Describe(p), p, "converted");
            AddEntry(entry);
            SnapshotPawn(p, now, full: true, ColonyHumans());
        }

        private void PollIdeos(int now, bool force)
        {
            if (!PawnSnapshot.IdeologyInPlay) return;
            FactionIdeosTracker ideos = Faction.OfPlayer?.ideos;
            if (ideos == null) return;

            Ideo primary = ideos.PrimaryIdeo;
            if (primary != null && primary.id != primaryIdeoId)
            {
                if (primaryIdeoId != -1)
                {
                    AddEntry(new ChronicleEntry
                    {
                        tick = now, category = "faith", subtype = "primary",
                        label = $"{RichText.Strip(primary.name)} is now the colony's primary ideoligion",
                    });
                }
                primaryIdeoId = primary.id;
            }

            foreach (Ideo ideo in ideos.AllIdeos)
            {
                if (ideo == null) continue;
                var data = PawnSnapshot.Ideo(ideo, ideo == primary);
                string sig = Json.Serialize(data);
                bool changed = !ideoSignature.TryGetValue(ideo.id, out string prevSig) || prevSig != sig;
                if (!changed && !force) continue;

                if (changed && ideoLast.TryGetValue(ideo.id, out var before)) RecordIdeoChanges(now, before, data);
                ideoSignature[ideo.id] = sig;
                ideoLast[ideo.id] = data;

                data["followers"] = SafeBelieverCount(ideo);
                writer.Write(new Dictionary<string, object>
                {
                    ["t"] = "ideo",
                    ["tick"] = now,
                    ["date"] = Dates.At(now),
                    ["ideo"] = data,
                });
            }
        }

        private void RecordIdeoChanges(int now, Dictionary<string, object> before, Dictionary<string, object> after)
        {
            string name = (string)after["name"];
            string oldName = (string)before["name"];
            if (oldName != name)
                AddEntry(new ChronicleEntry { tick = now, category = "faith", subtype = "renamed", label = $"{oldName} is now called {name}" });

            List<string> Items(Dictionary<string, object> d, string key) => d.TryGetValue(key, out object v) && v is List<string> l ? l : new List<string>();

            var memesBefore = Items(before, "memes");
            var memesAfter = Items(after, "memes");
            foreach (string meme in memesAfter.Except(memesBefore))
                AddEntry(new ChronicleEntry { tick = now, category = "faith", subtype = "meme", label = $"{name} adopted the {meme} meme" });
            foreach (string meme in memesBefore.Except(memesAfter))
                AddEntry(new ChronicleEntry { tick = now, category = "faith", subtype = "meme", label = $"{name} abandoned the {meme} meme" });

            var preceptsBefore = Items(before, "precepts");
            var preceptsAfter = Items(after, "precepts");
            var added = preceptsAfter.Except(preceptsBefore).ToList();
            var removed = preceptsBefore.Except(preceptsAfter).ToList();
            if (added.Count > 0 || removed.Count > 0)
            {
                string text = string.Join("\n", added.Select(x => "+ " + x).Concat(removed.Select(x => "− " + x)));
                AddEntry(new ChronicleEntry { tick = now, category = "faith", subtype = "reformed", label = $"{name}'s precepts changed", text = text });
            }
        }

        private static int SafeBelieverCount(Ideo ideo)
        {
            try { return ideo.ColonistBelieverCountCached; }
            catch (Exception) { return -1; }
        }

        private void PollArchive()
        {
            List<IArchivable> list = Find.Archive?.ArchivablesListForReading;
            if (list == null) return;

            var seen = new HashSet<string>(archiveSeenKeys);
            var fresh = new List<ChronicleEntry>();
            int maxTick = lastArchiveTick;

            foreach (IArchivable a in list)
            {
                int t = a.CreatedTicksGame;
                if (t < lastArchiveTick) continue;
                string key = t + "|" + a.ArchivedLabel;
                if (t > maxTick) maxTick = t;
                if (!seen.Add(key)) continue;

                ChronicleEntry e = FromArchivable(a);
                if (e != null) fresh.Add(e);
            }

            archiveSeenKeys = list.Where(a => a.CreatedTicksGame == maxTick).Select(a => a.CreatedTicksGame + "|" + a.ArchivedLabel).ToList();
            lastArchiveTick = maxTick;
            AddEntries(fresh);
        }

        private ChronicleEntry FromArchivable(IArchivable a)
        {
            string category, subtype = null;
            if (a is Letter letter)
            {
                if (MergeDeathLetter(letter)) return null;
                category = "letter";
                subtype = letter.def?.defName;
            }
            else if (a is Message message)
            {
                if (!ChronicleMod.Settings.recordMessages) return null;
                if (message.def != null && IgnoredMessageTypes.Contains(message.def.defName)) return null;
                category = "message";
                subtype = message.def?.defName;
            }
            else
            {
                category = "archive";
                subtype = a.GetType().Name;
            }

            var entry = new ChronicleEntry
            {
                tick = a.CreatedTicksGame,
                category = category,
                subtype = subtype,
                label = Clean(a.ArchivedLabel),
                text = category == "message" ? null : Clean(a.ArchivedTooltip),
            };
            AddPawnsFromTargets(entry, a.LookTargets);
            return entry;
        }

        private static void AddPawnsFromTargets(ChronicleEntry entry, LookTargets targets)
        {
            if (targets?.targets == null) return;
            foreach (GlobalTargetInfo target in targets.targets)
            {
                if (target.HasThing && target.Thing is Pawn p && !entry.pawnIds.Contains(p.ThingID))
                {
                    entry.pawnIds.Add(p.ThingID);
                    entry.pawnNames.Add(p.LabelShort);
                }
            }
        }

        private void PollTales()
        {
            List<Tale> tales = Find.TaleManager?.AllTalesListForReading;
            if (tales == null) return;

            var fresh = new List<ChronicleEntry>();
            int maxId = lastTaleId;
            for (int i = tales.Count - 1; i >= 0; i--)
            {
                Tale tale = tales[i];
                if (tale.id <= lastTaleId) break;
                if (tale.id > maxId) maxId = tale.id;
                try
                {
                    var entry = new ChronicleEntry
                    {
                        tick = GenDate.TickAbsToGame(tale.date),
                        category = "tale",
                        subtype = tale.def?.defName,
                        label = TaleLabel(tale),
                        text = tale.def?.LabelCap.ToString(),
                    };
                    foreach (TaleData_Pawn data in TalePawns(tale))
                    {
                        if (data?.pawn == null) continue;
                        entry.pawnIds.Add(data.pawn.ThingID);
                        entry.pawnNames.Add(data.pawn.LabelShort);
                    }
                    fresh.Add(entry);
                }
                catch (Exception e)
                {
                    if (Prefs.DevMode) Log.Warning($"[Rimlore] Skipped tale {tale.id}: {e.Message}");
                }
            }
            lastTaleId = maxId;
            AddEntries(fresh);
        }

        private static IEnumerable<TaleData_Pawn> TalePawns(Tale tale)
        {
            if (tale is Tale_SinglePawn single) yield return single.pawnData;
            if (tale is Tale_DoublePawn pair)
            {
                yield return pair.firstPawnData;
                yield return pair.secondPawnData;
            }
        }

        private static string TaleLabel(Tale tale)
        {
            string what = tale.def?.LabelCap.ToString() ?? "Tale";
            var who = new List<string>();
            if (tale is Tale_SinglePawn single)
            {
                who.Add(TalePawnLabel(single.pawnData, null));
            }
            else if (tale is Tale_DoublePawn pair)
            {
                string fight = FightLabel(tale.def?.defName, pair);
                if (fight != null) return fight;
                who.Add(TalePawnLabel(pair.firstPawnData, tale.def?.firstPawnSymbol));
                who.Add(TalePawnLabel(pair.secondPawnData, tale.def?.secondPawnSymbol));
            }
            who.RemoveAll(w => w == null);
            return who.Count > 0 ? what + ": " + string.Join(", ", who) : Clean(tale.ShortSummary);
        }

        private static string FightLabel(string defName, Tale_DoublePawn pair)
        {
            if (defName.NullOrEmpty()) return null;
            Pawn a = pair.firstPawnData?.pawn, b = pair.secondPawnData?.pawn;
            if (a == null || b == null || a == b) return null;

            string weapon = null;
            if (pair is Tale_DoublePawnAndDef withDef && withDef.defData?.def is ThingDef td && td.race == null)
                weapon = " with " + DeathCause.Indefinite(td.label);

            if (defName == "Wounded" || defName == "Downed")
            {
                Pawn victim = DeathCause.WasHurtBy(b, a) && !DeathCause.WasHurtBy(a, b) ? b : a;
                Pawn attacker = victim == a ? b : a;
                string verb = defName == "Wounded" ? "was wounded" : "was downed";
                return $"{DeathCause.Name(victim).CapitalizeFirst()} {verb} by {DeathCause.Name(attacker)}{weapon}";
            }
            if (defName.StartsWith("Killed") || defName == "Hunted")
            {
                Pawn victim = a.Dead && !b.Dead ? a : b.Dead && !a.Dead ? b : null;
                if (victim == null) return null;
                Pawn killer = victim == a ? b : a;
                return $"{DeathCause.Name(killer).CapitalizeFirst()} killed {DeathCause.Name(victim)}{weapon}";
            }
            return null;
        }

        private static string TalePawnLabel(TaleData_Pawn data, string symbol)
        {
            if (data == null) return null;
            string name = data.pawn?.LabelShort;
            if (name.NullOrEmpty()) name = data.name?.ToStringShort;
            if (name.NullOrEmpty()) name = data.kind?.label;
            if (name.NullOrEmpty()) return null;

            if (symbol.NullOrEmpty() || symbol.ToUpperInvariant().Contains("PAWN")) return name;
            return $"{name} ({symbol.ToLowerInvariant()})";
        }

        public void AddNote(string text, IEnumerable<Pawn> pawns)
        {
            int now = Find.TickManager.TicksGame;
            var pawnList = pawns?.ToList() ?? new List<Pawn>();
            string firstLine = text.Split('\n')[0];
            AddEntry(new ChronicleEntry
            {
                tick = now,
                category = "note",
                subtype = "player",
                label = firstLine.Length > 120 ? firstLine.Substring(0, 117) + "..." : firstLine,
                text = text,
                pawnIds = pawnList.Select(p => p.ThingID).ToList(),
                pawnNames = pawnList.Select(p => p.LabelShort).ToList(),
            });
            writer?.Flush();
        }

        private void TrimStoredEntries()
        {
            TrimCategory("message", MaxStoredMessages);
            TrimCategory("tale", MaxStoredTales);
        }

        private void TrimCategory(string category, int keep)
        {
            int excess = entries.Count(e => e.category == category) - keep;
            if (excess <= 0) return;
            entries.RemoveAll(e => e.category == category && excess-- > 0);
        }

        private void AddEntries(List<ChronicleEntry> fresh)
        {
            if (fresh.Count == 0) return;
            foreach (var e in fresh.OrderBy(e => e.tick)) AddEntry(e);
        }

        private void AddEntry(ChronicleEntry e)
        {
            e.date ??= Dates.At(e.tick);
            writer?.Write(e.ToJson());

            if (e.text != null && e.text.Length > MaxStoredTextLength)
                e.text = e.text.Substring(0, MaxStoredTextLength) + "...";

            int i = entries.Count;
            while (i > 0 && entries[i - 1].tick > e.tick) i--;
            entries.Insert(i, e);
        }

        private static string Clean(string s) => RichText.Strip(s);

        private static string Sanitize(string s)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim().Replace(' ', '_');
        }
    }

    public static class Dates
    {
        public static string At(int ticksGame)
        {
            try
            {
                long abs = GenDate.TickGameToAbs(ticksGame);
                return GenDate.DateFullStringAt(abs, LongLat());
            }
            catch (Exception)
            {
                return "tick " + ticksGame;
            }
        }

        private static Vector2 LongLat()
        {
            Map map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            return map != null ? Find.WorldGrid.LongLatOf(map.Tile) : Vector2.zero;
        }
    }
}
