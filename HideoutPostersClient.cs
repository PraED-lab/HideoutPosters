using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using EFT.Hideout;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace PraEDLab.HideoutPosters
{
    // 0.2.0 : remplace l'image du poster PraED par une image du dossier BepInEx/config/HideoutPosters
    [BepInPlugin("praedlab.hideoutposters", "Hideout Posters", PostersPlugin.Ver)]
    public class PostersPlugin : BaseUnityPlugin
    {
        internal const string Ver = "0.5.21";   // une seule version pour tout le mod
        internal static PostersPlugin Inst;
        internal static ManualLogSource Log;
        internal const string PosterTpl = "6a1b0c0de0000000000a0001";
        static readonly System.Collections.Generic.Dictionary<string, Texture2D> texs = new System.Collections.Generic.Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        static readonly System.Collections.Generic.Dictionary<string, string> tplFile = new System.Collections.Generic.Dictionary<string, string>();
        static Texture2D tex { get { return texs.Values.FirstOrDefault(); } }

        // meme calcul que le serveur : nom du fichier -> identifiant du poster
        internal static string IdFor(string stem, char kind)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var h = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(stem.ToLowerInvariant()));
                var sb = new System.Text.StringBuilder(); foreach (var b in h) sb.Append(b.ToString("x2"));
                return "6a1b0c0de" + kind + sb.ToString().Substring(0, 14);
            }
        }
        internal static void BuildMap()
        {
            tplFile.Clear();
            // sous-dossiers compris : chaque sous-dossier devient une categorie dans la fenetre P
            var files = Directory.GetFiles(Folder, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase).ToList();
            var top = files.Where(f => string.Equals(Path.GetDirectoryName(f).TrimEnd('\\', '/'), Folder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)).ToList();
            if (top.Count > 0) tplFile[PosterTpl] = top[0]; else if (files.Count > 0) tplFile[PosterTpl] = files[0];   // l'ancien poster unique garde la premiere image
            foreach (var f in files)
            {
                tplFile[IdFor(Path.GetFileNameWithoutExtension(f), '1')] = f;
            }
            LoadSizes();
            Log.LogInfo("[poster] " + files.Count + " image(s) : " + string.Join(", ", files.Select(f => Path.GetFileName(f)).ToArray()));
        }
        internal static System.Collections.Generic.List<string> Files { get { return tplFile.Values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase).ToList(); } }
        static void RebuildFile(string file)
        {
            try { FreePlace.Resize(file); } catch { }
            Keepers.RemoveAll(k => k == null);
            foreach (var k in Keepers) if (file == null || string.Equals(k.File, file, StringComparison.OrdinalIgnoreCase)) k.Rebuild();
        }
        internal static string FileFor(Item item)
        {
            if (item == null) return null;
            string f; return tplFile.TryGetValue(item.TemplateId.ToString(), out f) ? f : null;
        }
        internal static BepInEx.Configuration.ConfigEntry<float> Size;
        // taille de depart de chaque image : plus dans F12, juste un petit fichier (la molette la regle)
        static System.Collections.Generic.Dictionary<string, float> sizes; static bool sizesReady;
        static string SizeFile { get { return Path.Combine(BepInEx.Paths.ConfigPath, "HideoutPosters_sizes.txt"); } }
        static void LoadSizes()
        {
            if (sizes != null && sizesReady) return;
            if (sizes == null) sizes = new System.Collections.Generic.Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(SizeFile) && tplFile.Count == 0) return;   // images pas encore lues : on reessaiera
            sizesReady = true;
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                if (File.Exists(SizeFile))
                {
                    foreach (var l in File.ReadAllLines(SizeFile)) { var a = l.Split('|'); float v; if (a.Length == 2 && float.TryParse(a[1], System.Globalization.NumberStyles.Float, ic, out v)) sizes[a[0]] = Mathf.Clamp(v, 0.25f, 4f); }
                    return;
                }
                // une seule fois : on recupere les anciennes tailles de la section « Size per poster » de F12
                var cfg = Inst.Config.ConfigFilePath; if (!File.Exists(cfg)) return;
                bool sec = false; int n = 0;
                var byKey = Files.GroupBy(f => new string(Path.GetFileNameWithoutExtension(f).Select(ch => "=\n\t\\\"'[]".IndexOf(ch) >= 0 || char.IsControl(ch) ? ' ' : ch).ToArray()).Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                foreach (var raw in File.ReadAllLines(cfg))
                {
                    var l = raw.Trim();
                    if (l.StartsWith("[")) { sec = l.Equals("[Size per poster]", StringComparison.OrdinalIgnoreCase); continue; }
                    if (!sec || l.StartsWith("#") || !l.Contains("=")) continue;
                    var k = l.Substring(0, l.IndexOf('=')).Trim(); var vs = l.Substring(l.IndexOf('=') + 1).Trim(); float v; string f;
                    if (byKey.TryGetValue(k, out f) && float.TryParse(vs, System.Globalization.NumberStyles.Float, ic, out v) && !Mathf.Approximately(v, 1f)) { sizes[Rel(f)] = Mathf.Clamp(v, 0.25f, 4f); n++; }
                }
                SaveSizes(); Log.LogInfo("[poster] " + n + " taille(s) recuperee(s) de l'ancienne section « Size per poster »");
            }
            catch (Exception e) { Log.LogWarning("[poster] tailles : " + e.Message); }
        }
        static void SaveSizes()
        {
            try { var ic = System.Globalization.CultureInfo.InvariantCulture; File.WriteAllLines(SizeFile, sizes.Where(kv => !Mathf.Approximately(kv.Value, 1f)).OrderBy(kv => kv.Key).Select(kv => kv.Key + "|" + kv.Value.ToString("0.##", ic)).ToArray()); }
            catch (Exception e) { Log.LogWarning("[poster] tailles : " + e.Message); }
        }
        // molette pendant la pose : change la taille de depart de cette image
        internal static bool NudgeSize(string file, float delta)
        {
            if (file == null) return false; LoadSizes();
            float cur = SizeOf(file), v = Mathf.Clamp(Mathf.Round((cur + delta) * 20f) / 20f, 0.25f, 4f);
            if (Mathf.Approximately(v, cur)) return false;
            sizes[Rel(file)] = v; SaveSizes(); RebuildFile(file); return true;
        }
        internal static float SizeOf(string file) { if (file == null) return 1f; LoadSizes(); float v; return sizes.TryGetValue(Rel(file), out v) ? v : 1f; }
        internal static float ScaleFor(string file) { return (Size != null ? Size.Value : 1f) * SizeOf(file); }
        internal static BepInEx.Configuration.ConfigEntry<bool> Diag;
        internal static readonly System.Collections.Generic.List<PosterKeeper> Keepers = new System.Collections.Generic.List<PosterKeeper>();
        internal static string Rel(string file)
        {
            try { var root = Folder.TrimEnd('\\', '/') + Path.DirectorySeparatorChar; return file.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? file.Substring(root.Length) : Path.GetFileName(file); }
            catch { return Path.GetFileName(file); }
        }
        internal static string Category(string file) { var r = Rel(file); var d = Path.GetDirectoryName(r); return string.IsNullOrEmpty(d) ? "" : d.Replace('\\', '/'); }
        internal static void CopySize(string from, string to) { LoadSizes(); float v = SizeOf(from); sizes.Remove(Rel(from)); if (!Mathf.Approximately(v, 1f)) sizes[Rel(to)] = v; SaveSizes(); }
        internal static string FolderPath { get { return Folder; } }

        // ----- mode collection : les affiches se debloquent en les arrachant en raid -----
        internal static BepInEx.Configuration.ConfigEntry<bool> Collection;
        internal static BepInEx.Configuration.ConfigEntry<KeyCode> TakeKey;
        internal static BepInEx.Configuration.ConfigEntry<int> MaxPx;
        static System.Collections.Generic.HashSet<string> unlocked;
        static string UnlockFile { get { return Path.Combine(BepInEx.Paths.ConfigPath, "HideoutPosters_unlocked.txt"); } }
        static void LoadUnlocked()
        {
            if (unlocked != null) return;
            unlocked = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { if (File.Exists(UnlockFile)) foreach (var l in File.ReadAllLines(UnlockFile)) if (l.Trim().Length > 0) unlocked.Add(l.Trim()); } catch { }
        }
        static void SaveUnlocked() { try { File.WriteAllLines(UnlockFile, unlocked.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()); } catch (Exception e) { Log.LogWarning("[collection] sauvegarde : " + e.Message); } }
        internal static bool IsLocked(string file) { if (Collection == null || !Collection.Value || file == null) return false; LoadUnlocked(); return !unlocked.Contains(Rel(file)); }
        internal static bool Unlock(string file) { LoadUnlocked(); bool n = unlocked.Add(Rel(file)); if (n) SaveUnlocked(); return n; }
        internal static int UnlockedCount { get { LoadUnlocked(); var fs = Files; return fs.Count(f => unlocked.Contains(Rel(f))); } }
        internal static void MoveUnlock(string src, string dst) { LoadUnlocked(); if (unlocked.Remove(Rel(src))) { unlocked.Add(Rel(dst)); SaveUnlocked(); } }
        static string Folder { get { return Path.Combine(BepInEx.Paths.ConfigPath, "HideoutPosters"); } }

        void Awake()
        {
            Inst = this; Log = Logger;
            Collection = Config.Bind("Collection", "Collection mode", false, "Challenge mode: your posters start LOCKED. Find them on walls during raids and tear them down to unlock them for your hideout");
            Look.Bind(Config); Glow.Bind(Config);
            MaxPx = Config.Bind("Poster", "Max image size (px)", 1536, new BepInEx.Configuration.ConfigDescription("Bigger images are shrunk to this size (longest side) when loaded: faster hideout loading and less video memory, no visible loss on a wall poster. Your files are not changed. Reload the hideout to apply", new BepInEx.Configuration.AcceptableValueRange<int>(512, 4096)));
            TakeKey = Config.Bind("Collection", "Take poster key", KeyCode.Delete, "In raid: tear down the poster you are looking at (collection mode)");
            try { gameObject.AddComponent<FreePlace>(); } catch (Exception e) { Logger.LogWarning("[pose] " + e.Message); }
            try { gameObject.AddComponent<RaidPosters>(); } catch (Exception e) { Logger.LogWarning("[raid] " + e.Message); }
            try { RangeTV.Bind(Config); gameObject.AddComponent<RangeTV>(); } catch (Exception e) { Logger.LogWarning("[tv] " + e.Message); }
            BulletHoles.Patch();
            Size = Config.Bind("Poster", "Size", 1f, new BepInEx.Configuration.ConfigDescription("Size of ALL posters (1 = original size). Each image also remembers its own size (mouse wheel while placing)", new BepInEx.Configuration.AcceptableValueRange<float>(0.25f, 4f)));
            Diag = Config.Bind("Poster", "Bug report", false, "Click when a poster looks wrong: writes its full state to BepInEx/LogOutput.log (send that file with your bug report)");
            Diag.SettingChanged += (a, b) => { if (Diag.Value) { Snapshot("demande F12"); Diag.Value = false; } };
            Size.SettingChanged += (a, b) => { RebuildFile(null); Log.LogInfo("[poster] nouvelle taille : x" + Size.Value.ToString("0.00")); };
            try { Directory.CreateDirectory(Folder); Directory.CreateDirectory(Path.Combine(Folder, "Stickers")); } catch { }
            try { BuildMap(); } catch (Exception e) { Logger.LogWarning("[poster] lecture du dossier : " + e.Message); }
            try
            {
                var m = typeof(HideoutAreaStashController).GetMethod("SpawnNewItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                new Harmony("praedlab.hideoutposters").Patch(m, postfix: new HarmonyMethod(typeof(PostersPlugin).GetMethod("SpawnPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                // icone dans l'inventaire : notre affiche au lieu de l'icone Arena
                try
                {
                    var gi = typeof(ItemIconCreator).GetMethod("GetItemIcon", BindingFlags.Instance | BindingFlags.Public);
                    new Harmony("praedlab.hideoutposters.icon").Patch(gi, postfix: new HarmonyMethod(typeof(PostersPlugin).GetMethod("IconPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                }
                catch (Exception e) { Log.LogError("[poster] patch icone : " + e); }
                Log.LogInfo("Hideout Posters " + Ver + " charge (dossier d'images : " + Folder + ")");
            }
            catch (Exception e) { Log.LogError("[poster] patch : " + e); }
        }

        static void SpawnPostfix(Item item, Task<StashItemModel> __result)
        {
            try
            {
                if (__result == null) return;
                var file = FileFor(item);
                if (file == null) return;
                Inst.StartCoroutine(Inst.Apply(__result, file));
            }
            catch (Exception e) { Log.LogWarning("[poster] " + e.Message); }
        }

        internal static bool InStickerFolder(string file)
        {
            var d = Path.GetDirectoryName(Rel(file)) ?? "";
            return d.Replace('\\', '/').Split('/').Any(x => x.Equals("Stickers", StringComparison.OrdinalIgnoreCase) || x.Equals("Sticker", StringComparison.OrdinalIgnoreCase));
        }
        // remplissage depuis les bords : enleve le fond uni sans toucher aux zones de meme couleur a l'interieur du logo
        static void KeyOut(Color32[] px, int w, int h)
        {
            // couleurs du bord de l'image : 1 couleur = fond uni ; 2 couleurs alternees = faux damier de « transparence »
            var border = new System.Collections.Generic.List<Color32>();
            for (int x = 0; x < w; x += Math.Max(1, w / 200)) { border.Add(px[x]); border.Add(px[(h - 1) * w + x]); }
            for (int y = 0; y < h; y += Math.Max(1, h / 200)) { border.Add(px[y * w]); border.Add(px[y * w + w - 1]); }
            var opaque = border.Where(c => c.a >= 128).ToList(); if (opaque.Count < border.Count / 2) return;   // deja transparent
            var groups = opaque.GroupBy(c => new Color32((byte)(c.r / 24 * 24), (byte)(c.g / 24 * 24), (byte)(c.b / 24 * 24), 255)).OrderByDescending(g => g.Count()).ToList();
            var bgs = new System.Collections.Generic.List<Color32> { Avg(groups[0]) };
            if (groups.Count > 1 && groups[1].Count() > opaque.Count * 0.2f && Gray(Avg(groups[0])) && Gray(Avg(groups[1]))) { bgs.Add(Avg(groups[1])); Log.LogInfo("[sticker] faux damier detecte : on enleve les 2 couleurs"); }
            var seen = new bool[px.Length]; var q = new System.Collections.Generic.Queue<int>();
            System.Action<int> push = i => { if (!seen[i] && px[i].a >= 128 && bgs.Any(b => Dist(px[i], b) < 60)) { seen[i] = true; q.Enqueue(i); } };
            for (int x = 0; x < w; x++) { push(x); push((h - 1) * w + x); }
            for (int y = 0; y < h; y++) { push(y * w); push(y * w + w - 1); }
            int n = 0;
            while (q.Count > 0)
            {
                int i = q.Dequeue(); px[i].a = 0; n++;
                int x = i % w, y = i / w;
                if (x > 0) push(i - 1); if (x < w - 1) push(i + 1); if (y > 0) push(i - w); if (y < h - 1) push(i + w);
            }
            Log.LogInfo("[sticker] fond enleve : " + (100L * n / px.Length) + " % de l'image");
        }
        static Color32 Avg(System.Collections.Generic.IEnumerable<Color32> cs) { int r = 0, g = 0, b = 0, n = 0; foreach (var c in cs) { r += c.r; g += c.g; b += c.b; n++; } return n == 0 ? new Color32(255, 255, 255, 255) : new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255); }
        static bool Gray(Color32 c) { return Math.Abs(c.r - c.g) < 18 && Math.Abs(c.g - c.b) < 18 && c.r > 140; }   // blanc ou gris clair
        static int Dist(Color32 a, Color32 b) { return Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b); }
        internal static Texture2D ReloadImage(string file) { texs.Remove(file); gifs.Remove(file); return LoadImage(file); }

        // ----- affiches animees (GIF) -----
        internal class Anim { public Texture2D[] Frames; public float[] Delays; }
        static readonly System.Collections.Generic.HashSet<string> stickers = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal static bool IsSticker(string file) { if (file == null) return false; LoadImage(file); return stickers.Contains(file); }
        static readonly System.Collections.Generic.Dictionary<string, Anim> gifs = new System.Collections.Generic.Dictionary<string, Anim>(StringComparer.OrdinalIgnoreCase);
        internal static Anim AnimOf(string file) { Anim a; if (file == null) return null; if (!gifs.TryGetValue(file, out a)) { LoadImage(file); gifs.TryGetValue(file, out a); } return a; }
        internal static bool IsFrame(string file, Texture t) { Anim a; return t != null && file != null && gifs.TryGetValue(file, out a) && Array.IndexOf(a.Frames, t) >= 0; }
        static Texture2D LoadGif(string file)
        {
            try
            {
                int w, h; var frames = GifDecoder.Decode(File.ReadAllBytes(file), out w, out h);
                if (frames.Count == 0) { Log.LogWarning("[poster] GIF illisible : " + file); return null; }
                // memoire : on garde au plus ~160 Mo d'images par GIF (on saute des images si besoin)
                long per = (long)w * h * 4 * 4 / 3; int step = 1;
                while (frames.Count / step > 1 && per * (frames.Count / step) > 160L * 1024 * 1024) step++;
                var tex = new System.Collections.Generic.List<Texture2D>(); var del = new System.Collections.Generic.List<float>();
                for (int i = 0; i < frames.Count; i += step)
                {
                    var px = frames[i].Key;
                    if (i == 0) { int clr = 0; for (int k = 0; k < px.Length; k++) if (px[k].a < 128) clr++; if (clr > px.Length * 0.03f) stickers.Add(file); else stickers.Remove(file); }
                    bool stk = stickers.Contains(file);
                    for (int k = 0; k < px.Length; k++) px[k].a = stk ? (px[k].a < 128 ? (byte)0 : (byte)255) : (byte)8;
                    var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
                    t.SetPixels32(px); t.Apply(true, tex.Count > 0);   // la 1re reste lisible (vignette, icone)
                    t.wrapMode = TextureWrapMode.Clamp; t.anisoLevel = 16; t.mipMapBias = -0.35f; t.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    float d = 0f; for (int j = i; j < i + step && j < frames.Count; j++) d += frames[j].Value;
                    tex.Add(t); del.Add(Mathf.Max(0.02f, d));
                }
                gifs[file] = new Anim { Frames = tex.ToArray(), Delays = del.ToArray() };
                texs[file] = tex[0];
                Log.LogInfo("[poster] GIF charge : " + Path.GetFileName(file) + " (" + w + "x" + h + ", " + tex.Count + " image(s)" + (step > 1 ? ", 1 sur " + step : "") + ")");
                return tex[0];
            }
            catch (Exception e) { Log.LogWarning("[poster] GIF " + Path.GetFileName(file) + " : " + e.Message); return null; }
        }
        internal static double ImgMs;   // [perf] temps passe a charger les images
        internal static Texture2D LoadImage(string file)
        {
            Texture2D cached;
            if (texs.TryGetValue(file, out cached) && cached != null) return cached;
            if (!File.Exists(file)) { Log.LogWarning("[poster] image introuvable : " + file); return null; }
            if (file.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return LoadGif(file);
            var swImg = System.Diagnostics.Stopwatch.StartNew();
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!ImageConversion.LoadImage(t, File.ReadAllBytes(file))) { Log.LogWarning("[poster] image illisible : " + file); return null; }
            t.wrapMode = TextureWrapMode.Clamp; t.anisoLevel = 4;
            // image trop grande : on la reduit en memoire (le fichier n'est pas touche)
            try
            {
                int cap = MaxPx != null ? MaxPx.Value : 1536, mx = Mathf.Max(t.width, t.height);
                if (mx > cap)
                {
                    float k = (float)cap / mx; int w = Mathf.Max(1, Mathf.RoundToInt(t.width * k)), h = Mathf.Max(1, Mathf.RoundToInt(t.height * k));
                    var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                    var prev = RenderTexture.active; t.filterMode = FilterMode.Bilinear; Graphics.Blit(t, rt); RenderTexture.active = rt;
                    var t3 = new Texture2D(w, h, TextureFormat.RGBA32, true); t3.ReadPixels(new Rect(0, 0, w, h), 0, 0, false); t3.Apply(false);
                    RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                    Log.LogInfo("[poster] image reduite : " + Path.GetFileName(file) + " " + t.width + "x" + t.height + " -> " + w + "x" + h);
                    UnityEngine.Object.Destroy(t); t = t3; t.wrapMode = TextureWrapMode.Clamp;
                }
            }
            catch (Exception e) { Log.LogWarning("[poster] reduction : " + e.Message); }
            // le shader du poster utilise la transparence comme brillance : on rend l'image mate (papier), sinon elle reflete tout en blanc
            try
            {
                // un JPG est charge sans couche de transparence : on recopie l'image dans une texture qui en a une
                var px = t.GetPixels32();
                // dossier « Stickers » : on enleve le fond uni (blanc ou autre) en partant des bords
                if (InStickerFolder(file)) KeyOut(px, t.width, t.height);
                // fond transparent (logo, sticker) : on garde la decoupe ; sinon papier mat
                int clear = 0; for (int i = 0; i < px.Length; i++) if (px[i].a < 128) clear++;
                bool st = clear > px.Length * 0.03f;
                if (st) { stickers.Add(file); for (int i = 0; i < px.Length; i++) px[i].a = px[i].a < 128 ? (byte)0 : (byte)255; }
                else { stickers.Remove(file); for (int i = 0; i < px.Length; i++) px[i].a = 8; }
                var t2 = new Texture2D(t.width, t.height, TextureFormat.RGBA32, true);
                t2.SetPixels32(px); t2.Apply(true);
                t2.wrapMode = TextureWrapMode.Clamp; t2.anisoLevel = 16; t2.mipMapBias = -0.35f;
                Log.LogInfo("[poster] format d'origine " + t.format + " -> RGBA32 mat");
                UnityEngine.Object.Destroy(t); t = t2;
            }
            catch (Exception e) { Log.LogWarning("[poster] brillance : " + e.Message); }
            ImgMs += swImg.Elapsed.TotalMilliseconds;
            Log.LogInfo("[poster] image chargee : " + Path.GetFileName(file) + " (" + t.width + "x" + t.height + ") en " + swImg.ElapsedMilliseconds + " ms");
            t.hideFlags = HideFlags.DontUnloadUnusedAsset; texs[file] = t; return t;
        }

        IEnumerator Apply(Task<StashItemModel> task, string file)
        {
            while (!task.IsCompleted) yield return null;
            yield return null;
            if (task.IsFaulted || task.Result == null) { Log.LogWarning("[poster] modele introuvable" + (task.Exception != null ? " : " + task.Exception.GetBaseException().Message : "")); yield break; }
            var img = LoadImage(file);
            if (img == null) yield break;
            int n = 0;
            foreach (var r in task.Result.GetComponentsInChildren<Renderer>(true))
            {
                var old = r.gameObject.GetComponent<PosterKeeper>();
                if (old != null && old.Ok && old.File == file) { Log.LogInfo("[poster] " + r.name + " deja habille : on n'y touche plus"); n++; continue; }
                var mat = r.material;
                var mf = r.GetComponent<MeshFilter>();
                if (mat == null || mat.mainTexture == null || mf == null || mf.sharedMesh == null) continue;
                // l'image d'origine est un morceau d'une grande planche : on remplace la surface par une neuve qui utilise toute notre image
                var b = (old != null && old.Ok) ? old.Bounds0 : mf.sharedMesh.bounds;
                var b0 = b;
                Log.LogInfo("[poster] " + r.name + " : texture d'origine " + mat.mainTexture.name + " (" + mat.mainTexture.width + "x" + mat.mainTexture.height + "), taille " + b.size.ToString("F3"));
                mf.sharedMesh = BuildQuad(b, (float)img.width / img.height, ScaleFor(file));
                mat.mainTexture = img; mat.mainTextureScale = Vector2.one; mat.mainTextureOffset = Vector2.zero;
                // le poster d'origine a un 2e materiau (papier blanc du dos) : sur notre surface il se dessinait par-dessus l'image, une fois sur deux
                if (r.sharedMaterials.Length > 1) { Log.LogInfo("[poster] " + r.sharedMaterials.Length + " materiaux sur le poster : on ne garde que l'image (le papier blanc du dos passait par-dessus)"); r.sharedMaterials = new[] { mat }; }
                // plus de reflet metallique : il recouvrait l'image en blanc
                foreach (var cp in new[] { "_ReflectColor", "_SpecColor" }) if (mat.HasProperty(cp)) mat.SetColor(cp, Color.black);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
                // gardien : surveille le poster et remet l'image si le jeu la retire ou la detruit
                var kp = r.gameObject.GetComponent<PosterKeeper>() ?? r.gameObject.AddComponent<PosterKeeper>();
                kp.Init(r, mf, mf.sharedMesh, img, b0, file);
                GifPlayer.Attach(r, file);
                n++;
            }
            Log.LogInfo("[poster] image appliquee sur " + n + " partie(s) du poster");
        }

        // une surface plate (recto et verso) qui couvre exactement le poster d'origine, image entiere de 0 a 1


        static readonly System.Collections.Generic.Dictionary<string, ItemIcon> icons = new System.Collections.Generic.Dictionary<string, ItemIcon>();
        static void IconPostfix(Item item, ref IntVec2 size, ref ItemIcon __result)
        {
            try
            {
                var file = FileFor(item);
                if (file == null) return;
                int w = Mathf.Max(8, size.X), h = Mathf.Max(8, size.Y);
                string key = file + "|" + w + "x" + h;
                ItemIcon ic;
                if (icons.TryGetValue(key, out ic) && ic.Sprite != null) { __result = ic; return; }
                var img = LoadImage(file);
                if (img == null) return;
                // l'affiche entiere, centree dans la case, sans la deformer
                float sc = Mathf.Min((float)w / img.width, (float)h / img.height) * 0.92f;
                int dw = Mathf.Max(1, Mathf.RoundToInt(img.width * sc)), dh = Mathf.Max(1, Mathf.RoundToInt(img.height * sc));
                int ox = (w - dw) / 2, oy = (h - dh) / 2;
                var px = new Color32[w * h];
                for (int y = 0; y < dh; y++)
                    for (int x = 0; x < dw; x++)
                    {
                        Color c = img.GetPixelBilinear((x + 0.5f) / dw, (y + 0.5f) / dh); c.a = 1f;
                        px[(y + oy) * w + x + ox] = c;
                    }
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                t.SetPixels32(px); t.Apply(false, true); t.hideFlags = HideFlags.DontUnloadUnusedAsset; t.filterMode = FilterMode.Bilinear;
                ic = new ItemIcon(("praed-poster-" + key).GetHashCode());
                ic.Sprite = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
                icons[key] = ic;
                Log.LogInfo("[poster] icone d'inventaire creee : " + Path.GetFileName(file) + " (" + w + "x" + h + ")");
                __result = ic;
            }
            catch (Exception e) { Log.LogWarning("[poster] icone : " + e.Message); }
        }

        internal static void Snapshot(string why)
        {
            try
            {
                Log.LogInfo("[poster] ===== ETAT (" + why + ") =====");
                Log.LogInfo("[poster] limite qualite textures = " + QualitySettings.globalTextureMipmapLimit + ", streaming = " + QualitySettings.streamingMipmapsActive);
                int found = 0;
                foreach (var mf in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
                {
                    if (mf.sharedMesh == null || mf.sharedMesh.name != "HideoutPosterQuad") continue;
                    found++;
                    var r = mf.GetComponent<Renderer>();
                    string line = "[poster] surface #" + found + " sur " + mf.gameObject.name + " (parent " + (mf.transform.parent != null ? mf.transform.parent.name : "-") + ")";
                    if (r == null) { Log.LogInfo(line + " : pas de rendu"); continue; }
                    line += " actif=" + r.gameObject.activeInHierarchy + " affiche=" + r.enabled + " vu=" + r.isVisible + " materiaux=" + r.sharedMaterials.Length + " bloc=" + r.HasPropertyBlock();
                    Log.LogInfo(line);
                    var lod = r.GetComponentInParent<LODGroup>();
                    if (lod != null) Log.LogInfo("[poster]   groupe LOD : " + lod.name + " (" + lod.lodCount + " niveaux, actif=" + lod.enabled + ")");
                    for (int i = 0; i < r.sharedMaterials.Length; i++)
                    {
                        var m = r.sharedMaterials[i];
                        if (m == null) { Log.LogInfo("[poster]   materiau " + i + " : aucun"); continue; }
                        var t = m.mainTexture as Texture2D;
                        string ts = t == null ? "AUCUNE" : (t.name + " " + t.width + "x" + t.height + " id " + t.GetInstanceID() + " format " + t.format + " mips " + t.mipmapCount);
                        if (t != null && t.isReadable) { var c = t.GetPixel(t.width / 2, t.height / 2); ts += " pixel centre " + c; }
                        Log.LogInfo("[poster]   materiau " + i + " : " + m.name + " / shader " + m.shader.name + " (file " + m.renderQueue + ") texture " + ts);
                        foreach (var cp in new[] { "_Color", "_ReflectColor", "_SpecColor" }) if (m.HasProperty(cp)) Log.LogInfo("[poster]     " + cp + " = " + m.GetColor(cp));
                        Log.LogInfo("[poster]     mots-cles : " + string.Join(", ", m.shaderKeywords));
                    }
                }
                Log.LogInfo("[poster] " + found + " surface(s) de poster dans la scene, image en memoire : " + (tex != null ? tex.name + " id " + tex.GetInstanceID() : "aucune"));
                // les autres objets poster de la scene (copies faites par le jeu ?)
                int other = 0;
                foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                    if (r.name.StartsWith("Posters_12")) { var mf = r.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name != "HideoutPosterQuad") { other++; Log.LogInfo("[poster] autre poster : " + r.name + " forme " + mf.sharedMesh.name + " texture " + (r.sharedMaterial != null && r.sharedMaterial.mainTexture != null ? r.sharedMaterial.mainTexture.name : "aucune") + " affiche=" + r.enabled + " vu=" + r.isVisible); } }
                Log.LogInfo("[poster] ===== fin (" + other + " autre(s) poster(s) d'origine) =====");
            }
            catch (Exception e) { Log.LogWarning("[poster] etat : " + e); }
        }

        internal static Mesh BuildQuad(Bounds b, float aspect, float scale)
        {
            Vector3 sz = b.size;
            Vector3[] ax = { Vector3.right, Vector3.up, Vector3.forward };
            float[] len = { sz.x, sz.y, sz.z };
            int ni = 0; for (int i = 1; i < 3; i++) if (len[i] < len[ni]) ni = i;          // axe le plus fin = epaisseur
            int a1 = (ni + 1) % 3, a2 = (ni + 2) % 3;
            int vi = len[a1] >= len[a2] ? a1 : a2;                                         // le plus long = la hauteur
            // la forme suit l'image : le plus grand cote garde la taille du poster d'origine, l'autre s'adapte
            float maxSide = Mathf.Max(len[vi], len[3 - ni - vi]) * scale;
            float hh, ww;
            if (aspect >= 1f) { ww = maxSide; hh = maxSide / aspect; } else { hh = maxSide; ww = maxSide * aspect; }
            Log.LogInfo("[poster] forme : image " + aspect.ToString("0.00") + " -> " + ww.ToString("0.00") + " x " + hh.ToString("0.00") + " m");
            Vector3 n = ax[ni], up = ax[vi] * (hh * 0.5f);
            var verts = new System.Collections.Generic.List<Vector3>(); var uvs = new System.Collections.Generic.List<Vector2>();
            var norms = new System.Collections.Generic.List<Vector3>(); var tris = new System.Collections.Generic.List<int>();
            float w = ww * 0.5f;
            foreach (var face in new[] { n, -n })
            {
                Vector3 right = Vector3.Cross(ax[vi], -face).normalized * w;
                Vector3 c = b.center + face * (len[ni] * 0.5f + 0.001f);
                int o = verts.Count;
                verts.Add(c - right - up); uvs.Add(new Vector2(0, 0));
                verts.Add(c + right - up); uvs.Add(new Vector2(1, 0));
                verts.Add(c + right + up); uvs.Add(new Vector2(1, 1));
                verts.Add(c - right + up); uvs.Add(new Vector2(0, 1));
                for (int k = 0; k < 4; k++) norms.Add(face);
                tris.AddRange(new[] { o, o + 3, o + 2, o, o + 2, o + 1 });
            }
            var m = new Mesh { name = "HideoutPosterQuad" };
            var cols = new System.Collections.Generic.List<Color32>(); for (int k = 0; k < verts.Count; k++) cols.Add(new Color32(255, 255, 255, 255));
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetUVs(1, uvs); m.SetUVs(2, uvs); m.SetColors(cols); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }
    }

    public class PosterKeeper : MonoBehaviour
    {
        Renderer r; MeshFilter mf; Mesh quad; Texture2D img; float next; int fixes; Bounds b0; public string File; public Bounds Bounds0 { get { return b0; } }
        public bool Ok { get { return r != null && img != null && quad != null; } }
        public void Init(Renderer r0, MeshFilter mf0, Mesh q, Texture2D t, Bounds b, string file) { File = file; r = r0; mf = mf0; quad = q; img = t; b0 = b; next = 0; if (!PostersPlugin.Keepers.Contains(this)) PostersPlugin.Keepers.Add(this); }
        public void Rebuild()
        {
            if (mf == null || img == null) return;
            var old = quad;
            quad = PostersPlugin.BuildQuad(b0, (float)img.width / img.height, PostersPlugin.ScaleFor(File));
            mf.sharedMesh = quad;
            if (old != null && old != quad) Destroy(old);
        }
        void OnDestroy() { PostersPlugin.Keepers.Remove(this); }
        void Update()
        {
            if (r == null || Time.time < next) return;
            next = Time.time + 0.5f;
            try
            {
                if (r.HasPropertyBlock()) { r.SetPropertyBlock(null); Note("le jeu avait pose un reglage par-dessus le materiau : retire"); }
                if (mf != null && quad != null && mf.sharedMesh != quad) { mf.sharedMesh = quad; Note("la forme avait ete remplacee : remise"); }
                var mat = r.sharedMaterial;
                if (mat == null) return;
                if (r.sharedMaterials.Length > 1) { r.sharedMaterials = new[] { mat }; Note("le papier blanc etait revenu : retire"); }
                if (img == null) { img = PostersPlugin.ReloadImage(File); Note("l'image avait ete detruite par le jeu : rechargee"); if (img == null) return; }
                if (mat.mainTexture != img && !PostersPlugin.IsFrame(File, mat.mainTexture)) { Note("le materiau avait change (" + (mat.mainTexture != null ? mat.mainTexture.name : "aucune texture") + ") : image remise"); mat.mainTexture = img; mat.mainTextureScale = Vector2.one; mat.mainTextureOffset = Vector2.zero; }
            }
            catch (System.Exception e) { if (fixes < 20) { fixes++; PostersPlugin.Log.LogWarning("[poster] gardien : " + e.Message); } }
        }
        void Note(string s) { if (fixes < 20) { fixes++; PostersPlugin.Log.LogInfo("[poster] gardien : " + s); } }
    }

    // ===== Pose libre : coller une image du dossier n'importe ou dans la planque =====
    public class FreePlace : MonoBehaviour
    {
        class Placed { public string File; public Vector3 Pos; public Quaternion Rot; public GameObject Go; public float Scale = -1f; public int Seed; public int Lit; public int Fix; public int Curl; public int Col; public bool Off; public bool Fallen; public System.Collections.Generic.List<Vector3> Holes = new System.Collections.Generic.List<Vector3>();
            public string Anchor; public Vector3 LPos; public Quaternion LRot = Quaternion.identity;
            public Vector3 WPos { get { return Go != null ? Go.transform.position : Pos; } }
            public Quaternion WRot { get { return Go != null ? Go.transform.rotation : Rot; } } }   // Scale : taille propre a CETTE affiche (figee a la pose)
        static readonly System.Collections.Generic.List<Placed> placed = new System.Collections.Generic.List<Placed>();
        static BepInEx.Configuration.ConfigEntry<KeyCode> kMode, kPlace, kNext, kPrev, kRemove, kBack, kRotL, kRotR, kStraight, kLight, kHelp, kFix, kEdit, kSwitch;
        static string SaveFile { get { return Path.Combine(BepInEx.Paths.ConfigPath, "HideoutPosters_placed.txt"); } }
        int ghostSeed; float roll; int ghostLit, ghostFix, ghostCurl, ghostCol; float ghostScale = -1f; Placed editBackup; System.Collections.Generic.List<Vector3> editHoles; bool mode, pick; Vector2 pickScroll; int hover = -1; CursorLockMode oldLock; bool oldVis; int idx; GameObject ghost; string ghostFile; float nextCheck; bool spawned; string msg; float msgUntil;

        void Awake()
        {
            me = this;
            PostersPlugin.Collection.SettingChanged += (x, y) => { try { if (pick && uiOk) RefreshGrid(); } catch { } };
            var c = PostersPlugin.Inst.Config;
            kMode = c.Bind("Free placement", "Placement mode key", KeyCode.P, "Start / cancel placing a poster anywhere in the hideout");
            kPlace = c.Bind("Free placement", "Place key", KeyCode.Return, "Hang the poster where you look");
            kNext = c.Bind("Free placement", "Next image key", KeyCode.RightArrow, "Next image while placing");
            kPrev = c.Bind("Free placement", "Previous image key", KeyCode.LeftArrow, "Previous image while placing");
            kRemove = c.Bind("Free placement", "Remove key", KeyCode.Delete, "Take down the free poster you are looking at");
            cFall = c.Bind("Free placement", "Poster falls after damage", 30f, new BepInEx.Configuration.ConfigDescription("A poster shot too much comes off the wall and falls to the floor (about this many 9 mm hits, big calibers count more). It is back on the wall, brand new, next time you load the hideout. 0 = never", new BepInEx.Configuration.AcceptableValueRange<float>(0f, 200f)));
            kBack = c.Bind("Free placement", "Back to the list key", KeyCode.Backspace, "While placing: reopen the poster window on the same poster");
            kRotL = c.Bind("Free placement", "Rotate left key", KeyCode.R, "While placing: hold to turn the poster to the left (CTRL + mouse wheel also turns it). Avoid Q / E: the game uses them to lean");
            kRotR = c.Bind("Free placement", "Rotate right key", KeyCode.T, "While placing: hold to turn the poster to the right");
            // Q / E font aussi pencher le personnage : on passe une fois sur R / T
            if (kRotL.Value == KeyCode.Q && kRotR.Value == KeyCode.E) { kRotL.Value = KeyCode.R; kRotR.Value = KeyCode.T; PostersPlugin.Log.LogInfo("[pose] rotation : Q/E remplaces par R/T (Q/E penchent le personnage)"); }
            kLight = c.Bind("Free placement", "Light key", KeyCode.L, "While placing: off -> backlit (glows like a light box) -> lamp (a small lamp above lights it like in a gallery)");
            kFix = c.Bind("Free placement", "Fixings key", KeyCode.G, "While placing: choose how the poster holds (pins, tape, nails, staples, clips, magnets, duct tape...). SHIFT + this key: which corner is curled");
            kEdit = c.Bind("Free placement", "Edit poster key", KeyCode.Y, "Look at a hung poster and press this key to edit it (size, angle, light, color, fixings, image, position). [P] puts it back as it was");
            kSwitch = c.Bind("Free placement", "Light switch key", KeyCode.O, "Look at a lit poster and press this key to switch its light on / off. SHIFT + this key: switch ALL poster lights of the hideout");
            kHelp = c.Bind("Free placement", "Keys help key", KeyCode.H, "While placing: show / hide the list of all keys");
            kStraight = c.Bind("Free placement", "Straighten key", KeyCode.Mouse2, "While placing: put the poster back straight (middle mouse button by default)");
            Load();
        }

        static bool InHideout()
        {
            try { return Comfort.Common.Singleton<EFT.GameWorld>.Instantiated && Comfort.Common.Singleton<EFT.GameWorld>.Instance is HideoutGameWorld; } catch { return false; }
        }

        void Note(string m) { msg = m; msgUntil = Time.time + 3f; PostersPlugin.Log.LogInfo("[pose] " + m); }
        internal static void Toast(string m) { if (me != null) me.Note(m); }
        static FreePlace me;
        internal static void Toast(string m, float secs) { if (me == null) return; me.msg = m; me.msgUntil = Time.time + secs; }

        static string K(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.Mouse2: return "MIDDLE CLICK"; case KeyCode.Mouse1: return "RIGHT CLICK"; case KeyCode.Mouse0: return "LEFT CLICK"; case KeyCode.Return: return "ENTER"; case KeyCode.Backspace: return "BACKSPACE"; case KeyCode.Delete: return "DEL";
                case KeyCode.LeftArrow: return "←"; case KeyCode.RightArrow: return "→";
                default: { var n = k.ToString(); return n.StartsWith("Keypad") ? "NUM " + n.Substring(6) : n.ToUpperInvariant(); }
            }
        }
        static string Row(string key, string what) { return "<mark=#ffffff1c><color=#e2dfd3> " + key + " </color></mark><pos=150>" + "<color=#a9a69c>" + what + "</color>\n"; }
        TextMeshProUGUI infoTx; string infoSet; float rowKeysW;
        class LiveKey { public Image Img, Hi; public TextMeshProUGUI Tx; public Vector2 Pos; public Func<bool> Down; public bool Was; }
        readonly System.Collections.Generic.List<LiveKey> live = new System.Collections.Generic.List<LiveKey>();
        float wheelUntil;
        Func<bool> PressTest(string label)
        {
            if (label == "WHEEL") return () => Time.unscaledTime < wheelUntil;
            if (label == "SHIFT") return () => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (label == "CTRL") return () => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            foreach (var e in new[] { kPrev, kNext, kBack, kRotL, kRotR, kStraight, kLight, kPlace, kMode, kHelp, kRemove, kFix, kEdit, kSwitch })
                if (e != null && K(e.Value) == label) { var kc = e.Value; return () => Input.GetKey(kc); }
            return () => false;
        }
        void AnimateKeys()
        {
            if (Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.001f) wheelUntil = Time.unscaledTime + 0.15f;
            live.RemoveAll(k => k.Img == null);
            foreach (var k in live)
            {
                bool d = k.Down(); if (d == k.Was) continue; k.Was = d;
                if (k.Hi != null) k.Hi.gameObject.SetActive(d); else k.Img.color = d ? Gold : Line;
                if (k.Tx != null) k.Tx.color = d ? new Color(0.07f, 0.07f, 0.07f) : CapText;
                ((RectTransform)k.Img.transform).anchoredPosition = d ? k.Pos + new Vector2(0, -1) : k.Pos;
            }
        }
        bool showHelp; GameObject helpBox, keyRow; string helpSig, rowSig; TextMeshProUGUI measure;
        static readonly Color CapFill = new Color(0.14f, 0.145f, 0.15f, 0.97f), CapEdge = new Color(0.44f, 0.44f, 0.42f), CapText = new Color(0.92f, 0.91f, 0.86f), DescC = new Color(0.72f, 0.71f, 0.67f);
        float Wd(string s, float size)
        {
            if (measure == null) { measure = Txt("Measure", canvasGo.transform, "", size, Color.clear, TextAlignmentOptions.Left, true); measure.gameObject.SetActive(false); }
            measure.fontSize = size; return measure.GetPreferredValues(s).x;
        }
        static RectTransform At(Component c, float x, float y, float w, float h) { var r = (RectTransform)c.transform; r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h); return r; }
        // une touche de clavier dessinee : case, contour, petite epaisseur en bas
        float Cap(Transform parent, string label, float x, float y, bool draw)
        {
            float w = Mathf.Max(30f, Wd(label, 14f) + 18f);
            if (!draw) return w;
            var sp = TkBtn();
            var b = Frame("Key", parent, CapFill, CapEdge, 1); b.raycastTarget = false; At(b, x, y, w, 26);
            Image hi = null;
            if (sp != null) { hi = Img("Hi", b.transform, Color.white); hi.sprite = sp; hi.type = Image.Type.Simple; hi.raycastTarget = false; hi.gameObject.SetActive(false); }
            var t = Txt("L", b.transform, label, 14, CapText, TextAlignmentOptions.Center, true); Place(t, Vector2.zero, Vector2.one, new Vector2(2, 2), new Vector2(-2, 0));
            live.Add(new LiveKey { Img = b, Hi = hi, Tx = t, Pos = new Vector2(x, y), Down = PressTest(label) });
            return w;
        }
        float Word(Transform parent, string s, float x, float y, float size, Color c, bool bold, bool draw)
        {
            float w = Wd(s, size) + 4f; if (!draw) return w;
            var t = Txt("T", parent, s, size, c, TextAlignmentOptions.MidlineLeft, bold); At(t, x, y, w + 6f, 26); return w;
        }
        // combinaisons : "+" entre deux touches, "/" pour l'une ou l'autre
        float Keys(Transform parent, string[] tok, float x, float y, bool draw)
        {
            float cx = x;
            foreach (var k in tok)
            {
                if (k == "+" || k == "/") cx += Word(parent, k, cx + 3, y, 15, Dim, true, draw) + 6;
                else cx += Cap(parent, k, cx, y, draw) + 4;
            }
            return cx - x - 4;
        }
        void BuildKeyRow()
        {
            var items = new[] {
                new System.Collections.Generic.KeyValuePair<string[], string>(new[] { K(kPlace.Value) }, "hang"),
                new System.Collections.Generic.KeyValuePair<string[], string>(new[] { K(kMode.Value) }, "cancel"),
                new System.Collections.Generic.KeyValuePair<string[], string>(new[] { K(kHelp.Value) }, showHelp ? "hide keys" : "all keys") };
            string sig = string.Join("|", items.Select(it => string.Join(",", it.Key) + it.Value).ToArray());
            sig += TkBtn() != null ? "#tk" : "";
            if (sig == rowSig && keyRow != null) return; rowSig = sig;
            if (keyRow != null) Destroy(keyRow);
            float total = 0; foreach (var it in items) total += Keys(null, it.Key, 0, 0, false) + 8 + Wd(it.Value, 17) + 4 + 30; total -= 30;
            rowKeysW = total;
            var bg = Frame("KeyRow", canvasGo.transform, new Color(0.03f, 0.032f, 0.035f, 0.9f), Line, 1); bg.raycastTarget = false;
            var r = (RectTransform)bg.transform; r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.1f); r.pivot = new Vector2(0.5f, 0f); r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(total + 40, 84);
            var top = Img("Stripe", bg.transform, Gold); Place(top, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -2), Vector2.zero); top.raycastTarget = false;
            infoTx = Txt("Info", bg.transform, "", 20, new Color(0.88f, 0.87f, 0.82f), TextAlignmentOptions.Center, false); Place(infoTx, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -40), new Vector2(-14, -8));
            var sep = Img("Sep", bg.transform, Line); Place(sep, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -45), new Vector2(-14, -44)); sep.raycastTarget = false;
            var keys = new GameObject("Keys"); var kr = Rt(keys, bg.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, Vector2.zero);
            kr.pivot = new Vector2(0.5f, 0); kr.sizeDelta = new Vector2(total, 34); kr.anchoredPosition = new Vector2(0, 6);
            float x = 0;
            foreach (var it in items) { x += Keys(keys.transform, it.Key, x, -4, true) + 8; x += Word(keys.transform, it.Value, x, -4, 17, DescC, false, true) + 30; }
            keyRow = bg.gameObject; infoSet = null;
        }
        void BuildHelp()
        {
            Func<string, string[]> one = k => new[] { k };
            var rows = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string[], string>> {
                new System.Collections.Generic.KeyValuePair<string[], string>(new[] { K(kPrev.Value), "/", K(kNext.Value) }, "change poster"),
                new System.Collections.Generic.KeyValuePair<string[], string>(one(K(kBack.Value)), "back to the list"),
                new System.Collections.Generic.KeyValuePair<string[], string>(one("WHEEL"), "size"),
                new System.Collections.Generic.KeyValuePair<string[], string>(new[] { "SHIFT", "+", "WHEEL" }, "fine size"),
                new System.Collections.Generic.KeyValuePair<string[], string>(new[] { K(kRotL.Value), "/", K(kRotR.Value) }, "rotate"),
                new System.Collections.Generic.KeyValuePair<string[], string>(new[] { "CTRL", "+", "WHEEL" }, "rotate by 15°"),
                new System.Collections.Generic.KeyValuePair<string[], string>(new[] { "CTRL", "+", "SHIFT", "+", "WHEEL" }, "rotate by 1°"),
                new System.Collections.Generic.KeyValuePair<string[], string>(one(K(kStraight.Value)), "straighten"),
                new System.Collections.Generic.KeyValuePair<string[], string>(one(K(kFix.Value)), "fixings: pins, tape, nails, clips..."),
                new System.Collections.Generic.KeyValuePair<string[], string>(new[] { "SHIFT", "+", K(kFix.Value) }, "curled corner: which one"),
                new System.Collections.Generic.KeyValuePair<string[], string>(new[] { "SHIFT", "+", K(kLight.Value) }, "light color"),
                new System.Collections.Generic.KeyValuePair<string[], string>(one(K(kLight.Value)), "light: backlit, lamp, neons, string lights, frame"),
                new System.Collections.Generic.KeyValuePair<string[], string>(one(K(kPlace.Value)), "hang"),
                new System.Collections.Generic.KeyValuePair<string[], string>(one(K(kMode.Value)), "cancel") };
            var after = new[] { new System.Collections.Generic.KeyValuePair<string[], string>(one(K(kEdit.Value)), "edit the poster you look at"), new System.Collections.Generic.KeyValuePair<string[], string>(one(K(kSwitch.Value)), "light switch (look at a lit poster)"), new System.Collections.Generic.KeyValuePair<string[], string>(new[] { "SHIFT", "+", K(kSwitch.Value) }, "switch ALL the lights"), new System.Collections.Generic.KeyValuePair<string[], string>(one(K(kRemove.Value)), "take down the poster you look at"), new System.Collections.Generic.KeyValuePair<string[], string>(new[] { "SHIFT", "+", K(kRemove.Value) }, "repair it (remove bullet holes)") };
            string sig = string.Join("|", rows.Concat(after).Select(it => string.Join(",", it.Key) + it.Value).ToArray());
            sig += TkBtn() != null ? "#tk" : "";
            if (sig == helpSig && helpBox != null) return; helpSig = sig;
            if (helpBox != null) Destroy(helpBox);
            float keyCol = 0, descW = 0;
            foreach (var it in rows.Concat(after)) { keyCol = Mathf.Max(keyCol, Keys(null, it.Key, 0, 0, false)); descW = Mathf.Max(descW, Wd(it.Value, 16)); }
            keyCol += 24; const float RH = 29f, PAD = 18f;
            float W = PAD + keyCol + descW + PAD + 40, H = PAD + 30 + 10 + rows.Count * RH + 14 + 24 + after.Length * RH + PAD - 6;
            var box = Frame("Help", canvasGo.transform, new Color(0.03f, 0.032f, 0.035f, 0.93f), Line, 1); box.raycastTarget = false;
            var r = (RectTransform)box.transform; r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f); r.pivot = new Vector2(1f, 0.5f); r.anchoredPosition = new Vector2(-28f, 40f); r.sizeDelta = new Vector2(W, H);
            if (H > 860f) box.transform.localScale = Vector3.one * (860f / H);   // toujours dans l'ecran, au-dessus du bandeau
            var top = Img("Stripe", box.transform, Gold); Place(top, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -2), Vector2.zero); top.raycastTarget = false;
            float y = -PAD + 2;
            Word(box.transform, "PLACEMENT KEYS", PAD, y, 19, CapText, true, true); y -= 32;
            var l1 = Img("Sep", box.transform, Line); At(l1, PAD, y, W - 2 * PAD, 1); l1.raycastTarget = false; y -= 10;
            foreach (var it in rows) { Keys(box.transform, it.Key, PAD, y, true); Word(box.transform, it.Value, PAD + keyCol, y, 16, DescC, false, true); y -= RH; }
            y -= 4; var l2 = Img("Sep", box.transform, Line); At(l2, PAD, y, W - 2 * PAD, 1); l2.raycastTarget = false; y -= 8;
            Word(box.transform, "WHEN NOT PLACING", PAD, y + 2, 14, Dim, true, true); y -= 26;
            foreach (var it in after) { Keys(box.transform, it.Key, PAD, y, true); Word(box.transform, it.Value, PAD + keyCol, y, 16, DescC, false, true); y -= RH; }
            helpBox = box.gameObject;
        }
        void UpdateHud()
        {
            try
            {
                if (Bender() == null) return;    // pas de Bender : on garde le bandeau simple
                EnsureCanvas();
                string t = "", ht = "";
                if (!pick && mode)
                {
                    var files = PostersPlugin.Files;
                    string name = files.Count > 0 ? Path.GetFileNameWithoutExtension(files[Mathf.Clamp(idx, 0, files.Count - 1)]) : "-";
                    var cf = files[Mathf.Clamp(idx, 0, files.Count - 1)];
                    string lt = Glow.Names[Mathf.Clamp(ghostLit, 0, Glow.Names.Length - 1)];
                    t = (editBackup != null ? "<color=#c7c4b5>EDITING</color>   " : "") + "<b>" + name + "</b>  <color=#8a8a84>(" + (idx + 1) + "/" + files.Count + ")</color>     size <b>x" + GhostSize(cf).ToString("0.00") + "</b>     angle <b>" + Mathf.RoundToInt(roll) + "°</b>     light <b>" + lt + (ghostLit != 0 && ghostCol != 0 ? " · " + Glow.ColNames[ghostCol] : "") + "</b>     fix <b>" + Look.FixNames[ghostFix] + "</b>     corner <b>" + Look.CurlNames[ghostCurl] + "</b>";
                    ht = "x";
                }
                else if (!pick && Time.time <= msgUntil) t = msg;
                if (!(!pick && mode && ht.Length > 0) && hudText.text != t) { hudText.text = t; hudShadow.text = t; }
                bool placing = !pick && mode && ht.Length > 0;
                if (placing)
                {
                    BuildKeyRow();
                    if (infoTx != null && infoSet != t)
                    {
                        infoSet = t; infoTx.text = t;
                        float w = Mathf.Max(rowKeysW, infoTx.GetPreferredValues(t).x) + 40f;
                        ((RectTransform)keyRow.transform).sizeDelta = new Vector2(w, 84);
                    }
                    if (hudText.text.Length > 0) { hudText.text = ""; hudShadow.text = ""; }
                } if (keyRow != null && keyRow.activeSelf != placing) keyRow.SetActive(placing);
                if (placing) AnimateKeys();
                bool show = placing && showHelp;
                if (show) BuildHelp(); if (helpBox != null && helpBox.activeSelf != show) helpBox.SetActive(show);
            }
            catch { }
        }

        int slowN, spawnGen;
        // remet les affiches au mur une par frame (toutes d'un coup = gros freeze en entrant dans la planque)
        IEnumerator SpawnAll(int gen)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew(); double img0 = PostersPlugin.ImgMs, worst = 0; int n = 0;
            foreach (var p in placed.ToList())
            {
                if (!spawned || gen != spawnGen) yield break;   // on a quitte la planque entre-temps
                if (p != null && p.Go == null && placed.Contains(p))
                {
                    p.Fallen = false;
                    var one = System.Diagnostics.Stopwatch.StartNew();
                    try { Spawn(p); n++; } catch (Exception e) { PostersPlugin.Log.LogWarning("[pose] " + e.Message); }
                    worst = Math.Max(worst, one.Elapsed.TotalMilliseconds);
                }
                yield return null;
            }
            PostersPlugin.Log.LogInfo("[pose] " + n + " poster(s) libre(s) remis au mur");
            PostersPlugin.Log.LogInfo("[perf] affiches remises au mur une par frame : " + sw.ElapsedMilliseconds + " ms au total, pire frame " + (int)worst + " ms (images : " + (int)(PostersPlugin.ImgMs - img0) + " ms)");
        }
        void Update()
        {
            UpdateHud();
            bool inH = InHideout();
            // [perf] frames lentes dans la planque (au plus 15 par entree)
            if (inH && slowN < 15 && Time.unscaledDeltaTime > 0.1f) { slowN++; PostersPlugin.Log.LogInfo("[perf] frame lente : " + (int)(Time.unscaledDeltaTime * 1000f) + " ms (t=" + Time.unscaledTime.ToString("0.0") + " s)"); }
            if (!inH) slowN = 0;
            if (!inH)
            {
                if (spawned) { foreach (var p in placed) if (p.Go != null) Destroy(p.Go); spawned = false; }
                if (mode) Stop();
                if (pick) ClosePicker();
                return;
            }
            if (!spawned && Time.time > nextCheck) { nextCheck = Time.time + 1f; if (Camera.main != null) { spawned = true; StartCoroutine(SpawnAll(++spawnGen)); } }

            if (pick)
            {
                // fenetre ouverte : souris libre, Echap ou P pour fermer
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                bool typing = (search != null && search.isFocused) || (renameField != null && renameField.isFocused);
                if (Input.GetKeyDown(KeyCode.Escape)) { if (typing) { if (search != null) search.DeactivateInputField(); if (renameField != null) { renameField.DeactivateInputField(); if (renameRow != null) renameRow.SetActive(false); } } else ClosePicker(); }
                else if (!typing && Input.GetKeyDown(kMode.Value)) ClosePicker();
                return;
            }
            if (Input.GetKeyDown(kMode.Value)) { if (mode) { Stop(); Note("placement cancelled"); } else OpenPicker(); }
            if (Input.GetKeyDown(kRemove.Value) && !mode)
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) RepairLooked(); else RemoveLooked();
            }
            if (Input.GetKeyDown(kEdit.Value) && !mode) EditLooked();
            if (Input.GetKeyDown(kSwitch.Value) && !mode) { if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) SwitchAll(); else SwitchLooked(); }
            if (!mode) return;
            var files = PostersPlugin.Files;
            if (files.Count == 0) { Stop(); Note("no image in BepInEx/config/HideoutPosters"); return; }
            if (Input.GetKeyDown(kBack.Value)) { var cur = files[Mathf.Clamp(idx, 0, files.Count - 1)]; Stop(); selected = cur; OpenPicker(); return; }
            if (Input.GetKeyDown(kNext.Value)) idx = (idx + 1) % files.Count;
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Mathf.Abs(wheel) > 0.001f) { bool f5 = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); roll = Mathf.Round((roll + (wheel > 0 ? 1f : -1f) * (f5 ? 1f : 15f)) * 100f) / 100f; wheel = 0f; }
            if (Input.GetKey(kRotL.Value)) roll += 90f * Time.deltaTime;
            if (Input.GetKey(kRotR.Value)) roll -= 90f * Time.deltaTime;
            if (Input.GetKeyDown(kStraight.Value)) roll = 0f;
            roll = Mathf.Repeat(roll + 180f, 360f) - 180f;
            if (Mathf.Abs(wheel) > 0.001f && idx >= 0 && idx < files.Count)
            {
                bool fine = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                float dz = (wheel > 0 ? 1f : -1f) * (fine ? 0.05f : 0.15f);
                if (ghostScale > 0f) { float nv = Mathf.Clamp(Mathf.Round((ghostScale + dz) * 20f) / 20f, 0.25f, 4f); if (!Mathf.Approximately(nv, ghostScale)) { ghostScale = nv; if (ghost != null) Destroy(ghost); ghost = null; } }
                else if (PostersPlugin.NudgeSize(files[idx], dz))
                { if (ghost != null) Destroy(ghost); ghost = null; }   // meme look (punaises, coin) : seule la taille change
            }
            if (Input.GetKeyDown(kPrev.Value)) { idx = (idx - 1 + files.Count) % files.Count; for (int s2 = 0; s2 < files.Count && PostersPlugin.IsLocked(files[idx]); s2++) idx = (idx - 1 + files.Count) % files.Count; }
            if (Input.GetKeyDown(kNext.Value)) for (int s2 = 0; s2 < files.Count && PostersPlugin.IsLocked(files[idx]); s2++) idx = (idx + 1) % files.Count;
            idx = Mathf.Clamp(idx, 0, files.Count - 1);
            if (PostersPlugin.IsLocked(files[idx])) { Stop(); Note("no unlocked poster yet: find them in raids"); return; }
            string file = files[idx];
            RaycastHit hit;
            if (!Aim(out hit)) { if (ghost != null) ghost.SetActive(false); return; }
            if (Input.GetKeyDown(kHelp.Value)) showHelp = !showHelp;
            if (Input.GetKeyDown(kFix.Value))
            {
                bool sh = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (sh) { ghostCurl = (ghostCurl + 1) % Look.CurlNames.Length; Note("corner: " + Look.CurlNames[ghostCurl]); }
                else { ghostFix = (ghostFix + 1) % Look.FixNames.Length; Note("fixings: " + Look.FixNames[ghostFix]); }
                if (ghost != null) Destroy(ghost); ghost = null;
            }
            if (Input.GetKeyDown(kLight.Value) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) { ghostCol = (ghostCol + 1) % Glow.ColNames.Length; if (ghost != null) Destroy(ghost); ghost = null; Note("light color: " + Glow.ColNames[ghostCol]); }
            else if (Input.GetKeyDown(kLight.Value)) { ghostLit = (ghostLit + 1) % Glow.Names.Length; if (ghost != null) Destroy(ghost); ghost = null; Note("light: " + Glow.Names[ghostLit]); }
            if (ghost == null || ghostFile != file) { if (ghost != null) Destroy(ghost); if (ghostFile != file || ghostSeed == 0) { ghostSeed = UnityEngine.Random.Range(1, int.MaxValue); if (ghostFile != null && ghostFile != file) ghostScale = -1f; } ghost = Build(file, GhostSize(file), ghostSeed, ghostLit, ghostFix, ghostCurl, ghostCol); ghostFile = file; }
            if (ghost == null) return;
            ghost.SetActive(true);
            Quaternion rot = Orient(hit.normal) * Quaternion.Euler(0f, 0f, roll);
            ghost.transform.SetPositionAndRotation(hit.point, rot);
            if (Input.GetKeyDown(kPlace.Value))
            {
                // pas deux affiches au meme endroit (double validation = doublon invisible qui cache les trous)
                foreach (var q in placed)
                {
                    if (q == null || q.Go == null) continue;
                    var qt = q.Go.transform;
                    if ((qt.position - hit.point).sqrMagnitude < 0.12f * 0.12f && Vector3.Dot(qt.forward, rot * Vector3.forward) > 0.9f)
                    { Note("a poster is already here: take it down first"); PostersPlugin.Log.LogInfo("[pose] doublon refuse : " + Path.GetFileNameWithoutExtension(q.File) + " est deja la"); return; }
                }
                if (editBackup != null && editHoles != null && editHoles.Count > 0) BulletHoles.Rebuild(ghost, editHoles);
                var p = new Placed { Holes = editBackup != null && editHoles != null ? editHoles : new System.Collections.Generic.List<Vector3>(), File = file, Pos = hit.point, Rot = rot, Go = ghost, Scale = GhostSize(file), Seed = ghostSeed, Lit = ghostLit, Fix = ghostFix, Curl = ghostCurl, Col = ghostCol };
                var anc = MovingAnchor(hit.collider);
                if (anc != null) { p.Anchor = PathOf(anc); p.LPos = anc.InverseTransformPoint(hit.point); p.LRot = Quaternion.Inverse(anc.rotation) * rot; ghost.transform.SetParent(anc, true); PostersPlugin.Log.LogInfo("[pose] accrochee a un objet mobile : " + p.Anchor); }
                placed.Add(p); ghost = null; ghostFile = null; Save();
                if (editBackup != null) { editBackup = null; ghostScale = -1f; Stop(); Note("poster updated: " + Path.GetFileNameWithoutExtension(file)); return; }
                Note("poster hung: " + Path.GetFileNameWithoutExtension(file) + " (" + placed.Count + " free poster(s))");
            }
        }

        void Start2()
        {
            if (Camera.main == null) return;
            mode = true; Note("placement mode: aim at a wall");
        }
        void OpenPicker()
        {
            if (Camera.main == null) return;
            if (PostersPlugin.Files.Count == 0) { Note("no image in BepInEx/config/HideoutPosters"); return; }
            pick = true; hover = -1; oldLock = Cursor.lockState; oldVis = Cursor.visible;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            try { uiOk = BuildPickerUI(); } catch (Exception e) { uiOk = false; PostersPlugin.Log.LogWarning("[pose] fenetre du jeu impossible, fenetre simple : " + e.Message); }
        }
        void ClosePicker() { pick = false; Cursor.lockState = oldLock; Cursor.visible = oldVis; if (pickerGo != null) Destroy(pickerGo); pickerGo = null; }

        // ===== interface du jeu (TextMeshPro, police Bender) =====
        TextMeshProUGUI plTxt; Image plImg;
        bool uiOk; GameObject canvasGo, pickerGo; TextMeshProUGUI hudText, hudShadow;
        static TMP_FontAsset bender; static bool benderSearched;
        internal static TMP_FontAsset Bender()
        {
            if (benderSearched) return bender;
            benderSearched = true;
            try
            {
                var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                // Bender droit en priorite (ni gras ni italique)
                foreach (var f in all) if (f != null && f.name.IndexOf("bender", StringComparison.OrdinalIgnoreCase) >= 0 && f.name.IndexOf("bold", StringComparison.OrdinalIgnoreCase) < 0 && f.name.IndexOf("italic", StringComparison.OrdinalIgnoreCase) < 0) { bender = f; break; }
                if (bender == null) foreach (var f in all) if (f != null && f.name.IndexOf("bender", StringComparison.OrdinalIgnoreCase) >= 0 && f.name.IndexOf("italic", StringComparison.OrdinalIgnoreCase) < 0) { bender = f; break; }
                if (bender == null) foreach (var f in all) if (f != null && f.name.IndexOf("bender", StringComparison.OrdinalIgnoreCase) >= 0) { bender = f; break; }
                PostersPlugin.Log.LogInfo("[pose] police TMP : " + (bender != null ? bender.name : "Bender introuvable") + " (" + all.Length + " polices : " + string.Join(", ", all.Take(15).Select(f => f != null ? f.name : "-").ToArray()) + ")");
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[pose] police TMP : " + e.Message); }
            return bender;
        }
        static Sprite tkBtn; static float tkTry;
        // le fond creme texture des boutons actifs de Tarkov (« button_back_big »)
        public static Sprite TkBtn()
        {
            if (tkBtn != null || Time.unscaledTime < tkTry) return tkBtn;
            tkTry = Time.unscaledTime + 5f;
            try { tkBtn = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(x => x != null && x.name == "button_back_big"); } catch { }
            PostersPlugin.Log.LogInfo("[pose] sprite du jeu button_back_big : " + (tkBtn != null ? "trouve" : "introuvable (style simple)"));
            return tkBtn;
        }
        // comme le menu du jeu : juste le mot ; le fond creme apparait en fondu (survol, selection)
        static void Active(Image bg, TextMeshProUGUI tx, bool on)
        {
            if (bg == null) return;
            var f = bg.GetComponent<BtnFade>();
            if (f == null) { f = bg.gameObject.AddComponent<BtnFade>(); f.bg = bg; f.tx = tx; f.v = on ? 1f : 0f; f.target = f.v; f.Apply(); }
            f.target = on ? 1f : 0f; f.dim = false;
        }
        static Sprite white;
        static Sprite White() { if (white == null) white = Sprite.Create(Solid(), new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f)); return white; }
        internal static RectTransform Rt(GameObject go, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var r = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
            r.SetParent(parent, false); r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = oMin; r.offsetMax = oMax; return r;
        }
        internal static Image Img(string n, Transform parent, Color c)
        {
            var go = new GameObject(n); Rt(go, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var im = go.AddComponent<Image>(); im.sprite = White(); im.color = c; return im;
        }
        internal static TextMeshProUGUI Txt(string n, Transform parent, string text, float size, Color c, TextAlignmentOptions al, bool bold)
        {
            var go = new GameObject(n); Rt(go, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var t = go.AddComponent<TextMeshProUGUI>();
            var f = Bender(); if (f != null) t.font = f;
            t.text = text; t.fontSize = size; t.color = c; t.alignment = al; t.raycastTarget = false; t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis; if (bold) t.fontStyle = FontStyles.Bold; t.richText = true;
            return t;
        }
        void EnsureCanvas()
        {
            if (canvasGo != null) return;
            canvasGo = new GameObject("HideoutPosters_UI");
            DontDestroyOnLoad(canvasGo);
            var cv = canvasGo.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 32000;
            var sc = canvasGo.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            // bandeau de pose en bas de l'ecran
            var hud = new GameObject("Hud"); Rt(hud, canvasGo.transform, new Vector2(0, 0.13f), new Vector2(1, 0.2f), Vector2.zero, Vector2.zero);
            hudShadow = Txt("Shadow", hud.transform, "", 22, Color.black, TextAlignmentOptions.Center, false); ((RectTransform)hudShadow.transform).offsetMin = new Vector2(2, -2); ((RectTransform)hudShadow.transform).offsetMax = new Vector2(2, -2);
            hudText = Txt("Text", hud.transform, "", 22, new Color(0.85f, 0.84f, 0.79f), TextAlignmentOptions.Center, false);
            hudText.enableWordWrapping = true; hudShadow.enableWordWrapping = true;
        }
        // miniature opaque : l'image du mur a un alpha tres bas (reglage du reflet), elle serait presque invisible dans l'interface
        static readonly System.Collections.Generic.Dictionary<string, Texture2D> thumbs = new System.Collections.Generic.Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        static Texture2D Thumb(string file)
        {
            Texture2D t;
            if (thumbs.TryGetValue(file, out t) && t != null) return t;
            var src = PostersPlugin.LoadImage(file); if (src == null) return null;
            float sc = Mathf.Min(1f, 320f / Mathf.Max(src.width, src.height));
            int w = Mathf.Max(1, (int)(src.width * sc)), h = Mathf.Max(1, (int)(src.height * sc));
            var px = new Color32[w * h];
            bool stk = PostersPlugin.IsSticker(file);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { Color c = src.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h); if (stk) c = Color.Lerp(Card, c, c.a); c.a = 1f; px[y * w + x] = c; }
            t = new Texture2D(w, h, TextureFormat.RGBA32, false); t.SetPixels32(px); t.Apply(false, true); t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            thumbs[file] = t; return t;
        }
        // ===== Fenetre P, facon menus de Tarkov =====
        // palette Tarkov moderne : gris tres sombres, blanc casse en accent, aucun dore
        static readonly Color Gold = new Color(0.78f, 0.77f, 0.71f), Panel = new Color(0.035f, 0.037f, 0.04f, 0.98f), Card = new Color(0.085f, 0.088f, 0.092f), Line = new Color(0.22f, 0.225f, 0.23f), TextC = new Color(0.82f, 0.81f, 0.76f), Dim = new Color(0.5f, 0.5f, 0.48f);
        int tab;            // 0 = toutes les affiches, 1 = au mur
        int sortMode;       // 0 = nom, 1 = format, 2 = recentes
        string query = "", selected;
        TMP_InputField search;
        Transform gridContent;
        readonly System.Collections.Generic.Dictionary<Image, string> cards = new System.Collections.Generic.Dictionary<Image, string>();
        RawImage pvImg; AspectRatioFitter pvFit; TextMeshProUGUI pvName, pvInfo, pvSize, sortTx, countTx; Slider pvSlider; bool sliderLock;
        Image tabAllBg, tabWallBg, tabFavBg; TextMeshProUGUI tabAllTx, tabWallTx, tabFavTx, folderTx, favTx;
        string category = null;   // null = tous les dossiers
        TMP_InputField renameField; GameObject renameRow;
        // ----- favoris -----
        static readonly System.Collections.Generic.HashSet<string> favs = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool favLoaded;
        static string FavFile { get { return Path.Combine(BepInEx.Paths.ConfigPath, "HideoutPosters_favorites.txt"); } }
        static void LoadFavs() { if (favLoaded) return; favLoaded = true; try { if (File.Exists(FavFile)) foreach (var l in File.ReadAllLines(FavFile)) if (l.Trim().Length > 0) favs.Add(l.Trim()); } catch { } }
        static void SaveFavs() { try { File.WriteAllLines(FavFile, favs.ToArray()); } catch { } }
        static bool IsFav(string f) { LoadFavs(); return favs.Contains(PostersPlugin.Rel(f)); }
        static void ToggleFav(string f) { LoadFavs(); var r = PostersPlugin.Rel(f); if (!favs.Remove(r)) favs.Add(r); SaveFavs(); }
        System.Collections.Generic.List<string> Categories()
        {
            var l = new System.Collections.Generic.List<string> { null };
            foreach (var c in PostersPlugin.Files.Select(PostersPlugin.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.OrdinalIgnoreCase)) l.Add(c);
            return l;
        }
        float lastClick; string lastClickFile;
        static readonly System.Collections.Generic.Dictionary<string, long> recent = new System.Collections.Generic.Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        static bool recentLoaded;
        static string RecentFile { get { return Path.Combine(BepInEx.Paths.ConfigPath, "HideoutPosters_recent.txt"); } }
        static void LoadRecent()
        {
            if (recentLoaded) return; recentLoaded = true;
            try { if (File.Exists(RecentFile)) foreach (var l in File.ReadAllLines(RecentFile)) { var a = l.Split('|'); long t; if (a.Length == 2 && long.TryParse(a[1], out t)) recent[a[0]] = t; } } catch { }
        }
        static void Touch(string file)
        {
            LoadRecent(); recent[Path.GetFileName(file)] = DateTime.Now.Ticks;
            try { File.WriteAllLines(RecentFile, recent.Select(kv => kv.Key + "|" + kv.Value).ToArray()); } catch { }
        }
        static string Fmt(Texture2D t) { if (t == null) return "?"; float a = (float)t.width / t.height; return a > 1.08f ? "LANDSCAPE" : a < 0.92f ? "PORTRAIT" : "SQUARE"; }

        static Image Frame(string n, Transform parent, Color fill, Color border, float bw)
        {
            var b = Img(n, parent, border);
            var f = Img("Fill", b.transform, fill); f.raycastTarget = false;
            ((RectTransform)f.transform).offsetMin = new Vector2(bw, bw); ((RectTransform)f.transform).offsetMax = new Vector2(-bw, -bw);
            return b;
        }
        static RectTransform Place(Component c, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax) { var r = (RectTransform)c.transform; r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = oMin; r.offsetMax = oMax; return r; }
        Button TextButton(string n, Transform parent, string label, float size, Action onClick, out Image bg, out TextMeshProUGUI tx)
        {
            bg = Img(n, parent, new Color(0.16f, 0.165f, 0.17f));
            var btn = bg.gameObject.AddComponent<Button>(); var cb = btn.colors; cb.highlightedColor = Color.white; cb.pressedColor = new Color(0.8f, 0.8f, 0.8f); btn.colors = cb;
            btn.onClick.AddListener(() => onClick());
            tx = Txt("Label", bg.transform, label, size, TextC, TextAlignmentOptions.Center, true);
            if (n != "Star" && n != "Close")
            {
                Active(bg, tx, false);
                var b0 = bg; var t0 = tx; var hv = bg.gameObject.AddComponent<CardHover>();
                hv.enter = () => Active(b0, t0, true);
                hv.exit = () => RestoreBtn(b0, t0);
            }
            return btn;
        }

        readonly System.Collections.Generic.Dictionary<Image, Func<bool>> btnOn = new System.Collections.Generic.Dictionary<Image, Func<bool>>();
        void RestoreBtn(Image b, TextMeshProUGUI t)
        {
            Func<bool> f; bool on = b != null && btnOn.TryGetValue(b, out f) && f();
            Active(b, t, on);
            if (b == plImg && selected != null && PostersPlugin.IsLocked(selected)) { var f2 = b.GetComponent<BtnFade>(); if (f2 != null) { f2.dim = true; f2.Apply(); } }
        }
        bool BuildPickerUI()
        {
            EnsureCanvas(); LoadRecent();
            if (EventSystem.current == null) { var es = new GameObject("HideoutPosters_EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>(); DontDestroyOnLoad(es); }
            if (pickerGo != null) Destroy(pickerGo);
            var files = PostersPlugin.Files;
            if (selected == null || !files.Contains(selected)) selected = files.Count > 0 ? files[0] : null;
            pickerGo = new GameObject("Picker"); Rt(pickerGo, canvasGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var back = Img("Back", pickerGo.transform, new Color(0, 0, 0, 0.55f));
            var bb = back.gameObject.AddComponent<Button>(); bb.transition = Selectable.Transition.None; bb.onClick.AddListener(() => ClosePicker());

            var win = Frame("Window", pickerGo.transform, Panel, Line, 1);
            var wr = (RectTransform)win.transform; wr.anchorMin = wr.anchorMax = new Vector2(0.5f, 0.5f); wr.sizeDelta = new Vector2(1380, 820); wr.anchoredPosition = Vector2.zero;
            win.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;   // un clic dans la fenetre ne la ferme pas

            // ----- en-tete : bande doree, titre, onglets, recherche, tri, fermer -----
            var head = Img("Head", win.transform, new Color(0.065f, 0.068f, 0.072f)); Place(head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, -64), new Vector2(-1, -1));
            var stripe = Img("Stripe", head.transform, Gold); Place(stripe, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 2)); stripe.raycastTarget = false;
            var tl = Txt("Title", head.transform, "HIDEOUT POSTERS", 27, Gold, TextAlignmentOptions.Left, true); Place(tl, Vector2.zero, new Vector2(0, 1), new Vector2(20, 0), new Vector2(285, 0));
            TextButton("TabAll", head.transform, "ALL", 16, () => { tab = 0; RefreshGrid(); }, out tabAllBg, out tabAllTx); Place(tabAllBg, new Vector2(0, 0), new Vector2(0, 1), new Vector2(292, 12), new Vector2(372, -12));
            TextButton("TabFav", head.transform, "★ FAVORITES", 16, () => { tab = 2; RefreshGrid(); }, out tabFavBg, out tabFavTx); Place(tabFavBg, new Vector2(0, 0), new Vector2(0, 1), new Vector2(378, 12), new Vector2(528, -12));
            TextButton("TabWall", head.transform, "ON THE WALL", 16, () => { tab = 1; RefreshGrid(); }, out tabWallBg, out tabWallTx); Place(tabWallBg, new Vector2(0, 0), new Vector2(0, 1), new Vector2(534, 12), new Vector2(684, -12));
            Image fBg; TextButton("Folder", head.transform, "", 15, () => { var cats = Categories(); int k = cats.IndexOf(category); category = cats[(k + 1) % cats.Count]; RefreshGrid(); }, out fBg, out folderTx); Place(fBg, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-668, 12), new Vector2(-478, -12));
            // recherche
            var sBox = Frame("Search", head.transform, new Color(0.04f, 0.04f, 0.045f), Line, 1); Place(sBox, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-470, 12), new Vector2(-282, -12));
            var sArea = new GameObject("TextArea"); Rt(sArea, sBox.transform, Vector2.zero, Vector2.one, new Vector2(12, 2), new Vector2(-10, -2)); sArea.AddComponent<RectMask2D>();
            var ph = Txt("Placeholder", sArea.transform, "Search...", 18, Dim, TextAlignmentOptions.Left, false); ph.fontStyle = FontStyles.Italic;
            var st = Txt("Text", sArea.transform, "", 18, TextC, TextAlignmentOptions.Left, false);
            search = sBox.gameObject.AddComponent<TMP_InputField>();
            search.textViewport = (RectTransform)sArea.transform; search.textComponent = st; search.placeholder = ph; search.fontAsset = st.font; search.pointSize = 18;
            search.text = query; search.onValueChanged.AddListener(v => { query = v ?? ""; RefreshGrid(); });
            // tri
            Image sBg; TextButton("Sort", head.transform, "", 16, () => { sortMode = (sortMode + 1) % 3; RefreshGrid(); }, out sBg, out sortTx); Place(sBg, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-274, 12), new Vector2(-88, -12));
            // fermer
            Image xBg; TextMeshProUGUI xTx; TextButton("Close", head.transform, "×", 26, () => ClosePicker(), out xBg, out xTx);
            // croix de fermeture facon fenetres du jeu : petit carre sombre
            { var xr = (RectTransform)xBg.transform; xr.anchorMin = xr.anchorMax = new Vector2(1f, 0.5f); xr.pivot = new Vector2(1f, 0.5f); xr.anchoredPosition = new Vector2(-18f, 0f); xr.sizeDelta = new Vector2(34f, 34f); }
            xBg.color = new Color(0.02f, 0.02f, 0.022f, 1f); xTx.fontStyle = FontStyles.Normal; xTx.color = new Color(0.86f, 0.85f, 0.8f);
            Place(xTx, Vector2.zero, Vector2.one, new Vector2(0, 2), Vector2.zero);
            { var xb0 = xBg; var xt0 = xTx; var xh = xBg.gameObject.AddComponent<CardHover>(); xh.enter = () => { xb0.color = new Color(0.22f, 0.22f, 0.23f); xt0.color = Color.white; }; xh.exit = () => { xb0.color = new Color(0.02f, 0.02f, 0.022f, 1f); xt0.color = new Color(0.86f, 0.85f, 0.8f); }; }

            // ----- grille a gauche -----
            var sv = new GameObject("Scroll"); Rt(sv, win.transform, Vector2.zero, Vector2.one, new Vector2(16, 70), new Vector2(-400, -78));
            var srect = sv.AddComponent<ScrollRect>(); srect.horizontal = false; srect.scrollSensitivity = 50f; srect.movementType = ScrollRect.MovementType.Clamped;
            var vp = Img("Viewport", sv.transform, new Color(0, 0, 0, 0.01f)); vp.gameObject.AddComponent<RectMask2D>();
            var content = new GameObject("Content"); var crt = Rt(content, vp.transform, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero); crt.pivot = new Vector2(0.5f, 1f);
            var grid = content.AddComponent<GridLayoutGroup>(); grid.cellSize = new Vector2(176, 222); grid.spacing = new Vector2(12, 12); grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 5; grid.padding = new RectOffset(6, 6, 6, 6);
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            srect.viewport = (RectTransform)vp.transform; srect.content = crt; gridContent = content.transform;
            countTx = Txt("Count", win.transform, "", 16, Dim, TextAlignmentOptions.Left, false); Place(countTx, new Vector2(0, 0), new Vector2(1, 0), new Vector2(22, 38), new Vector2(-400, 64));
            var help = Txt("Help", win.transform, "Click: select  ·  Double-click / PLACE: hang it  ·  [Esc] / [" + kMode.Value + "] close", 16, Dim, TextAlignmentOptions.Left, false); Place(help, new Vector2(0, 0), new Vector2(1, 0), new Vector2(22, 10), new Vector2(-400, 36));

            // ----- apercu a droite -----
            var side = Frame("Preview", win.transform, new Color(0.07f, 0.075f, 0.08f), Line, 1); Place(side, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-384, 16), new Vector2(-16, -78));
            var pvBox = new GameObject("Pic"); Rt(pvBox, side.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -356), new Vector2(-16, -16));
            var pvGo = new GameObject("Img"); Rt(pvGo, pvBox.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            pvImg = pvGo.AddComponent<RawImage>(); pvImg.raycastTarget = false; pvFit = pvGo.AddComponent<AspectRatioFitter>(); pvFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            pvName = Txt("Name", side.transform, "", 24, Gold, TextAlignmentOptions.Left, true); Place(pvName, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -398), new Vector2(-18, -364));
            pvInfo = Txt("Info", side.transform, "", 16, Dim, TextAlignmentOptions.TopLeft, false); Place(pvInfo, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -462), new Vector2(-18, -402)); pvInfo.enableWordWrapping = true;
            pvSize = Txt("SizeLbl", side.transform, "", 17, TextC, TextAlignmentOptions.Left, true); Place(pvSize, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -494), new Vector2(-18, -468));
            pvSlider = MakeSlider(side.transform); Place(pvSlider, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -520), new Vector2(-18, -500));
            pvSlider.onValueChanged.AddListener(v =>
            {
                if (sliderLock || selected == null) return;
                PostersPlugin.NudgeSize(selected, v - PostersPlugin.SizeOf(selected));
                pvSize.text = "SIZE  x" + PostersPlugin.SizeOf(selected).ToString("0.00");
            });
            Image fvBg; TextButton("FavBtn", side.transform, "", 16, () => { if (selected != null) { ToggleFav(selected); RefreshGrid(); } }, out fvBg, out favTx);
            Place(fvBg, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(18, -572), new Vector2(-4, -536));
            Image rnBg; TextMeshProUGUI rnTx; TextButton("RenameBtn", side.transform, "RENAME", 16, () => StartRename(), out rnBg, out rnTx);
            Place(rnBg, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(4, -572), new Vector2(-18, -536));
            // champ de renommage (cache tant qu'on ne clique pas RENAME)
            var rb = Frame("RenameBox", side.transform, new Color(0.04f, 0.04f, 0.045f), Gold, 1); Place(rb, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -398), new Vector2(-18, -364));
            renameRow = rb.gameObject;
            var ra = new GameObject("TextArea"); Rt(ra, rb.transform, Vector2.zero, Vector2.one, new Vector2(10, 2), new Vector2(-10, -2)); ra.AddComponent<RectMask2D>();
            var rph = Txt("Placeholder", ra.transform, "New name + Enter", 18, Dim, TextAlignmentOptions.Left, false);
            var rtx = Txt("Text", ra.transform, "", 18, TextC, TextAlignmentOptions.Left, false);
            renameField = rb.gameObject.AddComponent<TMP_InputField>(); renameField.textViewport = (RectTransform)ra.transform; renameField.textComponent = rtx; renameField.placeholder = rph; renameField.fontAsset = rtx.font; renameField.pointSize = 18;
            renameField.onSubmit.AddListener(v => ApplyRename(v));
            renameRow.SetActive(false);
            Image rlBg; TextMeshProUGUI rlTx; TextButton("Reload", side.transform, "↻ RELOAD", 16, () => DoReload(), out rlBg, out rlTx);
            Place(rlBg, new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(18, 82), new Vector2(-4, 118));
            Image ofBg; TextMeshProUGUI ofTx; TextButton("OpenFolder", side.transform, "OPEN FOLDER", 16, () => { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + PostersPlugin.FolderPath + "\""); } catch (Exception e) { PostersPlugin.Log.LogWarning("[pose] dossier : " + e.Message); } }, out ofBg, out ofTx);
            Place(ofBg, new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(4, 82), new Vector2(-18, 118));
            Image plBg; TextMeshProUGUI plTx; TextButton("PlaceBtn", side.transform, "PLACE", 24, () => { if (selected != null) ChooseFile(selected); }, out plBg, out plTx);
            Place(plBg, new Vector2(0, 0), new Vector2(1, 0), new Vector2(18, 18), new Vector2(-18, 72)); Active(plBg, plTx, false); plTxt = plTx; plImg = plBg;
            btnOn.Clear();
            if (tabAllBg != null) btnOn[tabAllBg] = () => tab == 0; if (tabWallBg != null) btnOn[tabWallBg] = () => tab == 1; if (tabFavBg != null) btnOn[tabFavBg] = () => tab == 2;
            RefreshGrid();
            return true;
        }

        Slider MakeSlider(Transform parent)
        {
            var root = new GameObject("Slider"); Rt(root, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var bg = Img("Bg", root.transform, new Color(0.18f, 0.185f, 0.19f)); Place(bg, new Vector2(0, 0.35f), new Vector2(1, 0.65f), Vector2.zero, Vector2.zero);
            var fa = new GameObject("FillArea"); Rt(fa, root.transform, new Vector2(0, 0.35f), new Vector2(1, 0.65f), Vector2.zero, Vector2.zero);
            var fill = Img("Fill", fa.transform, Gold);
            var ha = new GameObject("HandleArea"); Rt(ha, root.transform, Vector2.zero, Vector2.one, new Vector2(8, 0), new Vector2(-8, 0));
            var hd = Img("Handle", ha.transform, new Color(0.9f, 0.89f, 0.84f)); ((RectTransform)hd.transform).sizeDelta = new Vector2(16, 0);
            var sl = root.AddComponent<Slider>(); sl.fillRect = (RectTransform)fill.transform; sl.handleRect = (RectTransform)hd.transform; sl.targetGraphic = hd;
            sl.minValue = 0.25f; sl.maxValue = 4f; sl.direction = Slider.Direction.LeftToRight;
            return sl;
        }

        void Select(string file)
        {
            selected = file;
            var t = file != null ? Thumb(file) : null;
            if (pvImg != null) { pvImg.texture = t; pvImg.enabled = t != null; if (t != null) pvFit.aspectRatio = (float)t.width / t.height; }
            var full = file != null ? PostersPlugin.LoadImage(file) : null;
            int onWall = file == null ? 0 : placed.Count(pp => string.Equals(pp.File, file, StringComparison.OrdinalIgnoreCase));
            if (pvName != null) pvName.text = file != null ? (PostersPlugin.IsLocked(file) ? "[LOCKED] " : "") + Path.GetFileNameWithoutExtension(file) : "";
            bool lockedSel = file != null && PostersPlugin.IsLocked(file);
            if (plTxt != null) plTxt.text = lockedSel ? "LOCKED" : "PLACE";
            Active(plImg, plTxt, false);
            if (plImg != null) { var f3 = plImg.GetComponent<BtnFade>(); if (f3 != null) { f3.dim = lockedSel; f3.Apply(); } }
            if (pvInfo != null) pvInfo.text = file == null ? "" : Fmt(full) + "   ·   " + (full != null ? full.width + " x " + full.height + " px" : "?") + (PostersPlugin.AnimOf(file) != null ? "   ·   GIF " + PostersPlugin.AnimOf(file).Frames.Length + " frames" : "") + "\nOn the wall: " + onWall + (full != null && Mathf.Max(full.width, full.height) < 800 ? "\n<color=#e6a05a>Small image: may look blurry when big</color>" : "");
            if (pvSize != null) pvSize.text = file != null ? "SIZE  x" + PostersPlugin.SizeOf(file).ToString("0.00") : "";
            if (favTx != null) { bool fv = file != null && IsFav(file); favTx.text = fv ? "★ FAVORITE" : "☆ FAVORITE"; favTx.color = fv ? Gold : TextC; }
            if (renameRow != null && renameRow.activeSelf) renameRow.SetActive(false);
            if (pvSlider != null) { sliderLock = true; pvSlider.value = file != null ? PostersPlugin.SizeOf(file) : 1f; sliderLock = false; }
        }

        void RefreshGrid()
        {
            if (gridContent == null) return;
            for (int i = gridContent.childCount - 1; i >= 0; i--) Destroy(gridContent.GetChild(i).gameObject);
            cards.Clear();
            Active(tabAllBg, tabAllTx, tab == 0); Active(tabWallBg, tabWallTx, tab == 1); Active(tabFavBg, tabFavTx, tab == 2);
            if (category != null && !Categories().Contains(category)) category = null;
            if (folderTx != null) folderTx.text = "FOLDER: " + (category == null ? "ALL" : category.Length == 0 ? "MAIN" : category.ToUpperInvariant());
            if (sortTx != null) sortTx.text = "SORT: " + (sortMode == 0 ? "NAME" : sortMode == 1 ? "FORMAT" : "RECENT");
            string q = (query ?? "").Trim();
            if (tab == 0 || tab == 2)
            {
                var list = PostersPlugin.Files.Where(f => (tab != 2 || IsFav(f)) && (category == null || string.Equals(PostersPlugin.Category(f), category, StringComparison.OrdinalIgnoreCase)) && (q.Length == 0 || Path.GetFileNameWithoutExtension(f).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                if (sortMode == 1) list = list.OrderBy(f => Fmt(PostersPlugin.LoadImage(f))).ThenBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase).ToList();
                else if (sortMode == 2) list = list.OrderByDescending(f => { long t; return recent.TryGetValue(Path.GetFileName(f), out t) ? t : 0L; }).ThenBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var f in list) MakeCard(f, null);
                if (countTx != null) countTx.text = (tab == 2 && list.Count == 0 ? "No favorite yet: click ☆ on a poster" : list.Count + " / " + PostersPlugin.Files.Count + " poster(s)" + (PostersPlugin.Collection.Value ? "   ·   collection: " + PostersPlugin.UnlockedCount + " / " + PostersPlugin.Files.Count + " unlocked" : "")) + "    ·    <color=#c7c4b5>P</color> = portrait   <color=#c7c4b5>L</color> = landscape   <color=#c7c4b5>S</color> = square";
            }
            else
            {
                var list = placed.Where(pp => q.Length == 0 || Path.GetFileNameWithoutExtension(pp.File).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                var cam = Camera.main;
                if (cam != null) list = list.OrderBy(pp => Vector3.Distance(cam.transform.position, pp.Pos)).ToList();
                foreach (var pp in list) MakeCard(pp.File, pp);
                if (countTx != null) countTx.text = list.Count + " free poster(s) on your walls";
            }
            Select(selected);
        }

        class KeyValuePair2 { public Transform pic; public string file; }
        readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Image, KeyValuePair2>> thumbQ = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Image, KeyValuePair2>>();
        bool thumbRun;
        IEnumerator ThumbQueue()
        {
            thumbRun = true; var sw = System.Diagnostics.Stopwatch.StartNew(); double worst = 0; int n = 0;
            while (thumbQ.Count > 0)
            {
                var it = thumbQ[0]; thumbQ.RemoveAt(0);
                if (it.Key == null || it.Value.pic == null) continue;   // menu ferme / grille refaite : rien a charger
                var one = System.Diagnostics.Stopwatch.StartNew();
                try { FillCard(it.Key, it.Value.pic, it.Value.file); n++; } catch (Exception e) { PostersPlugin.Log.LogWarning("[menu] " + e.Message); }
                worst = Math.Max(worst, one.Elapsed.TotalMilliseconds);
                yield return null;
            }
            thumbRun = false;
            if (n > 0) PostersPlugin.Log.LogInfo("[perf] vignettes du menu : " + n + " chargee(s) une par frame en " + sw.ElapsedMilliseconds + " ms, pire frame " + (int)worst + " ms");
        }
        // image de la carte + badge du format (demande l'image complete)
        void FillCard(Image card, Transform pic, string file)
        {
            var t = Thumb(file);
            if (t != null)
            {
                var ig = new GameObject("Img"); Rt(ig, pic, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                ig.transform.SetAsFirstSibling();   // sous le voile LOCKED
                var raw = ig.AddComponent<RawImage>(); raw.texture = t; raw.raycastTarget = false;
                var fit = ig.AddComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = (float)t.width / t.height;
            }
            var full = PostersPlugin.LoadImage(file); string f3 = Fmt(full);
            var badge = Txt("Fmt", card.transform, (PostersPlugin.IsSticker(file) ? "STICKER " : "") + (file.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? "GIF " : "") + (f3 == "LANDSCAPE" ? "L" : f3 == "PORTRAIT" ? "P" : "S"), 16, Gold, TextAlignmentOptions.TopRight, true); Place(badge, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-150, -26), new Vector2(-6, -4));
        }
        void MakeCard(string file, Placed onWall)
        {
            var card = Frame("Card", gridContent, Card, Line, 1);
            var btn = card.gameObject.AddComponent<Button>(); btn.transition = Selectable.Transition.None;
            // survol seulement (un EventTrigger bloquerait la molette de la liste)
            var hov = card.gameObject.AddComponent<CardHover>(); hov.enter = () => card.color = Gold; hov.exit = () => card.color = file == selected ? new Color(0.55f, 0.545f, 0.51f) : Line;
            cards[card] = file; if (file == selected) card.color = new Color(0.55f, 0.545f, 0.51f);
            btn.onClick.AddListener(() =>
            {
                bool dbl = lastClickFile == file && Time.unscaledTime - lastClick < 0.35f;
                lastClick = Time.unscaledTime; lastClickFile = file; Select(file);
                foreach (var kv in cards) if (kv.Key != null) kv.Key.color = kv.Value == file ? Gold : Line;
                if (dbl && onWall == null) ChooseFile(file);
            });
            var pic = new GameObject("Pic"); Rt(pic, card.transform, Vector2.zero, Vector2.one, new Vector2(10, 46), new Vector2(-10, -10));
            // vignette deja prete : tout de suite ; sinon chargee plus tard, une par frame (sinon le menu fige a la 1re ouverture)
            Texture2D tReady;
            if (thumbs.TryGetValue(file, out tReady) && tReady != null) FillCard(card, pic.transform, file);
            else { thumbQ.Add(new System.Collections.Generic.KeyValuePair<Image, KeyValuePair2>(card, new KeyValuePair2 { pic = pic.transform, file = file })); if (!thumbRun) StartCoroutine(ThumbQueue()); }
            if (onWall == null && PostersPlugin.IsLocked(file))
            {
                var veil = Img("Locked", pic.transform, new Color(0.02f, 0.02f, 0.025f, 0.82f)); Place(veil, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); veil.raycastTarget = false;
                var lk = Txt("LockTx", veil.transform, "LOCKED", 20, Dim, TextAlignmentOptions.Center, true); Place(lk, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }
            var strip = Img("Strip", card.transform, new Color(0.06f, 0.065f, 0.07f)); Place(strip, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(-1, 38)); strip.raycastTarget = false;
            var nm = Txt("Name", strip.transform, Path.GetFileNameWithoutExtension(file), 17, TextC, TextAlignmentOptions.Center, true); Place(nm, Vector2.zero, Vector2.one, new Vector2(6, 0), new Vector2(-6, 0));
            if (onWall == null)
            {
                Image stBg; TextMeshProUGUI stTx; TextButton("Star", card.transform, IsFav(file) ? "★" : "☆", 20, () => { ToggleFav(file); RefreshGrid(); }, out stBg, out stTx);
                Place(stBg, new Vector2(0, 1), new Vector2(0, 1), new Vector2(4, -32), new Vector2(32, -4)); stBg.color = new Color(0, 0, 0, 0.45f); stTx.color = IsFav(file) ? Gold : Dim;
            }
            if (onWall != null)
            {
                var cam = Camera.main; float d = cam != null ? Vector3.Distance(cam.transform.position, onWall.Pos) : 0f;
                var dist = Txt("Dist", card.transform, d.ToString("0") + " m", 15, Gold, TextAlignmentOptions.TopLeft, true); Place(dist, new Vector2(0, 1), new Vector2(0, 1), new Vector2(6, -26), new Vector2(80, -4));
                Image rb; TextMeshProUGUI rt; TextButton("Remove", card.transform, "TAKE DOWN", 15, () => { if (onWall.Go != null) Destroy(onWall.Go); placed.Remove(onWall); Save(); Note("poster taken down: " + Path.GetFileNameWithoutExtension(onWall.File)); RefreshGrid(); }, out rb, out rt);
                Place(rb, new Vector2(0, 0), new Vector2(1, 0), new Vector2(10, 46), new Vector2(-10, 76)); rb.color = new Color(0.45f, 0.12f, 0.1f);
            }
        }

        void StartRename()
        {
            if (selected == null || renameRow == null) return;
            renameRow.SetActive(true); renameField.text = Path.GetFileNameWithoutExtension(selected);
            renameField.ActivateInputField(); renameField.Select();
        }
        void ApplyRename(string v)
        {
            if (selected == null) return;
            renameRow.SetActive(false);
            var name = new string((v ?? "").Trim().Where(ch => Array.IndexOf(Path.GetInvalidFileNameChars(), ch) < 0).ToArray()).Trim();
            if (name.Length == 0 || name == Path.GetFileNameWithoutExtension(selected)) return;
            var dst = Path.Combine(Path.GetDirectoryName(selected), name + Path.GetExtension(selected));
            if (File.Exists(dst)) { Note("a poster is already called " + name); return; }
            try
            {
                var src = selected;
                File.Move(src, dst);
                foreach (var pp in placed) if (string.Equals(pp.File, src, StringComparison.OrdinalIgnoreCase)) pp.File = dst;
                Save();
                PostersPlugin.MoveUnlock(src, dst);
                LoadFavs(); if (favs.Remove(PostersPlugin.Rel(src))) { favs.Add(PostersPlugin.Rel(dst)); SaveFavs(); }
                LoadRecent(); long rt; if (recent.TryGetValue(Path.GetFileName(src), out rt)) { recent.Remove(Path.GetFileName(src)); recent[Path.GetFileName(dst)] = rt; }
                Texture2D th; if (thumbs.TryGetValue(src, out th)) { thumbs.Remove(src); thumbs[dst] = th; }
                PostersPlugin.BuildMap(); PostersPlugin.CopySize(src, dst);
                selected = dst; RefreshGrid();
                Note("renamed to " + name + " (restart the server to update it at Ragman)");
            }
            catch (Exception e) { Note("rename failed: " + e.Message); }
        }
        void DoReload()
        {
            int before = PostersPlugin.Files.Count;
            try { PostersPlugin.BuildMap(); } catch (Exception e) { PostersPlugin.Log.LogWarning("[pose] reload : " + e.Message); }
            int now = PostersPlugin.Files.Count;
            RefreshGrid();
            if (countTx != null) countTx.text = now + " poster(s) found (" + (now - before >= 0 ? "+" : "") + (now - before) + ")  ·  restart the server to sell new ones at Ragman";
        }

        void ChooseFile(string file)
        {
            var files = PostersPlugin.Files; int i = files.IndexOf(file); if (i < 0) return;
            if (PostersPlugin.IsLocked(file)) { Toast("LOCKED: find this poster in a raid and tear it down", 3f); return; }
            Touch(file); Choose(i);
        }
        void Choose(int i) { idx = i; ClosePicker(); Start2(); }

        // police du jeu (Bender) pour la fenetre et le bandeau, sinon police par defaut
        static Font gameFont; static bool fontSearched;
        internal static Font GameFont()
        {
            if (fontSearched) return gameFont;
            fontSearched = true;
            try
            {
                foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                    if (f != null && f.name.IndexOf("bender", StringComparison.OrdinalIgnoreCase) >= 0) { gameFont = f; if (f.name.IndexOf("bold", StringComparison.OrdinalIgnoreCase) < 0) break; }
                if (gameFont == null)
                    foreach (var fa in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>())
                        if (fa != null && fa.name.IndexOf("bender", StringComparison.OrdinalIgnoreCase) >= 0 && fa.sourceFontFile != null) { gameFont = fa.sourceFontFile; break; }
                PostersPlugin.Log.LogInfo("[pose] police : " + (gameFont != null ? gameFont.name : "par defaut (Bender introuvable)"));
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[pose] police : " + e.Message); }
            return gameFont;
        }
        static GUIStyle Styled(GUIStyle st) { var f = GameFont(); if (f != null) st.font = f; return st; }

        static Texture2D solid;
        static Texture2D Solid() { if (solid == null) { solid = new Texture2D(1, 1); solid.SetPixel(0, 0, Color.white); solid.Apply(); solid.hideFlags = HideFlags.DontUnloadUnusedAsset; } return solid; }
        static void Box(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Solid()); GUI.color = o; }

        void DrawPicker()
        {
            var files = PostersPlugin.Files;
            float W = Mathf.Min(Screen.width - 80f, 1100f), H = Mathf.Min(Screen.height - 120f, 760f);
            var win = new Rect((Screen.width - W) / 2f, (Screen.height - H) / 2f, W, H);
            Box(new Rect(win.x - 2, win.y - 2, win.width + 4, win.height + 4), new Color(0.3f, 0.3f, 0.3f, 0.9f));
            Box(win, new Color(0.04f, 0.045f, 0.05f, 0.97f));
            var title = Styled(new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft });
            title.normal.textColor = new Color(0.78f, 0.77f, 0.71f);
            GUI.Label(new Rect(win.x + 20, win.y + 10, W - 40, 40), "HIDEOUT POSTERS  —  " + files.Count + " poster(s)", title);
            var hint = Styled(new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleRight });
            hint.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
            GUI.Label(new Rect(win.x + 20, win.y + 10, W - 40, 40), "Click a poster to place it   ·   [Esc] / [" + kMode.Value + "] close", hint);

            const float cw = 170f, ch = 210f, gap = 14f;
            int cols = Mathf.Max(1, (int)((W - 40 + gap) / (cw + gap)));
            int rows = (files.Count + cols - 1) / cols;
            var view = new Rect(win.x + 20, win.y + 60, W - 40, H - 80);
            var content = new Rect(0, 0, view.width - 20, rows * (ch + gap));
            pickScroll = GUI.BeginScrollView(view, pickScroll, content);
            var name = Styled(new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip });
            float x0 = (content.width - (cols * cw + (cols - 1) * gap)) / 2f;
            var ev = Event.current;
            hover = -1;
            for (int i = 0; i < files.Count; i++)
            {
                int cx = i % cols, cy = i / cols;
                var cell = new Rect(x0 + cx * (cw + gap), cy * (ch + gap), cw, ch);
                if (cell.yMax < pickScroll.y || cell.y > pickScroll.y + view.height) continue;   // hors de vue : pas dessinee
                bool over = cell.Contains(ev.mousePosition);
                if (over) hover = i;
                Box(cell, over ? new Color(0.78f, 0.77f, 0.71f, 1f) : new Color(0.16f, 0.17f, 0.18f, 1f));
                var inner = new Rect(cell.x + 3, cell.y + 3, cell.width - 6, cell.height - 6);
                Box(inner, new Color(0.08f, 0.085f, 0.09f, 1f));
                var img = PostersPlugin.LoadImage(files[i]);
                var area = new Rect(inner.x + 8, inner.y + 8, inner.width - 16, inner.height - 46);
                if (img != null) GUI.DrawTexture(area, img, ScaleMode.ScaleToFit, false);
                name.normal.textColor = over ? new Color(0.78f, 0.77f, 0.71f) : new Color(0.92f, 0.92f, 0.92f);
                GUI.Label(new Rect(inner.x + 4, inner.yMax - 36, inner.width - 8, 30), Path.GetFileNameWithoutExtension(files[i]), name);
                if (ev.type == EventType.MouseDown && ev.button == 0 && over) { ev.Use(); Choose(i); GUI.EndScrollView(); return; }
            }
            GUI.EndScrollView();
            if (ev.type == EventType.MouseDown && ev.button == 0 && !win.Contains(ev.mousePosition)) { ev.Use(); ClosePicker(); }
        }

        void Stop()
        {
            mode = false; if (ghost != null) Destroy(ghost); ghost = null; ghostFile = null; ghostScale = -1f;
            if (editBackup != null) { var b = editBackup; editBackup = null; placed.Add(b); Spawn(b); Note("edit cancelled: poster put back"); }
        }
        float GhostSize(string file) { return ghostScale > 0f ? ghostScale : PostersPlugin.SizeOf(file); }

        static bool Aim(out RaycastHit hit)
        {
            hit = default(RaycastHit);
            var cam = Camera.main; if (cam == null) return false;
            var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, 8f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool ok = false;
            foreach (var h in hits)
            {
                if (h.distance < 0.35f) continue;   // le corps du joueur
                if (h.distance < best) { best = h.distance; hit = h; ok = true; }
            }
            return ok;
        }

        internal static Quaternion Orient(Vector3 normal)
        {
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(Camera.main != null ? Camera.main.transform.up : Vector3.forward, normal);
            return Quaternion.LookRotation(-normal, up.normalized);
        }

        static Material template;
        internal static Material MakeMaterial(Texture2D img)
        {
            if (template == null)
            {
                foreach (var r in FindObjectsOfType<Renderer>())
                {
                    var m = r.sharedMaterial;
                    if (m != null && m.shader != null && m.shader.name == "p0/Reflective/Bumped Specular") { template = m; break; }
                }
                if (template == null) { var sh = Shader.Find("p0/Reflective/Bumped Specular"); if (sh != null) template = new Material(sh); }
                if (template == null) { var sh = Shader.Find("Standard"); if (sh != null) template = new Material(sh); }
                PostersPlugin.Log.LogInfo("[pose] materiau modele : " + (template != null ? template.name + " / " + template.shader.name : "AUCUN"));
            }
            if (template == null) return null;
            var mat = new Material(template);
            mat.mainTexture = img; mat.mainTextureScale = Vector2.one; mat.mainTextureOffset = Vector2.zero;
            foreach (var cp in new[] { "_ReflectColor", "_SpecColor" }) if (mat.HasProperty(cp)) mat.SetColor(cp, Color.black);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            // le materiau modele du jeu porte un relief de papier froisse : on l'enleve, l'affiche est bien plate
            if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", null);
            if (mat.HasProperty("_BumpTex")) mat.SetTexture("_BumpTex", null);
            if (mat.HasProperty("_NormalMap")) mat.SetTexture("_NormalMap", null);
            return mat;
        }

        static Mesh Quad(float aspect, float scale)
        {
            float maxSide = 0.995f * scale, w, h;
            if (aspect >= 1f) { w = maxSide; h = maxSide / aspect; } else { h = maxSide; w = maxSide * aspect; }
            float x = w * 0.5f, y = h * 0.5f, z = -0.006f;
            var m = new Mesh { name = "HideoutPosterFree" };
            m.vertices = new[] { new Vector3(-x, -y, z), new Vector3(x, -y, z), new Vector3(x, y, z), new Vector3(-x, y, z) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        internal static GameObject Build(string file) { return Build(file, PostersPlugin.SizeOf(file), UnityEngine.Random.Range(1, int.MaxValue)); }
        internal static GameObject Build(string file, float own) { return Build(file, own, UnityEngine.Random.Range(1, int.MaxValue)); }
        internal static GameObject Build(string file, float own, int seed) { return Build(file, own, seed, 0); }
        internal static GameObject Build(string file, float own, int seed, int light) { return Build(file, own, seed, light, 0, 0, 0); }
        internal static GameObject Build(string file, float own, int seed, int light, int fix, int curl) { return Build(file, own, seed, light, fix, curl, 0); }
        internal static GameObject Build(string file, float own, int seed, int light, int fix, int curl, int col)
        {
            Glow.Pick = col;
            try { return BuildCore(file, own, seed, light, fix, curl); } finally { Glow.Pick = 0; }
        }
        static GameObject BuildCore(string file, float own, int seed, int light, int fix, int curl)
        {
            bool lit = light == 1;
            var img = PostersPlugin.LoadImage(file);
            if (img == null) return null;
            bool sticker = PostersPlugin.IsSticker(file);
            Material mat = lit ? Glow.LitMat(file, img, sticker) : null;
            if (mat == null) { mat = sticker ? Look.StickerMat(img) : MakeMaterial(img); }
            if (mat == null) return null;
            var go = new GameObject("HideoutPosterFree_" + Path.GetFileNameWithoutExtension(file));
            go.AddComponent<PosterTag>().File = file;
            float pw, ph; var look = Look.Roll(seed); Look.Choose(look, fix, curl);
            if (sticker) { look.Fix = 0; look.CurlCorner = -1; look.Wave = 0f; look.Flat = true; }
            go.AddComponent<MeshFilter>().sharedMesh = Look.Paper((float)img.width / img.height, (PostersPlugin.Size != null ? PostersPlugin.Size.Value : 1f) * own, look, out pw, out ph);
            var mr = go.AddComponent<MeshRenderer>();
            var bm = sticker ? null : Look.BackMat();
            mr.sharedMaterials = bm != null ? new[] { mat, bm } : new[] { mat };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            GifPlayer.Attach(mr, file);
            try { Look.Fixings(go.transform, pw, ph, look); } catch (Exception e) { PostersPlugin.Log.LogWarning("[look] " + e.Message); }
            if (lit) try { Glow.AddLight(go.transform, file, img, pw, ph); } catch (Exception e) { PostersPlugin.Log.LogWarning("[glow] " + e.Message); }
            if (light == 2) try { Glow.AddLamp(go.transform, pw, ph); } catch (Exception e) { PostersPlugin.Log.LogWarning("[lampe] " + e.Message); }
            if (light == 9 || light == 10) try { Glow.AddNeon(go.transform, file, img, pw, ph, false, seed); var nf = go.transform.Find("NeonFrame").gameObject; var fx = nf.AddComponent<NeonFx>(); fx.rainbow = light == 9; fx.baseCol = Glow.NeonColor(file, img); } catch (Exception e) { PostersPlugin.Log.LogWarning("[neon] " + e.Message); }
            if (light == 12) try { Glow.AddLamp(go.transform, pw, ph, new Color(0.55f, 0.2f, 1f), new Color(0.7f, 0.35f, 1f)); } catch (Exception e) { PostersPlugin.Log.LogWarning("[uv] " + e.Message); }
            if (light == 13) try { Glow.AddUplight(go.transform, pw, ph); } catch (Exception e) { PostersPlugin.Log.LogWarning("[uplight] " + e.Message); }
            if (light == 14) try { Glow.AddNeon(go.transform, file, img, pw, ph, false, seed); go.transform.Find("NeonFrame").gameObject.AddComponent<NeonPolice>(); } catch (Exception e) { PostersPlugin.Log.LogWarning("[police] " + e.Message); }
            if (light == 15) try { Glow.AddFire(go.transform, pw, ph, seed); } catch (Exception e) { PostersPlugin.Log.LogWarning("[feu] " + e.Message); }
            if (light == 11) try { Glow.AddString(go.transform, pw, ph, seed, true); var tw = go.transform.Find("StringLights").GetComponent<Twinkle>(); if (tw != null) tw.chase = true; } catch (Exception e) { PostersPlugin.Log.LogWarning("[chase] " + e.Message); }
            if (light == 3 || light == 4 || light == 6 || light == 8) try { Glow.AddNeon(go.transform, file, img, pw, ph, light == 4, seed); if (light == 6) go.transform.Find("NeonFrame").gameObject.AddComponent<NeonBlink>(); if (light == 8) go.transform.Find("NeonFrame").gameObject.AddComponent<NeonFaulty>().seed = seed; } catch (Exception e) { PostersPlugin.Log.LogWarning("[neon] " + e.Message); }
            if (light == 5 || light == 7) try { Glow.AddString(go.transform, pw, ph, seed, light == 7); } catch (Exception e) { PostersPlugin.Log.LogWarning("[guirlande] " + e.Message); }
            return go;
        }

        static void Spawn(Placed p)
        {
            if (p.Go != null) return;
            if (p.Scale <= 0f) p.Scale = PostersPlugin.SizeOf(p.File);   // ancienne sauvegarde : on fige la taille actuelle
            if (p.Seed == 0) p.Seed = (p.File.GetHashCode() ^ p.Pos.GetHashCode()) | 1;
            p.Go = Build(p.File, p.Scale, p.Seed, p.Off && p.Lit == 1 ? 0 : p.Lit, p.Fix, p.Curl, p.Col);
            Transform at = null;
            if (!string.IsNullOrEmpty(p.Anchor)) at = FindAnchor(p.Anchor);
            // ancien format (nom seul) : on verifie que c'est le bon objet ; le nouveau chemin est unique, la cible peut avoir bouge
            if (at != null && !p.Anchor.StartsWith("@") && (at.TransformPoint(p.LPos) - p.Pos).sqrMagnitude > 9f) at = null;
            if (!string.IsNullOrEmpty(p.Anchor)) PostersPlugin.Log.LogInfo("[pose] objet mobile " + (at != null ? "retrouve" : "INTROUVABLE") + " : " + p.Anchor);   // mauvais objet (meme nom ailleurs) : on reste a la place enregistree
            if (p.Go != null && at != null) { p.Go.transform.SetPositionAndRotation(at.TransformPoint(p.LPos), at.rotation * p.LRot); p.Go.transform.SetParent(at, true); }
            if (p.Go != null) { if (at == null) p.Go.transform.SetPositionAndRotation(p.Pos, p.Rot); if (p.Off && p.Lit >= 2) p.Go.AddComponent<LightsOff>().Apply(); BulletHoles.Rebuild(p.Go, p.Holes); }
        }

        internal static void Resize(string file)
        {
            foreach (var p in placed)
            {
                // chaque affiche posee garde sa propre taille : seul le reglage global (file == null) les redimensionne
                if (p.Go == null || file != null) continue;
                var mf0 = p.Go.GetComponent<MeshFilter>(); var oldMesh = mf0 != null ? mf0.sharedMesh : null;
                Destroy(p.Go); p.Go = null; if (oldMesh != null) Destroy(oldMesh); Spawn(p);
            }
        }

        // interrupteur : on garde les lampes, neons et guirlandes, juste eteints
        static void SetOff(Placed p, bool off)
        {
            if (p.Lit == 0 || p.Off == off) return;
            p.Off = off;
            if (p.Lit == 1 || p.Go == null) { if (p.Go != null) Destroy(p.Go); p.Go = null; Spawn(p); return; }   // retroeclairage : on refait l'affiche
            var lo = p.Go.GetComponent<LightsOff>();
            if (off) { if (lo == null) p.Go.AddComponent<LightsOff>().Apply(); }
            else if (lo != null) lo.Restore();
        }
        void SwitchLooked()
        {
            var p = LookedPlaced(); if (p == null) { Note("no free poster in front of you"); return; }
            if (p.Lit == 0) { Note("this poster has no light (set one with [" + K(kEdit.Value) + "] then [" + K(kLight.Value) + "])"); return; }
            SetOff(p, !p.Off); Save(); Note(Path.GetFileNameWithoutExtension(p.File) + ": light " + (p.Off ? "OFF" : "ON"));
        }
        void SwitchAll()
        {
            var lit = placed.Where(x => x.Lit != 0).ToList(); if (lit.Count == 0) { Note("no lit poster in the hideout"); return; }
            bool off = lit.Any(x => !x.Off);   // s'il en reste une allumee : on eteint tout, sinon on rallume tout
            foreach (var x in lit) SetOff(x, off); Save();
            Note((off ? "all poster lights OFF" : "all poster lights ON") + " (" + lit.Count + ")");
        }
        // une balle traverse-t-elle une affiche posee ?
        internal static void HitPosters(Vector3 o, Vector3 d, float maxD)
        {
            Placed best = null; float bestD = maxD; Vector3 bestLocal = Vector3.zero;
            foreach (var p in placed)
            {
                if (p.Go == null || p.Fallen) continue;
                var mf = p.Go.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var plane = new Plane(p.WRot * Vector3.back, p.WPos); var ray = new Ray(o, d);
                float dist; if (!plane.Raycast(ray, out dist) || dist > bestD) continue;
                var local = Quaternion.Inverse(p.WRot) * (ray.GetPoint(dist) - p.WPos); var ext = mf.sharedMesh.bounds.extents;
                if (Mathf.Abs(local.x) <= ext.x && Mathf.Abs(local.y) <= ext.y) { bestD = dist; best = p; bestLocal = local; }
            }
            if (best == null) return;
            RangeGame.OnPosterHit(best.Go, o + d * bestD);
            var e2 = best.Go.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            BulletHoles.Impact(best.Holes, new Vector2(bestLocal.x / e2.x, bestLocal.y / e2.y), e2);
            while (best.Holes.Count > 120) best.Holes.RemoveAt(0);
            BulletHoles.Rebuild(best.Go, best.Holes); Save();
            try { SpotterScope.Focus(best.Go, o + d * bestD); } catch (Exception ex) { PostersPlugin.Log.LogWarning("[longue-vue] " + ex.Message); }
            // trop abimee : l'affiche se decroche et tombe (elle revient neuve au prochain chargement de la planque)
            try
            {
                float lim = cFall != null ? cFall.Value : 30f; if (lim <= 0f) return;
                float dmg = 0f; foreach (var hh in best.Holes) dmg += Mathf.Max(0.3f, hh.z);
                if (dmg < lim) return;
                best.Fallen = true; best.Holes.Clear(); Save();
                PostersPlugin.Log.LogInfo("[chute] " + Path.GetFileNameWithoutExtension(best.File) + " se decroche (degats " + dmg.ToString("0") + " / " + lim.ToString("0") + ")");
                PostersPlugin.Inst.StartCoroutine(Fall(best.Go));
            }
            catch (Exception ex) { PostersPlugin.Log.LogWarning("[chute] " + ex.Message); }
        }
        internal static BepInEx.Configuration.ConfigEntry<float> cFall;
        // chute facon feuille de papier : elle glisse du mur, se balance, et finit a plat, image vers le haut
        static IEnumerator Fall(GameObject go)
        {
            if (go == null) yield break;
            var tr = go.transform; tr.SetParent(null, true);
            // lampes, neons, ombre portee... restent au mur : on les coupe ; les trous tombent avec l'affiche
            foreach (Transform c in tr) if (c.name != "BulletHoles") c.gameObject.SetActive(false);
            Vector3 p0 = tr.position; Quaternion r0 = tr.rotation;
            var img = r0 * Vector3.back;                                    // face image (vers le tireur)
            var flat = new Vector3(img.x, 0f, img.z); if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward; flat.Normalize();
            var right = Vector3.Cross(Vector3.up, flat);
            float floorY = p0.y - 1.5f; RaycastHit h;
            if (Physics.Raycast(p0 + img * 0.35f, Vector3.down, out h, 12f, ~0, QueryTriggerInteraction.Ignore)) floorY = h.point.y;
            float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            var pEnd = new Vector3(p0.x, floorY + 0.006f, p0.z) + flat * UnityEngine.Random.Range(0.3f, 0.7f) + right * side * UnityEngine.Random.Range(0.05f, 0.3f);
            // pas tout a fait a plat : un bord reste un peu souleve (le papier garde sa forme)
            float tiltX = UnityEngine.Random.Range(2f, 4.5f) * (UnityEngine.Random.value < 0.5f ? -1f : 1f), tiltY = UnityEngine.Random.Range(1.5f, 3.5f) * side;
            var rFlat = Quaternion.LookRotation(Vector3.down, flat) * Quaternion.Euler(0f, 0f, side * UnityEngine.Random.Range(5f, 35f));
            var rEnd = rFlat * Quaternion.Euler(tiltX, tiltY, 0f);
            var mfF = go.GetComponent<MeshFilter>(); var ext = mfF != null && mfF.sharedMesh != null ? Vector3.Scale(mfF.sharedMesh.bounds.extents, tr.lossyScale) : new Vector3(0.3f, 0.4f, 0f);
            pEnd.y += Mathf.Sin(Mathf.Abs(tiltX) * Mathf.Deg2Rad) * ext.y + Mathf.Sin(Mathf.Abs(tiltY) * Mathf.Deg2Rad) * ext.x;   // le bord bas touche le sol
            float fallH = Mathf.Max(0.2f, p0.y - pEnd.y), T = Mathf.Clamp(0.7f + fallH * 0.45f, 0.9f, 2.2f);
            for (float t = 0f; t < T; t += Time.deltaTime)
            {
                if (go == null) yield break;
                float u = t / T, e = u * u * (3f - 2f * u), damp = 1f - u;
                // vite au milieu, freinee a la fin : elle se pose sur un coussin d'air au lieu de taper le sol
                float fy = u * u * u * (u * (u * 6f - 15f) + 10f);
                float y = Mathf.Lerp(p0.y, pEnd.y, fy);
                // dernier plane : elle glisse un peu a l'horizontale juste avant de toucher
                float glide = Mathf.Clamp01((u - 0.7f) / 0.3f); glide = glide * glide;
                var hz = Vector3.Lerp(new Vector3(p0.x, 0f, p0.z), new Vector3(pEnd.x, 0f, pEnd.z), e) + right * Mathf.Sin(u * Mathf.PI * 2.5f) * 0.14f * damp * side - flat * (1f - glide) * 0.12f * Mathf.Clamp01((u - 0.55f) / 0.15f);
                tr.position = new Vector3(hz.x, y, hz.z);
                tr.rotation = Quaternion.Slerp(r0, rEnd, e) * Quaternion.Euler(Mathf.Sin(u * Mathf.PI * 3f) * 22f * damp, 0f, Mathf.Sin(u * Mathf.PI * 2.2f + 1f) * 16f * damp * side);
                yield return null;
            }
            if (go == null) yield break;
            // a l'arrivee : elle bascule un peu, deux ou trois fois, de moins en moins, puis s'immobilise
            const float S = 0.7f;
            for (float t = 0f; t < S; t += Time.deltaTime)
            {
                if (go == null) yield break;
                float u = t / S, a = Mathf.Exp(-u * 4.5f) * Mathf.Sin(u * Mathf.PI * 5f) * 5f;
                tr.rotation = rEnd * Quaternion.Euler(a, a * 0.4f * side, 0f);
                tr.position = pEnd + Vector3.up * Mathf.Abs(Mathf.Sin(a * Mathf.Deg2Rad)) * ext.y * 0.5f;
                yield return null;
            }
            if (go == null) yield break;
            tr.position = pEnd; tr.rotation = rEnd;
            PostersPlugin.Log.LogInfo("[chute] affiche au sol (" + T.ToString("0.0") + " s de chute, " + fallH.ToString("0.0") + " m)");
        }
        void EditLooked()
        {
            var p = LookedPlaced(); if (p == null) { Note("no free poster in front of you"); return; }
            var files = PostersPlugin.Files; int i = files.FindIndex(f => string.Equals(f, p.File, StringComparison.OrdinalIgnoreCase));
            if (i < 0) { Note("its image is missing from the folder"); return; }
            if (PostersPlugin.IsLocked(p.File)) { Note("LOCKED: find this poster in a raid first"); return; }
            // on retrouve l'angle par rapport a la position « droite » sur ce mur
            var normal = -(p.Rot * Vector3.forward); var baseQ = Orient(normal);
            roll = Vector3.SignedAngle(baseQ * Vector3.up, p.Rot * Vector3.up, baseQ * Vector3.forward);
            if (p.Off) { p.Off = false; }
            idx = i; ghostSeed = p.Seed; ghostLit = p.Lit; ghostFix = p.Fix; ghostCurl = p.Curl; ghostCol = p.Col; ghostScale = p.Scale > 0f ? p.Scale : PostersPlugin.SizeOf(p.File);
            if (p.Go != null) Destroy(p.Go); p.Go = null; placed.Remove(p); editBackup = p; editHoles = new System.Collections.Generic.List<Vector3>(p.Holes);
            ghost = null; ghostFile = p.File; mode = true;
            Note("editing " + Path.GetFileNameWithoutExtension(p.File) + ": [" + K(kPlace.Value) + "] confirm   [" + K(kMode.Value) + "] cancel");
        }
        Placed LookedPlaced()
        {
            var cam = Camera.main; if (cam == null) return null;
            Placed best = null; float bestD = float.MaxValue;
            foreach (var p in placed)
            {
                if (p.Go == null) continue;
                var mf = p.Go.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var plane = new Plane(p.WRot * Vector3.back, p.WPos);
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                float d; if (!plane.Raycast(ray, out d) || d > 8f) continue;
                var local = Quaternion.Inverse(p.WRot) * (ray.GetPoint(d) - p.WPos);
                var ext = mf.sharedMesh.bounds.extents;
                if (Mathf.Abs(local.x) <= ext.x && Mathf.Abs(local.y) <= ext.y && d < bestD) { bestD = d; best = p; }
            }
            return best;
        }
        // SHIFT + Suppr : on rebouche tous les trous de balles
        void RepairLooked()
        {
            var p = LookedPlaced(); if (p == null) { Note("no free poster in front of you"); return; }
            if (p.Holes.Count == 0) { Note(Path.GetFileNameWithoutExtension(p.File) + " has no bullet hole"); return; }
            int n = p.Holes.Count; p.Holes.Clear(); BulletHoles.Rebuild(p.Go, p.Holes); Save();
            Note(Path.GetFileNameWithoutExtension(p.File) + " repaired (" + n + " hole(s) removed)");
        }
        void RemoveLooked()
        {
            var cam = Camera.main; if (cam == null) return;
            Placed best = null; float bestD = float.MaxValue;
            foreach (var p in placed)
            {
                if (p.Go == null) continue;
                var mf = p.Go.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                // le rayon du regard traverse-t-il le poster ?
                var plane = new Plane(p.WRot * Vector3.back, p.WPos);
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                float d; if (!plane.Raycast(ray, out d) || d > 8f) continue;
                var local = Quaternion.Inverse(p.WRot) * (ray.GetPoint(d) - p.WPos);
                var ext = mf.sharedMesh.bounds.extents;
                if (Mathf.Abs(local.x) <= ext.x && Mathf.Abs(local.y) <= ext.y && d < bestD) { bestD = d; best = p; }
            }
            if (best == null) { Note("no free poster in front of you"); return; }
            Destroy(best.Go); placed.Remove(best); Save();
            Note("poster taken down: " + Path.GetFileNameWithoutExtension(best.File));
        }

        static string V3(Vector3 v) { var ic = System.Globalization.CultureInfo.InvariantCulture; return v.x.ToString("R", ic) + ";" + v.y.ToString("R", ic) + ";" + v.z.ToString("R", ic); }
        static string Q4(Quaternion q) { var ic = System.Globalization.CultureInfo.InvariantCulture; return q.x.ToString("R", ic) + ";" + q.y.ToString("R", ic) + ";" + q.z.ToString("R", ic) + ";" + q.w.ToString("R", ic); }
        static void ReadAnchor(Placed p, string s)
        {
            try
            {
                var a = s.Split('#'); if (a.Length < 3 || a[0].Length == 0) return; var ic = System.Globalization.CultureInfo.InvariantCulture;
                var v = a[1].Split(';').Select(x => float.Parse(x, ic)).ToArray(); var q = a[2].Split(';').Select(x => float.Parse(x, ic)).ToArray();
                p.Anchor = a[0]; p.LPos = new Vector3(v[0], v[1], v[2]); p.LRot = new Quaternion(q[0], q[1], q[2], q[3]);
            }
            catch { p.Anchor = null; }
        }
        // l'objet vise bouge-t-il (cible du stand de tir, porte...) ? on s'accroche a lui
        static Transform MovingAnchor(Collider c)
        {
            if (c == null) return null;
            for (var t = c.transform; t != null; t = t.parent)
            {
                if (t.GetComponent<Animator>() != null || t.GetComponent<Animation>() != null || t.GetComponent<Rigidbody>() != null || t.GetComponent<HingeJoint>() != null) return c.transform;
                // stand de tir de la planque (cibles papier, rails, poppers) : bouge par script
                foreach (var mb in t.GetComponents<MonoBehaviour>()) { if (mb == null) continue; var ns = mb.GetType().Namespace ?? ""; if (ns.StartsWith("EFT.Hideout.ShootingRange", StringComparison.Ordinal)) return c.transform; }
            }
            return null;
        }
        // chemin unique : index parmi les freres + nom (les cibles du stand portent toutes le meme nom)
        static string PathOf(Transform t) { var parts = new System.Collections.Generic.List<string>(); for (; t != null; t = t.parent) parts.Insert(0, (t.parent != null ? t.GetSiblingIndex().ToString(System.Globalization.CultureInfo.InvariantCulture) : "r") + ":" + t.name); return "@" + string.Join("/", parts.ToArray()); }
        static Transform FindAnchor(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!path.StartsWith("@")) { var g = GameObject.Find(path); return g != null ? g.transform : null; }   // ancienne sauvegarde
            var parts = path.Substring(1).Split('/');
            if (parts.Length == 0) return null;
            string rootName = parts[0].Substring(parts[0].IndexOf(':') + 1);
            for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
            {
                var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(si); if (!sc.isLoaded) continue;
                foreach (var root in sc.GetRootGameObjects())
                {
                    if (root.name != rootName) continue;
                    var t = root.transform; bool ok = true;
                    for (int i = 1; i < parts.Length && ok; i++)
                    {
                        int k = parts[i].IndexOf(':'); int idx; string nm = parts[i].Substring(k + 1);
                        int.TryParse(parts[i].Substring(0, k), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out idx);
                        Transform n = idx >= 0 && idx < t.childCount ? t.GetChild(idx) : null;
                        if (n == null || n.name != nm) { n = null; for (int j = 0; j < t.childCount; j++) if (t.GetChild(j).name == nm) { n = t.GetChild(j); break; } }
                        if (n == null) ok = false; else t = n;
                    }
                    if (ok) return t;
                }
            }
            return null;
        }
        static void Save()
        {
            try
            {
                var lines = placed.Select(p => string.Join("|", new[] { PostersPlugin.Rel(p.File),
                    p.Pos.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture), p.Pos.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture), p.Pos.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    p.Rot.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture), p.Rot.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture), p.Rot.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture), p.Rot.w.ToString("R", System.Globalization.CultureInfo.InvariantCulture), (p.Scale > 0f ? p.Scale : PostersPlugin.SizeOf(p.File)).ToString("R", System.Globalization.CultureInfo.InvariantCulture), p.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Lit.ToString(), p.Fix.ToString(), p.Curl.ToString(), p.Col.ToString(), p.Off ? "1" : "0", string.Join(";", p.Holes.Select(h => h.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ":" + h.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ":" + h.z.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).ToArray()), (p.Anchor ?? "") + "#" + V3(p.LPos) + "#" + Q4(p.LRot) })).ToArray();
                File.WriteAllLines(SaveFile, lines);
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[pose] sauvegarde : " + e.Message); }
        }

        static void Load()
        {
            placed.Clear();
            try
            {
                if (!File.Exists(SaveFile)) return;
                var dir = Path.Combine(BepInEx.Paths.ConfigPath, "HideoutPosters");
                foreach (var l in File.ReadAllLines(SaveFile))
                {
                    var a = l.Split('|'); if (a.Length < 8) continue;
                    Func<int, float> f = i => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
                    placed.Add(new Placed { File = Path.Combine(dir, a[0]), Pos = new Vector3(f(1), f(2), f(3)), Rot = new Quaternion(f(4), f(5), f(6), f(7)), Scale = a.Length >= 9 ? f(8) : -1f, Seed = a.Length >= 10 ? int.Parse(a[9], System.Globalization.CultureInfo.InvariantCulture) : (l.GetHashCode() & 0x7fffffff) | 1, Lit = a.Length >= 11 ? Mathf.Clamp(int.Parse(a[10], System.Globalization.CultureInfo.InvariantCulture), 0, Glow.Names.Length - 1) : 0, Fix = a.Length >= 12 ? Mathf.Clamp(int.Parse(a[11], System.Globalization.CultureInfo.InvariantCulture), 0, Look.FixNames.Length - 1) : 0, Curl = a.Length >= 13 ? Mathf.Clamp(int.Parse(a[12], System.Globalization.CultureInfo.InvariantCulture), 0, Look.CurlNames.Length - 1) : 0, Col = a.Length >= 14 ? Mathf.Clamp(int.Parse(a[13], System.Globalization.CultureInfo.InvariantCulture), 0, Glow.ColNames.Length - 1) : 0, Off = a.Length >= 15 && a[14] == "1", Holes = a.Length >= 16 ? BulletHoles.Parse(a[15]) : new System.Collections.Generic.List<Vector3>() }); if (a.Length >= 17) ReadAnchor(placed[placed.Count - 1], a[16]);
                }
                PostersPlugin.Log.LogInfo("[pose] " + placed.Count + " poster(s) libre(s) sauvegarde(s)");
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[pose] chargement : " + e.Message); }
        }

        void OnGUI()
        {
            if (pick && !uiOk) { GUI.depth = -1000; DrawPicker(); return; }
            if (pick) return;
            if (hudText != null) return;   // bandeau gere par l'interface du jeu (Update)
            if (!mode && Time.time > msgUntil) return;
            var st = Styled(new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, richText = true });
            string t;
            if (mode)
            {
                var files = PostersPlugin.Files;
                string name = files.Count > 0 ? Path.GetFileNameWithoutExtension(files[Mathf.Clamp(idx, 0, files.Count - 1)]) : "-";
                t = "<b>POSTER: " + name + "</b>  (" + (idx + 1) + "/" + files.Count + ")\n[" + kPrev.Value + "/" + kNext.Value + "] change   [WHEEL] size x" + PostersPlugin.SizeOf(files[Mathf.Clamp(idx, 0, files.Count - 1)]).ToString("0.00") + "   [" + kPlace.Value + "] hang   [" + kBack.Value + "] list   [" + kMode.Value + "] cancel";
            }
            else t = msg;
            var r = new Rect(0, Screen.height * 0.78f, Screen.width, 70);
            var sh = new GUIStyle(st); sh.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), t, sh);
            st.normal.textColor = new Color(0.85f, 0.84f, 0.79f);
            GUI.Label(r, t, st);
        }
    }

    // ===== Affiches placardees au hasard sur les murs des maps de raid =====
    public class RaidPosters : MonoBehaviour
    {
        static BepInEx.Configuration.ConfigEntry<bool> cOn;
        static BepInEx.Configuration.ConfigEntry<int> cCount;
        readonly System.Collections.Generic.List<GameObject> spawned = new System.Collections.Generic.List<GameObject>();
        readonly System.Collections.Generic.Dictionary<GameObject, string> fileOf = new System.Collections.Generic.Dictionary<GameObject, string>();
        float nextLook; GameObject hinted;
        object world; float next, lastPlaced; int tries; bool logged;
        System.Random rng = new System.Random();

        void Awake()
        {
            Me = this;
            var c = PostersPlugin.Inst.Config;
            cOn = c.Bind("In raid", "Posters in raids", true, "Your posters appear at random on walls during raids, around you as you explore");
            cCount = c.Bind("In raid", "Posters per raid", 12, new BepInEx.Configuration.ConfigDescription("How many of your posters are put up during a raid", new BepInEx.Configuration.AcceptableValueRange<int>(1, 50)));
        }

        internal static RaidPosters Me;
        internal readonly System.Collections.Generic.Dictionary<GameObject, System.Collections.Generic.List<Vector3>> raidHoles = new System.Collections.Generic.Dictionary<GameObject, System.Collections.Generic.List<Vector3>>();
        internal static void HitPosters(Vector3 o, Vector3 d, float maxD)
        {
            if (Me == null) return;
            GameObject best = null; float bestD = maxD; Vector3 bl = Vector3.zero;
            foreach (var g in Me.spawned)
            {
                if (g == null) continue; var mf = g.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var plane = new Plane(g.transform.rotation * Vector3.back, g.transform.position); var ray = new Ray(o, d);
                float dist; if (!plane.Raycast(ray, out dist) || dist > bestD) continue;
                var local = Quaternion.Inverse(g.transform.rotation) * (ray.GetPoint(dist) - g.transform.position);
                var ext = Vector3.Scale(mf.sharedMesh.bounds.extents, g.transform.localScale);
                if (Mathf.Abs(local.x) <= ext.x && Mathf.Abs(local.y) <= ext.y) { bestD = dist; best = g; bl = local; }
            }
            if (best == null) return;
            var sz = best.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            System.Collections.Generic.List<Vector3> hl; if (!Me.raidHoles.TryGetValue(best, out hl)) Me.raidHoles[best] = hl = new System.Collections.Generic.List<Vector3>();
            BulletHoles.Impact(hl, new Vector2(bl.x / (sz.x * best.transform.localScale.x), bl.y / (sz.y * best.transform.localScale.y)), Vector3.Scale(sz, best.transform.localScale)); BulletHoles.Rebuild(best, hl);
        }
        void Clear() { foreach (var g in spawned) if (g != null) Destroy(g); spawned.Clear(); raidHoles.Clear(); fileOf.Clear(); hinted = null; tries = 0; logged = false; }

        // l'affiche de raid que tu regardes (3 m max)
        GameObject Looked(Camera cam)
        {
            GameObject best = null; float bestD = 3f;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            foreach (var g in spawned)
            {
                if (g == null) continue;
                var mf = g.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var plane = new Plane(g.transform.rotation * Vector3.back, g.transform.position);
                float d; if (!plane.Raycast(ray, out d) || d > bestD) continue;
                var local = Quaternion.Inverse(g.transform.rotation) * (ray.GetPoint(d) - g.transform.position);
                var ext = Vector3.Scale(mf.sharedMesh.bounds.extents, g.transform.localScale);
                if (Mathf.Abs(local.x) <= ext.x + 0.05f && Mathf.Abs(local.y) <= ext.y + 0.05f) { bestD = d; best = g; }
            }
            return best;
        }

        void Collect(Camera cam)
        {
            if (!PostersPlugin.Collection.Value || cam == null) return;
            if (Time.time >= nextLook)
            {
                nextLook = Time.time + 0.25f;
                var g = Looked(cam); string f;
                if (g != null && g != hinted && fileOf.TryGetValue(g, out f))
                    FreePlace.Toast(PostersPlugin.IsLocked(f) ? "[" + PostersPlugin.TakeKey.Value + "] tear down « " + Path.GetFileNameWithoutExtension(f) + " »" : "« " + Path.GetFileNameWithoutExtension(f) + " » already in your collection", 1.5f);
                hinted = g;
            }
            if (!Input.GetKeyDown(PostersPlugin.TakeKey.Value)) return;
            var t = Looked(cam); string file;
            if (t == null || !fileOf.TryGetValue(t, out file)) return;
            bool isNew = PostersPlugin.Unlock(file);
            spawned.Remove(t); fileOf.Remove(t); Destroy(t); hinted = null;
            string nm = Path.GetFileNameWithoutExtension(file);
            FreePlace.Toast(isNew ? "UNLOCKED: « " + nm + " »  (" + PostersPlugin.UnlockedCount + " / " + PostersPlugin.Files.Count + ")" : "« " + nm + " » torn down (already unlocked)", 4f);
            PostersPlugin.Log.LogInfo("[collection] " + nm + (isNew ? " debloquee" : " deja debloquee") + " (" + PostersPlugin.UnlockedCount + "/" + PostersPlugin.Files.Count + ")");
        }

        void Update()
        {
            EFT.GameWorld gw = null;
            try { if (Comfort.Common.Singleton<EFT.GameWorld>.Instantiated) gw = Comfort.Common.Singleton<EFT.GameWorld>.Instance; } catch { }
            if (gw == null || gw is HideoutGameWorld) { if (spawned.Count > 0 || world != null) { Clear(); world = null; } return; }
            if (!ReferenceEquals(world, gw)) { Clear(); world = gw; next = Time.time + 8f; }   // nouveau raid : on laisse la map finir de charger
            Collect(Camera.main);
            if (!cOn.Value || Time.time < next || spawned.Count >= cCount.Value) return;
            next = Time.time + 1.2f;
            if (Time.time < lastPlaced + 6f) return;
            var player = gw.MainPlayer; var cam = Camera.main;
            if (player == null || cam == null) return;
            var files = PostersPlugin.Files; if (files.Count == 0) return;
            // mode collection : surtout des affiches encore a debloquer
            if (PostersPlugin.Collection.Value) { var lk = files.Where(PostersPlugin.IsLocked).ToList(); if (lk.Count > 0 && rng.NextDouble() < 0.75) files = lk; }
            for (int k = 0; k < 6 && spawned.Count < cCount.Value; k++)
            {
                tries++;
                if (TryPlace(player.Position, cam, files[rng.Next(files.Count)])) break;
            }
            if (!logged && tries > 400) { logged = true; PostersPlugin.Log.LogInfo("[raid] " + spawned.Count + " affiche(s) posee(s) apres " + tries + " essais"); }
        }

        // valable sur toutes les maps : il faut une surface reellement visible au point touche
        // (les murs invisibles n'ont aucun rendu autour d'eux, quel que soit leur nom)
        static bool Visible(Collider col, Vector3 pt)
        {
            try
            {
                var own = col.GetComponent<Renderer>();
                if (own != null && own.enabled && own.bounds.SqrDistance(pt) < 0.09f) return true;
                var t = col.transform;
                for (int up = 0; up < 3 && t != null; up++, t = t.parent)
                {
                    if (t.childCount > 300) break;
                    foreach (var r in t.GetComponentsInChildren<Renderer>(false))
                    {
                        if (r == null || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                        if (r.bounds.SqrDistance(pt) < 0.09f) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        static bool Bad(Collider col)
        {
            if (col == null || col.isTrigger || col.attachedRigidbody != null) return true;
            if (col.GetComponentInParent<EFT.Player>() != null) return true;
            if (col.GetComponentInParent<EFT.Interactive.WorldInteractiveObject>() != null) return true;   // portes, conteneurs...
            if (col.GetComponentInParent<EFT.Interactive.LootItem>() != null) return true;
            var n = col.gameObject.name.ToLowerInvariant();
            if (n.Contains("glass") || n.Contains("window") || n.Contains("fence") || n.Contains("grid") || n.Contains("net")) return true;
            // murs invisibles (« Cube », bloqueurs, limites de map) : rien de visible derriere
            if (n.StartsWith("cube") || n.Contains("obstruct") || n.Contains("exit") || n.Contains("sniper") || n.Contains("blocker") || n.Contains("invisible") || n.Contains("border") || n.Contains("bound")) return true;
            return false;
        }

        bool TryPlace(Vector3 me, Camera cam, string file)
        {
            // un point au hasard autour de toi, entre 12 et 60 m
            float ang = (float)(rng.NextDouble() * Math.PI * 2), dist = 12f + (float)rng.NextDouble() * 48f;
            var p = me + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
            RaycastHit g;
            if (!Physics.Raycast(p + Vector3.up * 25f, Vector3.down, out g, 60f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (g.normal.y < 0.7f || Bad(g.collider)) return false;
            // depuis le sol, on cherche un mur a hauteur d'yeux
            float a2 = (float)(rng.NextDouble() * Math.PI * 2);
            var dir = new Vector3(Mathf.Cos(a2), 0f, Mathf.Sin(a2));
            var o = g.point + Vector3.up * (1.5f + (float)rng.NextDouble() * 0.6f);
            RaycastHit w;
            if (!Physics.Raycast(o, dir, out w, 8f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (w.distance < 0.6f || Mathf.Abs(w.normal.y) > 0.15f || Bad(w.collider) || !Visible(w.collider, w.point)) return false;
            // pas sous tes yeux : il doit apparaitre hors de ta vue
            var vp = cam.WorldToViewportPoint(w.point);
            if (vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f) return false;
            // le mur doit etre assez grand et plat pour toute l'affiche
            var img = PostersPlugin.LoadImage(file); if (img == null) return false;
            float aspect = (float)img.width / img.height, side = 0.995f * Mathf.Min(PostersPlugin.ScaleFor(file), 1.6f);
            float hw = (aspect >= 1f ? side : side * aspect) * 0.5f, hh = (aspect >= 1f ? side / aspect : side) * 0.5f;
            var rot = FreePlace.Orient(w.normal);
            var right = rot * Vector3.right; var up = rot * Vector3.up;
            foreach (var corner in new[] { right * hw + up * hh, -right * hw + up * hh, right * hw - up * hh, -right * hw - up * hh })
            {
                RaycastHit c;
                var co = w.point + corner + w.normal * 0.25f;
                if (!Physics.Raycast(co, -w.normal, out c, 0.5f, ~0, QueryTriggerInteraction.Ignore)) return false;
                if (Vector3.Dot(c.normal, w.normal) < 0.97f || Mathf.Abs(c.distance - 0.25f) > 0.06f) return false;
            }
            foreach (var o2 in spawned) if (o2 != null && Vector3.Distance(o2.transform.position, w.point) < 15f) return false;   // bien reparties sur la map
            var go = FreePlace.Build(file);
            if (go == null) return false;
            lastPlaced = Time.time;
            go.transform.SetPositionAndRotation(w.point + w.normal * 0.004f, rot);
            if (PostersPlugin.ScaleFor(file) > 1.6f) go.transform.localScale = Vector3.one * (1.6f / PostersPlugin.ScaleFor(file));
            spawned.Add(go); fileOf[go] = file;
            PostersPlugin.Log.LogInfo("[raid] affiche " + spawned.Count + "/" + cCount.Value + " : " + Path.GetFileNameWithoutExtension(file) + " a " + Vector3.Distance(me, w.point).ToString("0") + " m (" + w.collider.name + ")");
            return true;
        }
    }

    // ----- aspect realiste : papier gondole, coin corne, punaises ou scotch -----
    static class Look
    {
        internal static BepInEx.Configuration.ConfigEntry<string> cFix;
        internal static BepInEx.Configuration.ConfigEntry<bool> cWave, cCurl;
        internal static BepInEx.Configuration.ConfigEntry<float> cWaveK;
        internal class L { public int Fix; public bool Four; public int CurlCorner = -1; public float Wave; public float P1, P2, K1, K2; public Color Pin, Tape; public float[] TapeAng = new float[4]; public bool Flat; public int Curl2 = -1; public Color Mag, Washi; }
        internal static void Bind(BepInEx.Configuration.ConfigFile c)
        {
            cFix = c.Bind("Look", "Fixings", "Random", new BepInEx.Configuration.ConfigDescription("How free and raid posters hold on the wall", new BepInEx.Configuration.AcceptableValueList<string>("Random", "Pins", "Tape", "None")));
            cCurl = c.Bind("Look", "Curled corner", true, "One corner sometimes comes unstuck from the wall");
            System.EventHandler ch = (a, b) => { try { FreePlace.Resize(null); } catch { } };
            cFix.SettingChanged += ch; cCurl.SettingChanged += ch;
        }
        static readonly Color[] PinCols = { new Color(0.75f, 0.1f, 0.08f), new Color(0.12f, 0.25f, 0.7f), new Color(0.85f, 0.75f, 0.1f), new Color(0.9f, 0.9f, 0.88f), new Color(0.12f, 0.5f, 0.2f), new Color(0.7f, 0.6f, 0.3f) };
        static readonly Color[] TapeCols = { new Color(0.86f, 0.81f, 0.64f), new Color(0.82f, 0.78f, 0.6f), new Color(0.58f, 0.59f, 0.6f), new Color(0.9f, 0.9f, 0.88f) };
        internal static readonly string[] FixNames = { "AUTO", "4 PINS", "2 PINS", "4 TAPE", "2 TAPE", "NAILS", "STAPLES", "BINDER CLIPS", "MAGNETS", "DUCT TAPE", "NONE", "CLOTHESPINS", "HANGING STRING", "WASHI TAPE", "CHEWING GUM", "TAPE X", "BOLTS" };
        internal static readonly string[] CurlNames = { "AUTO", "FLAT", "BOTTOM LEFT", "BOTTOM RIGHT", "TOP LEFT", "TOP RIGHT", "BOTH BOTTOM" };
        internal static void Choose(L l, int fix, int curl)
        {
            switch (fix)
            {
                case 1: l.Fix = 1; l.Four = true; break; case 2: l.Fix = 1; l.Four = false; break;
                case 3: l.Fix = 2; l.Four = true; break; case 4: l.Fix = 2; l.Four = false; break;
                case 5: l.Fix = 3; break; case 6: l.Fix = 4; break; case 7: l.Fix = 5; l.Four = false; break;
                case 8: l.Fix = 6; break; case 9: l.Fix = 7; break; case 10: l.Fix = 0; break;
                case 11: l.Fix = 8; break; case 12: l.Fix = 9; break; case 13: l.Fix = 2; l.Four = true; l.Tape = l.Washi; break;
                case 14: l.Fix = 10; break; case 15: l.Fix = 11; break; case 16: l.Fix = 12; break;
            }
            l.Curl2 = -1;
            if (curl == 1) l.CurlCorner = -1;
            else if (curl >= 2 && curl <= 5) l.CurlCorner = curl - 2;   // 0 bas gauche, 1 bas droite, 2 haut gauche, 3 haut droite
            else if (curl == 6) { l.CurlCorner = 0; l.Curl2 = 1; }
        }
        internal static L Roll(int seed)
        {
            var r = new System.Random(seed); var l = new L();
            string f = cFix != null ? cFix.Value : "Random";
            double rf = r.NextDouble();
            l.Fix = f == "Pins" ? 1 : f == "Tape" ? 2 : f == "None" ? 0 : rf < 0.3 ? 1 : rf < 0.6 ? 2 : rf < 0.75 ? 4 : rf < 0.85 ? 3 : rf < 0.95 ? 7 : 5;
            l.Four = r.NextDouble() < 0.55;
            if (cCurl == null || cCurl.Value) { double q = r.NextDouble(); if (l.Fix == 0 || !l.Four) { if (q < 0.7) l.CurlCorner = l.Fix == 0 ? r.Next(4) : r.Next(2); } else if (q < 0.25) l.CurlCorner = r.Next(4); }
            l.Wave = 0f; r.NextDouble();   // papier ondule retire : toujours plat (on garde le tirage pour ne pas changer les autres choix du look)
            l.P1 = (float)r.NextDouble() * 6.28f; l.P2 = (float)r.NextDouble() * 6.28f; l.K1 = 1f + (float)r.NextDouble() * 2f; l.K2 = 1f + (float)r.NextDouble() * 2f;
            l.Washi = new[] { new Color(0.95f, 0.6f, 0.7f), new Color(0.55f, 0.8f, 0.95f), new Color(0.7f, 0.9f, 0.6f), new Color(0.98f, 0.85f, 0.5f), new Color(0.8f, 0.7f, 0.95f) }[r.Next(5)];
            l.Mag = new[] { new Color(0.8f, 0.12f, 0.1f), new Color(0.12f, 0.3f, 0.8f), new Color(0.95f, 0.8f, 0.1f), new Color(0.08f, 0.08f, 0.09f), new Color(0.15f, 0.6f, 0.25f) }[r.Next(5)];
            l.Pin = PinCols[r.Next(PinCols.Length)]; l.Tape = TapeCols[r.Next(TapeCols.Length)];
            for (int i = 0; i < 4; i++) l.TapeAng[i] = (float)(r.NextDouble() * 20 - 10);
            return l;
        }
        // coins : 0 = bas gauche, 1 = bas droite, 2 = haut gauche, 3 = haut droite
        static Vector2 Corner(int c, float x, float y) { return new Vector2(c % 2 == 0 ? -x : x, c < 2 ? -y : y); }
        internal static Mesh Paper(float aspect, float scale, L l, out float w, out float h)
        {
            float maxSide = 0.995f * scale;
            if (aspect >= 1f) { w = maxSide; h = maxSide / aspect; } else { h = maxSide; w = maxSide * aspect; }
            int N = l.Flat ? 1 : 24; float x0 = w * 0.5f, y0 = h * 0.5f, z0 = l.Flat ? -0.003f : -0.006f, m = Mathf.Min(w, h);
            var v = new Vector3[(N + 1) * (N + 1)]; var uv = new Vector2[v.Length]; var col = new Color32[v.Length];
            float curlR = Mathf.Clamp(m * 0.2f, 0.05f, 0.18f);
            Vector2 cc = l.CurlCorner >= 0 ? Corner(l.CurlCorner, x0, y0) : Vector2.zero;
            for (int j = 0; j <= N; j++) for (int i = 0; i <= N; i++)
            {
                float u = (float)i / N, t = (float)j / N, px = -x0 + u * w, py = -y0 + t * h;
                float edge = Mathf.Clamp01((Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(t, 1 - t)) - 0.08f) * 6f);
                float wk = cWaveK != null ? cWaveK.Value : 1f;
                float wave = l.Wave * wk * 0.012f * Mathf.Clamp(m, 0.4f, 1.2f) * edge
                    * (0.55f + 0.45f * Mathf.Sin(u * Mathf.PI * (l.K1 + 1f) + l.P1)) * (0.55f + 0.45f * Mathf.Sin(t * Mathf.PI * (l.K2 + 1.5f) + l.P2))
                    + l.Wave * wk * 0.003f * edge * Mathf.Sin(u * 17f + t * 11f + l.P1);   // petits plis en plus
                float lift = 0f;
                if (l.CurlCorner >= 0)
                {
                    // distance au coin, mesuree en diagonale : le coin se replie le long d'une ligne
                    float dx = Mathf.Abs(px - cc.x), dy = Mathf.Abs(py - cc.y), d = (dx + dy) * 0.7071f;
                    float k = Mathf.Clamp01(1f - d / curlR); lift = curlR * 0.55f * k * k;
                }
                if (l.Curl2 >= 0)
                {
                    var c2 = Corner(l.Curl2, x0, y0);
                    float dx2 = Mathf.Abs(px - c2.x), dy2 = Mathf.Abs(py - c2.y), d2 = (dx2 + dy2) * 0.7071f;
                    float k2 = Mathf.Clamp01(1f - d2 / curlR); lift = Mathf.Max(lift, curlR * 0.55f * k2 * k2);
                }
                int idx = j * (N + 1) + i;
                v[idx] = new Vector3(px, py, z0 - wave - lift); uv[idx] = new Vector2(u, t); col[idx] = new Color32(255, 255, 255, 255);
            }
            var tri = new int[N * N * 6]; int q = 0;
            for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                int a = j * (N + 1) + i, b = a + 1, c = a + N + 1, d = c + 1;
                tri[q++] = a; tri[q++] = d; tri[q++] = b; tri[q++] = a; tri[q++] = c; tri[q++] = d;
            }
            if (l.Flat) { var fm = new Mesh { name = "HideoutPosterFree" }; fm.vertices = v; fm.uv = uv; fm.colors32 = col; fm.triangles = tri; fm.RecalculateNormals(); fm.RecalculateTangents(); fm.RecalculateBounds(); return fm; }
            // dos blanc du papier (visible sous le coin corne) : memes points, faces retournees
            int nv = v.Length; var v2 = new Vector3[nv * 2]; var uv2 = new Vector2[nv * 2]; var col2 = new Color32[nv * 2];
            for (int i = 0; i < nv; i++) { v2[i] = v[i]; v2[i + nv] = v[i]; uv2[i] = uv[i]; uv2[i + nv] = uv[i]; col2[i] = col[i]; col2[i + nv] = col[i]; }
            var back = new int[tri.Length]; for (int i = 0; i < tri.Length; i += 3) { back[i] = tri[i] + nv; back[i + 1] = tri[i + 2] + nv; back[i + 2] = tri[i + 1] + nv; }
            var mesh = new Mesh { name = "HideoutPosterFree" };
            mesh.vertices = v2; mesh.uv = uv2; mesh.colors32 = col2; mesh.subMeshCount = 2; mesh.SetTriangles(tri, 0); mesh.SetTriangles(back, 1);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }
        static Mesh pinMesh, tapeMesh; static readonly System.Collections.Generic.Dictionary<Color, Material> mats = new System.Collections.Generic.Dictionary<Color, Material>();
        internal static Material Mat(Color c, byte gloss)
        {
            Material mt; if (mats.TryGetValue(c, out mt) && mt != null) return mt;
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false); var px = new Color32[16]; Color32 c32 = c; c32.a = gloss; for (int i = 0; i < 16; i++) px[i] = c32;
            t.SetPixels32(px); t.Apply(false); t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            mt = FreePlace.MakeMaterial(t); if (mt != null) mats[c] = mt; return mt;
        }
        static Mesh PinMesh()
        {
            if (pinMesh != null) return pinMesh;
            // petit dome de punaise : 12 cotes
            const int S = 12; var v = new System.Collections.Generic.List<Vector3>(); var tr = new System.Collections.Generic.List<int>();
            v.Add(new Vector3(0, 0, -1f));
            for (int i = 0; i < S; i++) { float a = i * Mathf.PI * 2 / S; v.Add(new Vector3(Mathf.Cos(a) * 0.75f, Mathf.Sin(a) * 0.75f, -0.75f)); }
            for (int i = 0; i < S; i++) { float a = i * Mathf.PI * 2 / S; v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f)); }
            for (int i = 0; i < S; i++) { int a = 1 + i, b = 1 + (i + 1) % S; tr.Add(0); tr.Add(b); tr.Add(a); int c = a + S, d = b + S; tr.Add(a); tr.Add(b); tr.Add(d); tr.Add(a); tr.Add(d); tr.Add(c); }
            pinMesh = new Mesh { name = "HideoutPosterPin" }; pinMesh.SetVertices(v); pinMesh.SetTriangles(tr, 0);
            var uv = new Vector2[v.Count]; pinMesh.uv = uv; pinMesh.RecalculateNormals(); pinMesh.RecalculateBounds(); pinMesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return pinMesh;
        }
        static Mesh TapeMesh()
        {
            if (tapeMesh != null) return tapeMesh;
            tapeMesh = new Mesh { name = "HideoutPosterTape" };
            tapeMesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            tapeMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            tapeMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 }; tapeMesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            tapeMesh.RecalculateBounds(); tapeMesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tapeMesh;
        }
        static Shader cutSh; static bool cutTried;
        internal static Material StickerMat(Texture2D img)
        {
            if (!cutTried)
            {
                cutTried = true;
                var all = Resources.FindObjectsOfTypeAll<Shader>();
                var cuts = all.Where(x => x != null && x.name.IndexOf("cutout", StringComparison.OrdinalIgnoreCase) >= 0).Select(x => x.name).Distinct().ToList();
                PostersPlugin.Log.LogInfo("[sticker] shaders de decoupe : " + string.Join(" | ", cuts.Take(40).ToArray()));
                foreach (var n in new[] { "p0/Cutout/Reflective/Bumped Specular", "p0/Cutout/Reflective/Bumped Specular SMap", "p0/Cutout/Bumped Specular", "p0/Cutout/Bumped Diffuse", "p0/Cutout/Diffuse", "Legacy Shaders/Transparent/Cutout/Bumped Diffuse", "Legacy Shaders/Transparent/Cutout/Diffuse", "Transparent/Cutout/Diffuse" })
                { cutSh = all.FirstOrDefault(x => x != null && x.name == n) ?? Shader.Find(n); if (cutSh != null) break; }
                if (cutSh == null) { var c = all.FirstOrDefault(x => x != null && x.name.StartsWith("p0/Cutout", StringComparison.OrdinalIgnoreCase)); if (c != null) cutSh = c; }
                PostersPlugin.Log.LogInfo("[sticker] shader choisi : " + (cutSh != null ? cutSh.name : "AUCUN (stickers rectangulaires)"));
            }
            if (cutSh == null) return FreePlace.MakeMaterial(img);
            var m = new Material(cutSh);
            m.mainTexture = img; m.mainTextureScale = Vector2.one; m.mainTextureOffset = Vector2.zero;
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
            foreach (var cp in new[] { "_ReflectColor", "_SpecColor" }) if (m.HasProperty(cp)) m.SetColor(cp, Color.black);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            return m;
        }
        internal static Material BackMat() { return Mat(new Color(0.86f, 0.84f, 0.78f), 8); }
        static Material shadowMat; static bool shadowTried;
        static Material ShadowMat()
        {
            if (shadowTried) return shadowMat; shadowTried = true;
            Shader sh = null;
            foreach (var n in new[] { "Sprites/Default", "Legacy Shaders/Particles/Alpha Blended", "Particles/Alpha Blended", "UI/Default" }) { sh = Shader.Find(n); if (sh != null) break; }
            if (sh == null) { PostersPlugin.Log.LogInfo("[look] pas de shader transparent : pas d'ombres"); return null; }
            const int S = 64; var t = new Texture2D(S, S, TextureFormat.RGBA32, true); var px = new Color32[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { float dx = (x + 0.5f) / S * 2 - 1, dy = (y + 0.5f) / S * 2 - 1, d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy)); float a = (1 - d) * (1 - d); px[y * S + x] = new Color32(0, 0, 0, (byte)(a * 255)); }
            t.SetPixels32(px); t.Apply(true); t.wrapMode = TextureWrapMode.Clamp; t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            shadowMat = new Material(sh); shadowMat.mainTexture = t; if (shadowMat.HasProperty("_Color")) shadowMat.color = new Color(1, 1, 1, 0.55f); shadowMat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            PostersPlugin.Log.LogInfo("[look] ombres : " + sh.name);
            return shadowMat;
        }
        static void Shadow(Transform poster, Vector3 pos, float sx, float sy, float ang)
        {
            var sm = ShadowMat(); if (sm == null) return;
            var g = new GameObject("Shadow"); g.transform.SetParent(poster, false);
            g.transform.localPosition = pos; g.transform.localRotation = Quaternion.Euler(0, 0, ang); g.transform.localScale = new Vector3(sx, sy, 1f);
            g.AddComponent<MeshFilter>().sharedMesh = TapeMesh();
            var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = sm; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        }
        static Mesh cylM, cubeM;
        static Mesh CylM() { if (cylM == null) { var t = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cylM = t.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.Destroy(t); } return cylM; }
        static Mesh CubeM() { if (cubeM == null) { var t = GameObject.CreatePrimitive(PrimitiveType.Cube); cubeM = t.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.Destroy(t); } return cubeM; }
        static GameObject Piece(Transform parent, string name, Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localRotation = rot; g.transform.localScale = scale;
            g.AddComponent<MeshFilter>().sharedMesh = mesh; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return g;
        }
        // clous, agrafes, pinces, aimants, gros scotch gris
        static void Extra(Transform poster, float w, float h, L l)
        {
            float x0 = w * 0.5f, y0 = h * 0.5f, m = Mathf.Min(w, h);
            var flat = Quaternion.Euler(90f, 0f, 0f);   // cylindre couche : axe vers toi
            for (int c = 0; c < 4; c++)
            {
                var k = Corner(c, x0, y0); float sx = Mathf.Sign(k.x), sy = Mathf.Sign(k.y);
                if (l.Fix == 3 && c != l.CurlCorner && c != l.Curl2)   // clou a grosse tete
                {
                    float ins = Mathf.Clamp(m * 0.03f, 0.012f, 0.025f), r = 0.0045f; var pp = new Vector3(k.x - sx * ins, k.y - sy * ins, -0.0072f);
                    Piece(poster, "Nail", CylM(), pp, flat, new Vector3(r * 2, 0.0012f, r * 2), Mat(new Color(0.42f, 0.42f, 0.44f), 110));
                    Shadow(poster, new Vector3(pp.x + r * 0.4f, pp.y - r * 0.6f, -0.0064f), r * 4f, r * 4f, 0f);
                }
                if (l.Fix == 4 && c != l.CurlCorner && c != l.Curl2)   // agrafe en biais
                {
                    float ins = Mathf.Clamp(m * 0.025f, 0.01f, 0.02f); var pp = new Vector3(k.x - sx * ins, k.y - sy * ins, -0.007f);
                    float ang = (c == 0 || c == 3 ? 45f : -45f) + l.TapeAng[c];
                    Piece(poster, "Staple", CubeM(), pp, Quaternion.Euler(0, 0, ang), new Vector3(0.014f, 0.0016f, 0.0012f), Mat(new Color(0.75f, 0.76f, 0.78f), 140));
                }
                if (l.Fix == 6 && c != l.CurlCorner && c != l.Curl2)   // aimant rond
                {
                    float ins = Mathf.Clamp(m * 0.04f, 0.015f, 0.03f), r = 0.011f; var pp = new Vector3(k.x - sx * ins, k.y - sy * ins, -0.0085f);
                    Piece(poster, "Magnet", CylM(), pp, flat, new Vector3(r * 2, 0.0025f, r * 2), Mat(l.Mag, 90));
                    Shadow(poster, new Vector3(pp.x + r * 0.3f, pp.y - r * 0.5f, -0.0064f), r * 3.4f, r * 3.4f, 0f);
                }
            }
            if (l.Fix == 5)   // deux pinces a dessin noires en haut
            {
                var black = Mat(new Color(0.06f, 0.06f, 0.065f), 70); var steel = Mat(new Color(0.7f, 0.71f, 0.73f), 150);
                float cw = Mathf.Clamp(m * 0.09f, 0.03f, 0.05f);
                foreach (var fx in new[] { -0.28f, 0.28f })
                {
                    var pp = new Vector3(w * fx, y0 - cw * 0.3f, -0.009f);
                    Piece(poster, "Clip", CubeM(), pp, Quaternion.identity, new Vector3(cw, cw * 0.6f, 0.006f), black);
                    Piece(poster, "ClipWire", CubeM(), pp + new Vector3(-cw * 0.3f, cw * 0.45f, -0.003f), Quaternion.Euler(-35, 0, 0), new Vector3(0.0015f, cw * 0.6f, 0.0015f), steel);
                    Piece(poster, "ClipWire", CubeM(), pp + new Vector3(cw * 0.3f, cw * 0.45f, -0.003f), Quaternion.Euler(-35, 0, 0), new Vector3(0.0015f, cw * 0.6f, 0.0015f), steel);
                    Shadow(poster, new Vector3(pp.x + 0.003f, pp.y - cw * 0.35f, -0.0064f), cw * 1.4f, cw * 0.9f, 0f);
                }
            }
            if (l.Fix == 8)   // corde au-dessus + 2 pinces a linge en bois
            {
                var cord = Mat(new Color(0.55f, 0.48f, 0.36f), 20); var wood = Mat(new Color(0.72f, 0.56f, 0.36f), 30); var steel = Mat(new Color(0.7f, 0.71f, 0.73f), 150); var nail = Mat(new Color(0.4f, 0.4f, 0.42f), 110);
                float span = w + 0.16f, cy = y0 - 0.005f;
                Piece(poster, "Cord", CubeM(), new Vector3(0, cy, -0.009f), Quaternion.identity, new Vector3(span, 0.003f, 0.003f), cord);
                foreach (var ex in new[] { -span * 0.5f, span * 0.5f }) Piece(poster, "Nail", CylM(), new Vector3(ex, cy, -0.007f), flat, new Vector3(0.008f, 0.0012f, 0.008f), nail);
                foreach (var fx in new[] { -0.3f, 0.3f })
                {
                    var pp = new Vector3(w * fx, cy - 0.012f, -0.0105f); float a = (float)(l.TapeAng[fx < 0 ? 0 : 1] * 0.5f);
                    var g = Piece(poster, "Clothespin", CubeM(), pp, Quaternion.Euler(0, 0, a), new Vector3(0.011f, 0.07f, 0.007f), wood);
                    Piece(poster, "Spring", CubeM(), pp + new Vector3(0, 0.008f, -0.004f), Quaternion.Euler(0, 0, a), new Vector3(0.013f, 0.006f, 0.002f), steel);
                    Shadow(poster, new Vector3(pp.x + 0.003f, pp.y - 0.004f, -0.0064f), 0.025f, 0.08f, a);
                }
            }
            if (l.Fix == 10 || l.Fix == 12)
            {
                for (int c = 0; c < 4; c++)
                {
                    if (c == l.CurlCorner || c == l.Curl2) continue;
                    var k = Corner(c, x0, y0); float sx = Mathf.Sign(k.x), sy = Mathf.Sign(k.y);
                    if (l.Fix == 10)   // boule de chewing-gum rose ecrasee
                    {
                        float ins = Mathf.Clamp(m * 0.02f, 0.008f, 0.016f), r = 0.012f + l.TapeAng[c] * 0.0004f; var pp = new Vector3(k.x - sx * ins, k.y - sy * ins, -0.0075f);
                        Piece(poster, "Gum", Glow.SphereMesh(), pp, Quaternion.Euler(0, 0, l.TapeAng[c] * 4f), new Vector3(r * 1.6f, r * 1.2f, r * 0.45f), Mat(new Color(0.95f, 0.5f, 0.68f), 50));
                    }
                    else               // gros boulon industriel avec rondelle
                    {
                        float ins = Mathf.Clamp(m * 0.035f, 0.015f, 0.03f); var pp = new Vector3(k.x - sx * ins, k.y - sy * ins, -0.0075f);
                        Piece(poster, "Washer", CylM(), pp, flat, new Vector3(0.024f, 0.0015f, 0.024f), Mat(new Color(0.5f, 0.5f, 0.52f), 120));
                        Piece(poster, "Bolt", CylM(), pp + new Vector3(0, 0, -0.004f), flat * Quaternion.Euler(0, 30, 0), new Vector3(0.015f, 0.004f, 0.015f), Mat(new Color(0.35f, 0.33f, 0.3f), 90));
                        Shadow(poster, new Vector3(pp.x + 0.003f, pp.y - 0.005f, -0.0064f), 0.04f, 0.04f, 0f);
                    }
                }
            }
            if (l.Fix == 11)   // deux bandes de scotch croisees en X sur chaque coin
            {
                var tape = Mat(l.Tape, 14); float len = Mathf.Clamp(m * 0.16f, 0.06f, 0.13f), wid = len * 0.32f;
                for (int c = 0; c < 4; c++)
                {
                    if (c == l.CurlCorner || c == l.Curl2) continue;
                    var k = Corner(c, x0, y0); float baseA = c == 0 || c == 3 ? -45f : 45f;
                    for (int s2 = 0; s2 < 2; s2++)
                    {
                        float a = baseA + (s2 == 0 ? 0f : 90f) + l.TapeAng[c] * 0.5f;
                        Piece(poster, "Tape", TapeMesh(), new Vector3(k.x, k.y, -0.0068f - s2 * 0.0003f), Quaternion.Euler(0, 0, a), new Vector3(len, wid, 1f), tape);
                        Shadow(poster, new Vector3(k.x, k.y - wid * 0.08f, -0.0064f), len * 1.08f, wid * 1.5f, a);
                    }
                }
            }
            if (l.Fix == 9)   // accrochee a un clou par une ficelle
            {
                var str = Mat(new Color(0.62f, 0.55f, 0.42f), 20); var nail = Mat(new Color(0.4f, 0.4f, 0.42f), 110); var dark = Mat(new Color(0.08f, 0.08f, 0.08f), 20);
                float ny = y0 + Mathf.Clamp(h * 0.18f, 0.07f, 0.22f), ins = 0.02f;
                Piece(poster, "Nail", CylM(), new Vector3(0, ny, -0.008f), flat, new Vector3(0.01f, 0.0015f, 0.01f), nail);
                Shadow(poster, new Vector3(0.002f, ny - 0.004f, -0.0064f), 0.025f, 0.025f, 0f);
                foreach (var sx in new[] { -1f, 1f })
                {
                    var a2 = new Vector2(sx * (x0 - ins), y0 - ins); var b2 = new Vector2(0, ny); var mid = (a2 + b2) * 0.5f; var d = b2 - a2;
                    Piece(poster, "String", CubeM(), new Vector3(mid.x, mid.y, -0.0085f), Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg), new Vector3(d.magnitude, 0.0025f, 0.0025f), str);
                    Piece(poster, "Eyelet", CylM(), new Vector3(a2.x, a2.y, -0.0068f), flat, new Vector3(0.008f, 0.0008f, 0.008f), dark);
                }
            }
            if (l.Fix == 7)   // gros scotch gris a cheval sur chaque bord
            {
                var grey = Mat(new Color(0.55f, 0.56f, 0.57f), 40);
                float len = Mathf.Clamp(m * 0.2f, 0.07f, 0.15f), wid = len * 0.45f;
                var spots = new[] { new Vector3(0, y0, 90f), new Vector3(0, -y0, 90f), new Vector3(-x0, 0, 0f), new Vector3(x0, 0, 0f) };
                for (int i = 0; i < 4; i++)
                {
                    var sp = spots[i]; float a = sp.z + l.TapeAng[i] * 0.6f;
                    Piece(poster, "Tape", TapeMesh(), new Vector3(sp.x, sp.y, -0.0068f), Quaternion.Euler(0, 0, a), new Vector3(len, wid, 1f), grey);
                    Shadow(poster, new Vector3(sp.x, sp.y - wid * 0.06f, -0.0064f), len * 1.05f, wid * 1.4f, a);
                }
            }
        }
        internal static void Fixings(Transform poster, float w, float h, L l)
        {
            float x0 = w * 0.5f, y0 = h * 0.5f, m = Mathf.Min(w, h);
            // ombre sur le mur sous le coin corne
            if (l.CurlCorner >= 0)
            {
                float curlR = Mathf.Clamp(m * 0.2f, 0.05f, 0.18f); var k0 = Corner(l.CurlCorner, x0, y0);
                var inward = new Vector2(-Mathf.Sign(k0.x), -Mathf.Sign(k0.y)) * curlR * 0.18f;
                Shadow(poster, new Vector3(k0.x + inward.x, k0.y + inward.y - curlR * 0.08f, -0.002f), curlR * 1.5f, curlR * 1.5f, 0f);
            }
            if (l.Curl2 >= 0)
            {
                float curlR2 = Mathf.Clamp(m * 0.2f, 0.05f, 0.18f); var k2 = Corner(l.Curl2, x0, y0);
                var in2 = new Vector2(-Mathf.Sign(k2.x), -Mathf.Sign(k2.y)) * curlR2 * 0.18f;
                Shadow(poster, new Vector3(k2.x + in2.x, k2.y + in2.y - curlR2 * 0.08f, -0.002f), curlR2 * 1.5f, curlR2 * 1.5f, 0f);
            }
            if (l.Fix == 0) return;
            if (l.Fix >= 3) { Extra(poster, w, h, l); return; }   // 3 clous, 4 agrafes, 5 pinces, 6 aimants, 7 gros scotch, 8 pinces a linge, 9 ficelle
            for (int c = 0; c < 4; c++)
            {
                if (!l.Four && c < 2) continue;          // 2 fixations : seulement en haut
                if (c == l.CurlCorner || c == l.Curl2) continue;          // le coin corne n'est plus tenu
                var k = Corner(c, x0, y0);
                var g = new GameObject(l.Fix == 1 ? "Pin" : "Tape"); g.transform.SetParent(poster, false);
                var mr = g.AddComponent<MeshRenderer>(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var mf = g.AddComponent<MeshFilter>();
                if (l.Fix == 1)
                {
                    float ins = Mathf.Clamp(m * 0.035f, 0.012f, 0.03f), r = Mathf.Clamp(m * 0.012f, 0.005f, 0.009f);
                    var pp = new Vector3(k.x - Mathf.Sign(k.x) * ins, k.y - Mathf.Sign(k.y) * ins, -0.0068f);
                    g.transform.localPosition = pp;
                    g.transform.localScale = new Vector3(r, r, r * 0.9f);
                    Shadow(poster, new Vector3(pp.x + r * 0.35f, pp.y - r * 0.55f, -0.0063f), r * 3.2f, r * 3.2f, 0f);
                    mf.sharedMesh = PinMesh(); mr.sharedMaterial = Mat(l.Pin, 60);
                }
                else
                {
                    float len = Mathf.Clamp(m * 0.16f, 0.06f, 0.13f), wid = len * 0.32f;
                    g.transform.localPosition = new Vector3(k.x, k.y, -0.0068f);
                    float ang = (c == 0 || c == 3 ? -45f : 45f) + l.TapeAng[c];
                    g.transform.localRotation = Quaternion.Euler(0, 0, ang);
                    g.transform.localScale = new Vector3(len, wid, 1f);
                    mf.sharedMesh = TapeMesh(); mr.sharedMaterial = Mat(l.Tape, 14);
                    Shadow(poster, new Vector3(k.x, k.y - wid * 0.08f, -0.0064f), len * 1.08f, wid * 1.5f, ang);
                }
            }
        }
    }

    // ----- affiches retroeclairees : image lumineuse + lueur sur le mur -----
    static class Glow
    {
        internal static BepInEx.Configuration.ConfigEntry<float> cPower, cSpeed;
        internal static float Slow { get { return 1f / Mathf.Max(0.25f, cSpeed != null ? cSpeed.Value : 1f); } }   // multiplie les durees
        static Shader unlit; static bool tried; static bool uiShader;
        static readonly System.Collections.Generic.Dictionary<string, Texture2D> opaque = new System.Collections.Generic.Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        static readonly System.Collections.Generic.Dictionary<string, Color> avg = new System.Collections.Generic.Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        internal static void Bind(BepInEx.Configuration.ConfigFile c)
        {
            cPower = c.Bind("Look", "Light power", 1f, new BepInEx.Configuration.ConfigDescription("Strength of all poster lights: backlight, neons and spots (0 = no light at all, 1 = normal, 3 = max)", new BepInEx.Configuration.AcceptableValueRange<float>(0f, 3f)));
            cPower.SettingChanged += (a, b) => { try { FreePlace.Resize(null); } catch { } };
            cSpeed = c.Bind("Look", "Blinking speed", 1f, new BepInEx.Configuration.ConfigDescription("Speed of blinking neons, police lights and chasing lights (0.25 = very slow, 1 = normal, 2 = very fast)", new BepInEx.Configuration.AcceptableValueRange<float>(0.25f, 2f)));
        }
        static Shader Unlit()
        {
            if (tried) return unlit; tried = true;
            foreach (var n in new[] { "Unlit/Texture", "Legacy Shaders/Self-Illumin/Diffuse", "Self-Illumin/Diffuse", "UI/Default" }) { unlit = Shader.Find(n); if (unlit != null) break; }
            uiShader = unlit != null && unlit.name == "UI/Default";
            PostersPlugin.Log.LogInfo("[glow] shader lumineux : " + (unlit != null ? unlit.name : "AUCUN (lueur seule)"));
            return unlit;
        }
        internal static Material LitMat(string file, Texture2D img, bool sticker)
        {
            var sh = Unlit(); if (sh == null) return null;
            bool gif = PostersPlugin.AnimOf(file) != null;
            if (gif && uiShader && !sticker) return null;   // les images du GIF sont presque transparentes : on garde le rendu normal
            Texture2D t = img;
            if (uiShader && !sticker)
            {
                // le shader UI tient compte de la transparence : copie opaque de l'image
                if (!opaque.TryGetValue(file, out t) || t == null)
                {
                    var px = img.GetPixels32(); for (int i = 0; i < px.Length; i++) px[i].a = 255;
                    t = new Texture2D(img.width, img.height, TextureFormat.RGBA32, true); t.SetPixels32(px); t.Apply(true, true);
                    t.wrapMode = TextureWrapMode.Clamp; t.anisoLevel = 16; t.hideFlags = HideFlags.DontUnloadUnusedAsset; opaque[file] = t;
                }
            }
            var m = new Material(sh); m.mainTexture = t;
            if (m.HasProperty("_Color")) m.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            if (m.HasProperty("_Illum")) m.SetTexture("_Illum", t);
            return m;
        }
        static Color Average(string file, Texture2D img)
        {
            Color c; if (avg.TryGetValue(file, out c)) return c;
            float r = 0, g = 0, b = 0; int n = 0;
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) { var p = img.GetPixelBilinear((x + 0.5f) / 16f, (y + 0.5f) / 16f); r += p.r; g += p.g; b += p.b; n++; }
            c = new Color(r / n, g / n, b / n);
            float h, s, v; Color.RGBToHSV(c, out h, out s, out v); c = Color.HSVToRGB(h, Mathf.Clamp01(s * 1.3f), 1f);   // lueur franche, couleur dominante de l'affiche
            avg[file] = c; return c;
        }
        internal static Mesh SphereMesh() { return Sphere(); }
        static Mesh sphere, cyl;
        static Mesh Sphere() { if (sphere == null) { var t = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere = t.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.Destroy(t); } return sphere; }
        static Mesh Cyl() { if (cyl == null) { var t = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cyl = t.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.Destroy(t); } return cyl; }
        static Mesh box;
        static Mesh Box()
        {
            if (box != null) return box;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube); box = tmp.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.Destroy(tmp);
            return box;
        }
        static void Part(Transform parent, Vector3 pos, Vector3 scale, Material m, Quaternion rot)
        {
            var g = new GameObject("LampPart"); g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localRotation = rot; g.transform.localScale = scale;
            g.AddComponent<MeshFilter>().sharedMesh = Box(); var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        // petite lampe de galerie au-dessus de l'affiche, qui l'eclaire vers le bas
        internal static void AddLamp(Transform poster, float w, float h) { if (HasSingle) AddLamp(poster, w, h, SingleCol, SingleCol); else AddLamp(poster, w, h, new Color(1f, 0.92f, 0.78f), new Color(1f, 0.93f, 0.75f)); }
        internal static void AddLamp(Transform poster, float w, float h, Color lightCol, Color tubeCol)
        {
            float pw = cPower != null ? cPower.Value : 1f;
            var metal = Look.Mat(new Color(0.12f, 0.12f, 0.13f), 70); var bulb = Bright(tubeCol);
            float y = h * 0.5f + 0.05f, reach = Mathf.Clamp(Mathf.Max(w, h) * 0.3f, 0.18f, 0.45f), hw = Mathf.Clamp(w * 0.35f, 0.12f, 0.6f);
            var root = new GameObject("Lamp"); root.transform.SetParent(poster, false);
            Part(root.transform, new Vector3(0, y, -0.01f), new Vector3(0.06f, 0.04f, 0.02f), metal, Quaternion.identity);                    // fixation au mur
            Part(root.transform, new Vector3(0, y + 0.01f, -reach * 0.5f), new Vector3(0.012f, 0.012f, reach), metal, Quaternion.identity);  // bras
            Part(root.transform, new Vector3(0, y - 0.005f, -reach), new Vector3(hw, 0.035f, 0.05f), metal, Quaternion.Euler(-25f, 0, 0));   // tete
            Part(root.transform, new Vector3(0, y - 0.022f, -reach + 0.008f), new Vector3(hw * 0.92f, 0.004f, 0.03f), bulb, Quaternion.Euler(-25f, 0, 0)); // tube
            if (pw <= 0.01f) return;
            var lg = new GameObject("LampLight"); lg.transform.SetParent(root.transform, false);
            lg.transform.localPosition = new Vector3(0, y - 0.03f, -reach);
            lg.transform.localRotation = Quaternion.LookRotation(new Vector3(0, -h * 0.55f, reach), Vector3.up);
            var l = lg.AddComponent<Light>(); l.type = LightType.Spot; l.shadows = LightShadows.None; l.color = lightCol;
            l.spotAngle = Mathf.Clamp(Mathf.Atan2(Mathf.Max(w, h), reach) * Mathf.Rad2Deg * 1.6f, 60f, 140f); l.range = Mathf.Clamp(h * 1.8f + reach, 1f, 6f); l.intensity = 1.6f * pw;
            lg.AddComponent<GlowCull>().l = l;
        }
        internal static int Pick;   // couleur choisie pour l'affiche en cours de construction (0 = auto)
        internal static readonly string[] ColNames = { "AUTO", "MULTICOLOR", "WARM WHITE", "COLD WHITE", "CHRISTMAS", "RED", "ORANGE", "YELLOW", "GREEN", "CYAN", "BLUE", "PURPLE", "PINK" };
        static readonly Color[] Single = { Color.white, Color.white, new Color(1f, 0.8f, 0.55f), new Color(0.75f, 0.88f, 1f), new Color(1f, 0.15f, 0.1f), new Color(1f, 0.1f, 0.08f), new Color(1f, 0.45f, 0.05f), new Color(1f, 0.85f, 0.15f), new Color(0.15f, 1f, 0.3f), new Color(0.1f, 0.95f, 1f), new Color(0.15f, 0.3f, 1f), new Color(0.65f, 0.2f, 1f), new Color(1f, 0.3f, 0.75f) };
        internal static bool HasSingle { get { return Pick >= 2 && Pick != 4; } }   // une seule couleur imposee
        internal static Color SingleCol { get { return Single[Mathf.Clamp(Pick, 0, Single.Length - 1)]; } }
        internal static readonly string[] Names = { "off", "BACKLIT", "LAMP", "NEON", "BROKEN NEON", "STRING LIGHTS", "BLINKING NEON", "LIGHT FRAME", "FAULTY NEON", "RAINBOW NEON", "PULSE NEON", "CHASE FRAME", "UV BLACKLIGHT", "HORROR UPLIGHT", "POLICE NEON", "FIRE GLOW" };
        static readonly System.Collections.Generic.Dictionary<Color, Material> glowMats = new System.Collections.Generic.Dictionary<Color, Material>();
        // materiau qui brille tout seul (non eclaire) d'une couleur
        static Material dark;
        internal static Material DarkGlass() { if (dark == null) dark = Look.Mat(new Color(0.16f, 0.16f, 0.17f), 120); return dark; }   // tube ou ampoule eteinte
        internal static bool IsBright(Material m) { return m != null && glowMats.ContainsValue(m); }
        internal static Material Bright(Color c)
        {
            Material m; if (glowMats.TryGetValue(c, out m) && m != null) return m;
            var sh = Unlit();
            if (sh == null) return Look.Mat(c, 8);
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false); var px = new Color32[16]; Color32 c32 = c; c32.a = 255; for (int i = 0; i < 16; i++) px[i] = c32;
            t.SetPixels32(px); t.Apply(false); t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            m = new Material(sh); m.mainTexture = t; if (m.HasProperty("_Color")) m.color = Color.white; if (m.HasProperty("_Illum")) m.SetTexture("_Illum", t);
            m.hideFlags = HideFlags.DontUnloadUnusedAsset; glowMats[c] = m; return m;
        }
        static GameObject Bar(Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var g = new GameObject("Neon"); g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            g.AddComponent<MeshFilter>().sharedMesh = Box(); var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return g;
        }
        internal static Color NeonColor(string file, Texture2D img) { if (HasSingle) return SingleCol; if (Pick == 4) return new Color(1f, 0.12f, 0.1f); var c = Average(file, img); float hh, ss, vv; Color.RGBToHSV(c, out hh, out ss, out vv); return Color.HSVToRGB(hh, Mathf.Max(0.8f, ss), 1f); }   // couleur franche de neon
        internal static Material TubeMat(Color c) { return Bright(Color.Lerp(c, Color.white, 0.25f)); }
        internal static Material HaloMat(Color c)
        {
            if (haloBase == null) return null;
            Material m; if (!haloMats.TryGetValue(c, out m) || m == null)
            {
                m = new Material(haloBase); var cc = c; cc.a = 0.55f;
                if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, 0.5f));
                if (m.HasProperty("_Color")) m.color = cc;
                haloMats[c] = m;
            }
            return m;
        }
        // petit projecteur pose sous l'affiche, qui eclaire vers le haut : eclairage de film d'horreur
        internal static void AddUplight(Transform poster, float w, float h)
        {
            float pw = cPower != null ? cPower.Value : 1f;
            var metal = Look.Mat(new Color(0.1f, 0.1f, 0.11f), 80); var lens = Bright(new Color(1f, 0.95f, 0.85f));
            var root = new GameObject("Uplight"); root.transform.SetParent(poster, false);
            float y = -h * 0.5f - 0.09f;
            Part(root.transform, new Vector3(0, y - 0.03f, -0.04f), new Vector3(0.05f, 0.012f, 0.06f), metal, Quaternion.identity);
            Part(root.transform, new Vector3(0, y, -0.06f), new Vector3(0.06f, 0.05f, 0.05f), metal, Quaternion.Euler(35, 0, 0));
            Part(root.transform, new Vector3(0, y + 0.012f, -0.07f), new Vector3(0.045f, 0.035f, 0.004f), lens, Quaternion.Euler(35, 0, 0));
            if (pw <= 0.01f) return;
            var lg = new GameObject("UpLight"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3(0, y + 0.02f, -0.12f);
            lg.transform.localRotation = Quaternion.LookRotation(new Vector3(0, h * 0.7f, 0.12f), Vector3.up);
            var l = lg.AddComponent<Light>(); l.type = LightType.Spot; l.shadows = LightShadows.None; l.color = new Color(1f, 0.96f, 0.9f);
            l.spotAngle = 70f; l.range = Mathf.Clamp(h * 2f, 1.2f, 6f); l.intensity = 2.2f * pw; lg.AddComponent<GlowCull>().l = l;
        }
        // lueur orange qui danse, comme un feu de camp ou un brasero juste devant
        internal static void AddFire(Transform poster, float w, float h, int seed)
        {
            float pw = cPower != null ? cPower.Value : 1f; if (pw <= 0.01f) return;
            var lg = new GameObject("FireLight"); lg.transform.SetParent(poster, false); lg.transform.localPosition = new Vector3(0, -h * 0.55f, -0.5f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.shadows = LightShadows.None; l.color = new Color(1f, 0.5f, 0.15f);
            l.range = Mathf.Clamp(Mathf.Max(w, h) * 2.5f, 1.5f, 6f); l.intensity = 1.6f * pw;
            lg.AddComponent<GlowCull>().l = l; var f = lg.AddComponent<FireFlicker>(); f.l = l; f.baseI = l.intensity; f.seed = seed;
        }
        // tube neon tout autour de l'affiche, de sa couleur dominante ; « casse » = il gresille
        internal static void AddNeon(Transform poster, string file, Texture2D img, float w, float h, bool broken, int seed)
        {
            var c = NeonColor(file, img);
            var m = TubeMat(c);   // le coeur du tube est presque blanc, comme un vrai neon
            var root = new GameObject("NeonFrame"); root.transform.SetParent(poster, false);
            float t = Mathf.Clamp(Mathf.Min(w, h) * 0.018f, 0.009f, 0.02f), gap = 0.03f, x = w * 0.5f + gap, y = h * 0.5f + gap, z = -0.015f;
            // halo colore autour du tube (flou lumineux)
            float hw = t * 9f;
            Halo(root.transform, new Vector3(0, y, z + 0.002f), 2 * x + hw, hw, 0f, c);
            Halo(root.transform, new Vector3(0, -y, z + 0.002f), 2 * x + hw, hw, 0f, c);
            Halo(root.transform, new Vector3(-x, 0, z + 0.002f), 2 * y + hw, hw, 90f, c);
            Halo(root.transform, new Vector3(x, 0, z + 0.002f), 2 * y + hw, hw, 90f, c);
            Bar(root.transform, new Vector3(0, y, z), new Vector3(2 * x + t, t, t), m);
            Bar(root.transform, new Vector3(0, -y, z), new Vector3(2 * x + t, t, t), m);
            Bar(root.transform, new Vector3(-x, 0, z), new Vector3(t, 2 * y, t), m);
            Bar(root.transform, new Vector3(x, 0, z), new Vector3(t, 2 * y, t), m);
            var dark = Look.Mat(new Color(0.1f, 0.1f, 0.11f), 40);   // petites attaches du tube
            foreach (var p in new[] { new Vector2(-x, y), new Vector2(x, y), new Vector2(-x, -y), new Vector2(x, -y) }) Bar(root.transform, new Vector3(p.x, p.y, -0.006f), new Vector3(t * 2.2f, t * 2.2f, 0.012f), dark).name = "Clip";
            float pw = cPower != null ? cPower.Value : 1f;
            Light l = null;
            if (pw > 0.01f)
            {
                var lg = new GameObject("NeonLight"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3(0, 0, -Mathf.Clamp(Mathf.Max(w, h) * 0.25f, 0.15f, 0.6f));
                l = lg.AddComponent<Light>(); l.type = LightType.Point; l.shadows = LightShadows.None; l.color = c; l.range = Mathf.Clamp(Mathf.Max(w, h) * 2.8f, 1.2f, 7f); l.intensity = 1.8f * pw;
                lg.AddComponent<GlowCull>().l = l;
            }
            if (broken) { var f = root.AddComponent<NeonFlicker>(); f.l = l; f.seed = seed; }
        }
        static Material haloBase; static bool haloTried; static Mesh haloQuad;
        static readonly System.Collections.Generic.Dictionary<Color, Material> haloMats = new System.Collections.Generic.Dictionary<Color, Material>();
        static Material roundBase; static readonly System.Collections.Generic.Dictionary<Color, Material> roundMats = new System.Collections.Generic.Dictionary<Color, Material>();
        static void Halo(Transform parent, Vector3 pos, float len, float wid, float ang, Color c) { Halo(parent, pos, len, wid, ang, c, false); }
        static void Halo(Transform parent, Vector3 pos, float len, float wid, float ang, Color c, bool round)
        {
            if (!haloTried)
            {
                haloTried = true; Shader sh = null;
                foreach (var n in new[] { "Legacy Shaders/Particles/Additive", "Particles/Additive", "Sprites/Default", "UI/Default" }) { sh = Shader.Find(n); if (sh != null) break; }
                if (sh != null)
                {
                    // degrade : fort au centre (le tube), nul sur les bords
                    var tx = new Texture2D(4, 64, TextureFormat.RGBA32, false); var px = new Color32[4 * 64];
                    for (int yy = 0; yy < 64; yy++) { float d = Mathf.Abs((yy + 0.5f) / 64f * 2f - 1f); float a = Mathf.Pow(1f - d, 2.2f); for (int xx = 0; xx < 4; xx++) px[yy * 4 + xx] = new Color32(255, 255, 255, (byte)(a * 255)); }
                    tx.SetPixels32(px); tx.Apply(false); tx.wrapMode = TextureWrapMode.Clamp; tx.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    haloBase = new Material(sh); haloBase.mainTexture = tx; haloBase.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    PostersPlugin.Log.LogInfo("[neon] halo : " + sh.name);
                }
                haloQuad = new Mesh { name = "HaloQuad" };
                haloQuad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
                haloQuad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                haloQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 }; haloQuad.RecalculateNormals(); haloQuad.RecalculateBounds(); haloQuad.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            if (haloBase == null) return;
            if (round && roundBase == null)
            {
                const int S = 64; var tx = new Texture2D(S, S, TextureFormat.RGBA32, false); var px = new Color32[S * S];
                for (int yy = 0; yy < S; yy++) for (int xx = 0; xx < S; xx++) { float dx = (xx + 0.5f) / S * 2 - 1, dy = (yy + 0.5f) / S * 2 - 1; float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy)); px[yy * S + xx] = new Color32(255, 255, 255, (byte)(Mathf.Pow(1 - d, 2.5f) * 255)); }
                tx.SetPixels32(px); tx.Apply(false); tx.wrapMode = TextureWrapMode.Clamp; tx.hideFlags = HideFlags.DontUnloadUnusedAsset;
                roundBase = new Material(haloBase); roundBase.mainTexture = tx; roundBase.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            var dict = round ? roundMats : haloMats;
            Material m; if (!dict.TryGetValue(c, out m) || m == null)
            {
                m = new Material(round ? roundBase : haloBase); var cc = c; cc.a = 0.55f;
                if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, 0.5f));
                if (m.HasProperty("_Color")) m.color = cc;
                dict[c] = m;
            }
            var g = new GameObject("Halo"); g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localRotation = Quaternion.Euler(0, 0, ang); g.transform.localScale = new Vector3(len, wid, 1f);
            g.AddComponent<MeshFilter>().sharedMesh = haloQuad; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }
        // guirlande d'ampoules qui pend au-dessus de l'affiche
        internal static void AddString(Transform poster, float w, float h, int seed) { AddString(poster, w, h, seed, false); }
        internal static void AddString(Transform poster, float w, float h, int seed, bool frame)
        {
            var r = new System.Random(seed);
            var cols = new[] { new Color(1f, 0.25f, 0.2f), new Color(0.25f, 1f, 0.35f), new Color(0.3f, 0.5f, 1f), new Color(1f, 0.85f, 0.25f), new Color(1f, 0.4f, 0.9f) };
            bool warm = r.NextDouble() < 0.35;   // parfois une guirlande blanc chaud
            if (Pick == 1 || Pick == 4) warm = false; else if (Pick >= 2) warm = false;
            if (Pick == 4) cols = new[] { new Color(1f, 0.12f, 0.1f), new Color(0.1f, 0.9f, 0.25f), new Color(1f, 0.8f, 0.2f) };   // Noel : rouge, vert, or
            else if (HasSingle) cols = new[] { SingleCol };
            var root = new GameObject("StringLights"); root.transform.SetParent(poster, false);
            float span = w + 0.12f, top = h * 0.5f + 0.04f, sag = Mathf.Clamp(w * 0.08f, 0.03f, 0.12f);
            // le chemin du fil : un arc au-dessus, ou tout le tour du cadre (avec de petites boucles)
            var pts = new System.Collections.Generic.List<Vector3>();
            if (!frame)
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(span / 0.09f), 5, 40);
                for (int i = 0; i <= n; i++) { float u = (float)i / n; pts.Add(new Vector3(-span * 0.5f + u * span, top - sag * 4f * u * (1 - u), -0.02f)); }
            }
            else
            {
                float gx = w * 0.5f + 0.045f, gy = h * 0.5f + 0.045f, per = 4 * (gx + gy);
                int n = Mathf.Clamp(Mathf.RoundToInt(per / 0.085f), 12, 90);
                for (int i = 0; i < n; i++)
                {
                    float d = per * i / n; Vector2 q, outN;
                    if (d < 2 * gx) { q = new Vector2(-gx + d, gy); outN = Vector2.up; }
                    else if ((d -= 2 * gx) < 2 * gy) { q = new Vector2(gx, gy - d); outN = Vector2.right; }
                    else if ((d -= 2 * gy) < 2 * gx) { q = new Vector2(gx - d, -gy); outN = Vector2.down; }
                    else { d -= 2 * gx; q = new Vector2(-gx, -gy + d); outN = Vector2.left; }
                    float scallop = Mathf.Abs(Mathf.Sin(i * Mathf.PI / 3f)) * 0.012f;   // petites boucles entre les attaches
                    q += outN * scallop; pts.Add(new Vector3(q.x, q.y, -0.02f));
                }
                pts.Add(pts[0]);   // boucle fermee
            }
            var wire = Look.Mat(new Color(0.05f, 0.06f, 0.05f), 30);
            var bulbIdx = new System.Collections.Generic.List<int>(); var halos = new System.Collections.Generic.List<GameObject>(); var bulbs = new System.Collections.Generic.List<Renderer>(); var on = new System.Collections.Generic.List<Material>(); var off = new System.Collections.Generic.List<Material>();
            for (int i = 0; i < pts.Count; i++)
            {
                var pt = pts[i];
                if (i > 0)
                {
                    var prev = pts[i - 1]; var mid = (prev + pt) * 0.5f; var d = pt - prev;
                    var seg = new GameObject("Wire"); seg.transform.SetParent(root.transform, false); seg.transform.localPosition = mid;
                    seg.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg); seg.transform.localScale = new Vector3(d.magnitude, 0.0045f, 0.0045f);
                    seg.AddComponent<MeshFilter>().sharedMesh = Box(); var sr = seg.AddComponent<MeshRenderer>(); sr.sharedMaterial = wire; sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                if (!frame && (i == 0 || i == pts.Count - 1)) continue;
                if (frame && i == pts.Count - 1) continue;
                var c = warm ? new Color(1f, 0.82f, 0.55f) : cols[(i + seed) % cols.Length];
                // ampoule qui pend un peu de travers sous sa douille
                var hang = new GameObject("Hang"); hang.transform.SetParent(root.transform, false); hang.transform.localPosition = pt;
                hang.transform.localRotation = Quaternion.Euler(0, 0, (float)(r.NextDouble() * 30 - 15));
                var sock = new GameObject("Socket"); sock.transform.SetParent(hang.transform, false); sock.transform.localPosition = new Vector3(0, -0.008f, 0); sock.transform.localScale = new Vector3(0.009f, 0.007f, 0.009f);
                sock.AddComponent<MeshFilter>().sharedMesh = Cyl(); var sr2 = sock.AddComponent<MeshRenderer>(); sr2.sharedMaterial = wire; sr2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var bulb = new GameObject("Bulb"); bulb.transform.SetParent(hang.transform, false); bulb.transform.localPosition = new Vector3(0, -0.025f, 0); bulb.transform.localScale = new Vector3(0.016f, 0.024f, 0.016f);
                bulb.AddComponent<MeshFilter>().sharedMesh = Sphere(); var br = bulb.AddComponent<MeshRenderer>(); br.sharedMaterial = Bright(c); br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Halo(hang.transform, new Vector3(0, -0.025f, 0.004f), 0.07f, 0.07f, 0f, c, true);
                var hl = hang.transform.Find("Halo"); halos.Add(hl != null ? hl.gameObject : null);
                bulbs.Add(br); on.Add(Bright(c)); off.Add(Bright(c * 0.25f));
                bulbIdx.Add(i);
            }
            float pw = cPower != null ? cPower.Value : 1f;
            Light l = null; var lights = new Light[bulbs.Count];
            if (pw > 0.01f && bulbs.Count > 0)
            {
                // plusieurs petites lumieres de la couleur des ampoules : le mur se colore par endroits
                int nl = frame ? Mathf.Clamp(bulbs.Count / 5, 4, 8) : Mathf.Clamp(bulbs.Count / 3, 2, 5);
                if (frame) span = 2f * (w + h);
                for (int k = 0; k < nl; k++)
                {
                    int bi = Mathf.Clamp(Mathf.RoundToInt((k + 0.5f) * bulbs.Count / nl - 0.5f), 0, bulbs.Count - 1);
                    var lg = new GameObject("BulbLight"); lg.transform.SetParent(bulbs[bi].transform, false); lg.transform.localPosition = Vector3.zero;
                    lg.transform.SetParent(root.transform, true); lg.transform.localPosition += new Vector3(0, 0, -0.08f);
                    var bl = lg.AddComponent<Light>(); bl.type = LightType.Point; bl.shadows = LightShadows.None;
                    var mc = on[bi].HasProperty("_MainTex") && on[bi].mainTexture is Texture2D ? ((Texture2D)on[bi].mainTexture).GetPixel(0, 0) : Color.white;
                    bl.color = mc; bl.range = Mathf.Clamp(span / nl * 1.4f, 0.5f, 1.6f); bl.intensity = 0.9f * pw;
                    lg.AddComponent<GlowCull>().l = bl; lights[bi] = bl;
                }
            }
            var tw = root.AddComponent<Twinkle>(); tw.lights = lights; tw.halos = halos.ToArray(); tw.bulbs = bulbs.ToArray(); tw.on = on.ToArray(); tw.off = off.ToArray(); tw.l = l; tw.seed = seed;
        }
        internal static void AddLight(Transform poster, string file, Texture2D img, float w, float h)
        {
            float pw = cPower != null ? cPower.Value : 1f; if (pw <= 0.01f) return;
            var g = new GameObject("Backlight"); g.transform.SetParent(poster, false);
            g.transform.localPosition = new Vector3(0f, 0f, -Mathf.Clamp(Mathf.Max(w, h) * 0.35f, 0.2f, 0.8f));
            var l = g.AddComponent<Light>(); l.type = LightType.Point; l.shadows = LightShadows.None;
            l.color = HasSingle ? SingleCol : Average(file, img); l.range = Mathf.Clamp(Mathf.Max(w, h) * 2.2f, 1f, 5f); l.intensity = 0.9f * pw;
            l.renderMode = LightRenderMode.Auto;
            g.AddComponent<GlowCull>().l = l;
        }
    }
    // neon casse : il gresille et s'eteint par moments
    public class NeonFlicker : MonoBehaviour
    {
        public Light l; public int seed; Renderer[] rs; Renderer poster; float next, until; bool dark; System.Random r; float baseI;
        void Start() { rs = GetComponentsInChildren<Renderer>().Where(x => x.gameObject.name == "Neon" || x.gameObject.name == "Halo").ToArray(); poster = transform.parent != null ? transform.parent.GetComponent<Renderer>() : null; r = new System.Random(seed); if (l != null) baseI = l.intensity; next = Time.time + 1f; }
        void Update()
        {
            // on regarde si l'AFFICHE est a l'ecran (un tube eteint est toujours « invisible »)
            if (rs == null || rs.Length == 0 || Time.time < next || (poster != null && !poster.isVisible && !dark)) return;
            if (!dark && Time.time >= until) { dark = r.NextDouble() < 0.5; next = Time.time + (dark ? 0.04f + (float)r.NextDouble() * 0.12f : 0.05f + (float)r.NextDouble() * (r.NextDouble() < 0.2 ? 4f : 0.25f)); }
            else { dark = !dark; next = Time.time + (dark ? 0.05f + (float)r.NextDouble() * 0.6f : 0.05f + (float)r.NextDouble() * 3f); if (!dark) until = Time.time + (float)r.NextDouble() * 6f; }
            foreach (var x in rs) if (x != null) x.enabled = !dark;
            if (l != null) l.intensity = dark ? baseI * 0.08f : baseI;
        }
    }
    // enseigne qui clignote regulierement (allume / eteint)
    public class NeonBlink : MonoBehaviour
    {
        Renderer[] rs; Light l; float baseI, next; bool off;
        void Start() { rs = GetComponentsInChildren<Renderer>().Where(x => x.gameObject.name == "Neon" || x.gameObject.name == "Halo").ToArray(); l = GetComponentInChildren<Light>(); if (l != null) baseI = l.intensity; next = Time.time + 0.4f; }
        void Update()
        {
            if (Time.time < next) return;
            off = !off; next = Time.time + (off ? 0.3f : 0.55f) * Glow.Slow;
            foreach (var x in rs) if (x != null) x.enabled = !off;
            if (l != null) l.intensity = off ? baseI * 0.05f : baseI;
        }
    }
    // gyrophare de police : moitie rouge, moitie bleue, qui alternent
    public class NeonPolice : MonoBehaviour
    {
        Renderer[] tubes, halos; Light l; float next; int phase; Renderer poster;
        static readonly Color Red = new Color(1f, 0.08f, 0.05f), Blue = new Color(0.1f, 0.25f, 1f);
        void Start()
        {
            tubes = GetComponentsInChildren<Renderer>().Where(x => x.gameObject.name == "Neon").ToArray();
            halos = GetComponentsInChildren<Renderer>().Where(x => x.gameObject.name == "Halo").ToArray();
            l = GetComponentInChildren<Light>(); poster = transform.parent != null ? transform.parent.GetComponent<Renderer>() : null;
        }
        void Update()
        {
            if ((poster != null && !poster.isVisible) || Time.time < next) return;
            phase = (phase + 1) % 8; next = Time.time + 0.14f * Glow.Slow;
            bool redOn = phase < 4 ? phase % 2 == 0 : false, blueOn = phase >= 4 ? phase % 2 == 0 : false;   // 2 eclairs rouges, puis 2 bleus
            // haut + gauche = rouge, bas + droite = bleu (ordre de creation : haut, bas, gauche, droite)
            for (int i = 0; i < tubes.Length; i++) { bool red = i == 0 || i == 2; var on = red ? redOn : blueOn; if (tubes[i] != null) { tubes[i].enabled = on; tubes[i].sharedMaterial = Glow.TubeMat(red ? Red : Blue); } }
            for (int i = 0; i < halos.Length; i++) { bool red = i == 0 || i == 2; var on = red ? redOn : blueOn; var hm = Glow.HaloMat(red ? Red : Blue); if (halos[i] != null) { halos[i].enabled = on; if (hm != null) halos[i].sharedMaterial = hm; } }
            if (l != null) { l.color = redOn ? Red : blueOn ? Blue : l.color; l.intensity = redOn || blueOn ? 2f : 0.2f; }
        }
    }
    public class FireFlicker : MonoBehaviour
    {
        public Light l; public float baseI; public int seed; float t;
        void Update()
        {
            if (l == null || !l.enabled) return; t += Time.deltaTime;
            float n = Mathf.PerlinNoise(t * 3.1f + seed % 100, 0.37f) * 0.6f + Mathf.PerlinNoise(t * 9.3f, seed % 50 + 0.5f) * 0.4f;
            l.intensity = baseI * (0.55f + 0.9f * n);
            l.color = Color.Lerp(new Color(1f, 0.38f, 0.08f), new Color(1f, 0.62f, 0.25f), n);
        }
    }
    // neon arc-en-ciel (la couleur tourne) ou qui « respire » (monte et descend doucement)
    public class NeonFx : MonoBehaviour
    {
        public bool rainbow; public Color baseCol; Renderer[] tubes, halos; Light l; float baseI, t, next; int lastKey = -1; Renderer poster;
        void Start()
        {
            tubes = GetComponentsInChildren<Renderer>().Where(x => x.gameObject.name == "Neon").ToArray();
            halos = GetComponentsInChildren<Renderer>().Where(x => x.gameObject.name == "Halo").ToArray();
            l = GetComponentInChildren<Light>(); if (l != null) baseI = l.intensity;
            poster = transform.parent != null ? transform.parent.GetComponent<Renderer>() : null;
        }
        void Update()
        {
            if (poster != null && !poster.isVisible) return;
            t += Time.deltaTime;
            Color c; int key;
            if (rainbow) { float hue = Mathf.Repeat(t * 0.08f, 1f); key = Mathf.RoundToInt(hue * 36f) % 36; c = Color.HSVToRGB(key / 36f, 0.9f, 1f); if (l != null) l.color = Color.HSVToRGB(hue, 0.9f, 1f); }
            else { float k = 0.5f + 0.5f * Mathf.Sin(t * 2.2f); key = Mathf.RoundToInt(k * 8f); c = baseCol * (0.3f + 0.7f * key / 8f); c.a = 1f; if (l != null) l.intensity = baseI * (0.15f + 0.85f * k); }
            if (key == lastKey) return; lastKey = key;   // on ne change le materiau que par paliers (pas de cout a chaque image)
            var tm = Glow.TubeMat(c); var hm = Glow.HaloMat(c);
            foreach (var x in tubes) if (x != null) x.sharedMaterial = tm;
            if (hm != null) foreach (var x in halos) if (x != null) x.sharedMaterial = hm;
        }
    }
    // neon fatigue : 3 cotes clignotent ensemble, le 4e est decale, le rattrape, puis se decale encore
    public class NeonFaulty : MonoBehaviour
    {
        public int seed; Renderer[] a, b; Light l; float baseI, phA, phB, pb = 0.62f, glitch, synced; System.Random r; Renderer poster;
        void Start()
        {
            var neon = GetComponentsInChildren<Renderer>().Where(x => x.gameObject.name == "Neon").ToArray();
            var halo = GetComponentsInChildren<Renderer>().Where(x => x.gameObject.name == "Halo").ToArray();
            r = new System.Random(seed); int side = r.Next(4);   // le cote fatigue
            a = neon.Where((x, i) => i != side).Concat(halo.Where((x, i) => i != side)).ToArray();
            b = neon.Where((x, i) => i == side).Concat(halo.Where((x, i) => i == side)).ToArray();
            l = GetComponentInChildren<Light>(); if (l != null) baseI = l.intensity;
            poster = transform.parent != null ? transform.parent.GetComponent<Renderer>() : null;
            phB = (float)r.NextDouble();
        }
        void Update()
        {
            if (poster != null && !poster.isVisible) return;
            float PA = 0.85f * Glow.Slow;                // rythme des 3 cotes
            float dt = Time.deltaTime;
            phA = Mathf.Repeat(phA + dt / PA, 1f);
            phB = Mathf.Repeat(phB + dt / pb, 1f);
            float diff = Mathf.Repeat(phB - phA, 1f);
            if (synced > 0f) { synced -= dt; pb = PA; if (synced <= 0f) pb = PA * (0.72f + (float)r.NextDouble() * 0.1f); }   // recale un moment, puis il repart en avance
            else if (diff < 0.04f || diff > 0.96f) { synced = 1.5f + (float)r.NextDouble() * 3f; phB = phA; }        // il a rattrape les autres
            bool onA = phA < 0.6f, onB = phB < 0.6f;
            if (glitch > 0f) { glitch -= dt; onB = r.NextDouble() < 0.5; }
            else if (r.NextDouble() < dt * 0.25) glitch = 0.15f + (float)r.NextDouble() * 0.25f;   // petit rate de temps en temps
            foreach (var x in a) if (x != null && x.enabled != onA) x.enabled = onA;
            foreach (var x in b) if (x != null && x.enabled != onB) x.enabled = onB;
            if (l != null) l.intensity = baseI * ((onA ? 0.75f : 0.08f) + (onB ? 0.25f : 0f));
        }
    }
    // guirlande : les ampoules s'allument et s'eteignent doucement, une a une
    public class Twinkle : MonoBehaviour
    {
        public Renderer[] bulbs; public GameObject[] halos; public Light[] lights; public Material[] on, off; public Light l; float[] baseI; public bool chase; public int seed; float next; int step; System.Random r;
        void Start() { r = new System.Random(seed); }
        void Update()
        {
            if (bulbs == null || bulbs.Length == 0 || bulbs[0] == null || !bulbs[0].isVisible || Time.time < next) return;
            next = Time.time + (chase ? 0.13f : 0.4f) * Glow.Slow; step++;
            int mode = chase ? 3 : (step / 30) % 3;   // 3 animations qui tournent (ou le chenillard)
            for (int i = 0; i < bulbs.Length; i++)
            {
                bool lit = mode == 3 ? ((i - step) % 5 + 5) % 5 == 0 || ((i - step) % 5 + 5) % 5 == 1 : mode == 0 ? (i + step) % 3 != 0 : mode == 1 ? r.NextDouble() < 0.75 : (step % 2 == 0) == (i % 2 == 0);
                if (bulbs[i] != null) bulbs[i].sharedMaterial = lit ? on[i] : off[i];
                if (halos != null && i < halos.Length && halos[i] != null && halos[i].activeSelf != lit) halos[i].SetActive(lit);
                if (lights != null && i < lights.Length && lights[i] != null)
                {
                    if (baseI == null) { baseI = new float[lights.Length]; for (int q = 0; q < lights.Length; q++) baseI[q] = lights[q] != null ? lights[q].intensity : 0f; }
                    lights[i].intensity = lit ? baseI[i] : baseI[i] * 0.15f;
                }
            }
            if (l != null) l.intensity = Mathf.Lerp(l.intensity, 0.55f + (float)r.NextDouble() * 0.3f, 0.5f);
        }
    }
    // la lueur s'eteint quand tu es loin (aucun cout)
    // impacts de balles : on ecoute les tirs du joueur et on troue les affiches touchees
    public class BulletHoles : MonoBehaviour
    {
        // chaque impact reel calcule par le jeu (dispersion, recul, chute de la balle) : on remonte la trajectoire jusqu'a l'affiche
        internal static void Patch()
        {
            try
            {
                var h = new HarmonyLib.Harmony("praedlab.hideoutposters.holes");
                var post = new HarmonyLib.HarmonyMethod(typeof(BulletHoles).GetMethod("ShotPostfix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public));
                System.Type[] types; try { types = typeof(EFT.GameWorld).Assembly.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                int n = 0;
                foreach (var t in types.Where(t => typeof(EFT.GameWorld).IsAssignableFrom(t)))
                {
                    var m = t.GetMethod("ShotDelegate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
                    if (m != null && !m.IsAbstract) { h.Patch(m, postfix: post); n++; }
                }
                PostersPlugin.Log.LogInfo("[impacts] branche sur les tirs du jeu (" + n + ")");
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[impacts] " + e.Message); }
        }
        static int lastShotFrame = -1; static Vector3 lastHit;
        static void ShotPostfix(EFT.Ballistics.Shot shotResult)
        {
            try
            {
                if (shotResult == null || !shotResult.IsFirstHit) return;
                // meme tir vu deux fois (le jeu appelle deux fois) : meme image ET meme point d'impact. Les objets de tir sont recycles : jamais de test par reference
                if (Time.frameCount == lastShotFrame && (shotResult.HitPoint - lastHit).sqrMagnitude < 1e-6f) return;
                lastShotFrame = Time.frameCount; lastHit = shotResult.HitPoint;
                var start = shotResult.StartPosition; var hit = shotResult.HitPoint; var d = hit - start; float L = d.magnitude; if (L < 0.2f) return; d /= L;
                AmmoCard.LastDist = L;
                var w = shotResult.Weapon as EFT.InventoryLogic.Weapon; ReadCaliber(w != null ? w.AmmoCaliber : "");
                // taille reelle de la munition : un slug de 12 (18,5 mm) fait un trou 2 fois plus gros qu'un 9 mm
                AmmoSize(shotResult.Ammo, w != null ? w.AmmoCaliber : "");
                // la balle arrive en courbe : on remonte depuis l'impact avec sa direction AU MOMENT de l'impact
                // (l'affiche est juste devant le mur ou la cible : c'est la portion de trajectoire qui compte)
                var dirHit = shotResult.CurrentDirection; if (dirHit.sqrMagnitude < 0.01f) dirHit = d; dirHit.Normalize();
                SpotterScope.ShotDir = dirHit;
                var o2 = hit - dirHit * L;
                RangeGame.BeginShot(w, L, shotResult.HittedBallisticCollider != null ? shotResult.HittedBallisticCollider.transform : null, hit);
                FreePlace.HitPosters(o2, dirHit, L + 0.02f); RaidPosters.HitPosters(o2, dirHit, L + 0.02f);
                RangeGame.EndShot();
            }
            catch { }
        }
        // un mur ou un objet entre le canon et l'affiche ?
        internal static bool Blocked(Vector3 o, Vector3 d, float dist)
        {
            RaycastHit h; var start = o + d * 0.25f;
            return dist > 0.3f && Physics.Raycast(start, d, out h, dist - 0.3f, ~0, QueryTriggerInteraction.Ignore) && h.distance < dist - 0.3f - 0.04f;
        }
        internal static System.Collections.Generic.List<Vector3> Parse(string s)
        {
            var l = new System.Collections.Generic.List<Vector3>(); if (string.IsNullOrEmpty(s)) return l; var ic = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var part in s.Split(';'))
            {
                var xy = part.Split(':'); float x, y, z = 1f;
                if (xy.Length >= 2 && float.TryParse(xy[0], System.Globalization.NumberStyles.Float, ic, out x) && float.TryParse(xy[1], System.Globalization.NumberStyles.Float, ic, out y))
                { if (xy.Length >= 3) float.TryParse(xy[2], System.Globalization.NumberStyles.Float, ic, out z); l.Add(new Vector3(x, y, z)); }
            }
            return l;
        }
        // calibre de l'arme : taille du trou, ou gerbe de chevrotine
        static float caliberK = 1f; static bool buckshot;
        internal static void ReadCaliber(string cal)
        {
            caliberK = 1f; buckshot = false;
            try
            {
                var c = (cal ?? "").ToLowerInvariant();
                if (c.Contains("12g") || c.Contains("20g") || c.Contains("23x75")) caliberK = 0.5f;   // chaque plomb arrive tout seul : petits trous
                else if (c.Contains("127") || c.Contains("86x70") || c.Contains("338") || c.Contains("40x46") || c.Contains("26x75")) caliberK = 1.9f;
                else if (c.Contains("762x54") || c.Contains("762x51") || c.Contains("68x51") || c.Contains("366") || c.Contains("9x39")) caliberK = 1.35f;
                else if (c.Contains("46x30") || c.Contains("57x28") || c.Contains("9x18") || c.Contains("762x25")) caliberK = 0.75f;
            }
            catch { }
        }
        static readonly System.Collections.Generic.HashSet<string> logged = new System.Collections.Generic.HashSet<string>();
        static object Member(object o, string name)
        {
            if (o == null) return null; var t = o.GetType(); var fl = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.FlattenHierarchy;
            var f = t.GetField(name, fl); if (f != null) return f.GetValue(o);
            var p = t.GetProperty(name, fl); return p != null ? p.GetValue(o, null) : null;
        }
        // taille du trou d'apres la vraie munition : diametre et nombre de projectiles
        static void AmmoSize(EFT.InventoryLogic.Item ammo, string cal)
        {
            try
            {
                var tpl = Member(ammo, "Template"); if (tpl == null) return;
                float mm = 0f; int proj = 1;
                var dm = Member(tpl, "BulletDiameterMilimeters"); if (dm != null) mm = Convert.ToSingle(dm);
                var pc = Member(tpl, "ProjectileCount"); if (pc != null) proj = Convert.ToInt32(pc);
                var bb = Member(tpl, "buckshotBullets"); if (bb != null && Convert.ToInt32(bb) > proj) proj = Convert.ToInt32(bb);
                var c = (cal ?? "").ToLowerInvariant(); bool shotgun = c.Contains("12g") || c.Contains("20g") || c.Contains("23x75");
                if (mm < 0.5f) mm = shotgun ? (c.Contains("23x75") ? 23f : c.Contains("20g") ? 15.6f : 18.5f) : 9f * caliberK;   // diametre inconnu : on le devine
                if (proj > 1) mm = Mathf.Min(mm, 8.5f);                                                                        // chevrotine : chaque plomb est petit
                caliberK = Mathf.Clamp(mm / 9f, 0.4f, 2.8f);
                try
                {
                    var pen = Member(tpl, "PenetrationPower"); var dmg = Member(tpl, "Damage");
                    AmmoCard.LastPen = pen != null ? Convert.ToInt32(pen) : -1; AmmoCard.LastDmg = dmg != null ? Convert.ToInt32(dmg) : -1; AmmoCard.LastProj = proj;
                    AmmoCard.LastName = ammo != null ? AmmoCard.Loc(ammo.Name) : "?";
                }
                catch { }
                var key = (ammo != null ? ammo.TemplateId.ToString() : "?");
                if (logged.Add(key)) PostersPlugin.Log.LogInfo("[impacts] munition " + (ammo != null ? ammo.ShortName : "?") + " (" + cal + ") : " + mm.ToString("0.#") + " mm, " + proj + " projectile(s) -> trou x" + caliberK.ToString("0.00"));
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[impacts] munition : " + e.Message); }
        }
        internal static void Impact(System.Collections.Generic.List<Vector3> holes, Vector2 uv, Vector3 size)
        {
            if (!buckshot) { holes.Add(new Vector3(uv.x, uv.y, caliberK * (0.85f + UnityEngine.Random.value * 0.3f))); return; }
            const float spread = 0.09f;   // ~9 cm de gerbe
            for (int k = 0; k < 8; k++)
            {
                var off = UnityEngine.Random.insideUnitCircle * spread;
                holes.Add(new Vector3(uv.x + off.x / Mathf.Max(0.05f, size.x), uv.y + off.y / Mathf.Max(0.05f, size.y), 0.45f + UnityEngine.Random.value * 0.15f));
            }
        }
        static Material mat; static Texture2D mat0Tex;
        // 4 formes de trous dans une seule texture (2 x 2)
        static Material Mat()
        {
            if (mat != null) return mat;
            const int C = 128, S = C * 2; var t = new Texture2D(S, S, TextureFormat.RGBA32, false); var px = new Color32[S * S];   // sans mipmaps : le papier arrache reste visible de loin
            for (int cell = 0; cell < 4; cell++)
            {
                var r = new System.Random(11 + cell * 97); int ox = (cell % 2) * C, oy = (cell / 2) * C;
                int nSp = 9 + r.Next(10); var spikes = new float[nSp]; for (int i = 0; i < nSp; i++) spikes[i] = 0.5f + (float)r.NextDouble() * (0.28f + cell * 0.04f);
                var tears = new float[3]; for (int i = 0; i < 3; i++) tears[i] = (float)r.NextDouble();
                float holeR = 0.085f + cell * 0.01f;
                for (int y = 0; y < C; y++) for (int x = 0; x < C; x++)
                {
                    float dx = (x + 0.5f) / C * 2 - 1, dy = (y + 0.5f) / C * 2 - 1, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a01 = Mathf.Atan2(dy, dx) / (Mathf.PI * 2) + 0.5f, f = a01 * nSp; int i0 = (int)f % nSp, i1 = (i0 + 1) % nSp;
                    float rim = Mathf.Lerp(spikes[i0], spikes[i1], f - Mathf.Floor(f));
                    float tear = 0f; foreach (var ta in tears) { float da = Mathf.Abs(Mathf.DeltaAngle(a01 * 360f, ta * 360f)); if (da < 9f) tear = Mathf.Max(tear, (1f - da / 9f) * 0.42f); }
                    float hr = holeR + tear * 0.35f, n = (float)r.NextDouble();
                    Color32 c;
                    if (d < hr) c = new Color32((byte)(6 + n * 8), (byte)(5 + n * 6), 4, 255);
                    else if (d < hr + 0.03f) c = new Color32((byte)(70 + n * 30), (byte)(60 + n * 24), (byte)(48 + n * 16), 255);   // bord brule, fin
                    else if (d < rim + tear * 0.5f) { byte vv = (byte)(238 + n * 17); c = new Color32(vv, vv, (byte)(vv - 6), 255); }   // papier blanc arrache
                    else if (d < rim + 0.1f && n < 0.12f) { byte vv = (byte)(230 + n * 200); c = new Color32(vv, vv, (byte)(vv - 8), 255); }   // fibres qui depassent
                    else if (d < rim + 0.16f && n < 0.025f) c = new Color32(70, 62, 52, 255);
                    else c = new Color32(0, 0, 0, 0);
                    px[(oy + y) * S + ox + x] = c;
                }
            }
            t.SetPixels32(px); t.Apply(false); t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear; t.anisoLevel = 8; t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            mat0Tex = t;
            mat = new Material(Look.StickerMat(t)); if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.3f); return mat;
        }
        internal static Texture2D holeTex;
        internal static void EnsureHoleTex() { if (holeTex == null) { Mat(); holeTex = mat0Tex; } }
        // les trous font partie du papier : ils sont peints dans une copie de l'image de l'affiche
        static bool Bake(GameObject poster, PosterTag tag, System.Collections.Generic.List<Vector3> holes)
        {
            try
            {
                var mr = poster.GetComponent<MeshRenderer>(); var mf = poster.GetComponent<MeshFilter>(); if (mr == null || mf == null || mf.sharedMesh == null) return false;
                var baseTex = PostersPlugin.LoadImage(tag.File); if (baseTex == null) return false;
                var mat = mr.sharedMaterials.Length > 0 ? mr.sharedMaterials[0] : null; if (mat == null) return false;
                var old = poster.transform.Find("BulletHoles"); if (old != null) { old.name = "old"; Destroy(old.gameObject); }
                if (holes == null || holes.Count == 0)
                {
                    if (tag.Baked != null) { if (mat.mainTexture == tag.Baked) mat.mainTexture = baseTex; Destroy(tag.Baked); tag.Baked = null; }
                    tag.Stamped = 0; return true;
                }
                if (holeTex == null) { Mat(); holeTex = mat0Tex; }
                if (holeTex == null) return false;
                int W = baseTex.width, H = baseTex.height;
                if (tag.Baked == null || holes.Count < tag.Stamped || tag.Baked.width != W)
                {
                    if (tag.Baked != null) Destroy(tag.Baked);
                    tag.Baked = new Texture2D(W, H, TextureFormat.RGBA32, true); tag.Baked.SetPixels32(baseTex.GetPixels32()); tag.Baked.wrapMode = TextureWrapMode.Clamp; tag.Baked.anisoLevel = 16; tag.Baked.mipMapBias = -0.35f;
                    tag.Stamped = 0;
                }
                var size = mf.sharedMesh.bounds.size;
                bool sticker = PostersPlugin.IsSticker(tag.File);
                var px = tag.Baked.GetPixels32();   // une seule lecture de l'image, quel que soit le nombre de trous
                for (int h = tag.Stamped; h < holes.Count; h++) Stamp(px, tag.Baked.width, tag.Baked.height, holes[h], h, size, sticker);
                tag.Baked.SetPixels32(px);
                tag.Stamped = holes.Count; tag.Dirty = true;
                if (mat.mainTexture != tag.Baked) mat.mainTexture = tag.Baked;
                return true;
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[impacts] peinture : " + e.Message); return false; }
        }
        internal static BepInEx.Configuration.ConfigEntry<float> cHoleSize;
        static float HoleK { get { return cHoleSize != null ? cHoleSize.Value : 1.5f; } }
        internal static void Stamp(Color32[] px, int W, int H, Vector3 hd, int h, Vector3 size, bool sticker, bool overlay = false)
        {
            var r = new System.Random(h * 7919 + (int)(hd.x * 1000) * 31 + (int)(hd.y * 1000));
            float sz = (0.028f + (float)r.NextDouble() * 0.01f) * Mathf.Max(0.3f, hd.z) * HoleK, ang = (float)r.NextDouble() * Mathf.PI * 2f;
            int cell = r.Next(4); float u0 = (cell % 2) * 0.5f, v0 = (cell / 2) * 0.5f;
            float cx = (hd.x + 0.5f) * W, cy = (hd.y + 0.5f) * H, rx = sz / size.x * W * 0.5f, ry = sz / size.y * H * 0.5f;
            int x0 = Mathf.Max(0, (int)(cx - rx * 1.5f)), x1 = Mathf.Min(W - 1, (int)(cx + rx * 1.5f)), y0 = Mathf.Max(0, (int)(cy - ry * 1.5f)), y1 = Mathf.Min(H - 1, (int)(cy + ry * 1.5f));
            if (x1 <= x0 || y1 <= y0) return;

            float ca = Mathf.Cos(-ang), sa = Mathf.Sin(-ang);
            // anneau de frottement (plomb + graisse de la balle) : le petit cercle gris fonce qui rend les impacts visibles de loin
            float ph1 = (float)r.NextDouble() * 6.28f, ph2 = (float)r.NextDouble() * 6.28f;
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                float dx = (x - cx) / rx, dy = (y - cy) / ry, d = Mathf.Sqrt(dx * dx + dy * dy);
                float an = Mathf.Atan2(dy, dx), rr = 0.36f + 0.035f * Mathf.Sin(an * 3f + ph1) + 0.02f * Mathf.Sin(an * 7f + ph2);
                float k = d < rr ? Mathf.Clamp01(1f - (rr - d) / 0.1f) : Mathf.Clamp01(1f - (d - rr) / 0.3f);
                if (k <= 0f) continue;
                k = Mathf.Min(0.92f, k * (0.6f + 0.4f * k) * 0.95f);
                int i = y * W + x; var o = px[i];
                if (overlay) { if (o.a == 255) continue; px[i] = new Color32(22, 21, 20, (byte)Mathf.Max(o.a, (int)(k * 255))); continue; }   // calque GIF : gris plus ou moins opaque
                px[i] = new Color32((byte)(o.r * (1f - k) + 22 * k), (byte)(o.g * (1f - k) + 21 * k), (byte)(o.b * (1f - k) + 20 * k), o.a);
            }
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                float dx = (x - cx) / rx, dy = (y - cy) / ry;            // -1..1 dans le carre du trou
                float lx = dx * ca - dy * sa, ly = dx * sa + dy * ca;     // rotation
                if (lx < -1 || lx > 1 || ly < -1 || ly > 1) continue;
                var c = holeTex.GetPixelBilinear(u0 + (lx * 0.5f + 0.5f) * 0.5f, v0 + (ly * 0.5f + 0.5f) * 0.5f);
                if (c.a < 0.35f) continue;
                int i = y * W + x; var o = px[i];
                byte a = sticker || overlay ? (byte)255 : o.a;                     // papier mat : on garde la transparence-reflet de l'affiche
                px[i] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), a);
            }
        }
        // tous les trous d'une affiche = un seul objet (un seul dessin, quel que soit le nombre)
        internal static void Rebuild(GameObject poster, System.Collections.Generic.List<Vector3> holes)
        {
            if (poster == null) return;
            var tag = poster.GetComponent<PosterTag>();
            if (tag != null && PostersPlugin.AnimOf(tag.File) == null && Bake(poster, tag, holes)) return;
            if (tag != null && PostersPlugin.AnimOf(tag.File) != null && GifPlayer.SetHoles(poster, holes))
            {
                var od = poster.transform.Find("BulletHoles"); if (od != null) { var om = od.GetComponent<MeshFilter>(); if (om != null && om.sharedMesh != null) Destroy(om.sharedMesh); od.name = "old"; Destroy(od.gameObject); }
                return;
            }
            var old = poster.transform.Find("BulletHoles"); if (old != null) { var om = old.GetComponent<MeshFilter>(); if (om != null && om.sharedMesh != null) Destroy(om.sharedMesh); old.name = "old"; Destroy(old.gameObject); }
            if (holes == null || holes.Count == 0) return;
            var mf = poster.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return;
            var mesh = mf.sharedMesh; var size = mesh.bounds.size; var vs = mesh.vertices; int half = mesh.subMeshCount > 1 ? vs.Length / 2 : vs.Length;
            var ls = poster.transform.localScale;
            var v = new System.Collections.Generic.List<Vector3>(); var uv = new System.Collections.Generic.List<Vector2>(); var tri = new System.Collections.Generic.List<int>(); var nrm = new System.Collections.Generic.List<Vector3>();
            for (int h = 0; h < holes.Count; h++)
            {
                var hd = holes[h]; var lp = new Vector3(hd.x * size.x, hd.y * size.y, 0f);
                float z = -0.006f, best = float.MaxValue, lift = 0f;
                for (int i = 0; i < half; i++) { float dd = (vs[i].x - lp.x) * (vs[i].x - lp.x) + (vs[i].y - lp.y) * (vs[i].y - lp.y); if (dd < best) { best = dd; z = vs[i].z; } }
                for (int i = 0; i < half; i++) { float dd = (vs[i].x - lp.x) * (vs[i].x - lp.x) + (vs[i].y - lp.y) * (vs[i].y - lp.y); if (dd < 0.0025f) lift = Mathf.Min(lift, vs[i].z - z); }
                lp.z = z + lift - 0.0035f;
                var r = new System.Random(h * 7919 + (int)(hd.x * 1000) * 31 + (int)(hd.y * 1000));
                float sz = (0.028f + (float)r.NextDouble() * 0.01f) * Mathf.Max(0.3f, hd.z) * HoleK, ang = (float)r.NextDouble() * Mathf.PI * 2f;
                int cell = r.Next(4); float u0 = (cell % 2) * 0.5f, v0 = (cell / 2) * 0.5f;
                var ax = new Vector3(Mathf.Cos(ang) * sz * 0.5f / ls.x, Mathf.Sin(ang) * sz * 0.5f / ls.y, 0); var ay = new Vector3(-Mathf.Sin(ang) * sz * 0.5f / ls.x, Mathf.Cos(ang) * sz * 0.5f / ls.y, 0);
                int b = v.Count;
                v.Add(lp - ax - ay); v.Add(lp + ax - ay); v.Add(lp + ax + ay); v.Add(lp - ax + ay);
                uv.Add(new Vector2(u0, v0)); uv.Add(new Vector2(u0 + 0.5f, v0)); uv.Add(new Vector2(u0 + 0.5f, v0 + 0.5f)); uv.Add(new Vector2(u0, v0 + 0.5f));
                for (int k = 0; k < 4; k++) nrm.Add(Vector3.back);
                tri.Add(b); tri.Add(b + 2); tri.Add(b + 1); tri.Add(b); tri.Add(b + 3); tri.Add(b + 2);
            }
            var m = new Mesh { name = "BulletHoles" }; m.SetVertices(v); m.SetUVs(0, uv); m.SetNormals(nrm); m.SetTriangles(tri, 0);
            m.RecalculateTangents(); m.RecalculateBounds();   // le shader du jeu en a besoin : sinon l'eclairage change selon l'angle de vue
            var g = new GameObject("BulletHoles"); g.transform.SetParent(poster.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = m; var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = Mat(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
    // l'image d'une affiche posee (pour y peindre les trous de balles)
    public class PosterTag : MonoBehaviour
    {
        public string File; public Texture2D Baked; public int Stamped; public bool Dirty; float nextApply;
        void Update()
        {
            if (!Dirty || Baked == null || Time.unscaledTime < nextApply) return;
            Baked.Apply(true); Dirty = false; nextApply = Time.unscaledTime + 0.2f;   // au plus 5 fois par seconde, meme en rafale
        }
        void OnDestroy() { if (Baked != null) Destroy(Baked); }
    }
    public class LightsOff : MonoBehaviour
    {
        readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Renderer, Material>> mats = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Renderer, Material>>();
        readonly System.Collections.Generic.List<Behaviour> stopped = new System.Collections.Generic.List<Behaviour>();
        readonly System.Collections.Generic.List<Renderer> hidden = new System.Collections.Generic.List<Renderer>();
        public void Apply()
        {
            var dark = Glow.DarkGlass();
            foreach (var b in GetComponentsInChildren<Behaviour>(true))
                if (b is NeonFlicker || b is NeonBlink || b is NeonFaulty || b is NeonFx || b is NeonPolice || b is Twinkle || b is FireFlicker) { if (b.enabled) { b.enabled = false; stopped.Add(b); } }
            foreach (var l in GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject.name == "Halo") { if (r.enabled) { r.enabled = false; hidden.Add(r); } continue; }
                if (Glow.IsBright(r.sharedMaterial)) { mats.Add(new System.Collections.Generic.KeyValuePair<Renderer, Material>(r, r.sharedMaterial)); if (dark != null) r.sharedMaterial = dark; }
                if (!r.enabled && (r.gameObject.name == "Neon")) { r.enabled = true; hidden.Add(null); }   // un neon coupe en plein clignotement : on le montre eteint
            }
        }
        public void Restore()
        {
            foreach (var kv in mats) if (kv.Key != null) kv.Key.sharedMaterial = kv.Value;
            foreach (var r in hidden) if (r != null) r.enabled = true;
            foreach (var b in stopped) if (b != null) b.enabled = true;
            foreach (var l in GetComponentsInChildren<Light>(true)) l.enabled = true;
            Destroy(this);
        }
    }
    // tele du stand de tir : la camera qui filme les cibles rend dans une petite image -> on augmente sa resolution
    public class RangeTV : MonoBehaviour
    {
        internal static BepInEx.Configuration.ConfigEntry<float> cSharp;
        internal static BepInEx.Configuration.ConfigEntry<bool> cClean;
        static readonly System.Collections.Generic.List<Behaviour> tvFx = new System.Collections.Generic.List<Behaviour>();
        // couleur / noir et blanc des teles, applique tout de suite (pas besoin de recharger la planque)
        internal static void ApplyClean()
        {
            bool clean = cClean == null || cClean.Value; tvFx.RemoveAll(x => x == null);
            foreach (var b in tvFx) b.enabled = !clean;
        }
        internal static void Bind(BepInEx.Configuration.ConfigFile c)
        {
            cSharp = c.Bind("Poster", "Shooting range TV sharpness", 2f, new BepInEx.Configuration.ConfigDescription("Resolution of the shooting range TVs when you are at the range, so you can see your bullet holes on them (1 = game default, 2 = twice as sharp). Away from the range they go back to the game default, so the rest of the hideout stays smooth", new BepInEx.Configuration.AcceptableValueRange<float>(1f, 3f)));
            cClean = c.Bind("Poster", "Shooting range TV in color", true, "Shooting range TVs show a clean color picture instead of the black and white striped one");
            cClean.SettingChanged += (a, b) => { try { ApplyClean(); PostersPlugin.Log.LogInfo("[tv] teles " + (cClean.Value ? "en couleur" : "en noir et blanc d'origine") + " (" + tvFx.Count + " effet(s))"); } catch { } };
            SpotterScope.cOn = c.Bind("Poster", "Spotter scope on range TVs", true, "When you hit a poster, a shooting range TV turns and zooms on it so you can see your holes, like a spotting scope. It goes back to its target after a while");
            SpotterScope.cZoomCm = c.Bind("Poster", "Spotter scope close-up (cm)", 25f, new BepInEx.Configuration.ConfigDescription("How much of the poster the TV shows around your last hole, in cm (smaller = stronger zoom)", new BepInEx.Configuration.AcceptableValueRange<float>(8f, 100f)));
            BulletHoles.cHoleSize = c.Bind("Poster", "Bullet hole size", 1.5f, new BepInEx.Configuration.ConfigDescription("Size of bullet holes on posters (1 = real size, bigger = easier to see). Calibers keep their difference. New holes use it right away, old ones after reloading the hideout", new BepInEx.Configuration.AcceptableValueRange<float>(0.5f, 3f)));
            cTestCards = null;
            cLang = c.Bind("Range Day", "Language", "Auto", new BepInEx.Configuration.ConfigDescription("Range Day language. Auto follows your system language (French if French, otherwise English)", new BepInEx.Configuration.AcceptableValueList<string>(RDTranslate.LangNames)));
            cRewards = c.Bind("Range Day", "Rewards", true, "Range Day sends real game items as rewards. Turn off to play Range Day without touching your game economy");
            cRewardQty = c.Bind("Range Day", "Reward multiplier", 1, new BepInEx.Configuration.ConfigDescription("Number of items sent per reward", new BepInEx.Configuration.AcceptableValueRange<int>(1, 5)));
            cNight = c.Bind("Poster", "Shooting range night mode", false, "Turns off the shooting range lamps: practice with your flashlight, laser or night vision. Your poster lights and the TVs stay on");
            AmmoCard.cOn = c.Bind("Poster", "Ammo card on range TVs", true, "While a range TV is zoomed on your hole, a banner at the bottom shows your ammo, its penetration, its damage and the shot distance");
            SpotterScope.cHold = c.Bind("Poster", "Spotter scope zoom time (s)", 8f, new BepInEx.Configuration.ConfigDescription("Seconds the TV stays zoomed on your hole after each shot, then it goes back", new BepInEx.Configuration.AcceptableValueRange<float>(1f, 120f)));
        }
        readonly System.Collections.Generic.HashSet<int> seen = new System.Collections.Generic.HashSet<int>();
        float next; static Shader unlit;
        EFT.Hideout.ShootingRange.PaperTargetControl range;
        static string PathOf(Transform t) { var s = t.name; for (t = t.parent; t != null; t = t.parent) s = t.name + "/" + s; return s; }
        // mode nuit au stand : on eteint les lampes du stand (les neons des affiches et les teles restent allumes)
        internal static BepInEx.Configuration.ConfigEntry<bool> cNight, cRewards, cTestCards; internal static BepInEx.Configuration.ConfigEntry<int> cRewardQty; internal static BepInEx.Configuration.ConfigEntry<string> cLang;
        readonly System.Collections.Generic.List<Light> nightOff = new System.Collections.Generic.List<Light>();
        float nightNext; bool nightOn;
        // tubes et ampoules qui brillent tout seuls (materiau emissif) : copie eteinte, l'original est remis au reveil
        readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Renderer, Material[]>> nightMats = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Renderer, Material[]>>();
        static Material Dark(Material m)
        {
            var d = new Material(m) { name = m.name + " (nuit)" };
            foreach (var pn in new[] { "_EmissionColor", "_EmissiveColor", "_EmissionTint" }) if (d.HasProperty(pn)) d.SetColor(pn, Color.black);
            foreach (var pn in new[] { "_EmissionPower", "_EmissionStrength", "_EmissionIntensity", "_Emission", "_EmissivePower" }) if (d.HasProperty(pn)) { try { d.SetFloat(pn, 0f); } catch { } }
            foreach (var pn in new[] { "_EmissionMap", "_EmissiveMap", "_EmissionTex", "_Illum" }) if (d.HasProperty(pn)) d.SetTexture(pn, Texture2D.blackTexture);
            d.DisableKeyword("_EMISSION"); d.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            return d;
        }
        static bool Glows(Material m)
        {
            if (m == null || m.shader == null) return false;
            var sn = m.shader.name.ToLowerInvariant();
            return sn.Contains("emissive") || sn.Contains("illum") || m.IsKeywordEnabled("_EMISSION");
        }
        void Night()
        {
            bool want = cNight != null && cNight.Value && range != null;
            if (want == nightOn && (!want || Time.unscaledTime < nightNext)) return;
            nightNext = Time.unscaledTime + 1f;
            try
            {
                if (want && !nightOn)
                {
                    nightOff.Clear(); var names = new System.Collections.Generic.List<string>();
                    foreach (var l in FindObjectsOfType<Light>())
                    {
                        if (l == null || !l.enabled) continue;
                        var path = PathOf(l.transform).ToLowerInvariant();
                        if (!path.Contains("shooting_range")) continue;
                        if (l.GetComponentInParent<PosterKeeper>() != null || path.Contains("hideoutposter")) continue;   // nos lumieres d'affiches restent
                        l.enabled = false; nightOff.Add(l); if (names.Count < 12) names.Add(l.name + " (" + l.type + ")");
                    }
                    nightMats.Clear(); var glow = new System.Collections.Generic.List<string>();
                    foreach (var r in FindObjectsOfType<Renderer>())
                    {
                        if (r == null || !r.enabled) continue;
                        var path = PathOf(r.transform).ToLowerInvariant();
                        if (!path.Contains("shooting_range") || path.Contains("hideoutposter") || path.Contains("monitor")) continue;   // pas les teles ni nos affiches
                        var ms = r.sharedMaterials; bool any = false;
                        for (int i = 0; i < ms.Length; i++) if (Glows(ms[i])) { any = true; break; }
                        if (!any) continue;
                        var dark = new Material[ms.Length];
                        for (int i = 0; i < ms.Length; i++) dark[i] = Glows(ms[i]) ? Dark(ms[i]) : ms[i];
                        nightMats.Add(new System.Collections.Generic.KeyValuePair<Renderer, Material[]>(r, ms)); r.sharedMaterials = dark;
                        if (glow.Count < 15) glow.Add(r.name + " [" + ms.First(Glows).shader.name + "]");
                    }
                    nightOn = true;
                    PostersPlugin.Log.LogInfo("[nuit] stand eteint : " + nightOff.Count + " lampe(s) : " + string.Join(", ", names.ToArray()));
                    PostersPlugin.Log.LogInfo("[nuit] lueurs eteintes : " + nightMats.Count + " objet(s) : " + string.Join(", ", glow.ToArray()));
                }
                else if (want)
                {
                    // le jeu peut rallumer (generateur) : on les garde eteintes
                    foreach (var l in nightOff) if (l != null && l.enabled) l.enabled = false;
                }
                else if (nightOn)
                {
                    foreach (var l in nightOff) if (l != null) l.enabled = true;
                    foreach (var kv in nightMats) if (kv.Key != null) { foreach (var dm in kv.Key.sharedMaterials) if (dm != null && dm.name.EndsWith(" (nuit)")) Destroy(dm); kv.Key.sharedMaterials = kv.Value; }
                    nightMats.Clear();
                    PostersPlugin.Log.LogInfo("[nuit] stand rallume : " + nightOff.Count + " lampe(s)");
                    nightOff.Clear(); nightOn = false;
                }
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[nuit] " + e.Message); }
        }
        class Tv { public RenderTexture rt; public int w0, h0; public bool hi; public System.Collections.Generic.List<Renderer> screens = new System.Collections.Generic.List<Renderer>(); }
        static readonly System.Collections.Generic.List<Tv> tvs = new System.Collections.Generic.List<Tv>();
        float sharpNext; bool distLogged;
        void Sharp()
        {
            if (Time.unscaledTime < sharpNext || tvs.Count == 0) return; sharpNext = Time.unscaledTime + 0.5f;
            try
            {
                var me = Camera.main; float f = cSharp != null ? cSharp.Value : 2f;
                foreach (var tv in tvs)
                {
                    if (tv.rt == null) continue;
                    // distance au CENTRE visible de l'ecran (le point d'origine du modele peut etre ailleurs)
                    float best = 9999f;
                    if (me != null) foreach (var sc in tv.screens) if (sc != null) best = Mathf.Min(best, Vector3.Distance(sc.bounds.center, me.transform.position));
                    bool near = best < (tv.hi ? 20f : 15f);
                    if (!distLogged && me != null) { distLogged = true; PostersPlugin.Log.LogInfo("[tv] distance joueur -> teles : " + best.ToString("0.0") + " m (haute resolution sous 15 m)"); }
                    bool want = near && f > 1.01f;
                    if (want == tv.hi) continue;
                    int w = want ? Mathf.Min(2048, Mathf.RoundToInt(tv.w0 * f)) : tv.w0, h = want ? Mathf.Min(2048, Mathf.RoundToInt(tv.h0 * f)) : tv.h0;
                    tv.rt.Release(); tv.rt.width = w; tv.rt.height = h; tv.rt.Create(); tv.hi = want;
                    PostersPlugin.Log.LogInfo("[tv] " + tv.rt.name + " -> " + w + "x" + h + (want ? " (tu es au stand)" : " (loin du stand)"));
                }
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[tv] nettete : " + e.Message); }
        }
        void Update()
        {
            if (range == null) nightOn = false; else { Night(); Sharp(); if (tvs.Count >= 3) RangeDay.Ensure(tvs.SelectMany(x => x.screens).Where(x => x != null).ToList()); }
            // les 3 teles sont branchees et le stand existe toujours : plus aucun scan (le scan toutes les 3 s faisait un a-coup)
            if (range != null && seen.Count >= 3) return;
            if (Time.unscaledTime < next) return; next = Time.unscaledTime + 3f;
            try
            {
                // seulement dans la planque, quand le stand de tir existe (jamais en raid)
                if (range == null) range = FindObjectOfType<EFT.Hideout.ShootingRange.PaperTargetControl>();
                if (range == null) return;
                var swT = System.Diagnostics.Stopwatch.StartNew(); int n0 = seen.Count;
                try {
                // cameras actives OU desactivees (une camera qui rend a la demande n'est pas dans Camera.allCameras)
                foreach (var cam in Resources.FindObjectsOfTypeAll<Camera>())
                {
                    if (cam == null || !cam.gameObject.scene.IsValid() || !cam.gameObject.activeInHierarchy) continue;
                    var rt = cam.targetTexture; if (rt == null) continue;
                    // jamais la lunette ni rien de ce que porte le joueur
                    if (cam.GetComponentInParent<EFT.Player>() != null) continue;
                    var lowp = PathOf(cam.transform).ToLowerInvariant();
                    if (lowp.Contains("weapon") || lowp.Contains("optic") || lowp.Contains("scope") || lowp.Contains("player")) continue;
                    // uniquement les cameras du stand de tir (12_shooting_range/.../target_monitors/cameras/0X_camera_target), pas la camera de securite
                    if (!lowp.Contains("shooting_range") && !lowp.Contains("target_monitors")) continue;
                    if (!seen.Add(rt.GetInstanceID())) continue;
                    var comps = string.Join(",", cam.GetComponents<Component>().Select(x => x == null ? "?" : x.GetType().Name).ToArray());
                    PostersPlugin.Log.LogInfo("[tv] camera " + PathOf(cam.transform) + " enabled=" + cam.enabled + " -> " + rt.name + " " + rt.width + "x" + rt.height + " [" + comps + "]");
                    // les ecrans qui affichent cette image
                    var screens = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Renderer, int>>();
                    foreach (var r in FindObjectsOfType<Renderer>())
                    {
                        var ms = r.sharedMaterials;
                        for (int k = 0; k < ms.Length; k++)
                        {
                            var m = ms[k]; if (m == null) continue;
                            foreach (var pn in m.GetTexturePropertyNames()) if (m.GetTexture(pn) == rt) { screens.Add(new System.Collections.Generic.KeyValuePair<Renderer, int>(r, k)); PostersPlugin.Log.LogInfo("[tv]   ecran " + PathOf(r.transform) + " mat=" + m.name + " shader=" + m.shader.name + " prop=" + pn); break; }
                        }
                    }
                    if (screens.Count == 0) continue;   // pas une tele : on ne touche a rien
                    SpotterScope.Register(cam);
                    // haute resolution seulement quand le joueur est au stand (sinon 3 cameras en 2048 tournent pour rien et ca rame en entrant dans la planque)
                    tvs.RemoveAll(x => x.rt == null);
                    var tv = new Tv { rt = rt, w0 = rt.width, h0 = rt.height };
                    foreach (var kv in screens) if (kv.Key != null) tv.screens.Add(kv.Key);
                    tvs.Add(tv);
                    PostersPlugin.Log.LogInfo("[tv]   resolution d'origine " + rt.width + "x" + rt.height + " (haute resolution seulement pres du stand)");
                    {
                        // le noir et blanc + les lignes viennent d'un effet de la camera (CC_AnalogTV), pas de l'ecran : on le garde sous la main pour le couper / remettre en direct
                        int n = 0;
                        foreach (var b in cam.GetComponents<Behaviour>())
                        {
                            if (b == null || b is Camera) continue;
                            var tn = b.GetType().Name;
                            if (tn.IndexOf("AnalogTV", StringComparison.OrdinalIgnoreCase) >= 0 || tn.IndexOf("Grayscale", StringComparison.OrdinalIgnoreCase) >= 0 || tn.IndexOf("Noise", StringComparison.OrdinalIgnoreCase) >= 0)
                            { tvFx.RemoveAll(x => x == null); if (!tvFx.Contains(b)) tvFx.Add(b); n++; }
                        }
                        ApplyClean();
                        PostersPlugin.Log.LogInfo("[tv]   effet tele (" + n + ") : " + (cClean == null || cClean.Value ? "coupe, image en couleur sans lignes" : "garde, noir et blanc d'origine"));
                    }
                }
                } finally { PostersPlugin.Log.LogInfo("[perf] recherche des teles : " + swT.ElapsedMilliseconds + " ms (" + (seen.Count - n0) + " camera(s) branchee(s))"); }
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[tv] " + e.Message); }
        }
    }

    // longue-vue : une camera des teles du stand pivote et zoome sur l'affiche touchee (on voit les vrais trous)
    public class SpotterScope : MonoBehaviour
    {
        internal static BepInEx.Configuration.ConfigEntry<bool> cOn;
        internal static BepInEx.Configuration.ConfigEntry<float> cHold, cZoomCm;
        static readonly System.Collections.Generic.List<SpotterScope> cams = new System.Collections.Generic.List<SpotterScope>();
        static int next; internal static Vector3 ShotDir;
        Camera cam; Quaternion homeRot; float homeFov; Transform target; float last; float blend; Quaternion fromRot; float fromFov;
        internal static void Register(Camera c)
        {
            if (c == null || c.GetComponent<SpotterScope>() != null) return;
            var s = c.gameObject.AddComponent<SpotterScope>(); s.cam = c; s.homeRot = c.transform.rotation; s.homeFov = c.fieldOfView;
            cams.Add(s); AmmoCard.Warm();
            PostersPlugin.Log.LogInfo("[longue-vue] camera prete : " + c.name + " (fov " + c.fieldOfView.ToString("0.#") + ")");
        }
        static Vector3 Center(Transform t, out float size)
        {
            var mr = t.GetComponent<Renderer>(); size = 0.6f;
            if (mr == null) return t.position;
            var b = mr.bounds; size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)); return b.center;
        }
        static bool Sees(Camera c, Transform t)
        {
            float sz; var p = Center(t, out sz); var o = c.transform.position; var d = p - o; float dist = d.magnitude;
            if (dist < 0.3f || dist > 80f) return false;
            RaycastHit h;
            // rien entre la camera et l'affiche (l'affiche elle-meme n'a pas de collider : on tolere le support juste derriere)
            if (Physics.Raycast(o, d / dist, out h, dist - 0.15f, ~0, QueryTriggerInteraction.Ignore)) return false;
            return true;
        }
        Vector3 hitLocal; bool hasHit;
        internal static void Focus(GameObject poster) { Focus(poster, Vector3.zero, false); }
        internal static void Focus(GameObject poster, Vector3 hitWorld) { Focus(poster, hitWorld, true); }
        static void Focus(GameObject poster, Vector3 hitWorld, bool withHit)
        {
            if (cOn != null && !cOn.Value) return;
            cams.RemoveAll(x => x == null || x.cam == null);
            if (poster == null || cams.Count == 0) return;
            var t = poster.transform;
            // une tele suit deja cette affiche : on prolonge
            foreach (var s in cams) if (s.target == t) { s.last = Time.time; if (withHit) { s.hitLocal = t.InverseTransformPoint(hitWorld); s.hasHit = true; s.banner = AmmoCard.Render(s.banner); } return; }
            // sinon la tele qui voit l'affiche le plus DE FACE (jamais de profil), de preference une tele libre
            float dummy; var center = Center(t, out dummy);
            var front = -t.forward;   // l'affiche regarde vers -forward (cote image)
            SpotterScope best = null; float bestScore = -9f;
            foreach (var s in cams)
            {
                var toCam = (s.cam.transform.position - center).normalized;
                float face = Mathf.Abs(Vector3.Dot(front, toCam));   // 1 = pile en face, 0 = de profil
                if (face < 0.6f) continue;                           // plus de ~53 degres : vue trop rasante
                // la tele doit etre du cote du tireur (cote image) : jamais de dos, sinon elle filme l'envers de la cible
                // test geometrique : la tele et le joueur du meme cote du plan de l'affiche
                var me = Camera.main;
                if (me != null)
                {
                    float sc = Vector3.Dot(t.forward, s.cam.transform.position - center), sp = Vector3.Dot(t.forward, me.transform.position - center);
                    if (sc * sp < 0f) { PostersPlugin.Log.LogInfo("[longue-vue] " + s.cam.name + " ignoree : elle voit l'affiche de dos (tele " + sc.ToString("0.00") + " / joueur " + sp.ToString("0.00") + ")"); continue; }
                }
                if (!Sees(s.cam, t)) continue;
                float score = face - (s.target != null ? 0.25f : 0f) - (Time.time - s.last < 3f && s.target != null ? 0.5f : 0f);
                if (score > bestScore) { bestScore = score; best = s; }
            }
            if (best != null)
            {
                best.fromRot = best.cam.transform.rotation; best.fromFov = best.cam.fieldOfView; best.blend = 0f; best.target = t; best.last = Time.time; best.logged = false; best.hasHit = withHit; if (withHit) { best.hitLocal = t.InverseTransformPoint(hitWorld); best.banner = AmmoCard.Render(best.banner); }
                PostersPlugin.Log.LogInfo("[longue-vue] " + best.cam.name + " -> " + poster.name + " (face " + (bestScore).ToString("0.00") + ")");
                return;
            }
            PostersPlugin.Log.LogInfo("[longue-vue] aucune tele ne voit cette affiche");
        }
        float weight; Quaternion gameRot; float gameFov; bool applied; static bool hooked; internal bool logged;
        void LateUpdate()
        {
            if (cam == null) return;
            float hold = cHold != null ? cHold.Value : 30f;
            if (target != null && (Time.time - last > hold || (cOn != null && !cOn.Value))) target = null;
            weight = Mathf.MoveTowards(weight, target != null ? 1f : 0f, Time.deltaTime * 5f);   // 0,2 s pour zoomer / dezoomer
            if (!hooked)
            {
                hooked = true;
                // juste avant le rendu de la tele : on vise l'affiche ; juste apres : on rend la camera au jeu
                Camera.onPreCull += c => { if (c == null) return; var s2 = c.GetComponent<SpotterScope>(); if (s2 != null) s2.Apply(); };
                Camera.onPostRender += c => { if (c == null) return; var s2 = c.GetComponent<SpotterScope>(); if (s2 != null) s2.Restore(); };
            }
        }
        void Apply()
        {
            applied = false;
            if (weight <= 0.001f || cam == null) return;
            gameRot = cam.transform.rotation; gameFov = cam.fieldOfView; applied = true;
            var t = target != null ? target : lastTarget; if (t == null) { applied = false; return; }
            float sz; var p = Center(t, out sz);
            if (hasHit && t == target) p = t.TransformPoint(hitLocal);   // gros plan sur le dernier impact
            var d = p - cam.transform.position; float dist = Mathf.Max(0.3f, d.magnitude);
            var wantRot = Quaternion.LookRotation(d, Vector3.up);
            // vraie taille de l'affiche (largeur x hauteur), pas sa boite englobante : elle remplit la hauteur de l'ecran
            float pw = sz, ph = sz; var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) { var bs = mf.sharedMesh.bounds.size; var ls = t.lossyScale; pw = Mathf.Abs(bs.x * ls.x); ph = Mathf.Abs(bs.y * ls.y); }
            float aspect = Mathf.Max(0.5f, cam.aspect);
            float half = hasHit ? (cZoomCm != null ? cZoomCm.Value : 25f) * 0.005f : Mathf.Max(ph * 0.5f, pw * 0.5f / aspect) * 1.04f;   // hauteur visible : 25 cm autour du trou
            float wantFov = Mathf.Clamp(2f * Mathf.Atan2(half, dist) * Mathf.Rad2Deg, 1.5f, gameFov);
            float k = weight * weight * (3f - 2f * weight);
            cam.transform.rotation = Quaternion.Slerp(gameRot, wantRot, k);
            cam.fieldOfView = Mathf.Lerp(gameFov, wantFov, k);
            if (!logged && k > 0.99f) { logged = true; PostersPlugin.Log.LogInfo("[longue-vue] " + cam.name + " zoome sur " + t.name + " (fov " + gameFov.ToString("0") + " -> " + wantFov.ToString("0.0") + ")"); }
        }
        internal RenderTexture banner;
        void Restore()
        {
            if (!applied || cam == null) return;
            cam.transform.rotation = gameRot; cam.fieldOfView = gameFov; applied = false;
            // fiche munition en bas de l'image, seulement une fois le zoom fini
            if (banner != null && hasHit && target != null && weight > 0.95f && (AmmoCard.cOn == null || AmmoCard.cOn.Value)) AmmoCard.Draw(banner, cam);
        }
        Transform lastTarget;
        void Update() { if (target != null) lastTarget = target; }
    }

    // fiche munition : un bandeau (nom de la balle, penetration, degats, distance) dessine en bas de l'image de la tele
    internal static class AmmoCard
    {
        internal static BepInEx.Configuration.ConfigEntry<bool> cOn;
        internal static string LastName = "?"; internal static int LastPen = -1, LastDmg = -1, LastProj = 1; internal static float LastDist;
        static System.Reflection.MethodInfo loc; static bool locTried;
        // traduction du jeu (nom de la balle dans la langue du joueur), trouvee une fois par reflexion
        internal static void Warm()
        {
            if (locTried) return; locTried = true;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                System.Type[] ts; try { ts = typeof(EFT.Player).Assembly.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { ts = e.Types.Where(x => x != null).ToArray(); }
                foreach (var t in ts)
                {
                    if (!(t.IsAbstract && t.IsSealed)) continue;
                    foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                    {
                        if (m.Name != "Localized" || m.ReturnType != typeof(string)) continue;
                        var ps = m.GetParameters(); if (ps.Length == 0 || ps[0].ParameterType != typeof(string)) continue;
                        bool ok = true; for (int i = 1; i < ps.Length; i++) if (!ps[i].IsOptional) ok = false;
                        if (ok && (loc == null || ps.Length < loc.GetParameters().Length)) loc = m;
                    }
                }
                PostersPlugin.Log.LogInfo("[fiche] traduction du jeu : " + (loc != null ? loc.DeclaringType.Name + "." + loc.Name : "introuvable") + " (" + sw.ElapsedMilliseconds + " ms)");
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[fiche] traduction : " + e.Message); }
        }
        internal static string Loc(string key)
        {
            if (string.IsNullOrEmpty(key)) return "?";
            try
            {
                Warm(); if (loc == null) return key;
                var ps = loc.GetParameters(); var args = new object[ps.Length]; args[0] = key;
                for (int i = 1; i < ps.Length; i++) args[i] = ps[i].DefaultValue == DBNull.Value ? null : ps[i].DefaultValue;
                var r = loc.Invoke(null, args) as string; return string.IsNullOrEmpty(r) ? key : r;
            }
            catch { return key; }
        }
        static GameObject root; static Camera bcam; static TextMeshPro tx;
        const int W = 1024, H = 128;
        static bool Ensure()
        {
            if (bcam != null && tx != null) return true;
            try
            {
                if (root != null) UnityEngine.Object.Destroy(root);
                root = new GameObject("HideoutPosters_AmmoCard"); UnityEngine.Object.DontDestroyOnLoad(root);
                root.transform.position = new Vector3(0f, -5000f, 0f);
                bcam = root.AddComponent<Camera>(); bcam.enabled = false; bcam.orthographic = true; bcam.orthographicSize = 0.5f;
                bcam.nearClipPlane = 0.01f; bcam.farClipPlane = 3f; bcam.clearFlags = CameraClearFlags.SolidColor; bcam.backgroundColor = new Color(0.02f, 0.02f, 0.025f, 0.78f);
                bcam.allowHDR = false; bcam.allowMSAA = false; bcam.useOcclusionCulling = false;
                var tgo = new GameObject("Text"); tgo.transform.SetParent(root.transform, false); tgo.transform.localPosition = new Vector3(0f, 0f, 1f);
                tx = tgo.AddComponent<TextMeshPro>();
                var f = FreePlace.Bender(); if (f != null) tx.font = f;
                tx.rectTransform.sizeDelta = new Vector2(7.6f, 0.9f);
                tx.alignment = TextAlignmentOptions.Center; tx.enableWordWrapping = false; tx.richText = true;
                tx.enableAutoSizing = true; tx.fontSizeMin = 0.5f; tx.fontSizeMax = 6f; tx.color = new Color(0.92f, 0.91f, 0.86f);
                root.SetActive(false);
                return true;
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[fiche] " + e.Message); return false; }
        }
        // dessine la fiche de la derniere balle dans une petite image, une fois par tir
        internal static RenderTexture Render(RenderTexture rt)
        {
            if (cOn != null && !cOn.Value) return rt;
            try
            {
                if (!Ensure()) return rt;
                if (rt == null) { rt = new RenderTexture(W, H, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "HideoutPosters_AmmoCard" }; rt.Create(); }
                string pc = LastPen >= 40 ? "#7BD35A" : LastPen >= 25 ? "#F0A830" : "#E04A3A";
                string dmg = LastDmg < 0 ? "?" : LastProj > 1 ? LastProj + " x " + LastDmg : LastDmg.ToString();
                tx.text = "<b>" + LastName + "</b>   <color=#8A8A85>|</color>   " + LastDist.ToString("0") + " m\n"
                        + "<color=" + pc + ">PEN " + (LastPen < 0 ? "?" : LastPen.ToString()) + "</color>   <color=#8A8A85>|</color>   DMG " + dmg;
                root.SetActive(true); bcam.targetTexture = rt; bcam.aspect = (float)W / H; bcam.Render(); bcam.targetTexture = null; root.SetActive(false);
                PostersPlugin.Log.LogInfo("[fiche] " + LastName + " | " + LastDist.ToString("0") + " m | PEN " + LastPen + " | DMG " + dmg);
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[fiche] " + e.Message); if (root != null) root.SetActive(false); }
            return rt;
        }
        // pose la fiche en bas de l'image de la tele (appele juste apres son rendu)
        internal static void Draw(RenderTexture banner, Camera cam)
        {
            try
            {
                var target = RenderTexture.active; if (target == null) target = cam.targetTexture; if (target == null) return;
                float w = target.width, h = target.height, bh = Mathf.Round(h * 0.17f);
                GL.PushMatrix(); GL.LoadPixelMatrix(0, w, h, 0);
                Graphics.DrawTexture(new Rect(0, h - bh, w, bh), banner);
                GL.PopMatrix();
            }
            catch { }
        }
    }

    // ===== RANGE DAY : l'interrupteur du stand (action du jeu « RANGE DAY » avec la touche d'interaction) =====
    internal static class RangeDay
    {
        static bool patched;
        static bool done;
        internal static void Ensure(System.Collections.Generic.List<Renderer> screens)
        {
            if (done) return; done = true;
            try
            {
                Patch();
                // la vraie boite de commande du stand (АРСЕНАЛ) : on note ses composants pour s'y brancher
                foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>())
                {
                    var n = t.name.ToLowerInvariant();
                    if (n != "shooting_range_control" && n != "target_control_area" && n != "paper_target_control" && n != "shared_target_control") continue;
                    var comps = string.Join(",", t.GetComponents<Component>().Select(c => c == null ? "?" : c.GetType().FullName).ToArray());
                    PostersPlugin.Log.LogInfo("[range day] " + t.name + " : " + comps);
                }
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[range day] " + e.Message); }
        }
        static void Patch()
        {
            if (patched) return; patched = true;
            try
            {
                // la boite АРСЕНАЛ = RailTargetControl (vitesse / arreter / inspecter / rabattre) : ses actions viennent de InteractionStates(...)
                var m = typeof(EFT.Hideout.ShootingRange.RailTargetControl).GetMethod("InteractionStates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
                if (m == null) { PostersPlugin.Log.LogWarning("[range day] actions de la boite introuvables"); return; }
                prevField = HarmonyLib.AccessTools.Field(typeof(EFT.Hideout.ShootingRange.RailTargetControl), "_prevAvailableInteractionState");
                new HarmonyLib.Harmony("praedlab.hideoutposters.rangeday").Patch(m,
                    prefix: new HarmonyLib.HarmonyMethod(typeof(RangeDay).GetMethod("BoxPrefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)),
                    postfix: new HarmonyLib.HarmonyMethod(typeof(RangeDay).GetMethod("BoxPostfix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
                PostersPlugin.Log.LogInfo("[range day] branche sur la boite de commande du stand");
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[range day] patch : " + e.Message); }
        }
        static bool loggedBox; static System.Reflection.FieldInfo prevField;
        // le jeu reprend la ligne choisie au tour d'avant par son NUMERO : si c'etait RANGE DAY (5e ligne, ajoutee par nous),
        // sa liste a lui n'en a que 4 -> plantage. On lui fait croire que c'etait la 1re ligne, puis on remet RANGE DAY selectionne.
        static void BoxPrefix(object __instance, out bool __state)
        {
            __state = false;
            try
            {
                var prev = prevField != null ? prevField.GetValue(__instance) as EFT.UI.AvailableInteractionState : null;
                if (prev != null && prev.SelectedAction != null && prev.SelectedAction.Name == "RANGE DAY" && prev.Actions != null && prev.Actions.Count > 0)
                { prev.SelectedAction = prev.Actions[0]; __state = true; }
            }
            catch { }
        }
        static void BoxPostfix(ref EFT.UI.AvailableInteractionState __result, bool __state)
        {
            try
            {
                if (__result == null) return;
                if (__result.Actions == null) __result.Actions = new System.Collections.Generic.List<EFT.UI.InteractionAction>();
                if (__result.Actions.Any(x => x.Name == "RANGE DAY")) return;
                var ours = new EFT.UI.InteractionAction { Name = "RANGE DAY", Action = Open };
                __result.Actions.Add(ours);
                if (__state) __result.SelectedAction = ours;
                if (!loggedBox) { loggedBox = true; PostersPlugin.Log.LogInfo("[range day] action ajoutee a la boite : " + string.Join(" / ", __result.Actions.Select(x => x.Name).ToArray())); }
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[range day] action : " + e.Message); }
        }
        static void Open()
        {
            PostersPlugin.Log.LogInfo("[range day] interrupteur actionne : ouverture de la fenetre");
            try { RangeDayUI.Show(); } catch (Exception e) { PostersPlugin.Log.LogWarning("[range day] fenetre : " + e); }
        }
    }

    // ===== RANGE DAY : le defi Groupement (decompte, 5 tirs, mesure, medaille, record) =====
    internal static class RangeGame
    {
        enum St { Idle, Count, Run }
        static St st = St.Idle; static float t0; static int shots; static bool hitNow, inShot; static GameObject target; static bool incomplete;
        static readonly System.Collections.Generic.List<Vector3> pts = new System.Collections.Generic.List<Vector3>();
        static float distSum; static EFT.InventoryLogic.Weapon weapon;
        static GameObject hud; static TextMeshProUGUI big, small;
        static string File { get { return Path.Combine(BepInEx.Paths.ConfigPath, "HideoutPosters_rangeday.txt"); } }
        // records : id d'arme -> (nom, MOA)
        static System.Collections.Generic.Dictionary<string, System.Collections.Generic.KeyValuePair<string, float>> recs;
        static void LoadRecs()
        {
            if (recs != null) return; recs = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.KeyValuePair<string, float>>();
            try
            {
                if (System.IO.File.Exists(File)) foreach (var l in System.IO.File.ReadAllLines(File))
                {
                    var a = l.Split('|'); float v; int iv;
                    if (a.Length == 4 && a[0] == "grp" && float.TryParse(a[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) recs["0:" + a[1]] = new System.Collections.Generic.KeyValuePair<string, float>(a[2], v);
                    else if (a.Length == 5 && a[0] == "rec" && float.TryParse(a[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) recs[a[1] + ":" + a[2]] = new System.Collections.Generic.KeyValuePair<string, float>(a[3], v);
                    else if (a.Length == 2 && a[0] == "xp" && int.TryParse(a[1], out iv)) xp = iv;
                    else if (a.Length == 3 && a[0] == "cap" && int.TryParse(a[2], out iv)) { capDay = a[1]; capXp = iv; }
                    else if (a.Length == 2 && a[0] == "ach" && int.TryParse(a[1], out iv)) ach.Add(iv);
                    else if (a.Length == 2 && a[0] == "night" && int.TryParse(a[1], out iv)) nightWins = iv;
                    else if (a.Length == 3 && a[0] == "streak" && int.TryParse(a[2], out iv)) { streakDay = a[1]; streak = iv; }
                    else if (a.Length == 3 && a[0] == "mst" && int.TryParse(a[2], out iv)) mastery[a[1]] = iv;
                    else if (a.Length >= 2 && a[0] == "gold") goldSet.Add(string.Join("|", a.Skip(1).ToArray()));
                    else if (a.Length == 4 && a[0] == "day" && int.TryParse(a[2], out iv)) { if (a[1] == DateTime.Now.ToString("yyyy-MM-dd")) { dayKeyDone = a[1]; doneToday[iv] = a[3]; } }
                }
            }
            catch { }
            if (xp < 0) xp = 0;
        }
        static void SaveRecs()
        {
            try { System.IO.File.WriteAllLines(File, new[] { "xp|" + Math.Max(0, xp), "cap|" + capDay + "|" + capXp }.Concat(goldSet.Select(g => "gold|" + g)).Concat(mastery.Select(kv => "mst|" + kv.Key + "|" + kv.Value)).Concat(new[] { "streak|" + streakDay + "|" + streak, "night|" + nightWins }).Concat(ach.Select(x => "ach|" + x)).Concat(doneToday.Select(kv => "day|" + dayKeyDone + "|" + kv.Key + "|" + kv.Value)).Concat(recs.Select(kv => { var i = kv.Key.IndexOf(':'); return "rec|" + kv.Key.Substring(0, i) + "|" + kv.Key.Substring(i + 1) + "|" + kv.Value.Key.Replace("|", "/") + "|" + kv.Value.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture); })).ToArray()); } catch (Exception e) { PostersPlugin.Log.LogWarning("[range day] records : " + e.Message); }
        }
        // ---------- progression : XP, rangs (100), grades (25 x 4 niveaux) ----------
        static int xp = -1; static string capDay = ""; static int capXp;
        internal static readonly string[] Common = { "Scav", "Scav tireur d'élite", "Cultiste", "Garde de boss", "Raider", "Rogue", "Prêtre cultiste" };
        internal static readonly string[] Bear = { "Soldat", "Efreïtor", "Sergent subalterne", "Sergent", "Sergent-chef", "Starchina", "Praporchtchik", "Praporchtchik-chef", "Sous-lieutenant", "Lieutenant", "Lieutenant-chef", "Capitaine", "Major", "Lieutenant-colonel", "Colonel", "Général-major", "Général-colonel", "Maréchal" };
        internal static readonly string[] Usec = { "Private", "Private First Class", "Specialist", "Corporal", "Sergeant", "Staff Sergeant", "Sergeant First Class", "Master Sergeant", "Sergeant Major", "Warrant Officer", "Second Lieutenant", "First Lieutenant", "Captain", "Major", "Lieutenant Colonel", "Colonel", "Brigadier General", "General" };
        internal static int Need(int rank) { return 500 + 50 * (rank - 1); }      // XP pour passer de rank a rank+1
        internal static int Xp() { LoadRecs(); return Math.Max(0, xp); }
        internal static int Rank() { int r = 1, x = Xp(); while (r < 100 && x >= Need(r)) { x -= Need(r); r++; } return r; }
        internal static int Into() { int r = 1, x = Xp(); while (r < 100 && x >= Need(r)) { x -= Need(r); r++; } return x; }
        internal static float Frac() { int r = Rank(); return r >= 100 ? 1f : Mathf.Clamp01(Into() / (float)Need(r)); }
        internal static bool IsUsec()
        {
            try { var gw = Comfort.Common.Singleton<EFT.GameWorld>.Instance; var pl = gw != null ? gw.MainPlayer : null; return pl != null && pl.Side == EFT.EPlayerSide.Usec; } catch { return false; }
        }
        internal static string GradeName()
        {
            int r = Rank(), g = (r - 1) / 4, lv = (r - 1) % 4; string[] roman = { "I", "II", "III", "IV" };
            string n = g < Common.Length ? Common[g] : (IsUsec() ? Usec : Bear)[Math.Min(17, g - Common.Length)];
            return n.ToUpperInvariant() + "  " + roman[lv];
        }
        internal static int RecCount() { LoadRecs(); return recs != null ? recs.Count : 0; }
        internal static void RankReward(int rk, out string txt, out Color c)
        {
            if (rk >= 100) { txt = "Légendaire"; c = new Color(0.94f, 0.80f, 0.53f); return; }
            if ((rk - 1) % 4 == 0) { int g = (rk - 1) / 4; var all = Common.Concat((IsUsec() ? Usec : Bear).Take(18)).ToArray(); txt = "Épique · " + (g < all.Length ? all[g] : ""); c = new Color(0.76f, 0.64f, 0.96f); return; }
            if (rk % 10 == 0) { txt = "?"; c = new Color(0.73f, 0.64f, 0.92f); return; }
            txt = "Peu commun"; c = new Color(0.62f, 0.77f, 0.48f);
        }
        internal static string DoneLine() { var l = Today(); int n = 0; for (int i = 0; i < l.Count; i++) if (DoneMedal(i) != null) n++; return n + " / " + l.Count; }
        internal static string ProgText()
        {
            int r = Rank(), g = (r - 1) / 4; var side = IsUsec() ? Usec : Bear;
            var all = Common.Concat(side.Take(18)).ToArray(); var sb = new System.Text.StringBuilder();
            sb.Append("<size=44><color=#8a9aa6>TA PROGRESSION</color></size>\n");
            sb.Append("<size=26><color=#c9c2ae>XP totale <color=#f0cd86>" + Xp() + "</color>   ·   rang <color=#f0cd86>" + r + " / 100</color>   ·   défis du jour <color=#f0cd86>" + DoneLine() + "</color>   ·   records <color=#f0cd86>" + recs.Count + "</color></color></size>\n\n");
            sb.Append("<line-height=135%>");
            int from = Math.Max(0, g - 1), to = Math.Min(all.Length - 1, g + 3);
            for (int i = from; i <= to; i++)
            {
                string rr = "<size=20><color=#5f6d76>   rangs " + (i * 4 + 1) + "–" + (i * 4 + 4) + "</color></size>";
                if (i < g) sb.Append("<size=24><color=#6f8f5a>✓ " + all[i].ToUpperInvariant() + "</color></size>" + rr + "\n");
                else if (i == g) sb.Append("<size=40><color=#f0cd86>" + all[i].ToUpperInvariant() + "</color></size>" + rr + "\n");
                else sb.Append("<size=26><color=#7d8a93>" + all[i].ToUpperInvariant() + "</color></size>" + rr + "\n");
            }
            if (to < all.Length - 1) sb.Append("<size=22><color=#4d5a63>… encore " + (all.Length - 1 - to) + " grades jusqu'à " + all[all.Length - 1].ToUpperInvariant() + "</color></size>\n");
            sb.Append("<size=24>");
            sb.Append("\n<color=#7fa9a3>Puis PRESTIGE : on repart de Scav avec un insigne en plus.</color></size>");
            return sb.ToString();
        }
        internal static string SuccText()
        {
            var sb = new System.Text.StringBuilder("<size=44><color=#8a9aa6>SUCCÈS</color></size>\n<size=26><color=#c9c2ae>Des succès secrets se débloquent en jouant. À toi de les trouver.</color></size>\n\n<line-height=140%><size=30>");
            for (int i = 0; i < 12; i++) sb.Append("<mark=#ffffff10><color=#4d5a63>  ? ? ?  </color></mark>" + (i % 4 == 3 ? "\n\n" : "   "));
            return sb.Append("</size>").ToString();
        }
        internal static string XpLine() { int r = Rank(); return r >= 100 ? "RANG MAXIMUM" : Into() + " / " + Need(r) + " XP vers le rang " + (r + 1); }
        static int AddXp(int amount, bool permanent)
        {
            LoadRecs(); var today = DateTime.Now.ToString("yyyy-MM-dd");
            if (capDay != today) { capDay = today; capXp = 0; }
            if (permanent) { amount = Math.Min(amount, Math.Max(0, 300 - capXp)); capXp += amount; }
            int before = Rank(); xp = Xp() + amount; SaveRecs(); int after = Rank();
            if (after > before) { Snd(EFT.UI.EUISoundType.QuestFinished); PostersPlugin.Log.LogInfo("[range day] rang " + after + " : " + GradeName()); for (int rk = before + 1; rk <= after; rk++) { if (rk >= 100) SendReward(5, "rang 100"); else if ((rk - 1) % 4 == 0) RangeDayUI.ShowCards(4, "nouveau grade " + GradeName()); else if (rk % 10 == 0) RangeDayUI.ShowCards(4, "rang " + rk + " · surprise"); else SendReward(2, "rang " + rk); } }
            return amount;
        }
        // ---------- les 4 defis permanents ----------
        internal static readonly string[] Names = { "GROUPEMENT", "MOUCHE", "CHRONO", "ÉCONOMIE", "UNE BALLE", "DÉSIGNATION" };
        internal static readonly string[] Quick = { "5 balles dans la même cible, le plus serré possible", "3 balles au plus près du centre de la cible", "Touche 3 cibles différentes le plus vite possible", "10 balles : touche, vise le centre, ne rate pas", "Une seule balle, au plus près du centre", "20 s : tire sur la cible suspendue annoncée (gauche, milieu, droite)" };
        internal static readonly string[] Kick = { "5 BALLES  ·  15 M MINIMUM", "3 BALLES  ·  15 M MINIMUM", "3 CIBLES  ·  AU PLUS VITE", "10 BALLES  ·  POINTS", "1 BALLE  ·  15 M MINIMUM", "20 SECONDES  ·  RÉFLEXES" };
        internal static readonly string[] Desc = {
            "Tire 5 balles sur une même cible : une de tes affiches ou une cible du stand. On mesure l'écart entre tes deux trous les plus éloignés, ramené à la distance (MOA). Une balle à côté rend le tir incomplet.",
            "Tire 3 balles au plus près du centre de la cible. On mesure la distance moyenne de tes trous au centre, ramenée à la distance (MOA). Une balle à côté rend le tir incomplet.",
            "Touche 3 cibles différentes du stand le plus vite possible. Le chrono part à ton premier tir et s'arrête quand la 3e cible est touchée. 15 balles maximum.",
            "10 balles. +10 points par cible touchée, +5 en bonus à moins de 5 cm du centre, −5 par balle à côté. Fais le meilleur score.",
            "Une seule balle, le plus près possible du centre de la cible. Pas le droit à l'erreur : toute la pression est sur un tir.",
            "20 secondes. Tire sur les 3 cibles suspendues au rail. L'écran annonce laquelle : GAUCHE, MILIEU ou DROITE, et elle change toutes les 1,5 s. +10 sur la bonne, −10 sur la mauvaise, −5 à côté." };
        internal static readonly string[][] Med = { new[] { "BRONZE  ≤ 16 MOA", "ARGENT  ≤ 9 MOA", "OR  ≤ 4 MOA" }, new[] { "BRONZE  ≤ 15 MOA", "ARGENT  ≤ 8 MOA", "OR  ≤ 3,5 MOA" }, new[] { "BRONZE  ≤ 6 s", "ARGENT  ≤ 3,5 s", "OR  ≤ 2 s" }, new[] { "BRONZE  ≥ 80 pts", "ARGENT  ≥ 110 pts", "OR  ≥ 135 pts" }, new[] { "BRONZE  ≤ 10 MOA", "ARGENT  ≤ 5 MOA", "OR  ≤ 2 MOA" }, new[] { "BRONZE  ≥ 60 pts", "ARGENT  ≥ 100 pts", "OR  ≥ 140 pts" } };
        static readonly float[][] Thr = { new[] { 16f, 9f, 4f }, new[] { 15f, 8f, 3.5f }, new[] { 6f, 3.5f, 2f }, new[] { 80f, 110f, 135f }, new[] { 10f, 5f, 2f }, new[] { 60f, 100f, 140f } };
        internal static readonly float[] Diff = { 1.3f, 1.3f, 1f, 0.6f, 1.6f, 1f };
        internal static readonly string[] DiffName = { "DIFFICILE", "DIFFICILE", "MOYEN", "FACILE", "TRÈS DIFFICILE", "MOYEN" };
        static readonly string[] Unit = { "MOA", "MOA", "s", "pts", "MOA", "pts" };
        static int mode;
        static bool Lower(int m) { return m != 3 && m != 5; }
        internal static string RecordsText(int m)
        {
            LoadRecs(); var mine = recs.Where(kv => kv.Key.StartsWith(m + ":") && kv.Value.Key != "?" && !kv.Key.EndsWith(":?")).ToList();
            if (mine.Count == 0) return "<color=#7fa9a3>TES RECORDS</color>\n\nAucun pour l'instant.\nChaque arme garde son meilleur résultat.";
            var ord = Lower(m) ? mine.OrderBy(kv => kv.Value.Value) : mine.OrderByDescending(kv => kv.Value.Value);
            return "<color=#7fa9a3>TES RECORDS</color>\n\n" + string.Join("\n", ord.Take(8).Select(kv => kv.Value.Key + "  <color=#d9b46a>" + kv.Value.Value.ToString(m == 3 || m == 5 ? "0" : "0.0") + " " + Unit[m] + "</color>").ToArray());
        }
        static float ClassK(EFT.InventoryLogic.Weapon w, out string fam)
        {
            fam = "fusil d'assaut"; if (w == null) return 1f;
            var n = w.GetType().Name.ToLowerInvariant(); var cal = (w.AmmoCaliber ?? "").ToLowerInvariant();
            if (n.Contains("pistol") && !n.Contains("machine")) { fam = "pistolet"; return 3f; }
            if (n.Contains("smg") || n.Contains("submachine")) { fam = "pistolet-mitrailleur"; return 2f; }
            if (n.Contains("shotgun") || cal.Contains("12g") || cal.Contains("20g") || cal.Contains("23x75")) { fam = "fusil à pompe"; return 2f; }
            if (n.Contains("sniper")) { fam = "fusil à verrou"; return 1f / 1.5f; }
            if (n.Contains("marksman")) { fam = "DMR"; return 1f; }
            return 1f;
        }
        static readonly System.Collections.Generic.HashSet<Transform> touched = new System.Collections.Generic.HashSet<Transform>();
        static string rewardTxt = ""; static int lastBeep = -1, lastDesig = -1;
        static string streakDay = ""; static int streak;
        internal static int Streak() { LoadRecs(); string y = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd"), t = DateTime.Now.ToString("yyyy-MM-dd"); return streakDay == t || streakDay == y ? streak : 0; }
        static void MarkStreak() { var t = DateTime.Now.ToString("yyyy-MM-dd"); if (streakDay == t) return; streak = streakDay == DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd") ? streak + 1 : 1; streakDay = t; SaveRecs(); }
        static readonly System.Collections.Generic.HashSet<int> junkGiven = new System.Collections.Generic.HashSet<int>();
        internal static readonly string[] AchName = { "PREMIER PAS", "CHIRURGIEN", "PLUS VITE QUE TON OMBRE", "PAS UNE BALLE PERDUE", "NOCTAMBULE", "SANS VISER", "FIDÈLE", "COLLECTIONNEUR", "TOUT L'OR DU STAND", "GROS CALIBRE", "MAÎTRE ARMURIER", "ŒIL DE LYNX" };
        internal static readonly string[] AchDesc = { "Une première médaille", "Groupement ≤ 0,5 MOA", "Chrono en 1,5 s ou moins", "Économie à 140 points ou plus", "10 défis réussis en mode nuit", "Un or en tir au jugé", "30 jours de série", "Des défis avec 20 armes", "L'or sur les 6 défis permanents", "Un or au 12,7 mm", "Une arme en maîtrise 10", "Désignation à 180 points ou plus" };
        internal static readonly System.Collections.Generic.HashSet<int> ach = new System.Collections.Generic.HashSet<int>(); static int nightWins, missRun;
        internal static int AchCount() { LoadRecs(); return ach.Count; }
        static string Unlock(int id) { if (!ach.Add(id)) return ""; SaveRecs(); RangeDayUI.ShowCards(3, "succès " + AchName[id]); PostersPlugin.Log.LogInfo("[range day] succes : " + AchName[id]); return "\n<color=#f0cd86>SUCCÈS DÉBLOQUÉ : " + AchName[id] + "</color>"; }
        internal static readonly System.Collections.Generic.Dictionary<string, int> mastery = new System.Collections.Generic.Dictionary<string, int>();
        internal static int MLevel(int n) { int l = 1, need = 3; while (l < 10 && n >= need) { n -= need; l++; need += 2; } return l; }
        internal static readonly string[] UnitA = { " MOA", " MOA", " s", " pts", " MOA", " pts" };
        internal static System.Collections.Generic.List<string[]> ArsenalRows()
        {
            LoadRecs(); var byW = new System.Collections.Generic.Dictionary<string, string[]>();
            foreach (var kv in recs)
            {
                int c = kv.Key.IndexOf(':'); if (c < 0) continue; int m; if (!int.TryParse(kv.Key.Substring(0, c), out m) || m < 0 || m > 5) continue;
                var w = kv.Key.Substring(c + 1); if (w == "?" || kv.Value.Key == "?") continue; string[] row; if (!byW.TryGetValue(w, out row)) { row = new string[8]; row[0] = kv.Value.Key; row[7] = w; byW[w] = row; }
                row[m + 1] = kv.Value.Value.ToString(m == 3 || m == 5 ? "0" : "0.0") + UnitA[m] + (goldSet.Contains(kv.Key) ? " ★" : "");
            }
            return byW.Values.OrderByDescending(r => { int n; mastery.TryGetValue(r[7], out n); return n; }).ToList();
        }
        static readonly System.Collections.Generic.HashSet<string> goldSet = new System.Collections.Generic.HashSet<string>();
        // la partie serveur du mod tire un objet du jeu dans le palier et l'envoie par message
        internal static void SendRewardPub(int tier, string label, string tpl = null) { SendReward(tier, label, tpl); }
        internal static string Post(string path, string json)
        {
            Type rh = null; foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { rh = asm.GetType("SPT.Common.Http.RequestHandler", false); if (rh != null) break; }
            var m = rh != null ? rh.GetMethod("PostJson", new[] { typeof(string), typeof(string) }) : null;
            return m != null ? m.Invoke(null, new object[] { path, json }) as string : null;
        }
        internal static string PreviewTpl(int tier)
        {
            try { var r = Post("/hideoutposters/reward", "{\"tier\":" + tier + ",\"preview\":true}"); if (r == null) return null; int i = r.IndexOf("\"tpl\":\""); return i < 0 ? null : r.Substring(i + 7, 24); }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[range day] apercu : " + e.Message); return null; }
        }
        internal static System.Collections.Generic.List<Transform> Stands() { return FindStands() ? stands : null; }
        static void SendReward(int tier, string label, string tpl = null)
        {
            if (RangeTV.cRewards != null && !RangeTV.cRewards.Value) { PostersPlugin.Log.LogInfo("[range day] recompenses coupees (F12)"); return; }
            if (tpl == null && tier < 5 && UnityEngine.Random.value < 0.05f) { tier++; label += " · SURPRISE"; }
            int qty = RangeTV.cRewardQty != null ? RangeTV.cRewardQty.Value : 1; label = RDTranslate.Tr(label);
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Type rh = null;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { rh = asm.GetType("SPT.Common.Http.RequestHandler", false); if (rh != null) break; }
                    var m = rh != null ? rh.GetMethod("PostJson", new[] { typeof(string), typeof(string) }) : null;
                    if (m == null) { PostersPlugin.Log.LogWarning("[range day] recompense : RequestHandler introuvable"); return; }
                    var res = m.Invoke(null, new object[] { "/hideoutposters/reward", "{\"tier\":" + tier + ",\"qty\":" + qty + ",\"tpl\":\"" + (tpl ?? "") + "\",\"lang\":\"" + RDTranslate.Code + "\",\"label\":\"" + label.Replace("\"", "") + "\"}" }) as string;
                    PostersPlugin.Log.LogInfo("[range day] recompense palier " + tier + " -> " + (res ?? "(vide)"));
                }
                catch (Exception e) { PostersPlugin.Log.LogWarning("[range day] recompense : " + (e.InnerException ?? e).Message + " (partie serveur installee ?)"); }
            });
        }
        static float firstShot, centerSum; static int score, hitsN; static Transform lastHitT;
        static int MaxShots() { return mode == 0 ? 5 : mode == 1 ? 3 : mode == 2 ? 15 : mode == 4 ? 1 : mode == 5 ? 9999 : 10; }
        static bool Precise() { return mode <= 1 || mode == 4; }
        static string L(string m) { return "[range day] " + Names[mode].ToLowerInvariant() + " : " + m; }
        internal static void Start(int m = 0)
        {
            if (st != St.Idle) return; mode = Mathf.Clamp(m, 0, 5);
            if (mode == 5 && !FindRail()) { Show("INDISPONIBLE", "Les 3 cibles suspendues du stand sont introuvables", 6f); return; }
            st = St.Count; t0 = Time.unscaledTime; shots = 0; pts.Clear(); target = null; incomplete = false; distSum = 0f; weapon = null;
            touched.Clear(); firstShot = 0f; centerSum = 0f; score = 0; hitsN = 0;
            Hud(); PostersPlugin.Log.LogInfo(L("decompte"));
        }
        // ================= DEFIS DU JOUR =================
        internal class Daily { public int Mode, Diff; public string[] Cons; public bool Mystery; }
        static readonly string[] ConsKeys = { "fam:pistolet", "fam:pistolet-mitrailleur", "fam:fusil d'assaut", "fam:DMR", "fam:fusil à verrou", "fam:fusil à pompe", "fire:single", "fire:auto", "pose:crouch", "pose:prone", "pose:stand", "hip", "night", "dist:25", "dist:35" };
        internal static string ConsLabel(string c)
        {
            if (c.StartsWith("fam:")) { var f = c.Substring(4); return f.Substring(0, 1).ToUpperInvariant() + f.Substring(1); }
            switch (c)
            {
                case "fire:single": return "Coup par coup"; case "fire:auto": return "Automatique";
                case "pose:crouch": return "Accroupi"; case "pose:prone": return "Couché"; case "pose:stand": return "Debout";
                case "hip": return "Tir au jugé"; case "night": return "Mode nuit"; case "dist:25": return "25 m minimum"; case "dist:35": return "35 m minimum";
            }
            return c;
        }
        static bool Compatible(int mode, string c, System.Collections.Generic.List<string> have)
        {
            if (have.Contains(c)) return false;
            string fam = have.FirstOrDefault(x => x.StartsWith("fam:"));
            if (c.StartsWith("fam:") && fam != null) return false;
            if (c.StartsWith("fire:") && have.Any(x => x.StartsWith("fire:"))) return false;
            if (c.StartsWith("pose:") && have.Any(x => x.StartsWith("pose:"))) return false;
            if (c.StartsWith("dist:") && have.Any(x => x.StartsWith("dist:"))) return false;
            if (c == "fam:fusil à pompe" && (mode == 0 || mode == 1 || mode == 4)) return false;
            if (c == "fire:auto" && (fam == "fam:pistolet" || fam == "fam:fusil à verrou" || fam == "fam:fusil à pompe")) return false;
            if (c.StartsWith("fam:") && have.Contains("fire:auto") && (c == "fam:pistolet" || c == "fam:fusil à verrou" || c == "fam:fusil à pompe")) return false;
            if (c.StartsWith("dist:") && (mode == 2 || mode == 5)) return false;
            if (c == "fire:auto" && mode == 4) return false;
            return true;
        }
        static string dayKey; static System.Collections.Generic.List<Daily> today; static Daily flash; static bool flashRolled; internal static float flashUntil;
        // defi eclair : peut surgir une fois par session, 60 s pour le reussir une fois lance
        internal static bool HasFlash() { return flash != null && DoneMedal(Today().IndexOf(flash)) == null; }
        static void TestMystery() { if (today != null && RangeTV.cTestCards != null && RangeTV.cTestCards.Value) foreach (var x in today) x.Mystery = true; }
        internal static void RollFlash()
        {
            if (flashRolled) return; flashRolled = true;
            if (UnityEngine.Random.value > 0.35f) return;
            var r = new System.Random(); var d = new Daily { Mode = r.Next(6), Diff = 4 }; var cons = new System.Collections.Generic.List<string>(); int guard = 0;
            while (cons.Count < 1 && guard++ < 40) { var c = ConsKeys[r.Next(ConsKeys.Length)]; if (Compatible(d.Mode, c, cons)) cons.Add(c); }
            d.Cons = cons.ToArray(); flash = d; flashUntil = Time.unscaledTime + 600f; if (today != null) today.Add(d);
            PostersPlugin.Log.LogInfo("[range day] defi eclair : " + Names[d.Mode] + " + " + string.Join(", ", d.Cons));
        }
        internal static System.Collections.Generic.List<Daily> Today()
        {
            var key = DateTime.Now.ToString("yyyy-MM-dd");
            if (today != null && dayKey == key) { TestMystery(); return today; }
            dayKey = key; today = new System.Collections.Generic.List<Daily>();
            int seed = 17; foreach (var ch in "RangeDay" + key) seed = seed * 31 + ch;
            var r = new System.Random(seed);
            int n = 2 + r.Next(4);
            for (int i = 0; i < n; i++)
            {
                var d = new Daily { Mode = r.Next(6), Diff = i == 0 ? 0 : i == n - 1 ? 2 : r.Next(3) };
                var cons = new System.Collections.Generic.List<string>(); int want = d.Diff + 1, guard = 0;
                while (cons.Count < want && guard++ < 60) { var c = ConsKeys[r.Next(ConsKeys.Length)]; if (Compatible(d.Mode, c, cons)) cons.Add(c); }
                d.Cons = cons.ToArray(); today.Add(d);
            }
            // defi de la semaine : un jour different chaque semaine
            var wk = new System.Random(seed ^ (DateTime.Now.DayOfYear / 7 + DateTime.Now.Year * 53) * 7919);
            int wday = new System.Random(DateTime.Now.Year * 100 + System.Globalization.CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(DateTime.Now, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday)).Next(7);
            if ((int)DateTime.Now.DayOfWeek == wday)
            {
                var d = new Daily { Mode = r.Next(6), Diff = 3 }; var cons = new System.Collections.Generic.List<string>(); int guard = 0;
                while (cons.Count < 3 && guard++ < 80) { var c = ConsKeys[r.Next(ConsKeys.Length)]; if (Compatible(d.Mode, c, cons)) cons.Add(c); }
                d.Cons = cons.ToArray(); today.Add(d);
            }
            var mr2 = new System.Random(seed + 77);
            if (today.Count >= 3 && mr2.NextDouble() < 0.6) today[mr2.Next(today.Count)].Mystery = true;
            TestMystery();
            if (flash != null) today.Add(flash);
            return today;
        }
        internal static readonly string[] DiffDay = { "FACILE", "MOYEN", "DIFFICILE", "DÉFI DE LA SEMAINE", "DÉFI ÉCLAIR · 60 S" };
        internal static readonly int[] DayXp = { 100, 200, 350, 1000, 150 };
        internal static readonly string[] TierBase = { "COMMUN", "PEU COMMUN", "RARE", "RARE", "PEU COMMUN" };
        static readonly int[] TierIdx = { 1, 2, 3, 3, 2 };
        static readonly System.Collections.Generic.Dictionary<int, string> doneToday = new System.Collections.Generic.Dictionary<int, string>();
        internal static string DoneMedal(int i) { LoadRecs(); string m; return doneToday.TryGetValue(i, out m) && dayKeyDone == DateTime.Now.ToString("yyyy-MM-dd") ? m : null; }
        static string dayKeyDone = ""; static int daily = -1; static bool nightBefore;
        internal static void StartDaily(int i)
        {
            var list = Today(); if (i < 0 || i >= list.Count || st != St.Idle) return;
            var d = list[i];
            if (d.Cons.Contains("night") && RangeTV.cNight != null) { nightBefore = RangeTV.cNight.Value; RangeTV.cNight.Value = true; }
            Start(d.Mode); if (st == St.Idle) return; daily = i;
            PostersPlugin.Log.LogInfo("[range day] defi du jour " + (i + 1) + " : " + Names[d.Mode] + " + " + string.Join(", ", d.Cons));
        }
        // contrainte non respectee au moment du tir ?
        static string Broken(EFT.InventoryLogic.Weapon w, float dist)
        {
            if (daily < 0) return null; var d = Today()[daily];
            EFT.Player pl = null; try { var gw = Comfort.Common.Singleton<EFT.GameWorld>.Instance; pl = gw != null ? gw.MainPlayer : null; } catch { }
            foreach (var c in d.Cons)
            {
                try
                {
                    if (c.StartsWith("fam:")) { string fam; ClassK(w, out fam); if (fam != c.Substring(4)) return ConsLabel(c); }
                    else if (c == "fire:single" && w != null && w.SelectedFireMode != EFT.InventoryLogic.Weapon.EFireMode.single && w.SelectedFireMode != EFT.InventoryLogic.Weapon.EFireMode.semiauto && w.SelectedFireMode != EFT.InventoryLogic.Weapon.EFireMode.doubleaction) return ConsLabel(c);
                    else if (c == "fire:auto" && w != null && w.SelectedFireMode != EFT.InventoryLogic.Weapon.EFireMode.fullauto) return ConsLabel(c);
                    else if (c == "pose:prone" && pl != null && !pl.IsInPronePose) return ConsLabel(c);
                    else if (c == "pose:crouch" && pl != null && (pl.IsInPronePose || pl.PoseLevel > 0.6f)) return ConsLabel(c);
                    else if (c == "pose:stand" && pl != null && (pl.IsInPronePose || pl.PoseLevel < 0.9f)) return ConsLabel(c);
                    else if (c == "hip" && pl != null) { var fc = pl.HandsController as EFT.Player.FirearmController; if (fc != null && fc.IsAiming) return ConsLabel(c); }
                    else if (c == "dist:25" && dist < 25f) return ConsLabel(c);
                    else if (c == "dist:35" && dist < 35f) return ConsLabel(c);
                }
                catch { }
            }
            return null;
        }
        static void EndDaily(int medalRank)
        {
            if (daily < 0) return; int i = daily; daily = -1;
            var d = Today()[i];
            if (d.Cons.Contains("night") && RangeTV.cNight != null) RangeTV.cNight.Value = nightBefore;
            if (medalRank < 0) return;
            var key = DateTime.Now.ToString("yyyy-MM-dd"); if (dayKeyDone != key) { dayKeyDone = key; doneToday.Clear(); }
            string[] names = { "BRONZE", "ARGENT", "OR" }; string prev; int prevRank = -1;
            if (doneToday.TryGetValue(i, out prev)) prevRank = Array.IndexOf(names, prev);
            if (medalRank <= prevRank) return;
            doneToday[i] = names[medalRank]; SaveRecs();
        }
        static readonly System.Collections.Generic.List<Transform> stands = new System.Collections.Generic.List<Transform>();
        static int desig, good; static float nextDesig;
        static readonly string[] Side = { "GAUCHE", "MILIEU", "DROITE" };
        static readonly System.Collections.Generic.List<Vector3> hang = new System.Collections.Generic.List<Vector3>();
        // Designation : les 3 cibles suspendues au rail (celles qu'on voit), de gauche a droite
        static bool FindHanging()
        {
            hang.Clear(); var cam = Camera.main; if (cam == null) return false;
            var pts = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Vector3, string>>();
            foreach (var c in UnityEngine.Object.FindObjectsOfType<Collider>())
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                bool tgt = false, bad = false;
                for (var x = c.transform; x != null; x = x.parent) { var n = x.name.ToLowerInvariant(); if (n.Contains("target")) tgt = true; if (n.Contains("stand") || n.Contains("monitor") || n.Contains("camera") || n.Contains("control") || n.Contains("area")) bad = true; }
                if (!tgt || bad) continue;
                var b = c.bounds; if (b.size.y < 0.15f || b.size.y > 1.6f || b.size.x > 2f) continue;
                if ((b.center - cam.transform.position).magnitude > 80f) continue;
                if (pts.Any(p0 => (p0.Key - b.center).magnitude < 0.8f)) continue;
                pts.Add(new System.Collections.Generic.KeyValuePair<Vector3, string>(b.center, c.name));
            }
            PostersPlugin.Log.LogInfo("[range day] designation : cibles suspendues trouvees " + pts.Count + " : " + string.Join(", ", pts.Select(p0 => p0.Value + "@" + p0.Key.ToString("0.0")).ToArray()));
            if (pts.Count < 3) return false;
            var right = cam.transform.right; var o = cam.transform.position;
            var three = pts.OrderByDescending(p0 => p0.Key.y).Take(3).OrderBy(p0 => Vector3.Dot(p0.Key - o, right)).ToList();
            hang.AddRange(three.Select(p0 => p0.Key)); return true;
        }
        static readonly System.Collections.Generic.List<Transform> rail = new System.Collections.Generic.List<Transform>();
        // d'apres le log : les cibles suspendues s'appellent Target_stand_changed (1), (2) et (3)
        static bool FindRail()
        {
            rail.Clear(); var cam = Camera.main; if (cam == null) return false;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>())
                if (t.gameObject.activeInHierarchy && (t.name == "Target_stand_changed (1)" || t.name == "Target_stand_changed (2)" || t.name == "Target_stand_changed (3)") && !rail.Any(r => r.name == t.name)) rail.Add(t);
            PostersPlugin.Log.LogInfo("[range day] designation : cibles suspendues " + rail.Count + " : " + string.Join(", ", rail.Select(r => r.name).ToArray()));
            if (rail.Count < 3) return FindHanging() && false;
            var right = cam.transform.right; var o = cam.transform.position;
            rail.Sort((a, b) => Vector3.Dot(a.position - o, right).CompareTo(Vector3.Dot(b.position - o, right)));
            return true;
        }
        static int RailOf(Transform t) { for (var x = t; x != null; x = x.parent) { int i = rail.IndexOf(x); if (i >= 0) return i; } return -1; }
        static int HangOf(Vector3 world) { int best = -1; float bd = 1.5f * 1.5f; for (int i = 0; i < hang.Count; i++) { var d = world - hang[i]; d.y *= 0.5f; if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = i; } } return best; }
        static bool FindStands()
        {
            stands.Clear(); good = 0;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>())
                if (t.name.StartsWith("Target_stand") && t.gameObject.activeInHierarchy && (t.parent == null || !t.parent.name.StartsWith("Target_stand")) && !stands.Any(o2 => (o2.position - t.position).sqrMagnitude < 0.5f)) stands.Add(t);
            var cam = Camera.main; if (stands.Count < 3 || cam == null) { PostersPlugin.Log.LogInfo("[range day] designation : " + stands.Count + " cible(s) sur pied trouvee(s)"); return false; }
            var right = cam.transform.right; var o = cam.transform.position;
            stands.Sort((a, b) => Vector3.Dot(a.position - o, right).CompareTo(Vector3.Dot(b.position - o, right)));
            if (stands.Count > 3) stands.RemoveRange(3, stands.Count - 3);
            PostersPlugin.Log.LogInfo("[range day] designation : " + string.Join(", ", stands.Select(x => x.name).ToArray()));
            return true;
        }
        // la cible touchee peut etre une variante (cassee, changee) du meme pied : on prend le pied le plus proche
        static int StandOf(Transform t)
        {
            Transform st0 = null; for (var x = t; x != null; x = x.parent) if (x.name.StartsWith("Target_stand")) st0 = x;
            if (st0 == null || stands.Count == 0) return -1;
            int best = -1; float bd = 4f;
            for (int i = 0; i < stands.Count; i++) { float d = (stands[i].position - st0.position).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            if (best < 0) PostersPlugin.Log.LogInfo("[range day] designation : touche " + t.name + " (" + st0.name + ") loin des pieds");
            return best;
        }
        static bool IsTarget(Transform t) { for (; t != null; t = t.parent) if (t.name.ToLowerInvariant().Contains("target")) return true; return false; }
        static float CenterOff(Transform t, Vector3 world, Vector3 dir)
        {
            var c = t.GetComponent<Collider>(); var ctr = c != null ? c.bounds.center : t.position;
            var v = world - ctr; v -= Vector3.Project(v, dir); return v.magnitude;
        }
        static Vector3 shotDir;
        internal static void BeginShot(EFT.InventoryLogic.Weapon w, float L0, Transform hitT, Vector3 world)
        {
            if (st != St.Run) return;
            if (Precise() && L0 < 15f) { tooCloseUntil = Time.unscaledTime + 2.5f; PostersPlugin.Log.LogInfo(L("tir a " + L0.ToString("0.0") + " m ignore (15 m minimum)")); return; }
            var br = Broken(w, L0);
            if (br != null) { Snd(EFT.UI.EUISoundType.ErrorMessage); st = St.Idle; inShot = false; PostersPlugin.Log.LogInfo(L("contrainte non respectee : " + br)); Show("RATÉ", "Contrainte non respectée : " + br, 7f); EndDaily(-1); return; }
            inShot = true; hitNow = false; weapon = w ?? weapon; distSum += L0; lastHitT = hitT;
            if (shots == 0) firstShot = Time.unscaledTime;
            if (hitT == null) return;
            if (mode == 5)
            {
                int k = RailOf(hitT);
                if (k < 0) { PostersPlugin.Log.LogInfo("[range day] designation : touche " + hitT.name + " < " + (hitT.parent != null ? hitT.parent.name : "-")); return; }
                hitNow = true; if (k == desig) { score += 10; good++; } else score -= 10;
                return;
            }
            if (Precise())
            {
                if (target == null) { target = hitT.gameObject; PostersPlugin.Log.LogInfo(L("cible = " + hitT.name)); }
                if (hitT.gameObject != target) return;
                hitNow = true; pts.Add(hitT.InverseTransformPoint(world));
                if (mode == 1 || mode == 4) centerSum += CenterOff(hitT, world, SpotterScope.ShotDir.sqrMagnitude > 0.5f ? SpotterScope.ShotDir : Vector3.forward);
            }
            else if (IsTarget(hitT))
            {
                hitNow = true;
                if (mode == 2) touched.Add(hitT);
                else { hitsN++; score += 10; if (CenterOff(hitT, world, SpotterScope.ShotDir.sqrMagnitude > 0.5f ? SpotterScope.ShotDir : Vector3.forward) < 0.05f) score += 5; }
            }
        }
        internal static void OnPosterHit(GameObject go, Vector3 world) { }
        internal static bool MissedNow() { return st == St.Run && inShot && !hitNow; }
        internal static void EndShot()
        {
            if (st != St.Run || !inShot) return; inShot = false; shots++;
            if (!hitNow) { incomplete = true; if (mode == 3 || mode == 5) score -= 5; if (++missRun == 10) SendReward(0, "10 ratés d'affilée", "5c13cef886f774072e618e82"); } else missRun = 0;
            PostersPlugin.Log.LogInfo(L("tir " + shots + "/" + MaxShots() + (hitNow ? "" : " (hors cible)") + (mode == 2 ? ", cibles " + touched.Count + "/3" : mode == 3 ? ", score " + score : "")));
            if (mode == 2 && touched.Count >= 3) { Finish(); return; }
            if (mode == 5) return;
            if (shots >= MaxShots()) Finish();
        }
        static void Finish()
        {
            st = St.Idle;
            string fam; float k = ClassK(weapon, out fam); if (mode == 2 || mode == 3 || mode == 5) k = 1f;
            float dist = shots > 0 ? distSum / shots : 0f;
            string wname = weapon != null ? AmmoCard.Loc(weapon.ShortName) : "?"; string wid = weapon != null ? weapon.TemplateId.ToString() : "?";
            float val; string detail;
            if (Precise())
            {
                int need = MaxShots();
                if (incomplete || pts.Count < need || target == null) { Show("INCOMPLET", "Une balle a manqué la cible · pas de médaille  ·  <color=#9fc47a>+" + AddXp(daily >= 0 ? 0 : 10, true) + " XP</color>", 7f); EndDaily(-1); PostersPlugin.Log.LogInfo(L("incomplet (" + pts.Count + "/" + need + " sur la cible)")); return; }
                float len;
                if (mode == 0) { len = 0f; var w = pts.Select(p => target.transform.TransformPoint(p)).ToList(); for (int i = 0; i < w.Count; i++) for (int j = i + 1; j < w.Count; j++) len = Mathf.Max(len, Vector3.Distance(w[i], w[j])); }
                else len = centerSum / pts.Count;
                val = dist > 0.1f ? len / dist * 3437.75f : 99f;
                detail = val.ToString("0.0") + " MOA  ·  " + (len * 100f).ToString("0.0") + " cm " + (mode != 0 ? "du centre " : "") + "à " + dist.ToString("0") + " m";
            }
            else if (mode == 2)
            {
                if (touched.Count < 3) { Show("RATÉ", "3 cibles en 15 balles maximum  ·  <color=#9fc47a>+" + AddXp(daily >= 0 ? 0 : 10, true) + " XP</color>", 7f); EndDaily(-1); PostersPlugin.Log.LogInfo(L("rate (" + touched.Count + "/3 cibles)")); return; }
                val = Time.unscaledTime - firstShot; detail = val.ToString("0.00") + " s  ·  " + shots + " balles";
            }
            else if (mode == 5) { val = score; detail = score + " points  ·  " + good + " bonnes cibles"; }
            else { val = score; detail = score + " points  ·  " + hitsN + "/10 touchées"; }
            var th = Thr[mode]; float b = th[0] * k, s2 = th[1] * k, g = th[2] * k; bool lo = Lower(mode);
            System.Func<float, float, bool> ok = (v, t) => lo ? v <= t : v >= t;
            string medal = ok(val, g) ? "OR" : ok(val, s2) ? "ARGENT" : ok(val, b) ? "BRONZE" : "PAS DE MÉDAILLE";
            string col = ok(val, g) ? "#f0cd86" : ok(val, s2) ? "#d8dcdf" : ok(val, b) ? "#d28c5c" : "#a9a693";
            string u = Unit[mode], f = mode == 3 || mode == 5 ? "0" : "0.0";
            System.Func<float, string> gap = t => Mathf.Abs(val - t).ToString(f) + " " + u;
            string next = !ok(val, b) ? "À " + gap(b) + " du bronze" : !ok(val, s2) ? "À " + gap(s2) + " de l'argent" : !ok(val, g) ? "À " + gap(g) + " de l'or" : "Parfait.";
            LoadRecs(); bool rec = false; int rankBefore = Rank(); var key = mode + ":" + wid; string mstTxt = ""; rewardTxt = "";
            { int mn; mastery.TryGetValue(wid, out mn); int ml0 = MLevel(mn); mastery[wid] = mn + 1; if (MLevel(mn + 1) > ml0) mstTxt = "  ·  <color=#f0cd86>MAÎTRISE " + MLevel(mn + 1) + "</color>"; }
            System.Collections.Generic.KeyValuePair<string, float> old;
            if (wid != "?" && (!recs.TryGetValue(key, out old) || (lo ? val < old.Value : val > old.Value))) { recs[key] = new System.Collections.Generic.KeyValuePair<string, float>(wname, val); SaveRecs(); rec = true; }
            int mr = ok(val, g) ? 2 : ok(val, s2) ? 1 : ok(val, b) ? 0 : -1, gain;
            string[] dCons = daily >= 0 ? Today()[daily].Cons : new string[0];
            string achTxt = "";
            try
            {
                if (mr >= 0) achTxt += Unlock(0);
                if (mode == 0 && val <= 0.5f) achTxt += Unlock(1);
                if (mode == 2 && val <= 1.5f) achTxt += Unlock(2);
                if (mode == 3 && val >= 140f) achTxt += Unlock(3);
                if (mr >= 0 && RangeTV.cNight != null && RangeTV.cNight.Value && ++nightWins >= 10) achTxt += Unlock(4);
                if (mr == 2 && dCons.Contains("hip")) achTxt += Unlock(5);
                if (Streak() >= 30) achTxt += Unlock(6);
                if (mastery.Count >= 20) achTxt += Unlock(7);
                if (mr == 2 && weapon != null && (weapon.AmmoCaliber ?? "").Contains("127")) achTxt += Unlock(9);
                if (mastery.Values.Any(n => MLevel(n) >= 10)) achTxt += Unlock(10);
                if (mode == 5 && val >= 180f) achTxt += Unlock(11);
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[range day] succes : " + e.Message); }
            if (daily >= 0)
            {
                var dd = Today()[daily]; LoadRecs(); string prev = DoneMedal(daily); int pr = prev == null ? -1 : Array.IndexOf(new[] { "BRONZE", "ARGENT", "OR" }, prev);
                float[] fk = { 0.5f, 0.75f, 1f }; int full = DayXp[dd.Diff];
                int now2 = mr >= 0 ? Mathf.RoundToInt(full * fk[mr]) : 0, was = pr >= 0 ? Mathf.RoundToInt(full * fk[pr]) : 0;
                if (mr >= 0) MarkStreak();
                int bonus = Math.Min(10, Streak()) * 10;
                gain = AddXp(Mathf.RoundToInt(Math.Max(0, now2 - was) * (1f + bonus / 100f)) + (rec ? 50 : 0), false);
                string[] tiers = { "ABSURDE", "COMMUN", "PEU COMMUN", "RARE", "ÉPIQUE", "LÉGENDAIRE" }; int tb = TierIdx[dd.Diff], ti = mr < 0 ? 0 : Mathf.Clamp(tb + (mr - 1), 0, 5);
                if (mr < 0 && pr < 0 && junkGiven.Add(daily)) { SendReward(0, Names[dd.Mode] + " raté"); rewardTxt = "  ·  lot de consolation <color=#8cc0f0>ABSURDE</color> dans tes messages"; }
                if (mr > pr && (dd.Mystery || dd.Diff == 3)) { RangeDayUI.ShowCards(ti, dd.Diff == 3 ? "défi de la semaine" : "défi mystère"); rewardTxt = "\n<color=#f0cd86>DÉFI MYSTÈRE : choisis ta carte !</color>"; }
                else if (mr > pr) { SendReward(ti, Names[dd.Mode]); rewardTxt = "  ·  récompense <color=#8cc0f0>" + tiers[ti] + "</color> dans tes messages"; }
                EndDaily(mr);
            }
            else if (mr >= 0 && RangeTV.cTestCards != null && RangeTV.cTestCards.Value) { RangeDayUI.ShowCards(mr + 1, "test · " + Names[mode].ToLowerInvariant()); rewardTxt = "\n<color=#f0cd86>TEST : choisis ta carte !</color>"; gain = AddXp(Mathf.RoundToInt((mr == 2 ? 120 : mr == 1 ? 60 : 30) * Diff[mode]), true); }
            else if (mr == 2 && goldSet.Add(key)) { SaveRecs(); RangeDayUI.ShowCards(3, "1er or · " + Names[mode].ToLowerInvariant() + " · " + wname); rewardTxt = "\n<color=#f0cd86>1ER OR AVEC CETTE ARME : choisis ta carte !</color>"; gain = AddXp(Mathf.RoundToInt(120 * Diff[mode]) + (rec ? 50 : 0), true); }
            else gain = AddXp(Mathf.RoundToInt((mr == 2 ? 120 : mr == 1 ? 60 : mr == 0 ? 30 : 10) * Diff[mode]) + (rec ? 50 : 0), true);
            if (Enumerable.Range(0, 6).All(m => goldSet.Any(k2 => k2.StartsWith(m + ":")))) achTxt += Unlock(8);
            if (mode == 2 && val > 60f) { SendReward(0, "Chrono de plus de 60 s", "5bc9c29cd4351e003562b8a3"); achTxt += "\n<color=#8a9aa6>Plus de 60 s… une boîte de sprats t'attend dans tes messages</color>"; }
            string xpTxt = "  ·  <color=#9fc47a>+" + gain + " XP</color>" + mstTxt + rewardTxt + achTxt + (gain == 0 && daily < 0 && rewardTxt.Length == 0 ? " (plafond du jour)" : "") + (Rank() > rankBefore ? "\n<color=#f0cd86>NOUVEAU RANG " + Rank() + " · " + GradeName() + "</color>" : "");
            Snd(mr == 2 ? EFT.UI.EUISoundType.AchievementCompleted : mr == 1 ? EFT.UI.EUISoundType.QuestCompleted : mr == 0 ? EFT.UI.EUISoundType.QuestSubTrackComplete : EFT.UI.EUISoundType.QuestFailed);
            if (achTxt.Length > 0) Snd(EFT.UI.EUISoundType.AchievementCompleted);
            Show("<color=" + col + ">" + medal + "</color>", detail + "  ·  " + wname + "\n" + (rec ? "<color=#f0cd86>NOUVEAU RECORD</color>  ·  " : "") + next + xpTxt, 9f);
            PostersPlugin.Log.LogInfo(L(detail.Replace("à", "a") + ", " + fam + " x" + k.ToString("0.00") + ", " + medal + (rec ? ", record " + wname : "")));
        }
        static float showUntil, tooCloseUntil;
        internal static void ShowPub(string a, string b, float secs) { Show(a, b, secs); }
        // sons de l'interface du jeu (aucun son copie dans le mod)
        internal static void Snd(EFT.UI.EUISoundType t) { try { var g = Comfort.Common.Singleton<EFT.UI.GUISounds>.Instance; if (g != null) g.PlayUISound(t); } catch { } }
        static void Show(string a, string b, float secs)
        {
            Hud(); big.text = a; small.text = b; showUntil = Time.unscaledTime + secs; hud.SetActive(true);
        }
        static void Hud()
        {
            if (hud != null) { hud.SetActive(true); return; }
            hud = new GameObject("HideoutPosters_RangeHud"); UnityEngine.Object.DontDestroyOnLoad(hud);
            var cv = hud.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 31990;
            var sc = hud.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            hud.gameObject.AddComponent<RDTranslate>();
            big = FreePlace.Txt("Big", hud.transform, "", 72, new Color(0.94f, 0.80f, 0.53f), TextAlignmentOptions.Center, true); FreePlace.Rt(big.gameObject, hud.transform, new Vector2(0, 0.73f), new Vector2(1, 0.85f), Vector2.zero, Vector2.zero);
            small = FreePlace.Txt("Small", hud.transform, "", 26, new Color(0.91f, 0.89f, 0.82f), TextAlignmentOptions.Top, false); FreePlace.Rt(small.gameObject, hud.transform, new Vector2(0, 0.62f), new Vector2(1, 0.73f), Vector2.zero, Vector2.zero);
            small.enableWordWrapping = true; big.outlineWidth = 0.15f; big.outlineColor = new Color32(0, 0, 0, 200);
            hud.AddComponent<RangeHudTick>();
        }
        internal static void Tick()
        {
            if (big == null) return;
            float now = Time.unscaledTime;
            if (st == St.Count)
            {
                float e = now - t0; int n = 3 - Mathf.FloorToInt(e);
                if (e < 3f && n != lastBeep) { lastBeep = n; Snd(EFT.UI.EUISoundType.MenuCheckBox); }
                if (e < 3f) { big.text = n.ToString(); small.text = Names[mode] + "  ·  " + Kick[mode] + (daily >= 0 ? "\n<color=#f0cd86>" + string.Join("  ·  ", Today()[daily].Cons.Select(ConsLabel).ToArray()) + "</color>" : ""); big.transform.localScale = Vector3.one * (1.25f - 0.25f * (e - Mathf.Floor(e))); }
                else if (e < 3.7f) { if (lastBeep != 0) { lastBeep = 0; Snd(EFT.UI.EUISoundType.MenuInstallMag); } big.text = "TIREZ !"; small.text = ""; big.transform.localScale = Vector3.one; if (shots == 0 && st == St.Count) { } }
                if (e >= 3f && st == St.Count) { st = St.Run; t0 = now; firstShot = now; desig = UnityEngine.Random.Range(0, 3); nextDesig = now + 1.5f; PostersPlugin.Log.LogInfo(L("go")); }
                return;
            }
            if (st == St.Run)
            {
                if (mode == 5)
                {
                    if (now >= nextDesig) { int nd = UnityEngine.Random.Range(0, 2); desig = (desig + 1 + nd) % 3; nextDesig = now + 1.5f; }
                    if (desig != lastDesig) { lastDesig = desig; Snd(EFT.UI.EUISoundType.MenuCheckBox); }
                    big.text = Side[desig]; big.transform.localScale = Vector3.one * 0.8f;
                    small.text = "DÉSIGNATION  ·  " + score + " pts  ·  " + Mathf.Max(0f, 20f - (now - t0)).ToString("0.0") + " s";
                    if (now - t0 >= 20f) Finish();
                    return;
                }
                if (now - t0 > 0.7f)
                {
                    big.text = mode == 2 ? (shots > 0 ? (now - firstShot).ToString("0.0") + " s" : "0.0 s") : mode == 3 ? score + " pts" : shots + " / " + MaxShots();
                    big.transform.localScale = Vector3.one * 0.5f;
                    small.text = now < tooCloseUntil ? "<color=#ef8f6e>Trop près : recule à 15 m minimum</color>" : mode == 2 ? "CHRONO  ·  cibles " + touched.Count + " / 3" : mode == 3 ? "ÉCONOMIE  ·  balle " + shots + " / 10" : Names[mode] + "  ·  15 m minimum";
                }
                if (daily >= 0 && daily < Today().Count && Today()[daily].Diff == 4 && now - t0 > 60f) { st = St.Idle; Show("TROP TARD", "Le défi éclair dure 60 secondes", 6f); EndDaily(-1); }
                else if (now - t0 > 120f) { st = St.Idle; Show("TEMPS ÉCOULÉ", "Défi annulé au bout de 2 minutes", 5f); EndDaily(-1); }
                return;
            }
            big.transform.localScale = Vector3.one * 0.7f;
            if (now > showUntil && hud != null) hud.SetActive(false);
        }
    }
    public class RangeHudTick : MonoBehaviour { void Update() { try { RangeGame.Tick(); } catch { } } }

    // ===== RANGE DAY : la fenetre (etape 2 : la coque, sans les defis) =====
    public class RangeDayUI : MonoBehaviour
    {
        static RangeDayUI me;
        GameObject canvas; CursorLockMode oldLock; bool oldVis; int tab;
        readonly System.Collections.Generic.List<Image> tabBg = new System.Collections.Generic.List<Image>();
        readonly System.Collections.Generic.List<TextMeshProUGUI> tabTx = new System.Collections.Generic.List<TextMeshProUGUI>();
        TextMeshProUGUI body;
        static readonly Color BgTop = new Color(0.047f, 0.082f, 0.10f, 0.97f), Glass = new Color(0.086f, 0.133f, 0.165f, 0.85f), Edge = new Color(0.62f, 0.84f, 0.81f, 0.12f),
            GoldC = new Color(0.85f, 0.71f, 0.42f), Cream = new Color(0.91f, 0.89f, 0.82f), Teal = new Color(0.50f, 0.66f, 0.64f), Sub = new Color(0.66f, 0.65f, 0.58f);
        static readonly string[] Tabs = { "DÉFIS DU JOUR", "PERMANENTS", "ARSENAL", "SUCCÈS" };
        internal static bool IsOpen { get { return me != null; } }
        static bool inputPatched; static int blockUntil;
        // fenetre ouverte : le jeu ne recoit plus aucune touche (Echap ne fait pas quitter la planque, le clic ne tire pas, on ne bouge pas)
        static bool BlockInput(EFT.InputSystem.InputNodeAbstract __instance, System.Collections.Generic.List<EFT.InputSystem.ECommand> commands, ref float[] axes, ref EFT.InputSystem.ECursorResult shouldLockCursor)
        {
            if ((me == null && Time.frameCount > blockUntil) || !(__instance is EFT.InputSystem.InputTree)) return true;
            try { commands.Clear(); if (axes != null) for (int i = 0; i < axes.Length; i++) axes[i] = 0f; shouldLockCursor = EFT.InputSystem.ECursorResult.ShowCursor; } catch { }
            return false;
        }
        // ---------- defi mystere : 3 cartes a choisir a la souris ----------
        static int[] pendTiers; static string pendLabel;
        static readonly System.Collections.Generic.Queue<System.Collections.Generic.KeyValuePair<int, string>> cardQueue = new System.Collections.Generic.Queue<System.Collections.Generic.KeyValuePair<int, string>>();
        // plusieurs tirages peuvent tomber en meme temps (defi + rang + succes) : on les enchaine
        internal static void ShowCards(int baseTier, string label)
        {
            cardQueue.Enqueue(new System.Collections.Generic.KeyValuePair<int, string>(baseTier, label));
            if (me == null) OpenNextCards();
        }
        internal static void OpenNextCards()
        {
            if (cardQueue.Count == 0 || me != null) return;
            var nx = cardQueue.Dequeue(); int baseTier = nx.Key; string label = nx.Value;
            var r = new System.Random(); pendTiers = new int[3]; pendLabel = label;
            for (int i = 0; i < 3; i++) { int roll = r.Next(100), d = roll < 15 ? -2 : roll < 40 ? -1 : roll < 70 ? 0 : roll < 90 ? 1 : roll < 98 ? 2 : 3; pendTiers[i] = Mathf.Clamp(baseTier + d, 0, 5); }
            PostersPlugin.Log.LogInfo("[range day] cartes (" + label + ") : " + string.Join(" / ", pendTiers.Select(t => CardTN[t]).ToArray()));
            Show();
        }
        static readonly string[] CardTN = { "ABSURDE", "COMMUN", "PEU COMMUN", "RARE", "ÉPIQUE", "LÉGENDAIRE" };
        static readonly Color[] CardTC = { new Color(0.55f, 0.5f, 0.45f), new Color(0.78f, 0.78f, 0.75f), new Color(0.45f, 0.75f, 0.4f), new Color(0.35f, 0.6f, 0.95f), new Color(0.7f, 0.4f, 0.95f), new Color(0.98f, 0.72f, 0.25f) };
        int[] cTiers; Image[] cImg, cGlow, cFrame = new Image[3], cIcon = new Image[3]; string[] cTpl = new string[3]; float[] cIconAt = new float[3]; EFT.InventoryLogic.Item[] cItem = new EFT.InventoryLogic.Item[3]; bool[] cRetry = new bool[3]; ItemIcon[] cItemIcon = new ItemIcon[3]; string[] cName = new string[3]; float cDoneAt = -1; TextMeshProUGUI cHint; TextMeshProUGUI[] cTx; float[] cFlip; bool[] cShown; int cPick = -1; TextMeshProUGUI cTitle; float cBorn;
        internal void BuildCards(Transform win)
        {
            cTiers = pendTiers; pendTiers = null; cImg = new Image[3]; cGlow = new Image[3]; cTx = new TextMeshProUGUI[3]; cFlip = new[] { -1f, -1f, -1f }; cShown = new bool[3]; cBorn = Time.unscaledTime;
            var ov = Pic("Cards", win, sBack, Color.white, Image.Type.Simple); ov.raycastTarget = true;
            FreePlace.Rt(ov.gameObject, win, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var dim = Pic("Dim", ov.transform, sGlow, new Color(0, 0, 0, 0.55f), Image.Type.Simple); FreePlace.Rt(dim.gameObject, ov.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var tag = FreePlace.Txt("K", ov.transform, "RANGE DAY  ·  RÉCOMPENSE CACHÉE", 16, GoldC, TextAlignmentOptions.Center, false); FreePlace.Rt(tag.gameObject, ov.transform, new Vector2(0, 0.91f), new Vector2(1, 0.95f), Vector2.zero, Vector2.zero); tag.characterSpacing = 14;
            cTitle = FreePlace.Txt("H", ov.transform, (pendLabel ?? "défi mystère").ToUpperInvariant() + "\n<size=40%><color=#8a9aa6>CHOISIS UNE CARTE</color></size>", 60, Cream, TextAlignmentOptions.Center, true);
            FreePlace.Rt(cTitle.gameObject, ov.transform, new Vector2(0, 0.76f), new Vector2(1, 0.91f), Vector2.zero, Vector2.zero); cTitle.enableAutoSizing = true; cTitle.fontSizeMin = 18; cTitle.fontSizeMax = 60; cTitle.enableWordWrapping = false;
            var line = Pic("L", ov.transform, sRound4, new Color(0.85f, 0.71f, 0.42f, 0.35f), Image.Type.Sliced); var lr = (RectTransform)line.transform; lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.755f); lr.sizeDelta = new Vector2(420, 2);
            var vt = MysteryCards.RandomVerso(); var vs = vt != null ? Sprite.Create(vt, new Rect(0, 0, vt.width, vt.height), new Vector2(0.5f, 0.5f)) : null;
            for (int i = 0; i < 3; i++)
            {
                if (i > 0) { vt = MysteryCards.RandomVerso(); vs = vt != null ? Sprite.Create(vt, new Rect(0, 0, vt.width, vt.height), new Vector2(0.5f, 0.5f)) : null; }
                int k = i; var anchor = new Vector2(0.24f + i * 0.26f, 0.42f);
                var gl = Pic("G" + i, ov.transform, sGlow, new Color(0.85f, 0.71f, 0.42f, 0f), Image.Type.Simple); var gr2 = (RectTransform)gl.transform; gr2.anchorMin = gr2.anchorMax = anchor; gr2.sizeDelta = new Vector2(560, 700);
                var fr = Pic("F" + i, ov.transform, sRound4, new Color(0.85f, 0.71f, 0.42f, 0.55f), Image.Type.Sliced); var fr2 = (RectTransform)fr.transform; fr2.anchorMin = fr2.anchorMax = anchor; fr2.sizeDelta = new Vector2(312, 462);
                var im = Pic("C" + i, ov.transform, vs ?? sRound4, Color.white, vs != null ? Image.Type.Simple : Image.Type.Sliced); im.raycastTarget = true;
                var rt = (RectTransform)im.transform; rt.anchorMin = rt.anchorMax = anchor; rt.sizeDelta = new Vector2(300, 450);
                cFrame[i] = fr;
                var tx = FreePlace.Txt("T", im.transform, "", 40, Color.white, TextAlignmentOptions.Center, true); FreePlace.Rt(tx.gameObject, im.transform, Vector2.zero, Vector2.one, new Vector2(16, 16), new Vector2(-16, -16));
                im.gameObject.AddComponent<Button>().onClick.AddListener(() => PickCard(k));
                cImg[i] = im; cTx[i] = tx; cGlow[i] = gl; im.transform.localScale = Vector3.zero;
            }
            var tiers0 = (int[])cTiers.Clone();
            System.Threading.Tasks.Task.Run(() => { for (int i = 0; i < 3; i++) cTpl[i] = RangeGame.PreviewTpl(tiers0[i]); });
            cHint = FreePlace.Txt("F", ov.transform, "UN BON SCORE AMÉLIORE LES CARTES  ·  DE ABSURDE À LÉGENDAIRE", 15, Sub, TextAlignmentOptions.Center, false); FreePlace.Rt(cHint.gameObject, ov.transform, new Vector2(0, 0.04f), new Vector2(1, 0.09f), Vector2.zero, Vector2.zero); cHint.characterSpacing = 8;
        }
        void PickCard(int k)
        {
            if (cPick >= 0 || cTiers == null || Time.unscaledTime - cBorn < 1f) return; cPick = k; float now = Time.unscaledTime;
            cFlip[k] = now; for (int i = 0; i < 3; i++) if (i != k) cFlip[i] = now + 1.4f + (i > k ? 0.35f : 0f);
            RangeGame.SendRewardPub(cTiers[k], pendLabel + " · carte mystère", cTpl[k]);
            cTitle.text = "…";
            PostersPlugin.Log.LogInfo("[range day] carte choisie " + (k + 1) + " : " + CardTN[cTiers[k]]);
        }
        void CardsTick()
        {
            if (cTiers == null) return; float now = Time.unscaledTime;
            for (int i = 0; i < 3; i++)
            {
                if (cIcon[i] == null) continue;
                if (cItemIcon[i] != null && cItemIcon[i].Sprite != null && cIcon[i].sprite != cItemIcon[i].Sprite) { cIcon[i].sprite = cItemIcon[i].Sprite; cIcon[i].enabled = true; }
                // icone toujours absente : on redemande une fois au jeu
                if (!cIcon[i].enabled && !cRetry[i] && now - cIconAt[i] > 1.2f && cItem[i] != null) { cRetry[i] = true; try { cItemIcon[i] = EFT.UI.DragAndDrop.ItemViewFactory.LoadItemIcon(cItem[i], 2, true); } catch { } }
            }
            if (cPick >= 0 && cShown.All(x => x))
            {
                if (cDoneAt < 0) cDoneAt = now;
                float left = 6f - (now - cDoneAt); cHint.text = "FERMETURE DANS " + Mathf.CeilToInt(Mathf.Max(0, left)) + " S  ·  ÉCHAP POUR FERMER";
                if (left <= 0f) { Close(); return; }
            }
            else if (cPick >= 0) cHint.text = "LES AUTRES CARTES SE RETOURNENT…";
            for (int i = 0; i < 3; i++)
            {
                var t0 = cImg[i].transform; if (cFrame[i] != null) { cFrame[i].transform.localScale = t0.localScale; cFrame[i].transform.localRotation = t0.localRotation; }
                // entree : les cartes arrivent une par une
                float e = Mathf.Clamp01((now - cBorn - 0.15f * i) / 0.45f); e = 1f - (1f - e) * (1f - e) * (1f - e);
                if (cFlip[i] < 0 || now < cFlip[i])
                {
                    bool over = cPick < 0 && hovB != null && hovB.gameObject == cImg[i].gameObject;
                    float h = (over ? 1.07f : 1f + 0.012f * Mathf.Sin(now * 2.5f + i)) * e;
                    t0.localScale = Vector3.Lerp(t0.localScale, new Vector3(h, h, 1f), e < 1f ? 1f : Time.unscaledDeltaTime * 12f);
                    t0.localRotation = Quaternion.Euler(0, 0, over ? 0f : Mathf.Sin(now * 1.3f + i * 2f) * 1.2f);
                    cGlow[i].color = new Color(0.85f, 0.71f, 0.42f, Mathf.Lerp(cGlow[i].color.a, over ? 0.45f : 0.12f, Time.unscaledDeltaTime * 8f));
                    continue;
                }
                float t = Mathf.Clamp01((now - cFlip[i]) / 0.45f), sy = i == cPick ? 1.1f : 0.92f;
                t0.localScale = new Vector3(sy * Mathf.Abs(Mathf.Cos(t * Mathf.PI)), sy, 1f); t0.localRotation = Quaternion.identity;
                if (t >= 0.5f && !cShown[i])
                {
                    cShown[i] = true; RangeGame.Snd(i == cPick ? EFT.UI.EUISoundType.AchievementCompleted : EFT.UI.EUISoundType.MenuInspectorWindowOpen); cImg[i].sprite = sRound4; cImg[i].type = Image.Type.Sliced;
                    var c = CardTC[cTiers[i]]; bool mine = i == cPick;
                    cImg[i].color = mine ? new Color(0.06f, 0.09f, 0.11f, 1f) : new Color(0.05f, 0.06f, 0.07f, 1f);
                    cFrame[i].color = mine ? c : new Color(c.r, c.g, c.b, 0.35f);
                    cGlow[i].color = mine ? new Color(c.r, c.g, c.b, 0.6f) : new Color(0, 0, 0, 0);
                    // l'objet : son icone du jeu et son nom
                    try
                    {
                        if (cTpl[i] != null)
                        {
                            var id = Guid.NewGuid().ToString("N").Substring(0, 24);
                            var it = Comfort.Common.Singleton<EFT.ItemFactory>.Instance.CreateItem(id, cTpl[i], null);
                            if (it != null)
                            {
                                cName[i] = AmmoCard.Loc(it.ShortName); var nm = AmmoCard.Loc(it.Name); if (!string.IsNullOrEmpty(nm) && nm.Length < 60) cName[i] = nm;
                                cItemIcon[i] = EFT.UI.DragAndDrop.ItemViewFactory.LoadItemIcon(it, 1, true);
                                var ic = Pic("Icon", cImg[i].transform, null, Color.white, Image.Type.Simple); ic.preserveAspect = true; ic.enabled = false; ic.raycastTarget = false;
                                FreePlace.Rt(ic.gameObject, cImg[i].transform, new Vector2(0.2f, 0.42f), new Vector2(0.8f, 0.82f), Vector2.zero, Vector2.zero); cIcon[i] = ic; cIconAt[i] = Time.unscaledTime; cItem[i] = it;
                            }
                        }
                    }
                    catch (Exception ex) { PostersPlugin.Log.LogWarning("[range day] objet carte : " + ex.Message); }
                    var hx = "#" + ColorUtility.ToHtmlStringRGB(c);
                    cTx[i].alignment = TextAlignmentOptions.Bottom; cTx[i].enableWordWrapping = true; cTx[i].overflowMode = TextOverflowModes.Overflow; cTx[i].enableAutoSizing = true; cTx[i].fontSizeMin = 14; cTx[i].fontSizeMax = 40;
                    cTx[i].rectTransform.anchorMax = new Vector2(1f, 0.34f);
                    cTx[i].text = "<size=55%>" + (cName[i] ?? "") + "</size>\n<size=38%><color=" + hx + ">" + CardTN[cTiers[i]] + "</color>" + (mine ? "" : "  ·  <color=#6f7a80>RATÉ</color>") + "</size>";
                    cTx[i].color = mine ? Cream : new Color(0.62f, 0.64f, 0.62f);
                    if (i == cPick) cTitle.text = "<size=60%><color=" + hx + ">" + (cName[i] ?? CardTN[cTiers[i]]) + "</color></size>\n<size=40%><color=#8a9aa6>" + CardTN[cTiers[i]] + "  ·  VÉRIFIE TA MESSAGERIE</color></size>";
                }
                if (cShown[i] && i == cPick) cGlow[i].transform.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(now * 3f));
            }
        }
        internal static void Show()
        {
            if (me != null) return;
            if (!inputPatched)
            {
                inputPatched = true;
                try
                {
                    var m = typeof(EFT.InputSystem.InputNodeAbstract).GetMethod("TranslateInput", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                    new HarmonyLib.Harmony("praedlab.hideoutposters.rangeday.input").Patch(m, prefix: new HarmonyLib.HarmonyMethod(typeof(RangeDayUI).GetMethod("BlockInput", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
                    PostersPlugin.Log.LogInfo("[range day] touches du jeu bloquees pendant la fenetre");
                }
                catch (Exception e) { PostersPlugin.Log.LogWarning("[range day] blocage des touches : " + e.Message); }
            }
            var go = new GameObject("HideoutPosters_RangeDay"); DontDestroyOnLoad(go);
            RangeGame.RollFlash(); go.AddComponent<RDTranslate>(); me = go.AddComponent<RangeDayUI>(); me.Build(); if (pendTiers != null) me.BuildCards(me.winRt);
        }
        // ---------- textures dessinees une fois (degrades, arrondis, anneau, fond topo) ----------
        static Sprite sRound4, sPill, sRing, sDot, sGlow, sBack, sShine;
        static Sprite Spr(Texture2D t, Vector4 border) { t.wrapMode = TextureWrapMode.Clamp; t.hideFlags = HideFlags.DontUnloadUnusedAsset; return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border); }
        static Sprite Rounded(int r)
        {
            int n = r * 2 + 2; var t = new Texture2D(n, n, TextureFormat.RGBA32, false); var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, n - r), cy = Mathf.Clamp(y + 0.5f, r, n - r);
                float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(r - d + 0.5f)));
            }
            t.SetPixels32(px); t.Apply(false); return Spr(t, new Vector4(r, r, r, r));
        }
        static Sprite Radial(int n, float pow)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false); var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1, d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Pow(1 - d, pow)));
            }
            t.SetPixels32(px); t.Apply(false); return Spr(t, Vector4.zero);
        }
        static Sprite Ring(int n, float th)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false); var px = new Color32[n * n]; float R = n / 2f - 1;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - n / 2f) * (x + 0.5f - n / 2f) + (y + 0.5f - n / 2f) * (y + 0.5f - n / 2f));
                float a = Mathf.Clamp01(R - d + 0.5f) * Mathf.Clamp01(d - (R - th) + 0.5f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * a));
            }
            t.SetPixels32(px); t.Apply(false); return Spr(t, Vector4.zero);
        }
        // fond « nuit tactique » : degrade, lueurs, courbes de niveau, reticule, vignette
        static Sprite Backdrop()
        {
            int W = 800, H = 450; var t = new Texture2D(W, H, TextureFormat.RGBA32, false); var px = new Color[W * H];
            Color a = new Color(0.047f, 0.082f, 0.102f), b = new Color(0.031f, 0.043f, 0.055f), teal = new Color(0.114f, 0.227f, 0.227f), vio = new Color(0.169f, 0.133f, 0.220f);
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                float u = x / (float)W, v = 1 - y / (float)H;
                var c = Color.Lerp(a, b, (u * 0.6f + v * 0.4f));
                float g1 = Mathf.Clamp01(1 - Mathf.Sqrt((u - 0.08f) * (u - 0.08f) * 1.6f + (v + 0.1f) * (v + 0.1f) * 3f) / 0.75f);
                float g2 = Mathf.Clamp01(1 - Mathf.Sqrt((u - 1.05f) * (u - 1.05f) * 1.6f + (v - 1.1f) * (v - 1.1f) * 3f) / 0.7f);
                c = Color.Lerp(c, teal, g1 * g1 * 0.8f); c = Color.Lerp(c, vio, g2 * g2 * 0.6f);
                // courbes de niveau
                float wv = Mathf.Sin(u * 9f + Mathf.Sin(u * 3.1f) * 1.6f) * 0.05f;
                for (int k = 0; k < 3; k++) { float line = Mathf.Abs(v - (0.80f + k * 0.05f) - wv); if (line < 0.0016f) c = Color.Lerp(c, new Color(0.37f, 0.70f, 0.66f), 0.16f); line = Mathf.Abs(v - (0.12f + k * 0.05f) + wv); if (line < 0.0016f) c = Color.Lerp(c, new Color(0.37f, 0.70f, 0.66f), 0.16f); }
                // reticule
                float rx = (u - 0.83f) * W, ry = (v - 0.30f) * H, rr = Mathf.Sqrt(rx * rx + ry * ry);
                bool ret = (Mathf.Abs(rr - 95) < 0.9f || Mathf.Abs(rr - 66) < 0.9f || Mathf.Abs(rr - 38) < 0.9f || Mathf.Abs(rr - 11) < 0.9f) || ((Mathf.Abs(rx) < 0.6f || Mathf.Abs(ry) < 0.6f) && rr < 118);
                if (ret) c = Color.Lerp(c, new Color(0.85f, 0.71f, 0.42f), 0.09f);
                // vignette + lignes de balayage
                float vg = Mathf.Clamp01((Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) - 0.35f) / 0.4f); c = Color.Lerp(c, Color.black, vg * 0.55f);
                if (y % 3 == 0) c *= 1.025f;
                c.a = 0.985f; px[y * W + x] = c;
            }
            t.SetPixels(px); t.Apply(false); t.filterMode = FilterMode.Bilinear; return Spr(t, Vector4.zero);
        }
        static void Sprites()
        {
            if (sRound4 != null) return;
            sRound4 = Rounded(6); sPill = Rounded(24); sRing = Ring(256, 14); sDot = Radial(64, 0.6f); sGlow = Radial(256, 2.2f); sBack = Backdrop();
            var sh = new Texture2D(64, 1, TextureFormat.RGBA32, false); for (int x = 0; x < 64; x++) { float k = Mathf.Sin(x / 63f * Mathf.PI); sh.SetPixel(x, 0, new Color(1, 1, 1, k * k)); } sh.Apply(false); sShine = Spr(sh, Vector4.zero);
        }
        static Image Pic(string n, Transform p, Sprite sp, Color c, Image.Type ty)
        {
            var im = FreePlace.Img(n, p, c); im.sprite = sp; im.type = ty; im.raycastTarget = false; return im;
        }
        GameObject perm, dayPanel, succPanel, progPanel; int selMode;
        readonly System.Collections.Generic.Dictionary<int, Image> dayCards = new System.Collections.Generic.Dictionary<int, Image>(), dayEdges = new System.Collections.Generic.Dictionary<int, Image>();
        int daySel = -1; TextMeshProUGUI asideLbl, asideTitle, asideDesc, asideMedals, asideGo;
        void SelectDay(int i)
        {
            var list = RangeGame.Today(); if (i < 0 || i >= list.Count) return; daySel = i; var d = list[i];
            string[] tn = { "absurde", "commun", "peu commun", "rare", "épique", "légendaire" }; string[] tc = { "#8a8070", "#c7c7bf", "#9fc47a", "#8cc0f0", "#c2a4f5", "#f0cd86" };
            int tb = d.Diff == 0 ? 1 : d.Diff == 1 || d.Diff == 4 ? 2 : 3;
            asideLbl.text = (d.Mystery ? "MYSTÈRE · " : "") + RangeGame.DiffDay[d.Diff];
            asideTitle.text = RangeGame.Names[d.Mode] + "\n<size=60%><color=#c9c5b1>" + string.Join(" · ", d.Cons.Select(RangeGame.ConsLabel).ToArray()) + "</color></size>";
            asideDesc.text = RangeGame.Quick[d.Mode] + ".";
            string[] mn = { "BRONZE", "ARGENT", "OR" }; string[] mc = { "#d39a6a", "#d8dce0", "#f6d98a" }; var sb = new System.Text.StringBuilder();
            for (int m = 0; m < 3; m++) { int t = Mathf.Clamp(tb + m - 1, 0, 5); string dn0 = RangeGame.DoneMedal(i); int pd = dn0 == null ? -1 : Array.IndexOf(mn, dn0); sb.Append("<color=" + mc[m] + ">●</color>  " + mn[m] + "<pos=45%><color=" + tc[t] + ">" + (m <= pd ? "<color=#5f6d76>déjà obtenu</color>" : d.Mystery || d.Diff == 3 ? "cartes " + tn[t] : tn[t]) + "</color>\n"); }
            string done = RangeGame.DoneMedal(i); sb.Append("<size=80%><color=#8a9aa6>" + RangeGame.DayXp[d.Diff] + " XP pour l'or" + (done != null ? "  ·  déjà " + done.ToLowerInvariant() : "") + "</color></size>");
            asideMedals.text = sb.ToString(); asideGo.text = done != null ? "REJOUER" : "COMMENCER";
            foreach (var kv in dayEdges) kv.Value.color = new Color(0.85f, 0.71f, 0.42f, kv.Key == i ? 0.9f : 0f);
        }
        readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Image, ItemIcon>> icons = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Image, ItemIcon>>();
        void IconTick() { foreach (var kv in icons) { var sp = kv.Value != null ? kv.Value.Sprite : null; if (sp != null && kv.Key.sprite != sp) { kv.Key.sprite = sp; kv.Key.enabled = true; kv.Key.color = new Color(1, 1, 1, 0.9f); } } }
        void DayTick() { IconTick(); if (daySel >= 0 && dayEdges.ContainsKey(daySel)) { var c = dayEdges[daySel].color; c.a = 0.55f + 0.35f * Mathf.Sin(Time.unscaledTime * 2.6f); dayEdges[daySel].color = c; } } TextMeshProUGUI pKick, pTitle, pDesc, pNote, pRecs;
        readonly System.Collections.Generic.List<TextMeshProUGUI> pMed = new System.Collections.Generic.List<TextMeshProUGUI>(), selTx = new System.Collections.Generic.List<TextMeshProUGUI>();
        readonly System.Collections.Generic.List<Image> selBg = new System.Collections.Generic.List<Image>();
        void SelMode(int m)
        {
            selMode = m;
            for (int i = 0; i < selBg.Count; i++) { selBg[i].color = i == m ? new Color(0.85f, 0.71f, 0.42f, 0.95f) : new Color(1, 1, 1, 0.06f); selTx[i].color = i == m ? new Color(0.08f, 0.07f, 0.05f) : Sub; }
            if (pKick != null) pKick.text = RangeGame.DiffName[m] + "  ·  XP ×" + RangeGame.Diff[m].ToString("0.0") + "  ·  " + RangeGame.Kick[m];
            if (pTitle != null) pTitle.text = RangeGame.Names[m];
            if (pDesc != null) pDesc.text = RangeGame.Desc[m];
            for (int i = 0; i < pMed.Count; i++) pMed[i].text = RangeGame.Med[m][i];
            if (pNote != null) pNote.text = m <= 1 || m == 4 ? "Seuils pour fusils d'assaut et DMR. Pistolets ×3, pistolets-mitrailleurs et slugs ×2, fusils à verrou ÷1,5." : "Mêmes seuils pour toutes les armes.";
            if (pRecs != null) pRecs.text = RangeGame.RecordsText(m);
            tabT = 0f;
        }
        RectTransform shine, sweep, winRt, bodyRt; Image ring, halo, liveDot; float ringT, openT, tabT = 1f; CanvasGroup winCg, bodyCg; Image selGlow; GraphicRaycaster gr; bool closed; int lastClickFrame = -1;
        // le jeu ne transmet plus les clics a l'interface quand ses touches sont bloquees : on lit le clic nous-memes
        readonly System.Collections.Generic.Dictionary<Button, Color> baseCol = new System.Collections.Generic.Dictionary<Button, Color>(); Button hovB;
        // survol et appui : chaque bouton grossit, s'eclaire et s'enfonce au clic
        void Hover()
        {
            if (gr == null) return;
            Button over = null;
            try
            {
                var ped = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { position = Input.mousePosition };
                var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>(); gr.Raycast(ped, hits);
                foreach (var h in hits) { var b = h.gameObject != null ? h.gameObject.GetComponentInParent<Button>() : null; if (b != null && b.interactable) { over = b; break; } }
            }
            catch { }
            if (over != null && over != hovB) RangeGame.Snd(EFT.UI.EUISoundType.ButtonOver);
            hovB = over; bool down = Input.GetMouseButton(0); float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            foreach (var b in GetComponentsInChildren<Button>())
            {
                var img = b.GetComponent<Image>(); if (img == null) continue;
                if (cImg != null && Array.IndexOf(cImg, img) >= 0) continue;
                if (!baseCol.ContainsKey(b)) baseCol[b] = img.color;
                bool on = b == over; float target = on ? (down ? 0.96f : 1.045f) : 1f;
                var t = b.transform; float sc = Mathf.Lerp(t.localScale.x, target, dt * 18f); t.localScale = new Vector3(sc, sc, 1f);
                if ((selBg != null && selBg.Contains(img)) || (tabBg != null && System.Array.IndexOf(tabBg.ToArray(), img) >= 0)) continue;
                var bc = baseCol[b]; var hc = bc.grayscale > 0.5f ? new Color(1f, 0.93f, 0.72f, bc.a) : new Color(Mathf.Min(1, bc.r + 0.12f), Mathf.Min(1, bc.g + 0.14f), Mathf.Min(1, bc.b + 0.15f), Mathf.Max(bc.a, 0.9f));
                img.color = Color.Lerp(img.color, on ? hc : bc, dt * 14f);
            }
        }
        void Clicks()
        {
            if (gr == null || !Input.GetMouseButtonDown(0) || lastClickFrame == Time.frameCount) return;
            lastClickFrame = Time.frameCount;
            try
            {
                var ped = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { position = Input.mousePosition };
                var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                gr.Raycast(ped, hits);
                foreach (var h in hits)
                {
                    var b = h.gameObject != null ? h.gameObject.GetComponentInParent<Button>() : null;
                    if (b != null && b.interactable) { RangeGame.Snd(EFT.UI.EUISoundType.ButtonClick); b.onClick.Invoke(); return; }
                }
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[range day] clic : " + e.Message); }
        }
        void Build()
        {
            oldLock = Cursor.lockState; oldVis = Cursor.visible; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            Sprites();
            canvas = new GameObject("Canvas"); canvas.transform.SetParent(transform, false);
            var cv = canvas.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 32001;
            var sc = canvas.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            gr = canvas.AddComponent<GraphicRaycaster>();
            FreePlace.Img("Dim", canvas.transform, new Color(0f, 0f, 0f, 0.6f));
            // fenetre 1600 x 900, fond dessine
            var win = Pic("Window", canvas.transform, sBack, Color.white, Image.Type.Simple); win.raycastTarget = true;
            FreePlace.Rt(win.gameObject, canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-800, -450), new Vector2(800, 450));
            Frame(win.transform, new Color(0.62f, 0.84f, 0.81f, 0.16f));
            winRt = (RectTransform)win.transform; winCg = win.gameObject.AddComponent<CanvasGroup>(); winCg.alpha = 0f; winRt.localScale = Vector3.one * 0.96f;
            // grade : anneau dore qui se remplit + numero
            var rk = new GameObject("Rank"); FreePlace.Rt(rk, win.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -140), new Vector2(148, -36));
            Pic("RingBack", rk.transform, sRing, new Color(1, 1, 1, 0.08f), Image.Type.Simple);
            ring = Pic("Ring", rk.transform, sRing, GoldC, Image.Type.Filled); ring.fillMethod = Image.FillMethod.Radial360; ring.fillOrigin = 2; ring.fillClockwise = true; ring.fillAmount = 0f;
            halo = Pic("Halo", rk.transform, sGlow, new Color(0.85f, 0.71f, 0.42f, 0.18f), Image.Type.Simple); FreePlace.Rt(halo.gameObject, rk.transform, Vector2.zero, Vector2.one, new Vector2(-30, -30), new Vector2(30, 30)); halo.transform.SetAsFirstSibling();
            var lvlBg = Pic("LvlBg", rk.transform, sRound4, GoldC, Image.Type.Sliced); FreePlace.Rt(lvlBg.gameObject, rk.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-22, -12), new Vector2(22, 14));
            FreePlace.Txt("Lvl", lvlBg.transform, RangeGame.Rank().ToString(), 20, new Color(0.08f, 0.07f, 0.05f), TextAlignmentOptions.Center, true);
            var star = FreePlace.Txt("Star", rk.transform, "★", 40, new Color(0.85f, 0.71f, 0.42f, 0.9f), TextAlignmentOptions.Center, false);
            // identite
            var who = new GameObject("Who"); FreePlace.Rt(who, win.transform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(176, -146), new Vector2(0, -30));
            var l1 = FreePlace.Txt("Kicker", who.transform, "RANGE DAY  ·  PRESTIGE 0", 17, Teal, TextAlignmentOptions.TopLeft, false); FreePlace.Rt(l1.gameObject, who.transform, new Vector2(0, 0.72f), Vector2.one, Vector2.zero, Vector2.zero); l1.characterSpacing = 10;
            var l2 = FreePlace.Txt("Grade", who.transform, RangeGame.GradeName(), 60, Cream, TextAlignmentOptions.Left, true); FreePlace.Rt(l2.gameObject, who.transform, new Vector2(0, 0.24f), new Vector2(1, 0.82f), Vector2.zero, Vector2.zero); l2.enableAutoSizing = true; l2.fontSizeMin = 30; l2.fontSizeMax = 60;
            var l3 = FreePlace.Txt("Xp", who.transform, RangeGame.XpLine(), 19, Sub, TextAlignmentOptions.BottomLeft, false); FreePlace.Rt(l3.gameObject, who.transform, Vector2.zero, new Vector2(1, 0.24f), Vector2.zero, Vector2.zero);
            { int sk2 = RangeGame.Streak(); Stat(win.transform, 0, "SÉRIE", sk2 + (sk2 > 1 ? " jours" : " jour"), sk2 > 0 ? "+" + Math.Min(10, sk2) * 10 + " % XP" : "réussis un défi du jour", GoldC); }
            Stat(win.transform, 1, "AUJOURD'HUI", RangeGame.DoneLine(), "défis réussis", Cream);
            Stat(win.transform, 2, "NOUVEAUX DÉFIS", Countdown(), "dans", Cream);
            var close = Button(win.transform, "Close", "✕", new Vector2(1, 1), new Vector2(-92, -96), new Vector2(-40, -44), 24, Close);
            // barre d'XP lumineuse avec reflet qui glisse
            var bar = Pic("XpBar", win.transform, sRound4, new Color(1, 1, 1, 0.07f), Image.Type.Sliced); FreePlace.Rt(bar.gameObject, win.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(44, -176), new Vector2(-44, -170));
            var fill = Pic("Fill", bar.transform, sRound4, GoldC, Image.Type.Sliced); FreePlace.Rt(fill.gameObject, bar.transform, Vector2.zero, new Vector2(Mathf.Max(0.03f, RangeGame.Frac()), 1), Vector2.zero, Vector2.zero);
            var fg = Pic("FillGlow", bar.transform, sGlow, new Color(0.94f, 0.80f, 0.53f, 0.5f), Image.Type.Simple); FreePlace.Rt(fg.gameObject, bar.transform, Vector2.zero, new Vector2(Mathf.Max(0.03f, RangeGame.Frac()), 1), new Vector2(-14, -14), new Vector2(14, 14));
            var tm = new GameObject("SweepMask"); FreePlace.Rt(tm, bar.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); tm.AddComponent<RectMask2D>();
            var sw = Pic("Sweep", tm.transform, sShine, new Color(0.94f, 0.80f, 0.53f, 0.35f), Image.Type.Simple); sweep = (RectTransform)sw.transform; FreePlace.Rt(sw.gameObject, tm.transform, Vector2.zero, new Vector2(0, 1), new Vector2(-220, 0), Vector2.zero);
            var mask = new GameObject("ShineMask"); FreePlace.Rt(mask, fill.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); mask.AddComponent<RectMask2D>();
            var sh = Pic("Shine", mask.transform, sShine, new Color(1f, 0.95f, 0.8f, 0.55f), Image.Type.Simple); shine = (RectTransform)sh.transform; FreePlace.Rt(sh.gameObject, mask.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(-160, 0), new Vector2(0, 0));
            // onglets en pastilles
            float x0 = 44;
            for (int i = 0; i < Tabs.Length; i++)
            {
                int k = i; float w = 60 + Tabs[i].Length * 13.5f;
                var b = Pic("Tab" + i, win.transform, sPill, Color.clear, Image.Type.Sliced); b.raycastTarget = true;
                FreePlace.Rt(b.gameObject, win.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0, -246), new Vector2(x0 + w, -198)); x0 += w + 10;
                var bd = Pic("Edge", b.transform, sPill, new Color(1, 1, 1, 0.14f), Image.Type.Sliced); var inner = Pic("Inner", bd.transform, sPill, new Color(0.05f, 0.08f, 0.10f, 1f), Image.Type.Sliced); FreePlace.Rt(inner.gameObject, bd.transform, Vector2.zero, Vector2.one, new Vector2(1, 1), new Vector2(-1, -1));
                var t = FreePlace.Txt("Label", b.transform, Tabs[i], 19, Sub, TextAlignmentOptions.Center, true); t.characterSpacing = 4;
                var btn = b.gameObject.AddComponent<Button>(); btn.transition = Selectable.Transition.None; btn.onClick.AddListener(() => SetTab(k));
                tabBg.Add(b); tabTx.Add(t);
            }
            var live = FreePlace.Txt("Live", win.transform, (RangeGame.HasFlash() ? "DÉFI ÉCLAIR DISPONIBLE · onglet DÉFIS DU JOUR" : "Défi éclair possible pendant ta session"), 17, new Color(0.62f, 0.84f, 0.81f), TextAlignmentOptions.Right, false);
            FreePlace.Rt(live.gameObject, win.transform, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(0, -246), new Vector2(-44, -198));
            live.ForceMeshUpdate(); float lw = live.preferredWidth;
            liveDot = Pic("LiveDot", win.transform, sDot, new Color(0.37f, 0.88f, 0.69f, 1f), Image.Type.Simple);
            FreePlace.Rt(liveDot.gameObject, win.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-44 - lw - 30, -232), new Vector2(-44 - lw - 10, -212));
            // zone de contenu en verre teinte
            var content = Pic("Content", win.transform, sRound4, new Color(0.086f, 0.133f, 0.165f, 0.72f), Image.Type.Sliced); FreePlace.Rt(content.gameObject, win.transform, Vector2.zero, Vector2.one, new Vector2(44, 56), new Vector2(-44, -268));
            var cEdge = Pic("Edge", content.transform, sRound4, new Color(0.62f, 0.84f, 0.81f, 0.10f), Image.Type.Sliced); FreePlace.Rt(cEdge.gameObject, content.transform, new Vector2(0, 1), Vector2.one, new Vector2(0, -2), Vector2.zero);
            var bodyGo = new GameObject("BodyGroup"); bodyRt = FreePlace.Rt(bodyGo, content.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); bodyCg = bodyGo.AddComponent<CanvasGroup>();
            body = FreePlace.Txt("Body", bodyGo.transform, "", 30, Sub, TextAlignmentOptions.Center, false);
            var foot = FreePlace.Txt("Keys", win.transform, "<color=#e8e4d2>ÉCHAP</color>  fermer        <color=#e8e4d2>CLIC</color>  choisir", 17, Teal, TextAlignmentOptions.Right, false);
            FreePlace.Rt(foot.gameObject, win.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(44, 14), new Vector2(-44, 44));
            // onglet DEFIS DU JOUR (comme la maquette) : cartes a gauche, detail a droite, piste des rangs en bas
            dayPanel = new GameObject("Daily"); FreePlace.Rt(dayPanel, bodyGo.transform, Vector2.zero, Vector2.one, new Vector2(24, 16), new Vector2(-24, -16));
            {
                var list = RangeGame.Today(); dayCards.Clear(); dayEdges.Clear();
                var grid = new GameObject("Grid"); FreePlace.Rt(grid, dayPanel.transform, new Vector2(0, 0.2f), new Vector2(0.69f, 1f), Vector2.zero, Vector2.zero);
                var wk = list.FindIndex(x => x.Diff == 3); var others = Enumerable.Range(0, list.Count).Where(i => i != wk).ToList();
                float top = 1f; if (wk >= 0) top = 0.74f;
                int rowsN = Mathf.Max(1, (others.Count + 1) / 2); float rh = top / rowsN;
                var vt = MysteryCards.RandomVerso(); var vs = vt != null ? Sprite.Create(vt, new Rect(0, 0, vt.width, vt.height), new Vector2(0.5f, 0.5f)) : null;
                System.Action<int, Vector2, Vector2> card = (idx, a0, a1) =>
                {
                    var d = list[idx]; string done = RangeGame.DoneMedal(idx); bool week = d.Diff == 3;
                    Color dc = d.Diff == 0 ? new Color(0.62f, 0.77f, 0.48f) : d.Diff == 1 ? new Color(0.89f, 0.72f, 0.37f) : d.Diff == 2 ? new Color(0.94f, 0.56f, 0.43f) : d.Diff == 3 ? GoldC : new Color(0.37f, 0.88f, 0.69f);
                    if (d.Mystery) dc = new Color(0.73f, 0.64f, 0.92f);
                    var bg = Pic("D" + idx, grid.transform, sRound4, week ? new Color(0.20f, 0.16f, 0.08f, 0.95f) : new Color(0.086f, 0.133f, 0.165f, done != null ? 0.55f : 0.9f), Image.Type.Sliced); bg.raycastTarget = true;
                    FreePlace.Rt(bg.gameObject, grid.transform, a0, a1, new Vector2(6, 6), new Vector2(-6, -6));
                    var ed = Pic("E", bg.transform, sRound4, week ? new Color(0.85f, 0.71f, 0.42f, 0.5f) : new Color(0.62f, 0.84f, 0.81f, 0.10f), Image.Type.Sliced); FreePlace.Rt(ed.gameObject, bg.transform, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 2));
                    string lab = (d.Mystery ? "MYSTÈRE" : RangeGame.DiffDay[d.Diff]) + (week ? "" : "");
                    var dl1 = FreePlace.Txt("K", bg.transform, lab, 13, dc, TextAlignmentOptions.TopLeft, false); FreePlace.Rt(dl1.gameObject, bg.transform, Vector2.zero, Vector2.one, new Vector2(18, 10), new Vector2(-18, -12)); dl1.characterSpacing = 14;
                    var x1 = FreePlace.Txt("X", bg.transform, done != null ? "" : RangeGame.DayXp[d.Diff] + " XP", 14, Sub, TextAlignmentOptions.TopRight, false); FreePlace.Rt(x1.gameObject, bg.transform, Vector2.zero, Vector2.one, new Vector2(18, 10), new Vector2(-18, -12));
                    if (done != null) { var md = Pic("M", bg.transform, sDot, done == "OR" ? new Color(0.98f, 0.85f, 0.5f) : done == "ARGENT" ? new Color(0.85f, 0.87f, 0.9f) : new Color(0.85f, 0.6f, 0.4f), Image.Type.Simple); var mr = (RectTransform)md.transform; mr.anchorMin = mr.anchorMax = new Vector2(1, 1); mr.sizeDelta = new Vector2(22, 22); mr.anchoredPosition = new Vector2(-28, -22); }
                    string cons = string.Join(" ", d.Cons.Select(c => "<mark=#9fd6cf18> " + RangeGame.ConsLabel(c) + " </mark>").ToArray());
                    var t1 = FreePlace.Txt("T", bg.transform, "<size=" + (week ? 24 : 21) + ">" + RangeGame.Names[d.Mode] + "</size>\n<size=13><color=#c9c5b1>" + cons + "</color></size>", 21, Cream, TextAlignmentOptions.TopLeft, true);
                    FreePlace.Rt(t1.gameObject, bg.transform, Vector2.zero, Vector2.one, new Vector2(18, 10), new Vector2(week ? -170 : -18, -32)); t1.lineSpacing = 18;
                    string dfoot = done != null ? "<color=#9fc47a>Réussi · " + done.ToLowerInvariant() + (d.Mystery ? (done == "OR" ? " · cartes jouées" : "</color><color=#b9a3ea> · fais mieux pour rejouer les cartes") : " · récompense reçue") + "</color>" : d.Mystery ? "" : "<color=#8cc0f0>Récompense " + RangeGame.TierBase[d.Diff].ToLowerInvariant() + "</color>";
                    var f1 = FreePlace.Txt("F", bg.transform, dfoot, 14, Sub, week ? TextAlignmentOptions.Right : TextAlignmentOptions.BottomLeft, false); FreePlace.Rt(f1.gameObject, bg.transform, Vector2.zero, Vector2.one, new Vector2(18, 10), new Vector2(-18, -10));
                    if (week) { f1.text = "<size=13><color=#8a9aa6>RÉCOMPENSE</color></size>\n<size=22><color=#b9a3ea>3 CARTES</color></size>\n<size=14>1 000 XP</size>"; }
                    if (d.Mystery && done == null && vs != null)
                        for (int k = 0; k < 3; k++) { var tv = MysteryCards.RandomVerso(); var mi = Pic("V" + k, bg.transform, tv != null ? Sprite.Create(tv, new Rect(0, 0, tv.width, tv.height), new Vector2(0.5f, 0.5f)) : vs, Color.white, Image.Type.Simple); var mrt = (RectTransform)mi.transform; mrt.anchorMin = mrt.anchorMax = new Vector2(0, 0); mrt.pivot = new Vector2(0, 0); mrt.sizeDelta = new Vector2(30, 45); mrt.anchoredPosition = new Vector2(18 + k * 36, 10); }
                    int ci = idx; bg.gameObject.AddComponent<Button>().onClick.AddListener(() => SelectDay(ci));
                    var sel = Pic("S" + idx, grid.transform, sRound4, new Color(0.85f, 0.71f, 0.42f, 0f), Image.Type.Sliced); FreePlace.Rt(sel.gameObject, grid.transform, a0, a1, new Vector2(4, 4), new Vector2(-4, -4)); sel.transform.SetSiblingIndex(bg.transform.GetSiblingIndex());
                    dayCards[ci] = bg; dayEdges[ci] = sel;
                };
                if (wk >= 0) card(wk, new Vector2(0, 0.76f), new Vector2(1, 1));
                for (int j = 0; j < others.Count; j++) { int c = j % 2, r = j / 2; card(others[j], new Vector2(c * 0.5f, top - (r + 1) * rh), new Vector2((c + 1) * 0.5f, top - r * rh)); }
                // detail a droite
                var aside = Pic("Aside", dayPanel.transform, sRound4, new Color(0.08f, 0.12f, 0.15f, 0.95f), Image.Type.Sliced); FreePlace.Rt(aside.gameObject, dayPanel.transform, new Vector2(0.705f, 0.2f), new Vector2(1, 1), new Vector2(0, 6), new Vector2(0, -6));
                asideLbl = FreePlace.Txt("L", aside.transform, "", 13, GoldC, TextAlignmentOptions.TopLeft, false); FreePlace.Rt(asideLbl.gameObject, aside.transform, new Vector2(0, 0.9f), new Vector2(1, 1), new Vector2(22, 0), new Vector2(-22, -18)); asideLbl.characterSpacing = 14;
                asideTitle = FreePlace.Txt("T", aside.transform, "", 30, Cream, TextAlignmentOptions.TopLeft, true); FreePlace.Rt(asideTitle.gameObject, aside.transform, new Vector2(0, 0.72f), new Vector2(1, 0.9f), new Vector2(22, 0), new Vector2(-22, 0)); asideTitle.enableAutoSizing = true; asideTitle.fontSizeMin = 16; asideTitle.fontSizeMax = 30;
                asideDesc = FreePlace.Txt("D", aside.transform, "", 16, new Color(0.73f, 0.71f, 0.64f), TextAlignmentOptions.TopLeft, false); FreePlace.Rt(asideDesc.gameObject, aside.transform, new Vector2(0, 0.55f), new Vector2(1, 0.72f), new Vector2(22, 0), new Vector2(-22, 0));
                asideMedals = FreePlace.Txt("M", aside.transform, "", 17, Cream, TextAlignmentOptions.TopLeft, false); FreePlace.Rt(asideMedals.gameObject, aside.transform, new Vector2(0, 0.24f), new Vector2(1, 0.55f), new Vector2(22, 0), new Vector2(-22, 0)); asideMedals.lineSpacing = 40; asideMedals.overflowMode = TextOverflowModes.Overflow; asideMedals.enableWordWrapping = false;
                var dgo = Pic("Go", aside.transform, sRound4, new Color(0.90f, 0.87f, 0.78f), Image.Type.Sliced); dgo.raycastTarget = true; FreePlace.Rt(dgo.gameObject, aside.transform, new Vector2(0, 0), new Vector2(1, 0.2f), new Vector2(22, 18), new Vector2(-22, -4));
                asideGo = FreePlace.Txt("L", dgo.transform, "COMMENCER", 26, new Color(0.09f, 0.08f, 0.06f), TextAlignmentOptions.Center, true); asideGo.characterSpacing = 16;
                dgo.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (daySel >= 0) { int k = daySel; Close(); RangeGame.StartDaily(k); } });
                // piste des prochains rangs
                var tr = Pic("Track", dayPanel.transform, sRound4, new Color(0.06f, 0.10f, 0.13f, 0.8f), Image.Type.Sliced); FreePlace.Rt(tr.gameObject, dayPanel.transform, Vector2.zero, new Vector2(1, 0.17f), Vector2.zero, Vector2.zero);
                var tl = FreePlace.Txt("H", tr.transform, "<size=12><color=#7fa9a3>PROCHAINS RANGS</color></size>\nPiste de récompenses", 18, Cream, TextAlignmentOptions.Left, true); FreePlace.Rt(tl.gameObject, tr.transform, Vector2.zero, new Vector2(0.17f, 1), new Vector2(20, 0), Vector2.zero);
                int r0 = RangeGame.Rank();
                var ln = Pic("Ln", tr.transform, sRound4, new Color(1, 1, 1, 0.08f), Image.Type.Sliced); FreePlace.Rt(ln.gameObject, tr.transform, new Vector2(0.2f, 0.62f), new Vector2(0.97f, 0.62f), new Vector2(0, -1), new Vector2(0, 1));
                for (int i = 0; i < 6; i++)
                {
                    int drk = r0 + 1 + i; if (drk > 100) break; float x = 0.22f + i * 0.15f;
                    string rw; Color rc; RangeGame.RankReward(drk, out rw, out rc);
                    var nd = Pic("N" + i, tr.transform, sDot, i == 0 ? GoldC : new Color(0.08f, 0.13f, 0.16f), Image.Type.Simple); var nr = (RectTransform)nd.transform; nr.anchorMin = nr.anchorMax = new Vector2(x, 0.62f); nr.sizeDelta = new Vector2(44, 44);
                    var ring = Pic("R", nd.transform, sRing, i == 0 ? new Color(1, 1, 1, 0) : new Color(0.62f, 0.84f, 0.81f, 0.3f), Image.Type.Simple); FreePlace.Rt(ring.gameObject, nd.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                    var nt = FreePlace.Txt("T", nd.transform, rw == "?" ? "?" : drk.ToString(), 16, i == 0 ? new Color(0.08f, 0.07f, 0.05f) : Cream, TextAlignmentOptions.Center, true);
                    var lt = FreePlace.Txt("L", tr.transform, rw, 13, rc, TextAlignmentOptions.Top, false); FreePlace.Rt(lt.gameObject, tr.transform, new Vector2(x - 0.075f, 0f), new Vector2(x + 0.075f, 0.36f), Vector2.zero, Vector2.zero);
                }
                SelectDay(Enumerable.Range(0, list.Count).FirstOrDefault(i => RangeGame.DoneMedal(i) == null));
            }
            // onglet ARSENAL : une carte par arme (maitrise, records des 6 defis)
            progPanel = new GameObject("Arsenal"); FreePlace.Rt(progPanel, bodyGo.transform, Vector2.zero, Vector2.one, new Vector2(36, 24), new Vector2(-36, -24));
            {
                var rows = RangeGame.ArsenalRows();
                var hd = FreePlace.Txt("H", progPanel.transform, "ARSENAL  <size=60%><color=#8a9aa6>" + rows.Count + " ARME" + (rows.Count > 1 ? "S" : "") + "</color></size>", 30, Cream, TextAlignmentOptions.TopLeft, true); FreePlace.Rt(hd.gameObject, progPanel.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -50), Vector2.zero);
                if (rows.Count == 0) { var em = FreePlace.Txt("E", progPanel.transform, "Fais un défi pour ajouter ta première arme.", 24, Sub, TextAlignmentOptions.Center, false); FreePlace.Rt(em.gameObject, progPanel.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); }
                for (int i = 0; i < Math.Min(6, rows.Count); i++)
                {
                    var rw = rows[i]; int n; RangeGame.mastery.TryGetValue(rw[7], out n); int lvl = RangeGame.MLevel(n);
                    int cc = i % 3, rr = i / 3; float bx0 = cc / 3f, bx1 = (cc + 1) / 3f, by1 = 0.9f - rr * 0.45f, by0 = by1 - (rows.Count > 3 ? 0.43f : 0.85f);
                    var cd = Pic("W" + i, progPanel.transform, sRound4, new Color(0.086f, 0.133f, 0.165f, 0.9f), Image.Type.Sliced);
                    FreePlace.Rt(cd.gameObject, progPanel.transform, new Vector2(bx0, by0), new Vector2(bx1, by1), new Vector2(8, 0), new Vector2(-8, 0));
                    var ed = Pic("E", cd.transform, sRound4, lvl >= 10 ? GoldC : new Color(0.85f, 0.71f, 0.42f, 0.25f), Image.Type.Sliced); FreePlace.Rt(ed.gameObject, cd.transform, Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(5, 0));
                    try
                    {
                        EFT.InventoryLogic.Item it = null; var gw = Comfort.Common.Singleton<EFT.GameWorld>.Instance; var pl = gw != null ? gw.MainPlayer : null;
                        if (pl != null) it = pl.Inventory.GetPlayerItems(EFT.InventoryLogic.EPlayerItems.All).FirstOrDefault(x => x is EFT.InventoryLogic.Weapon && x.TemplateId.ToString() == rw[7]);
                        if (it != null)
                        {
                            var ic = EFT.UI.DragAndDrop.ItemViewFactory.LoadItemIcon(it, 1, false);
                            var wi = Pic("Icon", cd.transform, null, Color.white, Image.Type.Simple); wi.preserveAspect = true; wi.enabled = false;
                            FreePlace.Rt(wi.gameObject, cd.transform, new Vector2(0.15f, 0.03f), new Vector2(0.85f, 0.33f), Vector2.zero, Vector2.zero);
                            icons.Add(new System.Collections.Generic.KeyValuePair<Image, ItemIcon>(wi, ic));
                        }
                    }
                    catch (Exception e) { PostersPlugin.Log.LogWarning("[range day] icone arme : " + e.Message); }
                    var wn = FreePlace.Txt("N", cd.transform, rw[0].ToUpperInvariant(), 28, Cream, TextAlignmentOptions.TopLeft, true); FreePlace.Rt(wn.gameObject, cd.transform, new Vector2(0, 0.72f), new Vector2(0.62f, 1), new Vector2(22, 0), new Vector2(0, -14)); wn.enableAutoSizing = true; wn.fontSizeMin = 14; wn.fontSizeMax = 28;
                    var lv = FreePlace.Txt("L", cd.transform, "MAÎTRISE <size=150%><color=#f0cd86>" + lvl + "</color></size><color=#5f6d76>/10</color>", 15, Sub, TextAlignmentOptions.TopRight, true); FreePlace.Rt(lv.gameObject, cd.transform, new Vector2(0.62f, 0.72f), new Vector2(1, 1), Vector2.zero, new Vector2(-18, -14));
                    for (int k = 0; k < 10; k++) { var pip = Pic("P" + k, cd.transform, sRound4, k < lvl ? GoldC : new Color(1, 1, 1, 0.08f), Image.Type.Sliced); FreePlace.Rt(pip.gameObject, cd.transform, new Vector2(0.06f + k * 0.09f, 0.64f), new Vector2(0.13f + k * 0.09f, 0.68f), Vector2.zero, Vector2.zero); }
                    var sbx = new System.Text.StringBuilder();
                    for (int m = 0; m < 6; m++) sbx.Append("<color=#8a9aa6>" + RangeGame.Names[m] + "</color>  " + (rw[m + 1] != null ? "<color=#e8e2cf>" + rw[m + 1].Replace(" ★", "</color> <color=#f0cd86>★</color>") + "</color>" : "<color=#4d5a63>—</color>") + (m % 2 == 1 ? "\n" : "<pos=50%>"));
                    var st = FreePlace.Txt("R", cd.transform, sbx.ToString(), 21, Sub, TextAlignmentOptions.TopLeft, false); FreePlace.Rt(st.gameObject, cd.transform, new Vector2(0, 0.33f), new Vector2(1, 0.6f), new Vector2(22, 0), new Vector2(-12, -4)); st.lineSpacing = 34; st.enableAutoSizing = true; st.fontSizeMin = 12; st.fontSizeMax = 21;
                }
                if (rows.Count > 6) { var mo = FreePlace.Txt("M", progPanel.transform, "+ " + (rows.Count - 6) + " autres armes", 16, Sub, TextAlignmentOptions.BottomRight, false); FreePlace.Rt(mo.gameObject, progPanel.transform, Vector2.zero, new Vector2(1, 0.04f), Vector2.zero, Vector2.zero); }
            }
            // onglet SUCCES : vraies cartes
            succPanel = new GameObject("Succ"); FreePlace.Rt(succPanel, bodyGo.transform, Vector2.zero, Vector2.one, new Vector2(36, 24), new Vector2(-36, -24));
            var shd = FreePlace.Txt("H", succPanel.transform, "SUCCÈS SECRETS  <size=60%><color=#8a9aa6>" + RangeGame.AchCount() + " / 12 DÉBLOQUÉS</color></size>", 30, Cream, TextAlignmentOptions.TopLeft, true); FreePlace.Rt(shd.gameObject, succPanel.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -50), Vector2.zero);
            string[] hints = { "Le premier pas…", "Une question de précision…", "Plus vite que ton ombre…", "Pas une seule balle perdue…", "Quand la lumière s'éteint…", "Sans jamais viser…", "Fidèle au poste…", "Toujours plus d'armes…", "Tout l'or du stand…", "Ça tape fort…", "Une arme, une vie…", "Gauche, droite, milieu…" };
            for (int i = 0; i < 12; i++)
            {
                int c = i % 4, r = i / 4; float ax0 = c / 4f, ax1 = (c + 1) / 4f, ay1 = 1f - 0.1f - r * 0.3f, ay0 = ay1 - 0.27f;
                var card = Pic("C" + i, succPanel.transform, sRound4, new Color(0.086f, 0.133f, 0.165f, 0.9f), Image.Type.Sliced);
                FreePlace.Rt(card.gameObject, succPanel.transform, new Vector2(ax0, ay0), new Vector2(ax1, ay1), new Vector2(8, 0), new Vector2(-8, 0));
                var edge = Pic("E", card.transform, sRound4, new Color(0.85f, 0.71f, 0.42f, 0.18f), Image.Type.Sliced); FreePlace.Rt(edge.gameObject, card.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -4), Vector2.zero);
                bool got = RangeGame.ach.Contains(i); if (got) { card.color = new Color(0.16f, 0.14f, 0.09f, 0.95f); edge.color = GoldC; }
                var q = FreePlace.Txt("Q", card.transform, got ? "★" : "?", 64, new Color(0.30f, 0.36f, 0.40f), TextAlignmentOptions.Center, true); FreePlace.Rt(q.gameObject, card.transform, new Vector2(0, 0.35f), Vector2.one, Vector2.zero, Vector2.zero);
                var hn = FreePlace.Txt("N", card.transform, got ? "<color=#f0cd86>" + RangeGame.AchName[i] + "</color>\n" + RangeGame.AchDesc[i] : "<color=#5f6d76>VERROUILLÉ</color>\n<i>" + hints[i] + "</i>", 18, new Color(0.55f, 0.6f, 0.62f), TextAlignmentOptions.Center, false); FreePlace.Rt(hn.gameObject, card.transform, Vector2.zero, new Vector2(1, 0.4f), new Vector2(10, 8), new Vector2(-10, 0));
            }
            // onglet PERMANENTS : le Groupement
            perm = new GameObject("Perm"); FreePlace.Rt(perm, bodyGo.transform, Vector2.zero, Vector2.one, new Vector2(36, 30), new Vector2(-36, -30));
            for (int i = 0; i < 6; i++)
            {
                int k = i; var sb = Pic("Sel" + i, perm.transform, sPill, new Color(1, 1, 1, 0.06f), Image.Type.Sliced); sb.raycastTarget = true;
                FreePlace.Rt(sb.gameObject, perm.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(i * 205, -44), new Vector2(i * 205 + 195, 0));
                var st2 = FreePlace.Txt("L", sb.transform, RangeGame.Names[i], 18, Sub, TextAlignmentOptions.Center, true);
                sb.gameObject.AddComponent<Button>().onClick.AddListener(() => SelMode(k)); selBg.Add(sb); selTx.Add(st2);
            }
            var pk = FreePlace.Txt("Kicker", perm.transform, "DÉFI PERMANENT  ·  5 BALLES", 17, Teal, TextAlignmentOptions.TopLeft, false); FreePlace.Rt(pk.gameObject, perm.transform, new Vector2(0, 1), new Vector2(0.6f, 1), new Vector2(0, -90), new Vector2(0, -60)); pk.characterSpacing = 8; pKick = pk;
            var pt = FreePlace.Txt("Title", perm.transform, "GROUPEMENT", 56, Cream, TextAlignmentOptions.TopLeft, true); FreePlace.Rt(pt.gameObject, perm.transform, new Vector2(0, 1), new Vector2(0.6f, 1), new Vector2(0, -150), new Vector2(0, -90)); pTitle = pt;
            var pd = FreePlace.Txt("Desc", perm.transform, "Tire 5 balles sur une même cible : une de tes affiches ou une cible du stand. On mesure l'écart entre tes deux trous les plus éloignés, ramené à la distance (MOA). La première cible touchée devient la tienne ; une balle à côté rend le tir incomplet.", 22, Sub, TextAlignmentOptions.TopLeft, false);
            FreePlace.Rt(pd.gameObject, perm.transform, new Vector2(0, 1), new Vector2(0.6f, 1), new Vector2(0, -270), new Vector2(0, -160)); pd.enableWordWrapping = true; pDesc = pd;
            var med = new[] { "BRONZE  ≤ 16 MOA", "ARGENT  ≤ 9 MOA", "OR  ≤ 4 MOA" }; var mc = new[] { new Color(0.80f, 0.55f, 0.36f), new Color(0.80f, 0.82f, 0.84f), GoldC };
            for (int i = 0; i < 3; i++)
            {
                var m = Pic("Medal" + i, perm.transform, sRound4, new Color(1, 1, 1, 0.04f), Image.Type.Sliced); FreePlace.Rt(m.gameObject, perm.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(i * 250, -346), new Vector2(i * 250 + 236, -290));
                var dot = Pic("Dot", m.transform, sDot, mc[i], Image.Type.Simple); FreePlace.Rt(dot.gameObject, m.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, -11), new Vector2(36, 11));
                var mt = FreePlace.Txt("T", m.transform, med[i], 20, Cream, TextAlignmentOptions.Left, true); pMed.Add(mt); FreePlace.Rt(mt.gameObject, m.transform, Vector2.zero, Vector2.one, new Vector2(46, 0), new Vector2(-8, 0));
            }
            var note = FreePlace.Txt("Note", perm.transform, "Seuils pour fusils d'assaut et DMR. Pistolets ×3, pistolets-mitrailleurs et slugs ×2, fusils à verrou ÷1,5.", 17, Teal, TextAlignmentOptions.TopLeft, false);
            FreePlace.Rt(note.gameObject, perm.transform, new Vector2(0, 1), new Vector2(0.75f, 1), new Vector2(0, -386), new Vector2(0, -356)); note.enableWordWrapping = true; pNote = note;
            var recs = FreePlace.Txt("Records", perm.transform, RangeGame.RecordsText(0), 20, Cream, TextAlignmentOptions.TopLeft, false);
            FreePlace.Rt(recs.gameObject, perm.transform, new Vector2(0.64f, 0), new Vector2(1, 1), new Vector2(0, 110), new Vector2(0, -90)); recs.enableWordWrapping = true; recs.overflowMode = TextOverflowModes.Truncate; pRecs = recs;
            var go2 = Pic("Start", perm.transform, sRound4, new Color(0.90f, 0.87f, 0.78f), Image.Type.Sliced); go2.raycastTarget = true;
            FreePlace.Rt(go2.gameObject, perm.transform, new Vector2(0.64f, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 84));
            var gt = FreePlace.Txt("Label", go2.transform, "COMMENCER", 34, new Color(0.09f, 0.08f, 0.06f), TextAlignmentOptions.Center, true); gt.characterSpacing = 14;
            go2.gameObject.AddComponent<Button>().onClick.AddListener(() => { Close(); RangeGame.Start(selMode); });
            SelMode(0);
            SetTab(0);
            foreach (var tx in canvas.GetComponentsInChildren<TextMeshProUGUI>(true)) { tx.fontSize *= 0.72f; tx.fontSizeMin *= 0.72f; tx.fontSizeMax *= 0.72f; }
            PostersPlugin.Log.LogInfo("[range day] fenetre ouverte (police : " + (FreePlace.Bender() != null ? FreePlace.Bender().name : "par defaut") + ")");
        }
        static string Countdown() { var n = DateTime.Now; var t = n.Date.AddDays(1) - n; return t.Hours.ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00"); }
        void Stat(Transform win, int i, string k, string v, string u, Color vc)
        {
            float r = 112 + (2 - i) * 206;
            var box = Pic("Stat" + i, win, sRound4, new Color(0.06f, 0.10f, 0.125f, 0.75f), Image.Type.Sliced); FreePlace.Rt(box.gameObject, win, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-r - 192, -138), new Vector2(-r, -40));
            var e = Pic("Edge", box.transform, sRound4, new Color(0.62f, 0.84f, 0.81f, 0.14f), Image.Type.Sliced); FreePlace.Rt(e.gameObject, box.transform, new Vector2(0, 1), Vector2.one, new Vector2(0, -2), Vector2.zero);
            var a = FreePlace.Txt("K", box.transform, k, 14, Teal, TextAlignmentOptions.TopLeft, false); FreePlace.Rt(a.gameObject, box.transform, Vector2.zero, Vector2.one, new Vector2(16, 8), new Vector2(-12, -12)); a.characterSpacing = 8;
            var b = FreePlace.Txt(i == 2 ? "Timer" : "V", box.transform, v, 32, vc, TextAlignmentOptions.Left, true); FreePlace.Rt(b.gameObject, box.transform, Vector2.zero, Vector2.one, new Vector2(16, 2), new Vector2(-12, -4));
            if (u.Length > 0) { var c = FreePlace.Txt("U", box.transform, u, 15, Sub, TextAlignmentOptions.BottomLeft, false); FreePlace.Rt(c.gameObject, box.transform, Vector2.zero, Vector2.one, new Vector2(16, 8), new Vector2(-12, -8)); }
        }
        static void Frame(Transform t, Color c)
        {
            var top = FreePlace.Img("FrameT", t, c); FreePlace.Rt(top.gameObject, t, new Vector2(0, 1), Vector2.one, new Vector2(0, -1), Vector2.zero);
            var bot = FreePlace.Img("FrameB", t, c); FreePlace.Rt(bot.gameObject, t, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 1));
            var lf = FreePlace.Img("FrameL", t, c); FreePlace.Rt(lf.gameObject, t, Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(1, 0));
            var rt = FreePlace.Img("FrameR", t, c); FreePlace.Rt(rt.gameObject, t, new Vector2(1, 0), Vector2.one, new Vector2(-1, 0), Vector2.zero);
            foreach (var im in new[] { top, bot, lf, rt }) im.raycastTarget = false;
        }
        static Image Button(Transform parent, string name, string label, Vector2 anchor, Vector2 oMin, Vector2 oMax, float size, UnityEngine.Events.UnityAction act)
        {
            var b = FreePlace.Img(name, parent, new Color(0.06f, 0.10f, 0.125f, 0.8f)); b.sprite = sRound4; b.type = Image.Type.Sliced; FreePlace.Rt(b.gameObject, parent, anchor, anchor, oMin, oMax);
            FreePlace.Txt("Label", b.transform, label, size, Cream, TextAlignmentOptions.Center, false);
            var btn = b.gameObject.AddComponent<Button>(); btn.onClick.AddListener(act); return b;
        }
        void SetTab(int k)
        {
            if (k != tab || tabT >= 1f) tabT = 0f;
            tab = k;
            for (int i = 0; i < tabBg.Count; i++)
            {
                bool on = i == k;
                tabBg[i].color = on ? Cream : new Color(0, 0, 0, 0.01f);
                var edge = tabBg[i].transform.Find("Edge"); if (edge != null) edge.gameObject.SetActive(!on);
                tabTx[i].color = on ? new Color(0.08f, 0.08f, 0.06f) : Sub;
            }
            if (dayPanel != null) dayPanel.SetActive(k == 0);
            if (succPanel != null) succPanel.SetActive(k == 3);
            if (progPanel != null) progPanel.SetActive(k == 2);
            if (body != null) body.text = "";
            if (perm != null) perm.SetActive(k == 1);
        }
        float tick;
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            Hover(); Clicks(); if (closed) return; CardsTick(); DayTick();
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            // anneau qui se remplit a l'ouverture, reflet qui glisse sur la barre d'XP
            if (ring != null && ringT < 1f) { ringT = Mathf.Min(1f, ringT + Time.unscaledDeltaTime / 1.2f); ring.fillAmount = Mathf.SmoothStep(0f, 0.02f + 0.0f, ringT) + 0.0f; ring.fillAmount = Mathf.Lerp(0f, Mathf.Max(0.04f, RangeGame.Frac()), 1f - Mathf.Pow(1f - ringT, 3f)); }
            if (shine != null) { float w = ((RectTransform)shine.parent).rect.width; float x = Mathf.Repeat(Time.unscaledTime * 260f, w + 320f) - 160f; shine.anchoredPosition = new Vector2(x, 0); }
            float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
            // ouverture : fondu + leger zoom
            if (winCg != null && openT < 1f) { openT = Mathf.Min(1f, openT + dt / 0.28f); float e = 1f - Mathf.Pow(1f - openT, 3f); winCg.alpha = e; winRt.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, e); }
            // changement d'onglet : le contenu glisse en fondu
            if (bodyCg != null && tabT < 1f) { tabT = Mathf.Min(1f, tabT + dt / 0.22f); float e = 1f - Mathf.Pow(1f - tabT, 3f); bodyCg.alpha = e; bodyRt.anchoredPosition = new Vector2(0, -14f * (1f - e)); }
            // halo du grade qui respire, voyant qui pulse
            if (halo != null) halo.color = new Color(0.85f, 0.71f, 0.42f, 0.12f + 0.12f * (0.5f + 0.5f * Mathf.Sin(now * 1.8f)));
            if (liveDot != null) { float p = 0.5f + 0.5f * Mathf.Sin(now * 4f); liveDot.color = new Color(0.37f, 0.88f, 0.69f, 0.45f + 0.55f * p); liveDot.rectTransform.localScale = Vector3.one * (0.8f + 0.35f * p); }
            // reflet qui traverse toute la barre d'XP toutes les 4 s
            if (sweep != null) { float w = ((RectTransform)sweep.parent).rect.width; float c = Mathf.Repeat(now, 4f); sweep.anchoredPosition = new Vector2(c < 1.2f ? Mathf.Lerp(-220f, w + 220f, c / 1.2f) : -400f, 0); }
            // survol des onglets
            for (int i = 0; i < tabBg.Count; i++)
            {
                if (i == tab) continue;
                bool over = RectTransformUtility.RectangleContainsScreenPoint((RectTransform)tabBg[i].transform, Input.mousePosition, null);
                tabTx[i].color = Color.Lerp(tabTx[i].color, over ? Cream : Sub, dt * 12f);
                var ed = tabBg[i].transform.Find("Edge"); if (ed != null) { var im = ed.GetComponent<Image>(); if (im != null) im.color = Color.Lerp(im.color, over ? new Color(0.85f, 0.71f, 0.42f, 0.7f) : new Color(1, 1, 1, 0.14f), dt * 12f); }
            }
            if (Time.unscaledTime > tick)
            {
                tick = Time.unscaledTime + 1f;
                var t = canvas != null ? canvas.GetComponentsInChildren<TextMeshProUGUI>().FirstOrDefault(x => x.gameObject.name == "Timer") : null;
                if (t != null) t.text = Countdown();
            }
        }
        void Close()
        {
            if (closed) return;
            if (cTiers != null && cPick < 0) { cPick = new System.Random().Next(3); RangeGame.SendRewardPub(cTiers[cPick], pendLabel + " · carte mystère (au hasard)", cTpl[cPick]); }
            closed = true;
            Cursor.lockState = oldLock; Cursor.visible = oldVis;
            PostersPlugin.Log.LogInfo("[range day] fenetre fermee");
            blockUntil = Time.frameCount + 3;   // la touche Echap ne doit pas « traverser » vers le jeu
            me = null; Destroy(gameObject);
            if (cardQueue.Count > 0) new GameObject("HideoutPosters_NextCards").AddComponent<NextCards>();
        }
    }
    public class NextCards : MonoBehaviour { float t0; void Start() { t0 = Time.unscaledTime; } void Update() { if (Time.unscaledTime - t0 > 0.5f) { RangeDayUI.OpenNextCards(); Destroy(gameObject); } } }

    public class GlowCull : MonoBehaviour
    {
        public Light l; float next;
        void Update()
        {
            if (l == null || Time.time < next) return; next = Time.time + 0.5f;
            if (GetComponentInParent<LightsOff>() != null) { l.enabled = false; return; }
            var cam = Camera.main; l.enabled = cam != null && (cam.transform.position - transform.position).sqrMagnitude < 30f * 30f;
        }
    }

    // fait defiler les images d'un GIF ; ne travaille que quand l'affiche est a l'ecran
    public class GifPlayer : MonoBehaviour
    {
        Renderer r; PostersPlugin.Anim a; int i; float t;
        RenderTexture rt; Texture2D over; static Material blend; static bool blendTried;
        // trous sur un GIF : un calque (trous + anneaux) que la carte graphique pose sur chaque image du GIF
        internal static bool SetHoles(GameObject poster, System.Collections.Generic.List<Vector3> holes)
        {
            try
            {
                var g = poster.GetComponent<GifPlayer>(); if (g == null || g.a == null || g.a.Frames.Length == 0 || g.a.Frames[0] == null) return false;
                if (!blendTried) { blendTried = true; var sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default"); if (sh != null) blend = new Material(sh); PostersPlugin.Log.LogInfo("[impacts] calque GIF : " + (sh != null ? sh.name : "AUCUN shader")); }
                if (blend == null) return false;
                if (holes == null || holes.Count == 0) { g.Free(); g.Show(); return true; }
                if (BulletHoles.holeTex == null) BulletHoles.EnsureHoleTex();
                if (BulletHoles.holeTex == null) return false;
                var mf = poster.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return false;
                var f0 = g.a.Frames[0]; int W = f0.width, H = f0.height;
                var px = new Color32[W * H];
                var size = mf.sharedMesh.bounds.size;
                for (int h = 0; h < holes.Count; h++) BulletHoles.Stamp(px, W, H, holes[h], h, size, false, true);
                if (g.over == null || g.over.width != W || g.over.height != H) { if (g.over != null) Destroy(g.over); g.over = new Texture2D(W, H, TextureFormat.RGBA32, false); g.over.wrapMode = TextureWrapMode.Clamp; }
                g.over.SetPixels32(px); g.over.Apply(false);
                if (g.rt == null || g.rt.width != W || g.rt.height != H)
                {
                    if (g.rt != null) { g.rt.Release(); Destroy(g.rt); }
                    g.rt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { useMipMap = true, autoGenerateMips = true, wrapMode = TextureWrapMode.Clamp, anisoLevel = 16, name = "PosterGifHoles" };
                    g.rt.Create();
                }
                g.Show(); return true;
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[impacts] calque GIF : " + e.Message); return false; }
        }
        void Show()
        {
            var m = r != null ? r.sharedMaterial : null; if (m == null || a == null || a.Frames[i] == null) return;
            if (rt == null || over == null) { m.mainTexture = a.Frames[i]; return; }
            Graphics.Blit(a.Frames[i], rt); Graphics.Blit(over, rt, blend);
            if (m.mainTexture != rt) m.mainTexture = rt;
        }
        void Free() { if (rt != null) { rt.Release(); Destroy(rt); rt = null; } if (over != null) { Destroy(over); over = null; } }
        void OnDestroy() { Free(); }
        internal static void Attach(Renderer rr, string file)
        {
            if (rr == null || file == null || !file.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return;
            var an = PostersPlugin.AnimOf(file); if (an == null || an.Frames.Length < 2) return;
            var g = rr.gameObject.GetComponent<GifPlayer>() ?? rr.gameObject.AddComponent<GifPlayer>();
            g.r = rr; g.a = an; g.i = 0; g.t = UnityEngine.Random.value * an.Delays[0];
        }
        void Update()
        {
            if (r == null || a == null || !r.isVisible) return;
            t += Time.deltaTime; bool ch = false; int guard = 0;
            while (t >= a.Delays[i] && guard++ < 50) { t -= a.Delays[i]; i = (i + 1) % a.Frames.Length; ch = true; }
            if (!ch) return;
            Show();
        }
    }

    // lecteur GIF minimal (GIF87a/89a, LZW, entrelacement, transparence, modes d'effacement)
    static class GifDecoder
    {
        internal static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Color32[], float>> Decode(byte[] b, out int W, out int H)
        {
            var res = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Color32[], float>>();
            int p = 6; W = b[p] | b[p + 1] << 8; H = b[p + 2] | b[p + 3] << 8; int flags = b[p + 4]; int bgIdx = b[p + 5]; p += 7;
            Color32[] gct = null;
            if ((flags & 0x80) != 0) { int n = 1 << ((flags & 7) + 1); gct = ReadCT(b, p, n); p += n * 3; }
            var canvas = new Color32[W * H]; Color32[] saved = null;
            int delay = 10, trans = -1, disposal = 0;
            while (p < b.Length)
            {
                int blk = b[p++];
                if (blk == 0x3B) break;
                if (blk == 0x21)
                {
                    int lab = b[p++];
                    if (lab == 0xF9 && b[p] >= 4) { int pf = b[p + 1]; disposal = (pf >> 2) & 7; delay = b[p + 2] | b[p + 3] << 8; trans = (pf & 1) != 0 ? b[p + 4] : -1; }
                    while (p < b.Length) { int sz = b[p++]; if (sz == 0) break; p += sz; }
                    continue;
                }
                if (blk != 0x2C) break;
                int fx = b[p] | b[p + 1] << 8, fy = b[p + 2] | b[p + 3] << 8, fw = b[p + 4] | b[p + 5] << 8, fh = b[p + 6] | b[p + 7] << 8, ff = b[p + 8]; p += 9;
                var ct = gct;
                if ((ff & 0x80) != 0) { int n = 1 << ((ff & 7) + 1); ct = ReadCT(b, p, n); p += n * 3; }
                bool inter = (ff & 0x40) != 0;
                int minCode = b[p++];
                var data = new System.Collections.Generic.List<byte>();
                while (p < b.Length) { int sz = b[p++]; if (sz == 0) break; for (int k = 0; k < sz && p + k < b.Length; k++) data.Add(b[p + k]); p += sz; }
                var idx = Lzw(data, minCode, fw * fh);
                if (disposal == 3) saved = (Color32[])canvas.Clone();
                if (ct != null)
                {
                    int row = 0, pass = 0, inc = 8;
                    for (int y = 0; y < fh; y++)
                    {
                        int ry = y;
                        if (inter) { ry = row; row += inc; while (row >= fh && pass < 3) { pass++; row = pass == 1 ? 4 : pass == 2 ? 2 : 1; inc = pass == 1 ? 8 : pass == 2 ? 4 : 2; } }
                        int cy = fy + ry; if (cy < 0 || cy >= H) continue;
                        for (int x = 0; x < fw; x++)
                        {
                            int cx = fx + x; if (cx < 0 || cx >= W) continue;
                            int k = y * fw + x; if (k >= idx.Length) continue;
                            int ci = idx[k]; if (ci == trans || ci >= ct.Length) continue;
                            canvas[(H - 1 - cy) * W + cx] = ct[ci];   // GIF : haut en bas ; Unity : bas en haut
                        }
                    }
                }
                res.Add(new System.Collections.Generic.KeyValuePair<Color32[], float>((Color32[])canvas.Clone(), (delay <= 1 ? 10 : delay) / 100f));
                if (disposal == 2)
                {
                    var bg = trans >= 0 ? new Color32(0, 0, 0, 0) : gct != null && bgIdx < gct.Length ? gct[bgIdx] : new Color32(0, 0, 0, 255);
                    for (int y = fy; y < fy + fh && y < H; y++) for (int x = fx; x < fx + fw && x < W; x++) canvas[(H - 1 - y) * W + x] = bg;
                }
                else if (disposal == 3 && saved != null) canvas = saved;
                trans = -1; disposal = 0; delay = 10;
                if (res.Count >= 300) break;
            }
            return res;
        }
        static Color32[] ReadCT(byte[] b, int p, int n) { var c = new Color32[n]; for (int i = 0; i < n; i++) c[i] = new Color32(b[p + i * 3], b[p + i * 3 + 1], b[p + i * 3 + 2], 255); return c; }
        static int[] Lzw(System.Collections.Generic.List<byte> data, int minCode, int count)
        {
            var outp = new int[count]; int op = 0;
            int clear = 1 << minCode, eoi = clear + 1, size = minCode + 1, next = eoi + 1;
            var prefix = new int[4096]; var suffix = new byte[4096]; var stack = new byte[4097];
            for (int i = 0; i < clear; i++) { prefix[i] = -1; suffix[i] = (byte)i; }
            int old = -1, first = 0, bits = 0, acc = 0, dp = 0;
            while (op < count)
            {
                while (bits < size) { if (dp >= data.Count) return outp; acc |= data[dp++] << bits; bits += 8; }
                int code = acc & ((1 << size) - 1); acc >>= size; bits -= size;
                if (code == clear) { size = minCode + 1; next = eoi + 1; old = -1; continue; }
                if (code == eoi) break;
                if (old == -1) { if (code >= clear) break; outp[op++] = code; old = code; first = code; continue; }
                int c = code, sp = 0;
                if (code >= next) { stack[sp++] = (byte)first; c = old; }
                while (c >= clear && sp < 4096) { stack[sp++] = suffix[c]; c = prefix[c]; }
                first = c; stack[sp++] = (byte)c;
                while (sp > 0 && op < count) outp[op++] = stack[--sp];
                if (next < 4096) { prefix[next] = old; suffix[next] = (byte)first; next++; if (next == (1 << size) && size < 12) size++; }
                old = code;
            }
            return outp;
        }
    }

    public class BtnFade : MonoBehaviour
    {
        public Image bg; public TextMeshProUGUI tx; public float v, target; public bool dim;
        static readonly Color Light = new Color(0.82f, 0.81f, 0.76f), Dark = new Color(0.07f, 0.07f, 0.07f), DimC = new Color(0.4f, 0.4f, 0.38f);
        void Update() { if (Mathf.Approximately(v, target)) return; v = Mathf.MoveTowards(v, target, Time.unscaledDeltaTime * 7f); Apply(); }
        public void Apply()
        {
            if (bg == null) return;
            var sp = FreePlace.TkBtn();
            bg.sprite = sp; bg.type = Image.Type.Simple;
            float e = v * v * (3f - 2f * v);
            bg.color = sp != null ? new Color(1f, 1f, 1f, e) : new Color(0.78f, 0.77f, 0.71f, e);
            if (tx != null) tx.color = dim ? DimC : Color.Lerp(Light, Dark, e);
            bg.transform.localScale = Vector3.one * (0.97f + 0.03f * e);
        }
    }

    public class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action enter, exit;
        public void OnPointerEnter(PointerEventData e) { if (enter != null) enter(); }
        public void OnPointerExit(PointerEventData e) { if (exit != null) exit(); }
    }

    // ================= DEFI MYSTERE : 3 cartes face cachee sur les cibles sur pied, on tire sur celle qu'on veut =================
    public class MysteryCards : MonoBehaviour
    {
        static readonly string[] TN = { "ABSURDE", "COMMUN", "PEU COMMUN", "RARE", "ÉPIQUE", "LÉGENDAIRE" };
        static readonly Color[] TC = { new Color(0.55f, 0.5f, 0.45f), new Color(0.75f, 0.75f, 0.72f), new Color(0.45f, 0.75f, 0.4f), new Color(0.35f, 0.6f, 0.95f), new Color(0.7f, 0.4f, 0.95f), new Color(0.98f, 0.72f, 0.25f) };
        static MysteryCards me; static Texture2D verso; static Material mat;
        GameObject[] cards = new GameObject[3]; Renderer[] rends = new Renderer[3]; TextMeshPro[] texts = new TextMeshPro[3]; Vector3[] centers = new Vector3[3];
        int[] tier = new int[3]; float[] flipAt = { -1, -1, -1 }; bool[] shown = new bool[3]; int picked = -1; float born, pickedT; string label;
        static readonly System.Collections.Generic.Dictionary<int, Texture2D> versos = new System.Collections.Generic.Dictionary<int, Texture2D>();
        // plusieurs dos de cartes : un au hasard a chaque tirage
        static readonly System.Collections.Generic.List<int> bag = new System.Collections.Generic.List<int>();
        internal static Texture2D RandomVerso()
        {
            int n = typeof(MysteryCards).Assembly.GetManifestResourceNames().Count(x => x.StartsWith("rd_verso"));
            // on pioche sans remise : jamais deux fois le meme avant d'avoir vu tous les autres
            if (bag.Count == 0) { for (int q = 1; q <= Math.Max(1, n); q++) bag.Add(q); }
            int pick = UnityEngine.Random.Range(0, bag.Count), k = bag[pick]; bag.RemoveAt(pick); Texture2D t;
            if (versos.TryGetValue(k, out t)) return t;
            try
            {
                using (var st = typeof(MysteryCards).Assembly.GetManifestResourceStream(k == 1 ? "rd_verso.jpg" : "rd_verso" + k + ".jpg"))
                {
                    if (st == null) return Verso();
                    var b = new byte[st.Length]; st.Read(b, 0, b.Length);
                    t = new Texture2D(2, 2, TextureFormat.RGB24, true); ImageConversion.LoadImage(t, b); versos[k] = t; return t;
                }
            }
            catch { return Verso(); }
        }
        internal static Texture2D Verso()
        {
            if (verso != null) return verso;
            try
            {
                using (var st = typeof(MysteryCards).Assembly.GetManifestResourceStream("rd_verso.jpg"))
                {
                    var b = new byte[st.Length]; st.Read(b, 0, b.Length);
                    verso = new Texture2D(2, 2, TextureFormat.RGB24, true); ImageConversion.LoadImage(verso, b);
                }
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[range day] verso : " + e.Message); }
            return verso;
        }
        internal static bool Spawn(int baseTier, string lbl)
        {
            try
            {
                var st = RangeGame.Stands(); var cam = Camera.main; if (st == null || st.Count < 3 || cam == null) return false;
                if (me != null) Destroy(me.gameObject);
                var go = new GameObject("HideoutPosters_MysteryCards"); me = go.AddComponent<MysteryCards>(); me.label = lbl; me.born = Time.unscaledTime;
                if (mat == null) { var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Texture"); mat = new Material(sh); }
                var r = new System.Random();
                for (int i = 0; i < 3; i++)
                {
                    int roll = r.Next(100), d = roll < 15 ? -2 : roll < 40 ? -1 : roll < 70 ? 0 : roll < 90 ? 1 : roll < 98 ? 2 : 3;
                    me.tier[i] = Mathf.Clamp(baseTier + d, 0, 5);
                    Bounds bb = new Bounds(st[i].position + Vector3.up * 1.2f, Vector3.one * 0.5f); bool has = false;
                    foreach (var rr in st[i].GetComponentsInChildren<Renderer>()) { if (!rr.enabled) continue; if (!has) { bb = rr.bounds; has = true; } else bb.Encapsulate(rr.bounds); }
                    var toCam = cam.transform.position - bb.center; toCam.y = 0; toCam.Normalize();
                    float h = Mathf.Clamp(bb.size.y * 0.45f, 0.35f, 0.8f);
                    var c = GameObject.CreatePrimitive(PrimitiveType.Quad); c.name = "RD_Card" + i; var col = c.GetComponent<Collider>(); if (col != null) Destroy(col);
                    c.transform.SetParent(go.transform, false);
                    c.transform.position = bb.center + toCam * (bb.extents.magnitude * 0.15f + 0.05f) + Vector3.up * bb.size.y * 0.1f;
                    c.transform.rotation = Quaternion.LookRotation(-toCam, Vector3.up);
                    c.transform.localScale = new Vector3(h / 1.5f, h, 1f);
                    var rend = c.GetComponent<Renderer>(); rend.material = new Material(mat); rend.material.mainTexture = Verso(); rend.material.color = Color.white;
                    var tgo = new GameObject("T"); tgo.transform.SetParent(c.transform, false); tgo.transform.localPosition = new Vector3(0, 0, -0.01f);
                    var tx = tgo.AddComponent<TextMeshPro>(); var f = FreePlace.Bender(); if (f != null) tx.font = f;
                    tx.rectTransform.sizeDelta = new Vector2(0.9f, 0.9f); tx.alignment = TextAlignmentOptions.Center; tx.enableAutoSizing = true; tx.fontSizeMin = 0.05f; tx.fontSizeMax = 2f; tx.text = ""; tx.color = Color.white;
                    tgo.transform.localScale = new Vector3(1f, 1f / 1.5f, 1f);
                    me.cards[i] = c; me.rends[i] = rend; me.texts[i] = tx; me.centers[i] = bb.center;
                }
                PostersPlugin.Log.LogInfo("[range day] defi mystere : cartes " + string.Join(" / ", me.tier.Select(t => TN[t]).ToArray()));
                return true;
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[range day] cartes : " + e.Message); return false; }
        }
        internal static bool TryPick(Transform hitT, Vector3 world)
        {
            if (me == null || me.picked >= 0 || hitT == null) return false;
            int best = -1; float bd = 1.0f;
            for (int i = 0; i < 3; i++) { float d = (me.centers[i] - world).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            if (best < 0) return false;
            me.picked = best; me.pickedT = Time.unscaledTime; me.flipAt[best] = Time.unscaledTime;
            for (int i = 0; i < 3; i++) if (i != best) me.flipAt[i] = Time.unscaledTime + 1.6f + i * 0.3f;
            RangeGame.SendRewardPub(me.tier[best], me.label + " · carte mystère");
            RangeGame.ShowPub("<color=#" + ColorUtility.ToHtmlStringRGB(TC[me.tier[best]]) + ">" + TN[me.tier[best]] + "</color>", "Ta carte · récompense dans tes messages", 7f);
            PostersPlugin.Log.LogInfo("[range day] carte choisie " + (best + 1) + " : " + TN[me.tier[best]]);
            return true;
        }
        void Update()
        {
            float now = Time.unscaledTime;
            if (picked < 0 && now - born > 120f) { Destroy(gameObject); return; }
            if (picked >= 0 && now - pickedT > 14f) { Destroy(gameObject); return; }
            for (int i = 0; i < 3; i++)
            {
                if (cards[i] == null) continue;
                var sc = cards[i].transform.localScale; float w0 = Mathf.Abs(sc.y) / 1.5f;
                if (flipAt[i] < 0 || now < flipAt[i]) { if (picked < 0) cards[i].transform.localScale = new Vector3(w0 * (1f + 0.03f * Mathf.Sin(now * 3f + i)), sc.y, 1f); continue; }
                float t = Mathf.Clamp01((now - flipAt[i]) / 0.5f);
                cards[i].transform.localScale = new Vector3(w0 * Mathf.Abs(Mathf.Cos(t * Mathf.PI)), sc.y, 1f);
                if (t >= 0.5f && !shown[i])
                {
                    shown[i] = true; rends[i].material.mainTexture = Texture2D.whiteTexture;
                    var c = TC[tier[i]]; rends[i].material.color = i == picked ? c : Color.Lerp(c, new Color(0.15f, 0.15f, 0.15f), 0.55f);
                    texts[i].text = TN[tier[i]] + (i == picked ? "\n<size=60%>TA CARTE</size>" : "\n<size=60%>raté</size>"); texts[i].color = i == picked ? new Color(0.08f, 0.07f, 0.05f) : new Color(0.85f, 0.85f, 0.85f);
                }
            }
        }
    }

    // ================= traduction de Range Day : le texte est ecrit en francais puis traduit a l'affichage =================
    public class RDTranslate : MonoBehaviour
    {
        static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<System.Text.RegularExpressions.Regex, string>> table;
        static readonly System.Collections.Generic.Dictionary<string, string> cache = new System.Collections.Generic.Dictionary<string, string>();
        readonly System.Collections.Generic.Dictionary<TMP_Text, string> done = new System.Collections.Generic.Dictionary<TMP_Text, string>(); float next;
        internal static readonly string[] Codes = { "fr", "en", "de", "es", "it", "pt", "pl", "ru", "cs", "tr", "hu", "ro", "sk", "zh", "ja", "ko" };
        internal static readonly string[] LangNames = { "Auto", "Français", "English", "Deutsch", "Español", "Italiano", "Português", "Polski", "Русский", "Čeština", "Türkçe", "Magyar", "Română", "Slovenčina", "中文", "日本語", "한국어" };
        static string loaded;
        internal static string Code
        {
            get
            {
                var v = RangeTV.cLang != null ? RangeTV.cLang.Value : "Auto"; int i = Array.IndexOf(LangNames, v);
                if (i > 0) return Codes[i - 1]; if (v == "English") return "en";
                string c = "en"; try { c = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName; } catch { }
                if (Array.IndexOf(Codes, c) < 0) c = "en";
                if (typeof(RDTranslate).Assembly.GetManifestResourceInfo("rd_" + c + ".txt") == null && c != "fr") c = "en";
                return c;
            }
        }
        internal static bool En { get { return Code != "fr"; } }
        static void Load()
        {
            table = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<System.Text.RegularExpressions.Regex, string>>();
            try
            {
                loaded = Code; var st0 = typeof(RDTranslate).Assembly.GetManifestResourceStream("rd_" + loaded + ".txt") ?? typeof(RDTranslate).Assembly.GetManifestResourceStream("rd_en.txt");
                using (var st = st0) using (var rd = new StreamReader(st, System.Text.Encoding.UTF8))
                {
                    var pairs = new System.Collections.Generic.List<string[]>(); string line;
                    while ((line = rd.ReadLine()) != null) { int k = line.IndexOf('='); if (k > 0) pairs.Add(new[] { line.Substring(0, k), line.Substring(k + 1) }); }
                    foreach (var pr in pairs.OrderByDescending(x => x[0].Length))
                        table.Add(new System.Collections.Generic.KeyValuePair<System.Text.RegularExpressions.Regex, string>(new System.Text.RegularExpressions.Regex("(?<![\\p{L}])" + System.Text.RegularExpressions.Regex.Escape(pr[0]) + "(?![\\p{L}])", System.Text.RegularExpressions.RegexOptions.CultureInvariant), pr[1]));
                }
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[range day] traduction : " + e.Message); }
        }
        // le MOA devient un ecart en cm ramene a 100 m, plus parlant (1 MOA = 2,9 cm a 100 m)
        static readonly System.Text.RegularExpressions.Regex moa = new System.Text.RegularExpressions.Regex("(\\d+(?:[.,]\\d+)?)\\s*MOA");
        static string Moa(string s)
        {
            if (s.IndexOf("MOA", StringComparison.Ordinal) < 0) return s;
            s = moa.Replace(s, m => { float v; if (!float.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return m.Value; float cm = v * 2.91f; return (cm < 10f ? cm.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') : cm.ToString("0")) + " cm"; });
            return s.Replace("(MOA)", "(100 m)").Replace("MOA", "cm");
        }
        internal static string Tr(string s)
        {
            if (string.IsNullOrEmpty(s)) return s; if (!En) return Moa(s);
            if (table == null || loaded != Code) { cache.Clear(); Load(); }
            string o; if (cache.TryGetValue(s, out o)) return o;
            o = s; foreach (var kv in table) o = kv.Key.Replace(o, kv.Value.Replace("$", "$$"));
            var cd = Code; if (cd == "en" || cd == "zh" || cd == "ja" || cd == "ko") o = System.Text.RegularExpressions.Regex.Replace(o, "(\\d),(\\d)", "$1.$2");
            o = Moa(o);
            if (cache.Count > 4000) cache.Clear(); cache[s] = o; return o;
        }
        static readonly System.Collections.Generic.HashSet<TMP_FontAsset> cjkDone = new System.Collections.Generic.HashSet<TMP_FontAsset>(); static TMP_FontAsset[] cjkFonts; static string cjkFor;
        // chinois / japonais / coreen : on ajoute en secours les polices du jeu qui ont ces caracteres
        static void Cjk(TMP_FontAsset f, string code)
        {
            try
            {
                if (f == null) return;
                if (cjkFonts == null || cjkFor != code)
                {
                    cjkFor = code; cjkDone.Clear();
                    string want = code == "ja" ? "japan" : code == "ko" ? "kore" : "chin";
                    var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                    cjkFonts = all.Where(x => x != null && x.name.ToLowerInvariant().Contains(want)).Concat(all.Where(x => x != null && (x.name.ToLowerInvariant().Contains("cjk") || x.name.ToLowerInvariant().Contains("noto")))).Distinct().ToArray();
                    PostersPlugin.Log.LogInfo("[range day] polices " + code + " : " + string.Join(", ", cjkFonts.Select(x => x.name).ToArray()));
                }
                if (!cjkDone.Add(f)) return;
                if (f.fallbackFontAssetTable == null) f.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
                foreach (var c in cjkFonts) if (c != f && !f.fallbackFontAssetTable.Contains(c)) f.fallbackFontAssetTable.Insert(0, c);
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[range day] police cjk : " + e.Message); }
        }
        void LateUpdate()
        {
            if (Time.unscaledTime < next) return; next = Time.unscaledTime + 0.1f;
            var cdc = Code; bool cjk = cdc == "zh" || cdc == "ja" || cdc == "ko";
            foreach (var t in GetComponentsInChildren<TMP_Text>(true))
            {
                if (cjk) Cjk(t.font, cdc);
                string last; var cur = t.text; if (done.TryGetValue(t, out last) && last == cur) continue;
                var tr = Tr(cur); if (tr != cur) t.text = tr; done[t] = tr;
            }
        }
    }
}