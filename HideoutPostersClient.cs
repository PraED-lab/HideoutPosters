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

namespace PraEDLab.HideoutPosters
{
    // 0.2.0 : remplace l'image du poster PraED par une image du dossier BepInEx/config/HideoutPosters
    [BepInPlugin("praedlab.hideoutposters", "Hideout Posters", "0.4.1")]
    public class PostersPlugin : BaseUnityPlugin
    {
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
        static void BuildMap()
        {
            tplFile.Clear();
            var files = Directory.GetFiles(Folder).Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count > 0) tplFile[PosterTpl] = files[0];   // l'ancien poster unique garde la premiere image
            foreach (var f in files)
            {
                tplFile[IdFor(Path.GetFileNameWithoutExtension(f), '1')] = f;
            }
            foreach (var f in files)
            {
                if (fileSize.ContainsKey(f)) continue;
                var stem = Path.GetFileNameWithoutExtension(f);
                // F12 refuse certains caracteres dans les noms de reglages : on les remplace
                var key = new string(stem.Select(ch => "=\n\t\\\"'[]".IndexOf(ch) >= 0 || char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();
                if (key.Length == 0) key = "affiche";
                BepInEx.Configuration.ConfigEntry<float> e;
                try { e = Inst.Config.Bind("Size per poster", key, 1f, new BepInEx.Configuration.ConfigDescription("Size of the poster " + stem + " (1 = normal)", new BepInEx.Configuration.AcceptableValueRange<float>(0.25f, 3f)));
                }
                catch (Exception ex) { Log.LogWarning("[poster] reglage de taille impossible pour " + stem + " : " + ex.Message); continue; }
                var file = f;
                e.SettingChanged += (a, b) => { RebuildFile(file); Log.LogInfo("[poster] taille de " + stem + " : x" + e.Value.ToString("0.00")); };
                fileSize[f] = e;
            }
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
        static readonly System.Collections.Generic.Dictionary<string, BepInEx.Configuration.ConfigEntry<float>> fileSize = new System.Collections.Generic.Dictionary<string, BepInEx.Configuration.ConfigEntry<float>>(StringComparer.OrdinalIgnoreCase);
        internal static float ScaleFor(string file)
        {
            float g = Size != null ? Size.Value : 1f;
            BepInEx.Configuration.ConfigEntry<float> e;
            if (file != null && fileSize.TryGetValue(file, out e)) g *= e.Value;
            return g;
        }
        internal static BepInEx.Configuration.ConfigEntry<bool> Diag;
        internal static readonly System.Collections.Generic.List<PosterKeeper> Keepers = new System.Collections.Generic.List<PosterKeeper>();
        static string Folder { get { return Path.Combine(BepInEx.Paths.ConfigPath, "HideoutPosters"); } }

        void Awake()
        {
            Inst = this; Log = Logger;
            try { gameObject.AddComponent<FreePlace>(); } catch (Exception e) { Logger.LogWarning("[pose] " + e.Message); }
            Size = Config.Bind("Poster", "Size", 1f, new BepInEx.Configuration.ConfigDescription("Size of ALL posters (1 = original size). Each poster also has its own setting in « Size per poster »", new BepInEx.Configuration.AcceptableValueRange<float>(0.25f, 3f)));
            Diag = Config.Bind("Poster", "Bug report", false, "Click when a poster looks wrong: writes its full state to BepInEx/LogOutput.log (send that file with your bug report)");
            Diag.SettingChanged += (a, b) => { if (Diag.Value) { Snapshot("demande F12"); Diag.Value = false; } };
            Size.SettingChanged += (a, b) => { RebuildFile(null); Log.LogInfo("[poster] nouvelle taille : x" + Size.Value.ToString("0.00")); };
            try { Directory.CreateDirectory(Folder); } catch { }
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
                Log.LogInfo("Hideout Posters 0.4.1 charge (dossier d'images : " + Folder + ")");
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

        internal static Texture2D ReloadImage(string file) { texs.Remove(file); return LoadImage(file); }
        internal static Texture2D LoadImage(string file)
        {
            Texture2D cached;
            if (texs.TryGetValue(file, out cached) && cached != null) return cached;
            if (!File.Exists(file)) { Log.LogWarning("[poster] image introuvable : " + file); return null; }
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!ImageConversion.LoadImage(t, File.ReadAllBytes(file))) { Log.LogWarning("[poster] image illisible : " + file); return null; }
            t.wrapMode = TextureWrapMode.Clamp; t.anisoLevel = 4;
            // le shader du poster utilise la transparence comme brillance : on rend l'image mate (papier), sinon elle reflete tout en blanc
            try
            {
                // un JPG est charge sans couche de transparence : on recopie l'image dans une texture qui en a une
                var px = t.GetPixels32();
                for (int i = 0; i < px.Length; i++) px[i].a = 8;
                var t2 = new Texture2D(t.width, t.height, TextureFormat.RGBA32, true);
                t2.SetPixels32(px); t2.Apply(true);
                t2.wrapMode = TextureWrapMode.Clamp; t2.anisoLevel = 4;
                Log.LogInfo("[poster] format d'origine " + t.format + " -> RGBA32 mat");
                UnityEngine.Object.Destroy(t); t = t2;
            }
            catch (Exception e) { Log.LogWarning("[poster] brillance : " + e.Message); }
            Log.LogInfo("[poster] image chargee : " + Path.GetFileName(file) + " (" + t.width + "x" + t.height + ")");
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
                if (mat.mainTexture != img) { Note("le materiau avait change (" + (mat.mainTexture != null ? mat.mainTexture.name : "aucune texture") + ") : image remise"); mat.mainTexture = img; mat.mainTextureScale = Vector2.one; mat.mainTextureOffset = Vector2.zero; }
            }
            catch (System.Exception e) { if (fixes < 20) { fixes++; PostersPlugin.Log.LogWarning("[poster] gardien : " + e.Message); } }
        }
        void Note(string s) { if (fixes < 20) { fixes++; PostersPlugin.Log.LogInfo("[poster] gardien : " + s); } }
    }

    // ===== Pose libre : coller une image du dossier n'importe ou dans la planque =====
    public class FreePlace : MonoBehaviour
    {
        class Placed { public string File; public Vector3 Pos; public Quaternion Rot; public GameObject Go; }
        static readonly System.Collections.Generic.List<Placed> placed = new System.Collections.Generic.List<Placed>();
        static BepInEx.Configuration.ConfigEntry<KeyCode> kMode, kPlace, kNext, kPrev, kRemove;
        static string SaveFile { get { return Path.Combine(BepInEx.Paths.ConfigPath, "HideoutPosters_placed.txt"); } }
        bool mode; int idx; GameObject ghost; string ghostFile; float nextCheck; bool spawned; string msg; float msgUntil;

        void Awake()
        {
            var c = PostersPlugin.Inst.Config;
            kMode = c.Bind("Free placement", "Placement mode key", KeyCode.P, "Start / cancel placing a poster anywhere in the hideout");
            kPlace = c.Bind("Free placement", "Place key", KeyCode.Return, "Hang the poster where you look");
            kNext = c.Bind("Free placement", "Next image key", KeyCode.RightArrow, "Next image while placing");
            kPrev = c.Bind("Free placement", "Previous image key", KeyCode.LeftArrow, "Previous image while placing");
            kRemove = c.Bind("Free placement", "Remove key", KeyCode.Delete, "Take down the free poster you are looking at");
            Load();
        }

        static bool InHideout()
        {
            try { return Comfort.Common.Singleton<EFT.GameWorld>.Instantiated && Comfort.Common.Singleton<EFT.GameWorld>.Instance is HideoutGameWorld; } catch { return false; }
        }

        void Note(string m) { msg = m; msgUntil = Time.time + 3f; PostersPlugin.Log.LogInfo("[pose] " + m); }

        void Update()
        {
            bool inH = InHideout();
            if (!inH)
            {
                if (spawned) { foreach (var p in placed) if (p.Go != null) Destroy(p.Go); spawned = false; }
                if (mode) Stop();
                return;
            }
            if (!spawned && Time.time > nextCheck) { nextCheck = Time.time + 1f; if (Camera.main != null) { foreach (var p in placed) Spawn(p); spawned = true; PostersPlugin.Log.LogInfo("[pose] " + placed.Count + " poster(s) libre(s) remis au mur"); } }

            if (Input.GetKeyDown(kMode.Value)) { if (mode) { Stop(); Note("placement cancelled"); } else Start2(); }
            if (Input.GetKeyDown(kRemove.Value) && !mode) RemoveLooked();
            if (!mode) return;
            var files = PostersPlugin.Files;
            if (files.Count == 0) { Stop(); Note("no image in BepInEx/config/HideoutPosters"); return; }
            if (Input.GetKeyDown(kNext.Value)) idx = (idx + 1) % files.Count;
            if (Input.GetKeyDown(kPrev.Value)) idx = (idx - 1 + files.Count) % files.Count;
            idx = Mathf.Clamp(idx, 0, files.Count - 1);
            string file = files[idx];
            RaycastHit hit;
            if (!Aim(out hit)) { if (ghost != null) ghost.SetActive(false); return; }
            if (ghost == null || ghostFile != file) { if (ghost != null) Destroy(ghost); ghost = Build(file); ghostFile = file; }
            if (ghost == null) return;
            ghost.SetActive(true);
            Quaternion rot = Orient(hit.normal);
            ghost.transform.SetPositionAndRotation(hit.point, rot);
            if (Input.GetKeyDown(kPlace.Value))
            {
                var p = new Placed { File = file, Pos = hit.point, Rot = rot, Go = ghost };
                placed.Add(p); ghost = null; ghostFile = null; Save();
                Note("poster hung: " + Path.GetFileNameWithoutExtension(file) + " (" + placed.Count + " free poster(s))");
            }
        }

        void Start2()
        {
            if (Camera.main == null) return;
            mode = true; Note("placement mode: aim at a wall");
        }
        void Stop() { mode = false; if (ghost != null) Destroy(ghost); ghost = null; ghostFile = null; }

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

        static Quaternion Orient(Vector3 normal)
        {
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(Camera.main != null ? Camera.main.transform.up : Vector3.forward, normal);
            return Quaternion.LookRotation(-normal, up.normalized);
        }

        static Material template;
        static Material MakeMaterial(Texture2D img)
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

        static GameObject Build(string file)
        {
            var img = PostersPlugin.LoadImage(file);
            if (img == null) return null;
            var mat = MakeMaterial(img);
            if (mat == null) return null;
            var go = new GameObject("HideoutPosterFree_" + Path.GetFileNameWithoutExtension(file));
            go.AddComponent<MeshFilter>().sharedMesh = Quad((float)img.width / img.height, PostersPlugin.ScaleFor(file));
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static void Spawn(Placed p)
        {
            if (p.Go != null) return;
            p.Go = Build(p.File);
            if (p.Go != null) p.Go.transform.SetPositionAndRotation(p.Pos, p.Rot);
        }

        internal static void Resize(string file)
        {
            foreach (var p in placed)
            {
                if (p.Go == null || (file != null && !string.Equals(p.File, file, StringComparison.OrdinalIgnoreCase))) continue;
                var img = PostersPlugin.LoadImage(p.File); if (img == null) continue;
                var mf = p.Go.GetComponent<MeshFilter>();
                var old = mf.sharedMesh; mf.sharedMesh = Quad((float)img.width / img.height, PostersPlugin.ScaleFor(p.File)); if (old != null) Destroy(old);
            }
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
                var plane = new Plane(p.Rot * Vector3.back, p.Pos);
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                float d; if (!plane.Raycast(ray, out d) || d > 8f) continue;
                var local = Quaternion.Inverse(p.Rot) * (ray.GetPoint(d) - p.Pos);
                var ext = mf.sharedMesh.bounds.extents;
                if (Mathf.Abs(local.x) <= ext.x && Mathf.Abs(local.y) <= ext.y && d < bestD) { bestD = d; best = p; }
            }
            if (best == null) { Note("no free poster in front of you"); return; }
            Destroy(best.Go); placed.Remove(best); Save();
            Note("poster taken down: " + Path.GetFileNameWithoutExtension(best.File));
        }

        static void Save()
        {
            try
            {
                var lines = placed.Select(p => string.Join("|", new[] { Path.GetFileName(p.File),
                    p.Pos.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture), p.Pos.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture), p.Pos.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    p.Rot.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture), p.Rot.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture), p.Rot.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture), p.Rot.w.ToString("R", System.Globalization.CultureInfo.InvariantCulture) })).ToArray();
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
                    placed.Add(new Placed { File = Path.Combine(dir, a[0]), Pos = new Vector3(f(1), f(2), f(3)), Rot = new Quaternion(f(4), f(5), f(6), f(7)) });
                }
                PostersPlugin.Log.LogInfo("[pose] " + placed.Count + " poster(s) libre(s) sauvegarde(s)");
            }
            catch (Exception e) { PostersPlugin.Log.LogWarning("[pose] chargement : " + e.Message); }
        }

        void OnGUI()
        {
            if (!mode && Time.time > msgUntil) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, richText = true };
            string t;
            if (mode)
            {
                var files = PostersPlugin.Files;
                string name = files.Count > 0 ? Path.GetFileNameWithoutExtension(files[Mathf.Clamp(idx, 0, files.Count - 1)]) : "-";
                t = "<b>POSTER: " + name + "</b>  (" + (idx + 1) + "/" + files.Count + ")\n[" + kPrev.Value + "/" + kNext.Value + "] change   [" + kPlace.Value + "] hang   [" + kMode.Value + "] cancel";
            }
            else t = msg;
            var r = new Rect(0, Screen.height * 0.78f, Screen.width, 70);
            var sh = new GUIStyle(st); sh.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), t, sh);
            st.normal.textColor = new Color(1f, 0.85f, 0.4f);
            GUI.Label(r, t, st);
        }
    }
}