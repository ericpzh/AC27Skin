using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using ContextCross.View.Menu;
using ContextCross.Aircrafts;
using ContextCross.Aircrafts.Enums;
using ContextCross.States;
using ContextCross.View;
using ContextCross.View._2D;
using ContextCross.Dynamics;
using R3;


namespace AC27Skin
{
    public static class LevelSelectState
    {
        private static readonly Dictionary<string, Sprite> _bgCache = new();  // sceneKey → Sprite
        private static readonly Dictionary<string, Texture2D> _texCache = new();
        private static readonly Dictionary<string, Texture2D> _diagramTexCache = new(); // sceneKey → diagram texture
        private static readonly Dictionary<string, Sprite> _btnCache = new(); // sceneKey → button preview sprite
        private static readonly Dictionary<int, Sprite> _channelIconCache = new(); // InfoItem index → Sprite
        internal static bool _channelIconsApplied = false;
        private static string _lastDetectedScene;
        private static string _lastDescSet;
        private static string _lastAppliedBgScene;   // track what bg is currently applied
        private static string _lastAppliedDiagramScene; // track what diagram is currently applied (debug only)
        private static int _lastAirportIndex = -1;

        /// Public accessor for HideAirportReview patch to re-apply diagram after hide.
        public static string GetLastDetectedScene() => _lastDetectedScene;
        // Cache: airportIndex → sceneKey (set in DisplayAirportReview prefix)
        private static readonly Dictionary<int, string> _indexToScene = new();

        // Loaded from text.txt — unified flat map, sorted by key length descending
        public static List<KeyValuePair<string, string>> AllTextSorted => TextOverridesConfig.AllTextSorted;

        // sceneKey → bg filename (background image for the full-screen backdrop)
        private static IReadOnlyDictionary<string, string> BgFileMap => TextOverridesConfig.LvlSelBgMap;

        // sceneKey → airport diagram filename (RawImage on AirportReview panel)
        private static IReadOnlyDictionary<string, string> DiagramFileMap => TextOverridesConfig.LvlSelDiaMap;

        // sceneKey → button preview filename (separate from bg, 100% fill on AirportItem)
        private static IReadOnlyDictionary<string, string> BtnFileMap => TextOverridesConfig.LvlSelBtnMap;

        /// Dump transform hierarchy (IL2CPP wrapper blocks field reflection, but Transform works).
        public static void DumpHierarchy(MonoBehaviour view, int maxDepth = 3)
        {
            try
            {
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LvlSel] === TRANSFORM HIERARCHY DUMP ===");
                WalkTransform(view.transform, 0, maxDepth);
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LvlSel] === All Images: {view.GetComponentsInChildren<Image>(true).Length}, TMPs: {view.GetComponentsInChildren<TextMeshProUGUI>(true).Length} ===");
            }
            catch (Exception ex) { AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LvlSel] DumpHierarchy err: {ex.Message}"); }
        }

        static void WalkTransform(Transform t, int depth, int maxDepth)
        {
            if (t == null || depth > maxDepth) return;
            string pad = new string(' ', depth * 2);
            var comps = t.GetComponents<Component>();
            var cn = new List<string>();
            foreach (var c in comps) { if (c != null) cn.Add(c.GetType().Name); }
            var tmp = t.GetComponent<TextMeshProUGUI>();
            string txtInfo = tmp != null ? $" TMP='{tmp.text}'" : "";
            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LvlSel]   {pad}{t.name} active={t.gameObject.activeSelf} [{string.Join(", ", cn)}]{txtInfo}");
            int maxC = (depth == 0 && t.childCount > 30) ? 30 : t.childCount;
            for (int i = 0; i < maxC; i++)
                WalkTransform(t.GetChild(i), depth + 1, maxDepth);
            if (maxC < t.childCount)
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LvlSel]   {pad}  ... +{t.childCount - maxC} more children");
        }

        /// Called from DisplayAirportReview PREFIX. Scans ALL AirportItem components in the view
        /// hierarchy to find the one matching airportIndex, then reads its TMP text to detect scene.
        public static void OnDisplayAirportReview(MonoBehaviour view, int airportIndex)
        {
            if (airportIndex < 0) return;
            _lastAirportIndex = airportIndex;
            try
            {
                // Get ALL AirportItem components (including inactive prefab templates)
                var allItems = view.GetComponentsInChildren<AirportItem>(true);
                foreach (var ai in allItems)
                {
                    if (ai == null) continue;

                    // Read airportindex via reflection
                    var idxF = ai.GetType().GetField("airportindex", BindingFlags.Instance | BindingFlags.NonPublic);
                    int itemIdx = -1;
                    if (idxF != null) { try { itemIdx = (int)idxF.GetValue(ai); } catch { } }

                    if (itemIdx == airportIndex)
                    {
                        // Read TMP children to detect scene
                        var allTMP = ai.GetComponentsInChildren<TextMeshProUGUI>(true);
                        foreach (var tmp in allTMP)
                        {
                            if (tmp == null || string.IsNullOrEmpty(tmp.text)) continue;
                            string n = tmp.text;
                            string scene = DetectSceneFromText(n);
                            if (scene != null)
                            {
                                _indexToScene[airportIndex] = scene;
                                _lastDetectedScene = scene;
                                return;
                            }
                        }
                        return;
                    }
                }
            }
            catch (Exception ex) { AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LvlSel] OnDisplayReview err: " + ex.Message); }
        }

        /// Recursively find a child by name.
        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var result = FindChildByName(root.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }

        /// Classify text into scene key.
        private static string DetectSceneFromText(string txt)
        {
            if (string.IsNullOrEmpty(txt)) return null;
            if (txt.Contains("培训") || txt.Contains("见习") || txt.Contains("手札") || txt.Contains("教学") || txt.Contains("Tutorial")) return "tutorial";
            if (txt.Contains("济南") || txt.Contains("遥墙") || txt.Contains("老实人") || txt.Contains("ZSJN") || txt.Contains("Jinan") || txt.Contains("1992")) return "zsjn";
            if (txt.Contains("纽约") || txt.Contains("都会区") || txt.Contains("肯尼迪") || txt.Contains("JFK") || txt.Contains("无民") || txt.Contains("KJFK") || txt.Contains("Kennedy") || txt.Contains("1948")) return "kjfk";
            return null;
        }

        /// Detect scene from Desc → Info TMP text (most reliable when airport panel is shown).
        public static string DetectSceneFromDesc(MonoBehaviour view)
        {
            var descT = FindChildByName(view.transform, "Desc");
            if (descT == null) return null;
            var infoT = FindChildByName(descT, "Info");
            if (infoT == null) return null;
            var tmp = infoT.GetComponent<TextMeshProUGUI>();
            if (tmp == null || string.IsNullOrEmpty(tmp.text)) return null;
            string scene = DetectSceneFromText(tmp.text);
            return scene;
        }

        /// Detect scene from airport index using our cached index→scene map.
        public static string DetectSceneFromAirportIndex(MonoBehaviour view, int airportIndex)
        {
            if (airportIndex < 0) return null;
            if (_indexToScene.TryGetValue(airportIndex, out string scene)) return scene;
            return null;
        }

        /// Detect scene by scanning ALL TMP_Text in the view hierarchy for known keywords.
        public static string DetectSceneFromTMP(MonoBehaviour view)
        {
            try
            {
                var allTMP = view.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var t in allTMP)
                {
                    if (t == null || string.IsNullOrEmpty(t.text)) continue;
                    string scene = DetectSceneFromText(t.text);
                    if (scene != null)
                    {
                        return scene;
                    }
                }
            }
            catch (Exception ex) { AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LvlSel] DetectSceneTMP err: " + ex.Message); }
            return null;
        }

        /// Detect which airport scene is currently being shown by reading AirportInfoText / TitleText.
        [Obsolete] public static string DetectSceneFromInfoText(MonoBehaviour view) => DetectSceneFromTMP(view);

        public static void EnsureBgLoaded(string sceneKey)
        {
            // Re-validate: scene unload destroys native objects, managed wrapper lingers
            if (_bgCache.TryGetValue(sceneKey, out var existing) && existing != null) return;
            _bgCache.Remove(sceneKey);
            _texCache.Remove(sceneKey);
            if (!BgFileMap.TryGetValue(sceneKey, out string fn)) return;
            string path = Path.Combine(Paths.GameRootPath, "overrides", fn);
            if (!File.Exists(path)) { AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LvlSel] BG missing: {path}"); return; }
            byte[] data = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, data)) { AC27SkinPlugin.Logger.LogError($"[AC27Skin] [LvlSel] BG decode fail: {fn}"); return; }
            var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            _bgCache[sceneKey] = sp;
            _texCache[sceneKey] = tex;
            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LvlSel] BG loaded: {fn} ({tex.width}x{tex.height})");
        }

        /// Load channel icons (icon1-6.png) once into sprite cache.
        /// Only loads files that actually exist on disk — base game may have 1-6 InfoItems.
        private static void EnsureChannelIconsLoaded()
        {
            if (_channelIconCache.Count > 0) return;
            int[] indices = { 0, 1, 2, 3, 4, 5 }; // icon1.png → index 0, ... icon6.png → index 5
            foreach (int i in indices)
            {
                string fn = $"icon{i + 1}.png";
                string path = Path.Combine(Paths.GameRootPath, "overrides", fn);
                if (!File.Exists(path)) { continue; } // skip missing files silently — not every airport has all icons
                try
                {
                    byte[] data = File.ReadAllBytes(path);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(tex, data)) { AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [ChIcn] decode fail: {fn}"); continue; }
                    var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    _channelIconCache[i] = sp;
                    AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [ChIcn] Loaded: {fn} ({tex.width}x{tex.height})");
                }
                catch (Exception ex) { AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [ChIcn] error loading {fn}: {ex.Message}"); }
            }
            // Reset applied flag so next activation triggers reapply
            _channelIconsApplied = false;
        }

        /// Override AirportInfo InfoItem icons with twr/gnd/appr images.
        public static void ApplyChannelIcons(MonoBehaviour view)
        {
            EnsureChannelIconsLoaded();
            if (_channelIconCache.Count == 0) return;

            var levelPart = FindChildByName(view.transform, "LevelPart");
            if (levelPart == null || !levelPart.gameObject.activeInHierarchy) return;
            if (_channelIconsApplied) return; // already applied for this activation

            var airportInfo = FindChildByName(levelPart, "AirportInfo");
            if (airportInfo == null || !airportInfo.gameObject.activeInHierarchy) return;

            var group = FindChildByName(airportInfo, "group");
            if (group == null) return;

            bool anyApplied = false;
            for (int i = 0; i < group.childCount; i++)
            {
                if (!_channelIconCache.TryGetValue(i, out var sp)) continue;
                var infoItem = group.GetChild(i);
                var icon = FindChildByName(infoItem, "Icon");
                if (icon == null) continue;
                var img = icon.GetComponent<Image>();
                if (img != null && img.sprite != sp)
                {
                    img.sprite = sp;
                    anyApplied = true;
                }
            }
            if (anyApplied)
            {
                _channelIconsApplied = true;
                AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [ChIcn] Channel icons applied to AirportInfo");
            }
        }

        /// Set ALL background Images in the view to the custom sprite.
        /// Uses Transform walking to find Image components (avoids Il2Cpp field reflection).
        public static bool ApplyBg(MonoBehaviour view, string sceneKey)
        {
            EnsureBgLoaded(sceneKey);
            if (!_bgCache.TryGetValue(sceneKey, out var sp)) { AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LvlSel] BG apply fail: sprite not in cache for {sceneKey}"); return false; }

            try
            {
                bool ok = false;

                // Strategy 1: Find "Background" child → set all Images in its subtree (covers "1", "2")
                var bgParent = FindChildByName(view.transform, "Background");
                if (bgParent != null)
                {
                    var bgImages = bgParent.GetComponentsInChildren<Image>(true);
                    foreach (var img in bgImages)
                    {
                        if (img == null) continue;
                        img.sprite = sp;
                        img.color = Color.white; // ← reset color: DOTween may have left alpha mid-fade
                        ok = true;
                    }
                }
                else { AC27SkinPlugin.Logger.LogWarning("[AC27Skin] [LvlSel] BG: 'Background' child not found under " + view.name); }

                // Strategy 2: Set "BackgroundGradient" Image (gradient overlay, set to our bg too)
                var bgGrad = FindChildByName(view.transform, "BackgroundGradient");
                if (bgGrad != null)
                {
                    var gi = bgGrad.GetComponent<Image>();
                    if (gi != null) { gi.sprite = sp; gi.color = Color.white; ok = true; } // ← reset color too
                }

                // (AirportReview RawImage is handled separately by ApplyDiagram)

                if (ok) _lastAppliedBgScene = sceneKey;
                return ok;
            }
            catch (Exception ex) { AC27SkinPlugin.Logger.LogError($"[AC27Skin] [LvlSel] BG apply err: {ex.Message}"); return false; }
        }

        /// Load airport diagram texture (separate from background image).
        /// Alpha is baked to 40% at load time — no runtime color flash.
        public static void EnsureDiagramLoaded(string sceneKey)
        {
            if (_diagramTexCache.TryGetValue(sceneKey, out var existing) && existing != null) return;
            _diagramTexCache.Remove(sceneKey);
            if (!DiagramFileMap.TryGetValue(sceneKey, out string fn)) return;
            string path = Path.Combine(Paths.GameRootPath, "overrides", fn);
            if (!File.Exists(path)) { AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LvlSel] Diagram file missing: {path}"); return; }
            byte[] data = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, data)) { AC27SkinPlugin.Logger.LogError($"[AC27Skin] [LvlSel] Diagram decode fail: {fn}"); return; }
            // Bake 40% alpha into new pixel array (IL2CPP Color32 is a struct — can't modify ref directly)
            var src = tex.GetPixels32();
            var dst = new Color32[src.Length];
            for (int i = 0; i < src.Length; i++)
                dst[i] = new Color32(src[i].r, src[i].g, src[i].b, (byte)(src[i].a * 0.4f));
            tex.SetPixels32(dst);
            tex.Apply();
            _diagramTexCache[sceneKey] = tex;
            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LvlSel] Diagram loaded: {fn} ({tex.width}x{tex.height}) alpha=baked40%");
        }

        /// Load button preview image (separate from background/diagram).
        public static void EnsureBtnLoaded(string sceneKey)
        {
            if (_btnCache.TryGetValue(sceneKey, out var existing) && existing != null) return;
            _btnCache.Remove(sceneKey);
            if (!BtnFileMap.TryGetValue(sceneKey, out string fn)) return;
            string path = Path.Combine(Paths.GameRootPath, "overrides", fn);
            if (!File.Exists(path)) { AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LvlSel] Btn file missing: {path}"); return; }
            byte[] data = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, data)) { AC27SkinPlugin.Logger.LogError($"[AC27Skin] [LvlSel] Btn decode fail: {fn}"); return; }
            var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            _btnCache[sceneKey] = sp;
            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LvlSel] Btn loaded: {fn} ({tex.width}x{tex.height})");
        }

        /// Apply airport diagram to AirportReview → RawImage.
        /// The game uses RawImage (not Image) with Texture2D for the review overlay.
        /// NOTE: We always apply (no cache-based skip) because:
        ///   1) the game's HideAirportReview() can deactivate/hide the diagram panel
        ///   2) switching from tutorial (no diagram) to kjfk/zsjn must re-apply
        public static void ApplyDiagram(MonoBehaviour view, string sceneKey)
        {
            // Tutorial has no separate diagram
            if (sceneKey == "tutorial") return;

            var review = FindChildByName(view.transform, "AirportReview");
            if (review == null)
            {
                AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LvlSel] Diagram: 'AirportReview' NOT found in hierarchy. Root={view.name}");
                return;
            }

            EnsureDiagramLoaded(sceneKey);
            if (!_diagramTexCache.TryGetValue(sceneKey, out var tex))
            {
                AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LvlSel] Diagram: tex not in cache for {sceneKey}");
                return;
            }

            // Try 1: Find "Image" child → activate it → set RawImage.texture
            var imageChild = FindChildByName(review, "Image");
            if (imageChild != null)
            {
                if (!imageChild.gameObject.activeSelf) imageChild.gameObject.SetActive(true);
                var rawImg = imageChild.GetComponent<RawImage>();
                if (rawImg != null)
                {
                    rawImg.texture = tex;
                    rawImg.color = Color.white;
                    _lastAppliedDiagramScene = sceneKey;
                    return;
                }
                else
                {
                    var comps = imageChild.GetComponents<Component>();
                    var names = new List<string>();
                    foreach (var c in comps) { if (c != null) names.Add(c.GetType().FullName ?? c.GetType().Name); }
                    AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LvlSel] Diagram: AirportReview/Image has NO RawImage. Found: [{string.Join(", ", names)}]");
                }
            }
            else
            {
                AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LvlSel] Diagram: 'Image' child NOT found under AirportReview");
            }

            // Try 2: Any RawImage directly under AirportReview (the public AirportImage field)
            var allRaw = review.GetComponentsInChildren<RawImage>(true);
            foreach (var raw in allRaw)
            {
                if (raw == null) continue;
                raw.texture = tex;
                raw.color = Color.white;
                if (!raw.gameObject.activeSelf) raw.gameObject.SetActive(true);
                _lastAppliedDiagramScene = sceneKey;
                return;
            }

            AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LvlSel] Diagram: no RawImage found under AirportReview (review.active={review.gameObject.activeSelf})");
        }

        /// Apply button preview sprite (_btn.png) to each AirportItem's preview image.
        /// Detects each AirportItem's scene from its TMP text, then sets airportImage.
        /// preserveAspect=false for 100% fill (stretches to fill the preview area).
        public static void ApplyAirportItemPreviews(MonoBehaviour view)
        {
            try
            {
                var allItems = view.GetComponentsInChildren<AirportItem>(true);
                int applied = 0;

                foreach (var ai in allItems)
                {
                    if (ai == null) continue;

                    // Detect scene from AirportItem's own TMP children (the Name text)
                    string scene = null;
                    var allTMP = ai.GetComponentsInChildren<TextMeshProUGUI>(true);
                    foreach (var tmp in allTMP)
                    {
                        if (tmp == null || string.IsNullOrEmpty(tmp.text)) continue;
                        scene = DetectSceneFromText(tmp.text);
                        if (scene != null) break;
                    }
                    if (scene == null) continue;

                    // Load and cache btn preview sprite for this scene
                    EnsureBtnLoaded(scene);
                    if (!_btnCache.TryGetValue(scene, out var sp)) continue;

                    // Try reflection to access private Image airportImage field
                    var imgField = ai.GetType().GetField("airportImage",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (imgField != null)
                    {
                        try
                        {
                            var img = imgField.GetValue(ai) as Image;
                            if (img != null) { img.sprite = sp; img.preserveAspect = false; applied++; }
                        }
                        catch { }
                    }

                    // Also try: child named "Image" with Image component (fallback)
                    var imgChild = FindChildByName(ai.transform, "Image");
                    if (imgChild != null)
                    {
                        var childImg = imgChild.GetComponent<Image>();
                        if (childImg != null) { childImg.sprite = sp; childImg.preserveAspect = false; applied++; }
                    }
                }

                // (verbose log removed: AirportItem previews applied)
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LvlSel] AirportItem previews err: {ex.Message}");
            }
        }

        /// Kill DOTween background transition tweens on LevelSelectView to prevent
        /// the game from overwriting our custom background after hover/selection.
        public static void KillBackgroundTweens(MonoBehaviour view)
        {
            if (view == null) return;
            try
            {
                var vt = view.GetType();
                var fields = new[] { "_backgroundTransitionTween", "_pendingBackgroundDelayTween" };
                foreach (var fn in fields)
                {
                    var field = vt.GetField(fn,
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (field == null) continue;
                    var tween = field.GetValue(view);
                    if (tween == null) continue;
                    try
                    {
                        tween.GetType().GetMethod("Kill")?.Invoke(tween, new object[] { false });
                        field.SetValue(view, null);
                    }
                    catch (Exception ex) { AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LvlSel] KillTween {fn} err: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LvlSel] KillBackgroundTweens err: {ex.Message}");
            }
        }

        /// Re-apply custom button preview sprite to a specific AirportItem.
        /// Called from AirportItem.OnPointerExit patch to counteract the game's reset.
        /// Detects scene from the AirportItem's own TMP text (does NOT rely on _indexToScene
        /// because airportindex field is -1 when read from prefixes).
        public static void ReplaceAirportItemPreview(AirportItem ai)
        {
            if (ai == null) return;
            try
            {
                // Detect scene from AirportItem's own TMP text
                string scene = null;
                var allTMP = ai.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var tmp in allTMP)
                {
                    if (tmp == null || string.IsNullOrEmpty(tmp.text)) continue;
                    scene = DetectSceneFromText(tmp.text);
                    if (scene != null) break;
                }
                if (scene == null) return;

                EnsureBtnLoaded(scene);
                if (!_btnCache.TryGetValue(scene, out var sp)) return;

                // Set airportImage.sprite
                var imgField = ai.GetType().GetField("airportImage",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (imgField != null)
                {
                    var img = imgField.GetValue(ai) as Image;
                    if (img != null && img.sprite != sp)
                    {
                        img.sprite = sp;
                        img.preserveAspect = false;
                    }
                }

                // Also kill the mask fade tween if active
                var maskField = ai.GetType().GetField("_maskFadeTween",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (maskField != null)
                {
                    var tween = maskField.GetValue(ai);
                    if (tween != null)
                    {
                        try
                        {
                            tween.GetType().GetMethod("Kill")?.Invoke(tween, new object[] { false });
                            maskField.SetValue(ai, null);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        /// Check if LevelSelectView has any active background DOTween tweens.
        public static bool HasActiveBackgroundTweens(MonoBehaviour view)
        {
            if (view == null) return false;
            try
            {
                var fields = new[] { "_backgroundTransitionTween", "_pendingBackgroundDelayTween" };
                foreach (var fn in fields)
                {
                    var field = view.GetType().GetField(fn,
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (field == null) continue;
                    var tween = field.GetValue(view);
                    if (tween != null) return true;
                }
            }
            catch { }
            return false;
        }

        /// Replace texts in the view (Transform-walking, no Il2Cpp field reflection)
        public static int ReplaceAllTexts(MonoBehaviour view)
        {
            int cntText = 0;
            var map = AllTextSorted;

            var all = view.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var t in all)
            {
                if (t == null || string.IsNullOrEmpty(t.text)) continue;
                bool changed = false;
                string normalized = TextReplacer.NormalizeForMatch(t.text);
                foreach (var kv in map)
                {
                    if (normalized.Contains(kv.Value) && kv.Key != kv.Value)
                        continue;
                    if (normalized.Contains(kv.Key))
                    {
                        t.text = normalized.Replace(kv.Key, kv.Value);
                        changed = true;
                        break;
                    }
                }
                if (changed)
                {
                    t.ForceMeshUpdate(true, true);
                    TextReplacer.TryDisableLocalization(t);
                    cntText++;
                }
            }

            return cntText;
        }

        /// Find the description TMP. Strategy:
        /// 1. Find "Desc" child → its "Info" child → TMP (most reliable)
        /// 2. Fallback: "AirportDesc" or "AirportInfoPanel" child
        /// 3. Last resort: longest non-button TMP text (>5 chars)
        private static TMP_Text FindDescriptionTMP(MonoBehaviour view)
        {
            // Method 1 (primary): "Desc" child → "Info" child TMP
            var desc = FindChildByName(view.transform, "Desc");
            if (desc != null)
            {
                var info = FindChildByName(desc, "Info");
                if (info != null)
                {
                    var tmp = info.GetComponent<TextMeshProUGUI>();
                    if (tmp != null) return tmp;
                }
                // Also try any TMP directly under Desc
                var anyTMP = desc.GetComponentInChildren<TextMeshProUGUI>(true);
                if (anyTMP != null) return anyTMP;
            }

            // Method 2: "AirportDesc" child
            var ad = FindChildByName(view.transform, "AirportDesc");
            if (ad != null)
            {
                var tmp = ad.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null) return tmp;
            }

            // Method 3: "AirportInfoPanel" child
            var ap = FindChildByName(view.transform, "AirportInfoPanel");
            if (ap != null)
            {
                var tmp = ap.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null) return tmp;
            }

            // Method 4 (fallback): Find longest TMP text that isn't a known button/label
            var allTMP = view.GetComponentsInChildren<TextMeshProUGUI>(true);
            TMP_Text best = null;
            int bestLen = 0;
            foreach (var t in allTMP)
            {
                if (t == null || string.IsNullOrEmpty(t.text)) continue;
                int len = t.text.Length;
                // Lower threshold to catch shorter descriptions too
                if (len > 5 && len > bestLen)
                {
                    bool isButton = t.text.Contains("开始") || t.text.Contains("设置")
                                 || t.text.Contains("退出") || t.text.Contains("更新")
                                 || t.text.Contains("返回") || t.text.Contains("回溯")
                                 || t.text.Contains("模块") || t.text.Contains("愿望")
                                 || t.text.Contains("加入") || t.text.Contains("游戏")
                                 || t.text.Contains("机场名") || t.text.Contains("加载");
                    if (!isButton) { best = t; bestLen = len; }
                }
            }
            return best;
        }

        /// Full refresh: detect scene FIRST (before touching any text), then apply everything.
        public static void FullRefresh(MonoBehaviour view, int airportIndex = -1)
        {
            // === STEP 0: Nuke localization first — text replacements MUST stick ===
            int locsDisabled = TextReplacer.DisableAllLocalization(view.gameObject);

            // === STEP 1: Detect scene BEFORE we modify any text ===
            string sceneByIndex = DetectSceneFromAirportIndex(view, airportIndex);
            string sceneByDesc = sceneByIndex == null ? DetectSceneFromDesc(view) : null;
            string scene = sceneByIndex ?? sceneByDesc ?? _lastDetectedScene ?? DetectSceneFromTMP(view);
            _lastDetectedScene = scene;

            // === STEP 2: Text replacements (covers labels, buttons, descriptions — all in one pass) ===
            int textCount = ReplaceAllTexts(view);

            // === STEP 3: Apply background, airport diagram, and preview images ===
            if (scene != null)
            {
                bool bgOk = ApplyBg(view, scene);
                ApplyDiagram(view, scene);
                ApplyAirportItemPreviews(view);
                ApplyChannelIcons(view); // also apply channel icons when LevelPart becomes active
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LvlSel] FullRefresh: scene='{scene}' locs={locsDisabled} texts={textCount} bg={bgOk} airportIdx={airportIndex}");
            }
            else
            {
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LvlSel] FullRefresh: scene=NULL locs={locsDisabled} texts={textCount} airportIdx={airportIndex} (skipped bg/dia)");
            }
        }

        // ── Diagnostic: dump transform hierarchy ──
        public static void DumpTransformHierarchy(Transform t, string indent, System.Text.StringBuilder sb)
        {
            if (t == null) return;
            var img = t.GetComponent<Image>();
            var tmp = t.GetComponent<TMP_Text>();
            var rawImg = t.GetComponent<RawImage>();
            var extra = "";
            if (img != null) extra = $" [Image: sprite='{img.sprite?.name ?? "NULL"}' color={img.color}]";
            if (tmp != null) extra += $" [TMP_Text: '{(tmp.text?.Length > 20 ? tmp.text.Substring(0, 20) : tmp.text ?? "")}']";
            if (rawImg != null) extra += $" [RawImage]";
            sb.AppendLine($"{indent}{t.name} (active={t.gameObject.activeSelf}){extra}");
            for (int i = 0; i < t.childCount; i++)
                DumpTransformHierarchy(t.GetChild(i), indent + "  ", sb);
        }
    }

    // ---- Patch: InitializeUI ----

    [HarmonyPatch]
    public static class LvSel_Init
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ContextCross.View.Menu.LevelSelectView");
            if (t == null) return null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == "InitializeUI" && m.GetParameters().Length == 0) return m;
            return null;
        }
        [HarmonyPostfix] static void Postfix(MonoBehaviour __instance)
        {
            try
            {
                LevelSelectState.FullRefresh(__instance);
            }
            catch (Exception ex) { AC27SkinPlugin.Logger.LogError("[AC27Skin] [LvlSel] Init err: " + ex); }
        }
    }

    // ---- Patch: DisplayAirportReview (hover → bg + desc) ----
    // PREFIX: capture airportIndex for reliable scene detection.
    // POSTFIX: apply overrides after UI is updated.

    [HarmonyPatch]
    public static class LvSel_Review
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ContextCross.View.Menu.LevelSelectView");
            if (t == null) return null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == "DisplayAirportReview" && m.GetParameters().Length == 1) return m;
            return null;
        }
        [HarmonyPrefix] static void Prefix(MonoBehaviour __instance, object[] __args)
        {
            try
            {
                int idx = __args != null && __args.Length > 0 ? (int)__args[0] : -1;
                LevelSelectState.OnDisplayAirportReview(__instance, idx);
            }
            catch { }
        }
        [HarmonyPostfix] static void Postfix(MonoBehaviour __instance, object[] __args)
        {
            try
            {
                int idx = __args != null && __args.Length > 0 ? (int)__args[0] : -1;

                // 1. Kill game's DOTween background transition tweens (prevent overwrite)
                LevelSelectState.KillBackgroundTweens(__instance);

                // 2. Apply all our custom visuals
                LevelSelectState.FullRefresh(__instance, idx);

                // 3. Start/reset per-frame animation guard — 3s auto-stop if no hover activity
                string scene = LevelSelectState.GetLastDetectedScene();
                if (!string.IsNullOrEmpty(scene))
                {
                    var guard = __instance.gameObject.GetComponent<BackgroundGuard>();
                    if (guard != null) { guard.SceneKey = scene; guard.enabled = true; }
                    else
                    {
                        guard = __instance.gameObject.AddComponent<BackgroundGuard>();
                        guard.TargetView = __instance;
                        guard.SceneKey = scene;
                    }
                    guard.ResetCountdown(); // reset 3s timer on each hover
                }
            }
            catch (Exception ex) { AC27SkinPlugin.Logger.LogError("[AC27Skin] [LvlSel] Review err: " + ex); }
        }
    }

    // ---- Patch: HideAirportReview (hover exit → game hides diagram) ----
    // NOTE: This may NOT be called during hover switching between AirportItems.
    // The BackgroundGuard's auto-stop timer handles the main case.
    // If this DOES fire, we speed up cooldown to 1.5s.

    [HarmonyPatch]
    public static class LvSel_HideReview
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ContextCross.View.Menu.LevelSelectView");
            if (t == null) return null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == "HideAirportReview" && m.GetParameters().Length == 0) return m;
            return null;
        }
        [HarmonyPostfix] static void Postfix(MonoBehaviour __instance)
        {
            try
            {
                string scene = LevelSelectState.DetectSceneFromTMP(__instance)
                            ?? LevelSelectState.GetLastDetectedScene();

                if (scene != null)
                {
                    LevelSelectState.ApplyBg(__instance, scene);
                    LevelSelectState.ApplyDiagram(__instance, scene);
                }
                LevelSelectState.ApplyAirportItemPreviews(__instance);

                var guard = __instance.gameObject.GetComponent<BackgroundGuard>();
                if (guard != null && guard.isActiveAndEnabled)
                {
                    if (scene != null) guard.SceneKey = scene;
                    guard.StartCooldown(1.5f);
                }
            }
            catch (Exception ex) { AC27SkinPlugin.Logger.LogError("[AC27Skin] [LvlSel] HideReview err: " + ex); }
        }
    }

    // ---- Patch: ShowLevelPart (click airport → show levels) ----

    [HarmonyPatch]
    public static class LvSel_ShowLevel
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ContextCross.View.Menu.LevelSelectView");
            if (t == null) return null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == "ShowLevelPart" && m.GetParameters().Length == 1) return m;
            return null;
        }
        [HarmonyPostfix] static void Postfix(MonoBehaviour __instance, object[] __args)
        {
            try
            {
                int idx = __args != null && __args.Length > 0 ? (int)__args[0] : -1;
                LevelSelectState.FullRefresh(__instance, idx);
            }
            catch (Exception ex) { AC27SkinPlugin.Logger.LogError("[AC27Skin] [LvlSel] ShowLevel err: " + ex); }
        }
    }

    // ---- Patch: ShowAirportPart (back to airport list) ----

    [HarmonyPatch]
    public static class LvSel_ShowAirport
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ContextCross.View.Menu.LevelSelectView");
            if (t == null) return null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == "ShowAirportPart" && m.GetParameters().Length == 0) return m;
            return null;
        }
        [HarmonyPostfix] static void Postfix(MonoBehaviour __instance)
        {
            try
            {
                LevelSelectState._channelIconsApplied = false; // reset for new LevelPart activation
                LevelSelectState.FullRefresh(__instance);
            }
            catch (Exception ex) { AC27SkinPlugin.Logger.LogError("[AC27Skin] [LvlSel] ShowAirport err: " + ex); }
        }
    }

    // ---- Patch: UpdateAirportList (data loaded → try refresh) ----

    [HarmonyPatch]
    public static class LvSel_AirportList
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ContextCross.View.Menu.LevelSelectView");
            if (t == null) return null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == "UpdateAirportList" && m.GetParameters().Length == 1) return m;
            return null;
        }
        [HarmonyPostfix] static void Postfix(MonoBehaviour __instance)
        {
            try { LevelSelectState.FullRefresh(__instance); }
            catch (Exception ex) { AC27SkinPlugin.Logger.LogError("[AC27Skin] [LvlSel] AirportList err: " + ex); }
        }
    }

    // ---- Patch: UpdateLevelPartName (level name changed → re-apply text) ----

    [HarmonyPatch]
    public static class LvSel_LevelPartName
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ContextCross.View.Menu.LevelSelectView");
            if (t == null) return null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == "UpdateLevelPartName" && m.GetParameters().Length == 1) return m;
            return null;
        }
        [HarmonyPostfix] static void Postfix(MonoBehaviour __instance)
        {
            try { LevelSelectState.ReplaceAllTexts(__instance); }
            catch { }
        }
    }

    // ---- Patch: AirportItem.OnPointerExit (prevent game from resetting preview sprite) ----

    [HarmonyPatch]
    public static class AirportItem_Exit
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ContextCross.View.Menu.AirportItem");
            if (t == null) return null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == "OnPointerExit" && m.GetParameters().Length == 1) return m;
            return null;
        }

        [HarmonyPostfix]
        static void Postfix(MonoBehaviour __instance)
        {
            try
            {
                LevelSelectState.ReplaceAirportItemPreview(__instance as AirportItem);
            }
            catch { }
        }
    }

    // ---- Patch: AirportItem.Create (created/recycled → re-apply preview sprite + text) ----
    // KEY INSIGHT from Il2CppDump: AirportItem.Create(AirportData, LevelSelectView, int) is
    // the entry point where the game sets the preview Image.sprite and nameText TMP.
    // Without this hook, the game's DOTween-scheduled sprite swaps override our custom previews.

    [HarmonyPatch]
    public static class AirportItem_Create
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ContextCross.View.Menu.AirportItem");
            if (t == null) return null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == "Create" && m.GetParameters().Length == 3) return m;
            return null;
        }
        [HarmonyPostfix] static void Postfix(MonoBehaviour __instance)
        {
            try
            {
                // Re-apply our custom button preview after game sets original
                LevelSelectState.ReplaceAirportItemPreview(__instance as AirportItem);
            }
            catch { }
        }
    }

    // ---- Patch: AirportItem.OnPointerEnter (hover enter → prevent game from resetting preview) ----
    // Il2CppDump shows OnPointerEnter(PointerEventData) exists on AirportItem.
    // The game uses DOTween to fade/unfade the mask image — the postfix re-applies our sprite.

    [HarmonyPatch]
    public static class AirportItem_Enter
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ContextCross.View.Menu.AirportItem");
            if (t == null) return null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == "OnPointerEnter" && m.GetParameters().Length == 1) return m;
            return null;
        }
        [HarmonyPostfix] static void Postfix(MonoBehaviour __instance)
        {
            try
            {
                LevelSelectState.ReplaceAirportItemPreview(__instance as AirportItem);
            }
            catch { }
        }
    }

    // ---- Patch: UpdateLevelList (level list data updated → re-apply all text overrides) ----
    // Il2CppDump: private void UpdateLevelList(List<LevelData> value)
    // Called by the ViewModel when airport selection changes → recreates LevelItem GameObjects.

    [HarmonyPatch]
    public static class LvSel_LevelList
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ContextCross.View.Menu.LevelSelectView");
            if (t == null) return null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == "UpdateLevelList" && m.GetParameters().Length == 1) return m;
            return null;
        }
        [HarmonyPostfix] static void Postfix(MonoBehaviour __instance)
        {
            try
            {
                AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [LvlSel] UpdateLevelList fired → FullRefresh");
                LevelSelectState.FullRefresh(__instance);
            }
            catch (Exception ex) { AC27SkinPlugin.Logger.LogError("[AC27Skin] [LvlSel] LevelList err: " + ex); }
        }
    }

    // ---- Patch: InitializeBackgroundImages (game sets default bgs → re-apply ours) ----
    // Il2CppDump: private void InitializeBackgroundImages()
    // Called during LevelSelectView init — game sets BackgroundImages list with default sprites.

    [HarmonyPatch]
    public static class LvSel_InitBg
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ContextCross.View.Menu.LevelSelectView");
            if (t == null) return null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == "InitializeBackgroundImages" && m.GetParameters().Length == 0) return m;
            return null;
        }
        [HarmonyPostfix] static void Postfix(MonoBehaviour __instance)
        {
            try
            {
                AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [LvlSel] InitializeBackgroundImages fired → re-apply bg");
                string scene = LevelSelectState.DetectSceneFromTMP(__instance)
                            ?? LevelSelectState.GetLastDetectedScene();
                if (scene != null)
                {
                    LevelSelectState.ApplyBg(__instance, scene);
                    LevelSelectState.ApplyAirportItemPreviews(__instance);
                }
            }
            catch (Exception ex) { AC27SkinPlugin.Logger.LogError("[AC27Skin] [LvlSel] InitBg err: " + ex); }
        }
    }

    // ==================== BackgroundGuard: per-frame override for fixed duration ====================
    // The game's DOTween tweens can't be reliably detected via IL2CPP reflection (GetField returns null),
    // and HideAirportReview() is NOT called during hover switching between airport items.
    //
    // STRATEGY: Auto-reset timer. Every DisplayAirportReview(hover) call resets a 3s countdown.
    // After 3s with NO hover activity, the guard self-destructs. This handles both cases:
    //   - continuously hovering between items (timer keeps resetting)
    //   - hovering away from all items (timer runs out after 3s)
    //   - clicking an airport (ShowLevelPart changes view, guard stops naturally)


    public class BackgroundGuard : MonoBehaviour
    {
        public MonoBehaviour TargetView;
        public string SceneKey;
        private float _elapsed;
        private int _frameCount;
        private float _maxDuration = 3f; // auto-stop after 3s of no hover activity
        private const float TextRefreshInterval = 0.3f;
        private float _textTimer;

        public BackgroundGuard(IntPtr ptr) : base(ptr) { }

        /// Reset the countdown timer. Called on every hover (DisplayAirportReview) 
        /// to keep the guard alive during hover activity.
        public void ResetCountdown()
        {
            _elapsed = 0f;
        }

        /// Force a shorter countdown (used on HideAirportReview if it fires).
        public void StartCooldown(float duration)
        {
            _elapsed = 0f;
            _maxDuration = duration;
            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Guard] Cooldown {duration:F1}s for scene={SceneKey}");
        }

        private static bool _lvlDiagDone = false;
        private static bool _lvlDiagDone3 = false;

        void Start()
        {
            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Guard] Started for scene={SceneKey} (auto-stop after {_maxDuration:F1}s idle)");
            LevelSelectState._channelIconsApplied = false; // reset for this new LevelPart activation

            // ── One-time diagnostic: dump full TargetView hierarchy ──
            if (!_lvlDiagDone && TargetView != null)
            {
                _lvlDiagDone = true;
                var sb = new System.Text.StringBuilder();
                LevelSelectState.DumpTransformHierarchy(TargetView.transform, "", sb);
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LvlDiag] TargetView ({TargetView.GetType().Name}) full hierarchy:\n{sb}");
            }

            // Removed FindObjectsOfType search (crashes in IL2CPP)
        }

        void Update()
        {
            _elapsed += Time.deltaTime;
            _frameCount++;

            if (TargetView == null)
            {
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Guard] Stopped after {_frameCount} frames ({_elapsed:F1}s) - target null");
                Destroy(this);
                return;
            }

            if (_elapsed > _maxDuration)
            {
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Guard] Stopped after {_frameCount} frames ({_elapsed:F1}s) - idle timeout (max={_maxDuration}F1)");
                Destroy(this);
                return;
            }

            // ── Diagnostic: dump AirportInfo InfoItems once LevelPart is active ──
            if (!_lvlDiagDone3)
            {
                var levelPart = TargetView.transform.Find("LevelPart");
                if (levelPart != null && levelPart.gameObject.activeInHierarchy)
                {
                    var airportInfo = levelPart.Find("AirportInfo");
                    if (airportInfo != null && airportInfo.gameObject.activeInHierarchy)
                    {
                        _lvlDiagDone3 = true;
                        var group = airportInfo.Find("group");
                        if (group != null)
                        {
                            var sb = new System.Text.StringBuilder();
                            for (int i = 0; i < group.childCount; i++)
                            {
                                var child = group.GetChild(i);
                                var icon = child.Find("Icon");
                                var iconImg = icon?.GetComponent<Image>();
                                var allTmp = child.GetComponentsInChildren<TMP_Text>(true);
                                var allText = child.GetComponentsInChildren<Text>(true);
                                var label = "";
                                foreach (var t in allTmp) if (!string.IsNullOrEmpty(t.text)) { label = t.text; break; }
                                if (string.IsNullOrEmpty(label))
                                    foreach (var t in allText) if (!string.IsNullOrEmpty(t.text)) { label = t.text; break; }
                                sb.AppendLine($"  InfoItem[{i}] '{child.name}': icon='{iconImg?.sprite?.name ?? "NULL"}' label='{label}'");
                            }
                            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LvlDiag] AirportInfo group InfoItems:\n{sb}");
                        }
                        var levelList = levelPart.Find("LevelList");
                        if (levelList != null)
                        {
                            var viewport = levelList.Find("Viewport");
                            var content = viewport?.Find("Content");
                            if (content != null && content.childCount > 0)
                            {
                                var sb2 = new System.Text.StringBuilder();
                                for (int i = 0; i < content.childCount; i++)
                                {
                                    var item = content.GetChild(i);
                                    if (item.gameObject.activeInHierarchy)
                                        LevelSelectState.DumpTransformHierarchy(item, "  ", sb2);
                                }
                                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LvlDiag] LevelList Content ({content.childCount} items):\n{sb2}");
                            }
                        }
                    }
                }
            }

            try
            {
                // Kill DOTween transitions first, then re-apply our bg+sprite+color
                LevelSelectState.KillBackgroundTweens(TargetView);
                LevelSelectState.ApplyBg(TargetView, SceneKey);
                LevelSelectState.ApplyAirportItemPreviews(TargetView);
                LevelSelectState.ApplyChannelIcons(TargetView);

                _textTimer += Time.deltaTime;
                if (_textTimer >= TextRefreshInterval)
                {
                    _textTimer = 0f;
                    LevelSelectState.ApplyDiagram(TargetView, SceneKey);
                    LevelSelectState.FullRefresh(TargetView);
                }

                // heartbeat removed (verbose)
            }
            catch { }
        }

        void OnDestroy()
        {
            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Guard] Destroyed — total frames={_frameCount}, elapsed={_elapsed:F1}s");
        }

        static string GetPath(Transform t)
        {
            var path = t.name;
            var p = t.parent;
            while (p != null) { path = p.name + "/" + path; p = p.parent; }
            return path;
        }
    }

    // ==================== Settings Page Text Overrides ====================


}
