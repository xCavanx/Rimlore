using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Verse;

namespace ColonyChronicle
{
    public class ChronicleWriter
    {
        public const string DataFileName = "chronicle-data.js";

        public static string BaseFolder => Path.Combine(GenFilePaths.SaveDataFolderPath, "Rimlore");

        private static string LegacyBaseFolder => Path.Combine(GenFilePaths.SaveDataFolderPath, "ColonyChronicle");

        private readonly string folder;
        private readonly List<string> pending = new List<string>();
        private bool failed;

        public string Folder => folder;
        public string DataPath => Path.Combine(folder, DataFileName);

        public ChronicleWriter(string folderName)
        {
            folder = Path.Combine(BaseFolder, folderName);
            try
            {
                if (Directory.Exists(LegacyBaseFolder) && !Directory.Exists(BaseFolder))
                    Directory.Move(LegacyBaseFolder, BaseFolder);
                Directory.CreateDirectory(folder);
                CopyViewer();
            }
            catch (Exception e)
            {
                failed = true;
                Log.Error($"[Rimlore] Could not create history folder {folder}: {e}");
            }
        }

        public void Write(Dictionary<string, object> record)
        {
            pending.Add("C(" + Json.Serialize(record) + ");");
        }

        public void Flush()
        {
            if (pending.Count == 0 || failed) return;
            try
            {
                var sb = new StringBuilder();
                foreach (var line in pending) sb.Append(line).Append('\n');
                File.AppendAllText(DataPath, sb.ToString(), new UTF8Encoding(false));
                pending.Clear();
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[Rimlore] Could not write {DataPath}: {e.Message}", 0x43C4A1);
            }
        }

        private void CopyViewer()
        {
            string src = Path.Combine(ChronicleMod.RootDir ?? "", "Viewer", "index.html");
            if (File.Exists(src))
                File.Copy(src, Path.Combine(folder, "index.html"), overwrite: true);
        }

        public void OpenFolder() => OpenPath(folder);

        public void OpenViewer()
        {
            Flush();
            string viewer = Path.Combine(folder, "index.html");
            OpenPath(File.Exists(viewer) ? viewer : folder);
        }

        private static void OpenPath(string path)
        {
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
                {
                    string opener = Directory.Exists("/System/Library") ? "open" : "xdg-open";
                    Process.Start(new ProcessStartInfo(opener, "\"" + path + "\"") { UseShellExecute = false });
                }
                else
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception e)
            {
                Messages.Message("Couldn't open " + path + ": " + e.Message, RimWorld.MessageTypeDefOf.RejectInput, historical: false);
            }
        }
    }
}
