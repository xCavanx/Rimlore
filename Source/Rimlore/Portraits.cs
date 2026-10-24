using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyChronicle
{
    public class Portraits
    {
        private const int RefreshTicks = GenDate.TicksPerDay;
        private static readonly Vector2 Size = new Vector2(200f, 280f);

        private readonly Dictionary<string, int> lastTried = new Dictionary<string, int>();

        public static bool Wanted(Pawn p) => p != null && !p.Dead && p.RaceProps.Humanlike;

        public void Poll(IEnumerable<Pawn> pawns, string folder, int now)
        {
            if (folder.NullOrEmpty()) return;
            Pawn next = null;
            int oldest = int.MaxValue;
            foreach (Pawn p in pawns)
            {
                if (!Wanted(p)) continue;
                if (!lastTried.TryGetValue(p.ThingID, out int tried))
                {
                    next = p;
                    break;
                }
                if (now - tried >= RefreshTicks && tried < oldest)
                {
                    oldest = tried;
                    next = p;
                }
            }
            if (next == null) return;
            lastTried[next.ThingID] = now;
            string dir = Path.Combine(folder, "portraits");
            Save(next, Path.Combine(dir, next.ThingID + ".png"), Size, Vector3.zero, 1f);
            Save(next, Path.Combine(dir, next.ThingID + "-face.png"), ColonistBarColonistDrawer.PawnTextureSize * 2f,
                ColonistBarColonistDrawer.PawnTextureCameraOffset, ColonistBarColonistDrawer.PawnTextureCameraZoom);
        }

        private static void Save(Pawn p, string path, Vector2 size, Vector3 offset, float zoom)
        {
            Texture2D tex = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture rt = PortraitsCache.Get(p, size, Rot4.South, offset, zoom, true, false);
                if (rt == null) return;
                RenderTexture.active = rt;
                tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
                tex.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;

                byte[] png = tex.EncodeToPNG();
                if (png == null || png.Length == 0) return;

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(png)) return;
                File.WriteAllBytes(path, png);
            }
            catch (Exception e)
            {
                if (Prefs.DevMode) Log.Warning($"[Rimlore] Couldn't save a portrait of {p}: {e.Message}");
            }
            finally
            {
                RenderTexture.active = previous;
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }
    }
}
