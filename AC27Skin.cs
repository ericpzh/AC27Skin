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
    [BepInPlugin("com.ac27.skin", "AC27 Skin", "3.1.1")]
    public class AC27SkinPlugin : BasePlugin
    {
        internal static BepInEx.Logging.ManualLogSource Logger;

        // IL2CPP-safe alternative to PatchAll — manually resolves TargetMethod and patches
        // without triggering Assembly.GetTypes() which throws TypeLoadException on IL2CPP types.
        static void SafePatchAll(Harmony harmony, Type patchType)
        {
            // 1. Resolve target method: either via TargetMethod() or [HarmonyPatch] attribute
            MethodBase target = null;
            var tm = patchType.GetMethod("TargetMethod",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (tm != null) target = tm.Invoke(null, null) as MethodBase;

            if (target == null)
            {
                var attr = patchType.GetCustomAttribute<HarmonyPatch>();
                if (attr?.info?.declaringType != null && !string.IsNullOrEmpty(attr.info.methodName))
                    target = AccessTools.Method(attr.info.declaringType, attr.info.methodName);
            }
            if (target == null) return; // can't patch

            // 2. Find prefix/postfix/transpiler/finalizer methods by attribute
            HarmonyMethod prefix = null, postfix = null, transpiler = null, finalizer = null;
            foreach (var m in patchType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.GetCustomAttribute<HarmonyPrefix>() != null)
                    prefix = new HarmonyMethod(m);
                else if (m.GetCustomAttribute<HarmonyPostfix>() != null)
                    postfix = new HarmonyMethod(m);
                else if (m.GetCustomAttribute<HarmonyTranspiler>() != null)
                    transpiler = new HarmonyMethod(m);
                else if (m.GetCustomAttribute<HarmonyFinalizer>() != null)
                    finalizer = new HarmonyMethod(m);
            }

            var processor = harmony.CreateProcessor(target);
            if (prefix != null)    processor.AddPrefix(prefix);
            if (postfix != null)   processor.AddPostfix(postfix);
            if (transpiler != null) processor.AddTranspiler(transpiler);
            if (finalizer != null) processor.AddFinalizer(finalizer);
            processor.Patch();
        }

        public override void Load()
        {
            Logger = Log;  // expose for static helpers across the namespace

            Log.LogInfo("========================================");
            Log.LogInfo(" AC25 Code Mod v3.1.1 LOADED! (Harmony)");
            Log.LogInfo("========================================");

            var harmony = new Harmony("com.ac25.mod.harmony");

            // All patches via SafePatchAll — never calls Assembly.GetTypes()
            void TryPatch(string name, Type patchType)
            {
                try
                {
                    SafePatchAll(harmony, patchType);
                    Log.LogInfo($"[Diag] Patch [{name}] OK");
                }
                catch (Exception ex)
                {
                    Log.LogError($"[Diag] Patch [{name}] FAILED: {ex.GetType().Name}: {ex.Message}");
                }
            }

            TryPatch("MainMenuPatch",          typeof(MainMenuPatch));
            TryPatch("UpdateLogoPatch",         typeof(UpdateLogoPatch));
            TryPatch("LvSel_Init",              typeof(LvSel_Init));
            TryPatch("LvSel_Review",            typeof(LvSel_Review));
            TryPatch("LvSel_HideReview",        typeof(LvSel_HideReview));
            TryPatch("LvSel_ShowLevel",         typeof(LvSel_ShowLevel));
            TryPatch("LvSel_ShowAirport",       typeof(LvSel_ShowAirport));
            TryPatch("LvSel_AirportList",       typeof(LvSel_AirportList));
            TryPatch("LvSel_LevelPartName",     typeof(LvSel_LevelPartName));
            TryPatch("LvSel_LevelList",         typeof(LvSel_LevelList));
            TryPatch("LvSel_InitBg",            typeof(LvSel_InitBg));
            TryPatch("AirportItem_Exit",        typeof(AirportItem_Exit));
            TryPatch("AirportItem_Create",      typeof(AirportItem_Create));
            TryPatch("AirportItem_Enter",       typeof(AirportItem_Enter));
            TryPatch("QuitViewPatch",            typeof(QuitViewPatch));
            TryPatch("QuitWithWishlistPatch",   typeof(QuitWithWishlistViewPatch));
            TryPatch("LiveryModdingPatch",      typeof(LiveryModdingViewProxyPatch));
            TryPatch("SettingsTextPatch",       typeof(SettingsTextPatch));
            TryPatch("SettingsTextPatch.Show",  typeof(SettingsTextPatch.ShowPatch));
            // All aircraft overrides disabled — keep game defaults
            //TryPatch("FlightDirectionOverride", typeof(FlightDirectionOverride));
            //TryPatch("CallSignOverride",        typeof(CallSignOverride));
            //TryPatch("TaxiSpeedOverride",       typeof(TaxiSpeedOverride));
            //TryPatch("DynamicsUpdatePostfix",   typeof(DynamicsUpdatePostfix));
            //TryPatch("AircraftLogPostfix",      typeof(AircraftLogPostfix));
            //TryPatch("StripBtnViewInitPatch",   typeof(StripBtnViewInitPatch));
            //TryPatch("CallSignViewInitPatch",   typeof(CallSignViewInitPatch));

            // === LoadingView Tips override — double hook ===
            // Use safe type lookup that never calls Assembly.GetTypes() on UnityEngine.CoreModule
            Type lvType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var asmName = asm.GetName().Name;
                if (asmName.StartsWith("UnityEngine.") || asmName.StartsWith("Unity.")) continue;
                try { lvType = asm.GetType("ContextCross.View.Menu.LoadingView", false); if (lvType != null) break; }
                catch { }
            }
            if (lvType != null)
            {
                // Hook 1: Prefix on OnStringLoaded(string newText) — intercept localization string
                var mOnStringLoaded = lvType.GetMethod("OnStringLoaded", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mOnStringLoaded != null)
                {
                    var p = harmony.CreateProcessor(mOnStringLoaded);
                    p.AddPrefix(new HarmonyMethod(typeof(LoadingTipOverride), nameof(LoadingTipOverride.OnStringLoadedPrefix)));
                    p.Patch();
                    Log.LogInfo("[Diag] Patch [OnStringLoaded] OK");
                }
                else { Log.LogWarning("[Diag] Patch [OnStringLoaded] FAILED"); }

                // Hook 2: Postfix on Show(LoadingContext) — belt-and-suspenders for level scene loads
                var mShow = lvType.GetMethod("Show", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mShow != null)
                {
                    var p = harmony.CreateProcessor(mShow);
                    p.AddPostfix(new HarmonyMethod(typeof(LoadingTipOverride), nameof(LoadingTipOverride.ShowPostfix)));
                    p.Patch();
                    Log.LogInfo("[Diag] Patch [Show] OK");
                }
                else { Log.LogWarning("[Diag] Patch [Show] FAILED"); }
            }
            else { Log.LogWarning("[Diag] Patch [LoadingTip] FAILED — LoadingView type not found"); }

            Log.LogInfo(" Load() complete — all patches registered");


            ClassInjector.RegisterTypeInIl2Cpp<ModBehaviour>();
            ClassInjector.RegisterTypeInIl2Cpp<DelayedTextUpdater>();
            ClassInjector.RegisterTypeInIl2Cpp<BackgroundGuard>();
            ClassInjector.RegisterTypeInIl2Cpp<SettingsDelayedUpdater>();
            SceneManager.add_sceneLoaded(new Action<Scene, LoadSceneMode>(OnSceneLoaded));
        }

        private static string FixedTipText => TextOverridesConfig.FixedTipText;
        internal static MonoBehaviour _cachedLoadingView;

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Tips] OnSceneLoaded: scene='{scene.name}' mode={mode}");

                var existing = GameObject.Find("AC27Skin_Manager");
                if (existing == null)
                {
                    var go = new GameObject("AC27Skin_Manager");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<ModBehaviour>();
                }

                // Find LoadingView via FindObjectsOfTypeAll (more reliable than scene-root scanning)
                CacheLoadingViewIfMissing();
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogError("[AC27Skin] Scene injection error: " + ex);
            }
        }

        // Scans ALL loaded scenes for LoadingView (not just the newly loaded one)
        internal static void CacheLoadingViewIfMissing()
        {
            if (_cachedLoadingView != null && _cachedLoadingView.isActiveAndEnabled) return;
            _cachedLoadingView = null;

            try
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded) continue;
                    foreach (var root in scene.GetRootGameObjects())
                        if (FindLoadingViewOn(root)) return;
                }
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [Tips] CacheLoadingView error: {ex.Message}");
            }
        }

        private static bool FindLoadingViewOn(GameObject go)
        {
            if (go == null) return false;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().FullName == "ContextCross.View.Menu.LoadingView")
                {
                    var mb = c as MonoBehaviour;
                    if (mb != null && mb.isActiveAndEnabled)
                    {
                        // ── Cache immediately even if _tipsText is NULL ──
                        // IL2CPP may resolve serialized fields lazily; ForceTipsText will retry
                        _cachedLoadingView = mb;
                        AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Tips] Cached LoadingView! go='{go.name}'");
                        return true;
                    }
                }
            }
            for (int i = 0; i < go.transform.childCount; i++)
            {
                if (FindLoadingViewOn(go.transform.GetChild(i).gameObject))
                    return true;
            }
            return false;
        }

        // Lightweight: force-set _tipsText.text. Used by the continuous guard.
        // - Tries _tipsText field first; falls back to GetComponentInChildren<TMP_Text>
        // - Caches the TMP_Text reference once found for speed
        // - Silent when text is correct (avoid log spam)
        private static TMP_Text _cachedTipsTMP;

        internal static void ForceTipsText(MonoBehaviour loadingView)
        {
            // Use cached TMP_Text if available, otherwise find/re-find it
            if (_cachedTipsTMP == null || !_cachedTipsTMP.gameObject.activeInHierarchy)
            {
                var t = loadingView.GetType();

                // Strategy 1: try reflection field _tipsText (most reliable)
                var tipsField = t.GetField("_tipsText", BindingFlags.Instance | BindingFlags.NonPublic);
                if (tipsField != null)
                    _cachedTipsTMP = tipsField.GetValue(loadingView) as TMP_Text;

                // Strategy 2: GetComponentsInChildren → pick the one with LocalizeStringEvent attached
                if (_cachedTipsTMP == null)
                {
                    var allTMP = loadingView.GetComponentsInChildren<TMP_Text>(true);
                    foreach (var tmp in allTMP)
                    {
                        if (tmp == null || tmp.name.Contains("Progress") || tmp.name.Contains("Slider")) continue;
                        // Tips text has a LocalizeStringEvent on the same GameObject
                        var hasLSE = false;
                        var comps = tmp.GetComponents<MonoBehaviour>();
                        foreach (var c in comps)
                        {
                            if (c != null && c.GetType().Name.IndexOf("LocalizeStringEvent", StringComparison.OrdinalIgnoreCase) >= 0)
                            { hasLSE = true; break; }
                        }
                        if (hasLSE) { _cachedTipsTMP = tmp; break; }
                    }
                    // Strategy 3: last resort — any TMP_Text not named "Progress"
                    if (_cachedTipsTMP == null)
                    {
                        foreach (var tmp in allTMP)
                        {
                            if (tmp != null && !tmp.name.Contains("Progress") && !tmp.name.Contains("Slider"))
                            { _cachedTipsTMP = tmp; break; }
                        }
                    }
                }

                if (_cachedTipsTMP == null) return; // still not available

                // Disable _tipsTextEvent field on LoadingView
                var evtField = t.GetField("_tipsTextEvent", BindingFlags.Instance | BindingFlags.NonPublic);
                if (evtField != null)
                {
                    var locEvt = evtField.GetValue(loadingView) as MonoBehaviour;
                    if (locEvt != null && locEvt.enabled) { locEvt.enabled = false; AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [Tips] Disabled _tipsTextEvent on first cache"); }
                }
                // Also disable all LocalizeStringEvent components on the TMP_Text's GameObject
                foreach (var lse in _cachedTipsTMP.GetComponents<MonoBehaviour>())
                {
                    if (lse != null && lse.GetType().Name.IndexOf("LocalizeStringEvent", StringComparison.OrdinalIgnoreCase) >= 0)
                    { lse.enabled = false; AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Tips] Disabled {lse.GetType().Name} on tips TMP"); }
                }
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Tips] Tips TMP cached: name='{_cachedTipsTMP.name}' id={_cachedTipsTMP.GetInstanceID()}");
            }

            var tipsText = _cachedTipsTMP;
            var currentText = tipsText.text ?? "";

            if (currentText == FixedTipText) return; // already correct, silent

            // Write via property + internal m_text field (belt-and-suspenders for IL2CPP)
            tipsText.text = FixedTipText;

            var mTextField = typeof(TMP_Text).GetField("m_text", BindingFlags.Instance | BindingFlags.NonPublic);
            if (mTextField != null)
                mTextField.SetValue(tipsText, FixedTipText);

            tipsText.ForceMeshUpdate(true, true);

            var shortOld = currentText.Length > 30 ? currentText.Substring(0, 30) + "..." : currentText;
            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Tips] Guard wrote! old='{shortOld}'");
        }
    }

    // ==================== Shared text replacement logic ====================

    public static class TextReplacer
    {
        // Loaded from text.txt
        // NOTE: the sorted list is re-created on each access — cache locally if calling in a hot loop.
        public static List<KeyValuePair<string, string>> AllTextSorted => TextOverridesConfig.AllTextSorted;

        private static string CompanyReplaceFrom => TextOverridesConfig.CompanyFrom;
        private static string CompanyReplaceTo => TextOverridesConfig.CompanyTo;

        /// Disable ALL LocalizeStringEvent components in the view hierarchy BEFORE text replacement.
        /// This prevents the game's localization system from overwriting our custom text.
        public static int DisableAllLocalization(GameObject root)
        {
            int count = 0;
            var allMono = root.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var mb in allMono)
            {
                if (mb != null && mb.GetType().FullName == "UnityEngine.Localization.Components.LocalizeStringEvent")
                {
                    mb.enabled = false;
                    count++;
                }
            }
            return count;
        }

        public static void SafeSetTMPText(TMP_Text tmp, string newText)
        {
            if (tmp == null) return;
            tmp.text = newText;
            tmp.ForceMeshUpdate(true, true);
        }

        public static void TryDisableLocalization(TMP_Text tmp)
        {
            if (tmp == null) return;
            var go = tmp.gameObject;
            for (int i = 0; i < 4 && go != null; i++)
            {
                var allMono = go.GetComponents<MonoBehaviour>();
                foreach (var mb in allMono)
                {
                    if (mb != null && mb.GetType().FullName == "UnityEngine.Localization.Components.LocalizeStringEvent")
                    {
                        mb.enabled = false;
                    }
                }
                go = go.transform.parent?.gameObject;
            }
        }

        /// Strip newlines so that Contains/Replace works even when the game TMP
        /// text has embedded \\n while the override key in text.txt doesn't.
        public static string NormalizeForMatch(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\r\n", "").Replace("\n", "").Replace("\r", "");
        }

        public static int ReplaceButtonTexts(MonoBehaviour menuView)
        {
            int replaced = 0;
            var map = AllTextSorted;

            // 1. Search TMP inside Buttons
            var buttons = menuView.GetComponentsInChildren<Button>(true);
            foreach (var btn in buttons)
            {
                if (btn == null) continue;
                var allTMP = btn.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var tmp in allTMP)
                {
                    if (tmp == null || string.IsNullOrEmpty(tmp.text)) continue;
                    string original = NormalizeForMatch(tmp.text);
                    foreach (var kv in map)
                    {
                        if (original.Contains(kv.Value) && kv.Key != kv.Value)
                            continue;
                        if (original.Contains(kv.Key))
                        {
                            SafeSetTMPText(tmp, original.Replace(kv.Key, kv.Value));
                            replaced++;
                            break;
                        }
                    }
                }
            }

            // 2. Also search ALL TMP in the menuView (some labels are outside buttons)
            var allTMP2 = menuView.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var tmp in allTMP2)
            {
                if (tmp == null || string.IsNullOrEmpty(tmp.text)) continue;
                string original = NormalizeForMatch(tmp.text);
                foreach (var kv in map)
                {
                    if (original.Contains(kv.Value) && kv.Key != kv.Value)
                        continue;
                    if (original.Contains(kv.Key))
                    {
                        SafeSetTMPText(tmp, original.Replace(kv.Key, kv.Value));
                        replaced++;
                        break;
                    }
                }
            }

            return replaced;
        }

        public static bool ReplaceVersionText(MonoBehaviour menuView)
        {
            var allTMP = menuView.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var tmp in allTMP)
            {
                if (tmp == null || string.IsNullOrEmpty(tmp.text)) continue;
                // Match either "playtest/" pattern (release) or version number like "0000" / "0.0.0"
                int idx = tmp.text.IndexOf("playtest/", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) idx = tmp.text.IndexOf("0000");   // playtest builds use "0000"
                if (idx < 0) idx = tmp.text.IndexOf("游戏版本"); // fallback: "游戏版本" prefix
                if (idx >= 0)
                {
                    // Keep prefix text before version, add version + newline + company
                    string prefix;
                    if (tmp.text.IndexOf("游戏版本") >= 0)
                        prefix = tmp.text.Substring(0, tmp.text.IndexOf(":") >= 0 ? tmp.text.IndexOf(":") + 1 : tmp.text.Length);
                    else
                        prefix = tmp.text.Substring(0, idx);
                    string verText = TextOverridesConfig.VersionText ?? "伊雷娜版";
                    SafeSetTMPText(tmp, prefix + " " + verText + "\n" + CompanyReplaceTo);
                    AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Text] Version replaced: prefix='{prefix}' ver='{verText}' company='{CompanyReplaceTo}'");
                    return true;
                }
            }
            return false;
        }

        public static int ReplaceCompanyText(MonoBehaviour menuView)
        {
            var allTMP = menuView.GetComponentsInChildren<TextMeshProUGUI>(true);
            int count = 0;
            foreach (var tmp in allTMP)
            {
                if (tmp == null || string.IsNullOrEmpty(tmp.text)) continue;
                if (tmp.text.Contains(CompanyReplaceFrom))
                {
                    SafeSetTMPText(tmp, tmp.text.Replace(CompanyReplaceFrom, CompanyReplaceTo));
                    count++;
                }
            }
            return count;
        }

        /// Replace all TextMeshProUGUI texts using the text.txt override map (exact or partial match)
        public static int ReplaceTexts(MonoBehaviour view)
        {
            int cnt = 0;
            var map = AllTextSorted;
            var all = view.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var t in all)
            {
                if (t == null || string.IsNullOrEmpty(t.text)) continue;
                bool changed = false;
                string normalized = NormalizeForMatch(t.text);
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
                    TryDisableLocalization(t);
                    cnt++;
                }
            }
            return cnt;
        }
    }

    // ==================== Patch: UpdateLogo() - intercept logo init ====================

    [HarmonyPatch]
    public static class UpdateLogoPatch
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("ContextCross.View.Menu.MainMenuView");
            return type != null ? AccessTools.Method(type, "UpdateLogo") : null;
        }

        [HarmonyPostfix]
        static void Postfix(MonoBehaviour __instance)
        {
            try
            {
                AC27SkinPlugin.Logger.LogInfo("[AC27Skin] >>> UpdateLogo() called! Replacing logo + text...");
                LogoReplacer.Replace(__instance);
                // Re-disable localization (UpdateLogo may create new localized children)
                int locsDisabled = TextReplacer.DisableAllLocalization(__instance.gameObject);
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin]   UpdateLogo: disabled {locsDisabled} new LocalizeStringEvent(s)");
                // Re-apply text replacements (for new localized children)
                TextReplacer.ReplaceButtonTexts(__instance);
                TextReplacer.ReplaceVersionText(__instance);
                TextReplacer.ReplaceCompanyText(__instance);
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogError("[AC27Skin] UpdateLogo patch error: " + ex);
            }
        }
    }

    // ==================== Logo replacement logic ====================

    public static class LogoReplacer
    {
        // Cache the loaded texture/sprite to avoid repeated I/O
        private static Texture2D _cachedTex;
        private static Sprite _cachedSprite;
        private static bool _loadAttempted;
        // The child Image rect is 807x720, preserveAspect compresses our 1920x1080 sprite
        // to 807x454. BaseScale=2.0 → rendered 1614x908 in a 1920x1080 canvas = 84% fill.
        // This looks good at all resolutions because Bg width already tracks canvas scaling.
        private const float BaseLogoScale = 3.1f;
        private const float RefWidth = 1920f;

        private static void EnsureLoaded()
        {
            // In IL2CPP, scene unload destroys native Texture2D/Sprite but leaves
            // managed wrappers alive. Unity's == returns true for destroyed objects.
            // Re-load if cache was invalidated by scene unload.
            if (_loadAttempted && _cachedTex != null && _cachedSprite != null) return;

            // Clean up stale/destroyed cache
            _cachedTex = null;
            _cachedSprite = null;
            _loadAttempted = true;

            string path = Path.Combine(Paths.GameRootPath, "overrides", "title.png");
            if (!File.Exists(path))
            {
                AC27SkinPlugin.Logger.LogWarning("[AC27Skin] [Logo] title.png not found: " + path);
                return;
            }

            byte[] data = File.ReadAllBytes(path);
            _cachedTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(_cachedTex, data))
            {
                AC27SkinPlugin.Logger.LogError("[AC27Skin] [Logo] Failed to decode title.png");
                _cachedTex = null;
                return;
            }
            _cachedSprite = Sprite.Create(_cachedTex,
                new Rect(0, 0, _cachedTex.width, _cachedTex.height),
                new Vector2(0.5f, 0.5f));
            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Logo] Loaded title.png: {_cachedTex.width}x{_cachedTex.height}");
        }

        /// Find LogoHolder by traversing hierarchy (IL2CPP reflection can't see private fields)
        public static Transform FindLogoHolder(MonoBehaviour menuView)
        {
            if (menuView == null) return null;
            var t = menuView.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (child.name == "LogoHolder")
                    return child;
            }
            return null;
        }

        public static void Replace(MonoBehaviour menuView)
        {
            try
            {
                var logoHolder = FindLogoHolder(menuView);
                if (logoHolder == null) return;

                EnsureLoaded();
                if (_cachedTex == null) return;

                int replaced = 0;
                bool firstTime = _lastReplacedLogoChildCount < 0;
                int currentChildren = logoHolder.childCount;

                var scale = new Vector3(BaseLogoScale, BaseLogoScale, 1f);

                // 1. Check logoHolder GameObject itself
                var ownImg = logoHolder.GetComponent<Image>();
                if (ownImg != null) { ownImg.sprite = _cachedSprite; ownImg.preserveAspect = true; logoHolder.localScale = scale; replaced++; }
                var ownRaw = logoHolder.GetComponent<RawImage>();
                if (ownRaw != null) { ownRaw.texture = _cachedTex; logoHolder.localScale = scale; replaced++; }

                // 2. Check ALL children (including inactive)
                var allImages = logoHolder.GetComponentsInChildren<Image>(true);
                foreach (var img in allImages)
                {
                    if (img == null) continue;
                    img.sprite = _cachedSprite;
                    img.preserveAspect = true;
                    img.transform.localScale = scale;
                    replaced++;
                }
                var allRaw = logoHolder.GetComponentsInChildren<RawImage>(true);
                foreach (var raw in allRaw)
                {
                    if (raw == null) continue;
                    raw.texture = _cachedTex;
                    raw.transform.localScale = scale;
                    replaced++;
                }

                // 3. Also check parent
                if (logoHolder.parent != null)
                {
                    var parentImg = logoHolder.parent.GetComponent<Image>();
                    if (parentImg != null) { parentImg.sprite = _cachedSprite; parentImg.preserveAspect = true; replaced++; }
                }

                // Only log on first run or when child count changes
                if (firstTime || currentChildren != _lastReplacedLogoChildCount)
                {
                    AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Logo] Replaced: {replaced} component(s) on {currentChildren} child(ren)");
                    _lastReplacedLogoChildCount = currentChildren;
                }
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogError("[AC27Skin] [Logo] Error: " + ex);
            }
        }
        
        private static int _lastReplacedLogoChildCount = -1;
    }

    // ==================== Harmony Patch: MainMenuView.OnEnable() ====================

    [HarmonyPatch]
    public static class MainMenuPatch
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("ContextCross.View.Menu.MainMenuView");
            return type != null ? AccessTools.Method(type, "OnEnable") : null;
        }

        [HarmonyPostfix]
        static void PatchMainMenu(MonoBehaviour __instance)
        {
            try
            {
                AC27SkinPlugin.Logger.LogInfo("[AC27Skin] >>> OnEnable() fired!");

                // 1. Full reflection dump
                DumpAllPrivateFields(__instance);

                // 2. Full hierarchy dump
                DumpTransformHierarchy(__instance.transform, 0);

                // 3. Check logoHolder's own components
                CheckLogoHolderOwnComponents(__instance);

                // 4. Nuke all localization BEFORE touching text (makes replacements stick!)
                int locsDisabled = TextReplacer.DisableAllLocalization(__instance.gameObject);
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin]   Disabled {locsDisabled} LocalizeStringEvent(s)");

                // 5. Text replacement (should stick now — no localization to revert)
                int btns = TextReplacer.ReplaceButtonTexts(__instance);
                bool ver = TextReplacer.ReplaceVersionText(__instance);
                int co = TextReplacer.ReplaceCompanyText(__instance);
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin]   Text: buttons={btns}, version={ver}, company={co}");

                // 6. Logo (might already have children from inspector-setup)
                LogoReplacer.Replace(__instance);

                // 7. Schedule ONE delayed verification (catches any late-loaded children)
                ScheduleDelayedUpdate(__instance);

                AC27SkinPlugin.Logger.LogInfo("[AC27Skin]   OnEnable patch DONE.");
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogError("[AC27Skin] OnEnable error: " + ex);
            }
        }

        // ====== DIAGNOSTIC DUMP ======

        static void DumpAllPrivateFields(MonoBehaviour menuView)
        {
            var type = menuView.GetType();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var fields = type.GetFields(flags);
            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] === FIELD DUMP ({fields.Length} fields) ===");

            foreach (var field in fields)
            {
                try
                {
                    var val = field.GetValue(menuView);
                    string s;
                    if (val == null)
                    {
                        s = "NULL";
                    }
                    else
                    {
                        var vt = val.GetType();
                        if (vt == typeof(string)) s = $"\"{val}\"";
                        else if (vt == typeof(bool) || vt == typeof(int) || vt == typeof(float)) s = val.ToString();
                        else if (typeof(UnityEngine.Object).IsAssignableFrom(vt))
                        {
                            var uo = val as UnityEngine.Object;
                            s = uo ? $"[{vt.Name}] name='{uo.name}'" : $"[{vt.Name}] (destroyed)";
                        }
                        else s = $"[{vt.Name}] {val}";
                    }
                    AC27SkinPlugin.Logger.LogInfo($"[AC27Skin]   {field.FieldType.Name} {field.Name} = {s}");
                }
                catch { }
            }
        }

        static void DumpTransformHierarchy(Transform t, int depth)
        {
            if (t == null || depth > 4) return;
            string pad = new string(' ', depth * 2);
            var comps = t.GetComponents<Component>();
            var cn = new List<string>();
            foreach (var c in comps) { if (c != null) cn.Add(c.GetType().Name); }

            // Also log TMP text if any
            var tmp = t.GetComponent<TextMeshProUGUI>();
            string txtInfo = tmp != null ? $" TMP='{tmp.text}'" : "";

            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin]   {pad}{t.name} active={t.gameObject.activeSelf} [{string.Join(", ", cn)}]{txtInfo}");

            int max = (depth == 0 && t.childCount > 25) ? 25 : t.childCount;
            for (int i = 0; i < max; i++)
                DumpTransformHierarchy(t.GetChild(i), depth + 1);
            if (max < t.childCount)
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin]   {pad}  ... +{t.childCount - max} more");
        }

        static void CheckLogoHolderOwnComponents(MonoBehaviour menuView)
        {
            var lh = LogoReplacer.FindLogoHolder(menuView);
            if (lh == null) { AC27SkinPlugin.Logger.LogWarning("[AC27Skin]   LogoHolder NOT FOUND in hierarchy"); return; }

            var comps = lh.GetComponents<Component>();
            var names = new List<string>();
            foreach (var c in comps) { if (c != null) names.Add(c.GetType().Name); }
            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin]   LogoHolder '{lh.name}' OWN comps: [{string.Join(", ", names)}]");
            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin]   LogoHolder OWN: Image={lh.GetComponent<Image>() != null}, RawImage={lh.GetComponent<RawImage>() != null}, CanvasRenderer={lh.GetComponent<CanvasRenderer>() != null}");

            // Dump children at OnEnable time
            if (lh.childCount > 0)
            {
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin]   LogoHolder has {lh.childCount} children at OnEnable:");
                for (int i = 0; i < lh.childCount; i++)
                {
                    var child = lh.GetChild(i);
                    var ccomps = child.GetComponents<Component>();
                    var cn = new List<string>();
                    foreach (var c in ccomps) { if (c != null) cn.Add(c.GetType().Name); }
                    AC27SkinPlugin.Logger.LogInfo($"[AC27Skin]     [{i}] '{child.name}' active={child.gameObject.activeSelf} [{string.Join(", ", cn)}]");
                }
            }
            else
            {
                AC27SkinPlugin.Logger.LogInfo("[AC27Skin]   LogoHolder has 0 children at OnEnable (will be populated by UpdateLogo)");
            }
        }

        // ====== DELAYED UPDATE ======

        public static void ScheduleDelayedUpdate(MonoBehaviour menuView)
        {
            var go = menuView.gameObject;
            var updater = go.GetComponent<DelayedTextUpdater>();
            if (updater == null) updater = go.AddComponent<DelayedTextUpdater>();
            updater.menuView = menuView;
            updater.ResetTimer();
        }
    }

    // ==================== Delayed Text Updater (Update-based near-instant) ====================
    // Pass 1: 0.01s (next frame, <=16ms) — catches async-loaded UI nearly instantly
    // Pass 2: 0.2s — safety net
    // Pass 3: 0.5s — final safety

    public class DelayedTextUpdater : MonoBehaviour
    {
        public MonoBehaviour menuView;
        private float _timer;
        private int _attempt;
        private float _nextDelay = 0.01f;  // first pass almost instant

        public DelayedTextUpdater(IntPtr ptr) : base(ptr) { }

        public void ResetTimer()
        {
            _timer = 0f;
            _attempt = 0;
            _nextDelay = 0.01f;
        }

        void Update()
        {
            if (menuView == null)
            {
                Destroy(this);
                return;
            }

            _timer += Time.deltaTime;
            if (_timer < _nextDelay) return;
            _timer = 0f;

            _attempt++;
            try
            {
                int locsDisabled = TextReplacer.DisableAllLocalization(menuView.gameObject);
                int btns = TextReplacer.ReplaceButtonTexts(menuView);
                int co = TextReplacer.ReplaceCompanyText(menuView);
                int txts = TextReplacer.ReplaceTexts(menuView);
                LogoReplacer.Replace(menuView);

                int total = btns + co + txts;
                if (total > 0 || locsDisabled > 0)
                    AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Delay:{_attempt}] btns={btns} co={co} locsDisabled={locsDisabled}");
                else
                    AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Delay:{_attempt}] all stable — done.");
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogError($"[AC27Skin] [Delay:{_attempt}] Error: {ex}");
            }

            // Progress the delay for next pass
            if (_attempt == 1) _nextDelay = 0.2f;
            else if (_attempt == 2) _nextDelay = 0.5f;

            if (_attempt >= 3)
            {
                AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [Delay] Finished 3 verification passes");
                Destroy(this);
            }
        }
    }

    // ==================== Patch: QuitView.OnEnable() — apply text overrides to exit confirmation ====================
    [HarmonyPatch]
    public static class QuitViewPatch
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("ContextCross.View.Menu.QuitView");
            return type != null ? AccessTools.Method(type, "OnEnable") : null;
        }

        [HarmonyPostfix]
        static void Postfix(MonoBehaviour __instance)
        {
            try
            {
                AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [QuitView] OnEnable() — applying text overrides");

                // 1. Nuke localization immediately
                int locsDisabled = TextReplacer.DisableAllLocalization(__instance.gameObject);
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [QuitView]   Disabled {locsDisabled} LocalizeStringEvent(s)");

                // 2. Dump hierarchy for diagnosis
                DumpQuitHierarchy(__instance);

                // 3. Text replacement (immediate pass, may be overwritten by async Show)
                int btns = TextReplacer.ReplaceButtonTexts(__instance);
                bool ver = TextReplacer.ReplaceVersionText(__instance);
                int co = TextReplacer.ReplaceCompanyText(__instance);
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [QuitView]   btns={btns}, version={ver}, company={co}");

                // 4. Schedule delayed passes to catch late-loaded UI (same pattern as MainMenu)
                ScheduleDelayedText(__instance);
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogError("[AC27Skin] [QuitView] error: " + ex);
            }
        }

        static void DumpQuitHierarchy(MonoBehaviour __instance)
        {
            var t = __instance.transform;
            for (int i = 0; i < t.childCount && i < 15; i++)
            {
                var c = t.GetChild(i);
                var allTMP = c.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var tmp in allTMP)
                {
                    if (tmp != null && !string.IsNullOrEmpty(tmp.text))
                        AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [QuitView]   child='{c.name}' TMP='{tmp.text}'");
                }
            }
        }

        static void ScheduleDelayedText(MonoBehaviour menuView)
        {
            var go = menuView.gameObject;
            var updater = go.GetComponent<DelayedTextUpdater>();
            if (updater == null) updater = go.AddComponent<DelayedTextUpdater>();
            updater.menuView = menuView;
            updater.ResetTimer();
        }
    }

    // ==================== Patch: QuitWithWishlistView.OnEnable() — apply text overrides to exit+wishlist confirmation ====================
    [HarmonyPatch]
    public static class QuitWithWishlistViewPatch
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("ContextCross.View.Menu.QuitWithWishlistView");
            return type != null ? AccessTools.Method(type, "OnEnable") : null;
        }

        [HarmonyPostfix]
        static void Postfix(MonoBehaviour __instance)
        {
            try
            {
                AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [QuitWishlist] OnEnable() — applying text overrides");

                int locsDisabled = TextReplacer.DisableAllLocalization(__instance.gameObject);
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [QuitWishlist]   Disabled {locsDisabled} LocalizeStringEvent(s)");

                int btns = TextReplacer.ReplaceButtonTexts(__instance);
                bool ver = TextReplacer.ReplaceVersionText(__instance);
                int co = TextReplacer.ReplaceCompanyText(__instance);
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [QuitWishlist]   btns={btns}, version={ver}, company={co}");

                // Schedule delayed passes
                var go = __instance.gameObject;
                var updater = go.GetComponent<DelayedTextUpdater>();
                if (updater == null) updater = go.AddComponent<DelayedTextUpdater>();
                updater.menuView = __instance;
                updater.ResetTimer();
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogError("[AC27Skin] [QuitWishlist] error: " + ex);
            }
        }
    }

    // ==================== Patch: LiveryModdingViewProxy.Awake() — apply text overrides to Mod/Livery view ====================
    [HarmonyPatch]
    public static class LiveryModdingViewProxyPatch
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("ContextCross.View.Menu.LiveryModdingViewProxy");
            return type != null ? AccessTools.Method(type, "Awake") : null;
        }

        [HarmonyPostfix]
        static void Postfix(MonoBehaviour __instance)
        {
            try
            {
                AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [LiveryMod] Awake() — applying text overrides");

                int locsDisabled = TextReplacer.DisableAllLocalization(__instance.gameObject);
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LiveryMod]   Disabled {locsDisabled} LocalizeStringEvent(s)");

                int btns = TextReplacer.ReplaceButtonTexts(__instance);
                bool ver = TextReplacer.ReplaceVersionText(__instance);
                int co = TextReplacer.ReplaceCompanyText(__instance);
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LiveryMod]   btns={btns}, version={ver}, company={co}");

                // Replace all custom text from text.txt
                int extra = TextReplacer.ReplaceTexts(__instance);
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LiveryMod]   extra replaced={extra}");

                // Schedule delayed passes
                var go = __instance.gameObject;
                var updater = go.GetComponent<DelayedTextUpdater>();
                if (updater == null) updater = go.AddComponent<DelayedTextUpdater>();
                updater.menuView = __instance;
                updater.ResetTimer();
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogError("[AC27Skin] [LiveryMod] error: " + ex);
            }
        }
    }

    // ==================== TXT-based Text Override Config ====================
    public static class TextOverridesConfig
    {
        private static bool _loaded;
        private static string _fixedTipText;
        private static string _companyFrom = "CCC Games";
        private static string _companyTo;
        private static string _versionText;
        private static Dictionary<string, string> _allText;

        // Image maps are hardcoded (not in text.txt)
        private static readonly Dictionary<string, string> _lvlSelBgMap = new() {
            {"tutorial", "tutorial/background.png"},
            {"zsjn", "zsjn/background.png"},
            {"kjfk", "kjfk/background.png"}
        };
        private static readonly Dictionary<string, string> _lvlSelDiaMap = new() {
            {"zsjn", "zsjn/diagram.png"},
            {"kjfk", "kjfk/diagram.png"}
        };
        private static readonly Dictionary<string, string> _lvlSelBtnMap = new() {
            {"tutorial", "tutorial/button.png"},
            {"zsjn", "zsjn/button.png"},
            {"kjfk", "kjfk/button.png"}
        };

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            string path = Path.Combine(Paths.GameRootPath, "overrides", "text.txt");
            if (!File.Exists(path)) { AC27SkinPlugin.Logger.LogError("[AC27Skin] [Config] text.txt not found at: " + path); return; }
            try
            {
                _allText = new Dictionary<string, string>();

                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                    // Skip image-path entries (they're hardcoded above)
                    if (line.EndsWith(".png")) continue;

                    int colonIdx = line.IndexOf(':');
                    if (colonIdx <= 0 || colonIdx >= line.Length - 1) continue;

                    string key = line.Substring(0, colonIdx);
                    string value = line.Substring(colonIdx + 1);

                    // Special keys (not general text replacements)
                    if (key == "tips") { _fixedTipText = value; continue; }
                    if (key == "version") { _versionText = value; continue; }
                    if (key == "CCC Games") { _companyTo = value; continue; }

                    // Everything else: flat key→value — original in-game text → replacement
                    _allText[key] = value;
                }

                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Config] text.txt loaded: tips={_fixedTipText!=null}, version={_versionText}, company={_companyTo}, textOverrides={_allText.Count}");
            }
            catch (Exception ex) { AC27SkinPlugin.Logger.LogError("[AC27Skin] [Config] Failed to load text.txt: " + ex.Message); }
        }

        public static string FixedTipText { get { EnsureLoaded(); return _fixedTipText; } }
        public static string CompanyFrom { get { EnsureLoaded(); return _companyFrom; } }
        public static string CompanyTo { get { EnsureLoaded(); return _companyTo; } }
        public static string VersionText { get { EnsureLoaded(); return _versionText; } }

        /// All text overrides as a flat key→value map.
        /// Sorted by key length descending so "非常低" matches before "低".
        public static List<KeyValuePair<string, string>> AllTextSorted
        {
            get
            {
                EnsureLoaded();
                var list = new List<KeyValuePair<string, string>>(_allText);
                list.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
                return list;
            }
        }

        public static IReadOnlyDictionary<string, string> LvlSelBgMap { get { EnsureLoaded(); return _lvlSelBgMap; } }
        public static IReadOnlyDictionary<string, string> LvlSelDiaMap { get { EnsureLoaded(); return _lvlSelDiaMap; } }
        public static IReadOnlyDictionary<string, string> LvlSelBtnMap { get { EnsureLoaded(); return _lvlSelBtnMap; } }
    }

    // ==================== LevelSelectView state & logic ====================

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

    public static class SettingsTextState
    {
        // Loaded from text.txt — unified flat map, sorted by key length descending
        private static List<KeyValuePair<string, string>> SortedMap => TextOverridesConfig.AllTextSorted;

        /// Apply settings text replacements to the given MonoBehaviour.
        public static int Apply(MonoBehaviour obj)
        {
            return ApplyDetailed(obj, false);
        }

        /// Same as Apply but with per-TMP diagnostic logging when verbose=true.
        public static int ApplyDetailed(MonoBehaviour obj, bool verbose)
        {
            int count = 0;
            var map = SortedMap;
            var allTMP = obj.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (verbose)
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Settings:DIAG] obj={obj.name}, active={obj.gameObject.activeSelf}, TMP count={allTMP.Length}");

            int skippedEmpty = 0, unmatched = 0;
            foreach (var tmp in allTMP)
            {
                if (tmp == null) continue;
                if (string.IsNullOrEmpty(tmp.text)) { skippedEmpty++; continue; }

                string original = TextReplacer.NormalizeForMatch(tmp.text);
                bool found = false;
                foreach (var kv in map)
                {
                    if (original.Contains(kv.Value) && kv.Key != kv.Value)
                        continue;

                    if (original.Contains(kv.Key))
                    {
                        string newText = original.Replace(kv.Key, kv.Value);
                        TextReplacer.SafeSetTMPText(tmp, newText);
                        if (verbose)
                            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Settings:DIAG]   '{tmp.name}' \"{kv.Key}\"→\"{kv.Value}\" | orig=\"{original.Trim()}\"");
                        count++;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    unmatched++;
                    if (verbose)
                        AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Settings:DIAG]   '{tmp.name}' UNMATCHED: \"{original.Trim()}\"");
                }
            }

            if (verbose)
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Settings:DIAG]   summary: matched={count}, unmatched={unmatched}, empty={skippedEmpty}, totalTMP={allTMP.Length}");
            return count;
        }

        public static void ApplyWithLog(MonoBehaviour obj, string tag)
        {
            try
            {
                int count = ApplyDetailed(obj, true);
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Settings:{tag}] Applied {count} text replacements");
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogError($"[AC27Skin] [Settings:{tag}] Error: {ex}");
            }
        }
    }

    // ---- Patch: MainMenuSettingsViewProxy.OnEnable + Show (settings page text override) ----
    // OnEnable fires when the proxy wakes up; Show fires when the settings panel is displayed.
    // We patch BOTH so text overrides are applied early AND late.
    [HarmonyPatch]
    public static class SettingsTextPatch
    {
        // === Patch OnEnable ===
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ContextCross.View.Menu.MainMenuSettingsViewProxy");
            if (t == null) return null;
            // Try OnEnable first
            var m = AccessTools.Method(t, "OnEnable");
            if (m != null) return m;
            // Fallback: find by scanning
            foreach (var mm in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (mm.Name == "OnEnable" && mm.GetParameters().Length == 0) return mm;
            return null;
        }

        [HarmonyPostfix]
        static void Postfix(MonoBehaviour __instance)
        {
            try
            {
                AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [Settings] MainMenuSettingsViewProxy.OnEnable()");
                ApplyToProxy(__instance);
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogError("[AC27Skin] [Settings] OnEnable error: " + ex);
            }
        }

        // === Patch Show (separate Harmony method) ===
        public static class ShowPatch
        {
            static MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("ContextCross.View.Menu.MainMenuSettingsViewProxy");
                if (t == null) return null;
                var m = AccessTools.Method(t, "Show");
                if (m != null) return m;
                foreach (var mm in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    if (mm.Name == "Show") return mm;
                return null;
            }

            [HarmonyPostfix]
            static void Postfix(MonoBehaviour __instance)
            {
                try
                {
                    AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [Settings] MainMenuSettingsViewProxy.Show()");
                    ApplyToProxy(__instance);
                }
                catch { }
            }
        }

        // Shared logic: apply to proxy's children AND settingsView field
        static void ApplyToProxy(MonoBehaviour proxy)
        {
            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Settings:DIAG] ApplyToProxy: proxy.name='{proxy.name}', active={proxy.gameObject.activeSelf}, childCount={proxy.transform.childCount}");

            // Nuke localization FIRST so replacements stick
            int locsDisabled = TextReplacer.DisableAllLocalization(proxy.gameObject);
            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Settings:DIAG] Disabled {locsDisabled} LocalizeStringEvent(s)");

            // Scan the proxy itself (settings UI is in proxy's transform children)
            SettingsTextState.ApplyWithLog(proxy, "proxy");

            // Also try the settingsView field (deeper child)
            var field = proxy.GetType().GetField("settingsView",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                var settingsView = field.GetValue(proxy) as MonoBehaviour;
                if (settingsView != null && settingsView != proxy)
                {
                    TextReplacer.DisableAllLocalization(settingsView.gameObject);
                    SettingsTextState.ApplyWithLog(settingsView, "field");
                }
            }

            // Schedule 2 quick verification passes
            ScheduleSettingsDelayed(proxy);
        }

        static void ScheduleSettingsDelayed(MonoBehaviour proxy)
        {
            var go = proxy.gameObject;
            var updater = go.GetComponent<SettingsDelayedUpdater>();
            if (updater == null) updater = go.AddComponent<SettingsDelayedUpdater>();
            updater.proxy = proxy;
            updater.ResetTimer();
        }
    }

    public class SettingsDelayedUpdater : MonoBehaviour
    {
        public MonoBehaviour proxy;
        private float _timer;
        private int _attempt;

        public SettingsDelayedUpdater(IntPtr ptr) : base(ptr) { }

        public void ResetTimer()
        {
            _timer = 0f;
            _attempt = 0;
        }

        void Update()
        {
            if (proxy == null) { Destroy(this); return; }

            _timer += Time.deltaTime;
            if (_timer < 0.8f) return;
            _timer = 0f;
            _attempt++;

            try
            {
                int locsDisabled = TextReplacer.DisableAllLocalization(proxy.gameObject);
                int count = SettingsTextState.ApplyDetailed(proxy, false);
                if (count > 0 || locsDisabled > 0)
                    AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Settings] Delayed:{_attempt} applied {count} texts, disabled {locsDisabled} locs");
                else
                    AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Settings] Delayed:{_attempt} all stable — done.");
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogError($"[AC27Skin] [Settings] Delayed:{_attempt} error: {ex}");
            }

            if (_attempt >= 2)
            {
                AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [Settings] Delayed: finished 2 verification passes");
                Destroy(this);
            }
        }
    }

    // ==================== Minimal MonoBehaviour ====================

    public class ModBehaviour : MonoBehaviour
    {
        public ModBehaviour(IntPtr ptr) : base(ptr) { }

        private float _aircraftLogTimer;
        //private float _callSignTimer; // DISABLED — callsign overrides removed
        private float _tipsScanTimer;
        private int _guardTick;



        void Awake()
        {
            AC27SkinPlugin.Logger.LogInfo("[AC27Skin] ModBehaviour.Awake()");

            // ── Override taxi speeds via IL2CPP native API (reflection can't touch static readonly in IL2CPP) ──
            try
            {
                IntPtr klass = Il2CppClassPointerStore<Dynamics>.NativeClassPtr;
                if (klass != IntPtr.Zero)
                {
                    // All speed/acceleration overrides disabled — keep game defaults
                    //SetStaticFloat(klass, "StdTaxiSpeed", 210f);
                    //SetStaticFloat(klass, "LowTaxiSpeed", 210f);
                    //SetStaticFloat(klass, "TurnSpeed", 210f);
                    //SetStaticFloat(klass, "ParkingSpeed", 210f);
                    //SetStaticFloat(klass, "BeginTaxiAcceleration", 210f);
                    //SetStaticFloat(klass, "RestartTaxiAcceleration", 210f);
                    //SetStaticFloat(klass, "StopTaxiAcceleration", 210f);
                    //SetStaticFloat(klass, "ParkingAcceleration", 210f);
                    AC27SkinPlugin.Logger.LogInfo("[AC27Skin] Speed & acceleration overrides DISABLED (game defaults)");
                }
                else
                    AC27SkinPlugin.Logger.LogWarning("[AC27Skin] Dynamics native class pointer is null, taxi override skipped");
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] Taxi speed override failed: {ex.Message}");
            }
        }

        private static unsafe void SetStaticFloat(IntPtr klass, string fieldName, float value)
        {
            IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(klass, fieldName);
            if (field != IntPtr.Zero)
                IL2CPP.il2cpp_field_static_set_value(field, &value);
        }

        void Start()
        {
        }

        void Update()
        {

            // ── Periodic aircraft logging (every 5 seconds) ──
            _aircraftLogTimer += Time.deltaTime;
            if (_aircraftLogTimer >= 5f)
            {
                _aircraftLogTimer = 0f;
                LogAllAircraftData();
            }

            // ── Continuous tip guard: only polls when LoadingView is active ──
            _tipsScanTimer += Time.deltaTime;
            if (_tipsScanTimer >= 0.3f)
            {
                _tipsScanTimer = 0f;
                _guardTick++;
                try
                {
                    // If cached view is stale or missing, try to re-find it
                    if (AC27SkinPlugin._cachedLoadingView == null || !AC27SkinPlugin._cachedLoadingView.isActiveAndEnabled)
                    {
                        if (AC27SkinPlugin._cachedLoadingView == null)
                        {
                            AC27SkinPlugin.CacheLoadingViewIfMissing();
                            if (_guardTick % 30 == 1 && AC27SkinPlugin._cachedLoadingView == null)
                                AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [Tips] Guard: no LoadingView found yet");
                        }
                        else
                        {
                            AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [Tips] Guard: cached view became INACTIVE, clearing");
                            AC27SkinPlugin._cachedLoadingView = null;
                        }
                    }

                    if (AC27SkinPlugin._cachedLoadingView != null)
                    {
                        AC27SkinPlugin.ForceTipsText(AC27SkinPlugin._cachedLoadingView);
                    }
                }
                catch { AC27SkinPlugin._cachedLoadingView = null; }
            }

            // ── Periodic callsign override (every 0.5s) ── DISABLED
            //_callSignTimer += Time.deltaTime;
            //if (_callSignTimer >= 0.5f)
            //{
            //    _callSignTimer = 0f;
            //    OverrideAircraftFields();
            //}
        }

        void OnDestroy()
        {
        }

        /// <summary>
        /// Find all Aircraft instances in the scene and log TaxiSpeed, CallSign, FlightDirection.
        /// </summary>
        private void LogAllAircraftData()
        {
            // Minimal scan — no log spam
        }

        /// <summary>
        /// Override TaxiSpeed and/or CallSign on all aircraft. DISABLED
        private void OverrideAircraftFields()
        {
            // All speed/callsign overrides disabled — keep game defaults
            /*
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                string DetermineDisplay(object aircraft)
                {
                    try
                    {
                        var fp = aircraft.GetType().GetProperty("FlightPlan", flags)?.GetValue(aircraft);
                        if (fp == null) return null;
                        var dir = (EFlightDirection?)fp.GetType().GetProperty("FlightDirection", flags)?.GetValue(fp);
                        return dir == EFlightDirection.Departure ? "ES1111" : "CA3115";
                    }
                    catch { return null; }
                }

                // ── Helper: override TMP text on a view ──
                void OverrideTmpOnView(UnityEngine.Object view)
                {
                    try
                    {
                        var vt = view.GetType();
                        var aircraft = vt.GetProperty("_aircraft", flags)?.GetValue(view);
                        if (aircraft == null) return;
                        string display = DetermineDisplay(aircraft);
                        if (display == null) return;
                        var tmp = vt.GetProperty("_textMeshProUGUI", flags)?.GetValue(view) as TMP_Text;
                        if (tmp != null && tmp.text != display)
                            tmp.text = display;
                    }
                    catch { }
                }

                // ── Scan ALL derived views (AircraftButtonBaseView, CallSignView, StripBtnView) ──
                string[] viewTypeNames = {
                    "ContextCross.View.AircraftButtonBaseView",
                    "ContextCross.View.CallSignView",
                    "ContextCross.View.StripBtnView"
                };
                foreach (var tn in viewTypeNames)
                {
                    var t = AccessTools.TypeByName(tn);
                    if (t == null) continue;
                    foreach (var view in Resources.FindObjectsOfTypeAll(Il2CppType.From(t)))
                    {
                        if (view == null) continue;
                        var go = (view as MonoBehaviour)?.gameObject;
                        if (go == null || string.IsNullOrEmpty(go.scene.name)) continue;
                        OverrideTmpOnView(view);
                    }
                }


            }
            catch { }
            */
        }

    }

    // ==================== HOOK: Override FlightDirection (Property Getter) ====================
    // Patches FlightPlanState.get_FlightDirection() — the property getter.
    // This is the CLEANEST hook: it's a C# property, so Harmony can intercept it directly.
    //
    // ── HOW TO USE ──
    //   Uncomment any Prefix/Postfix below.
    //   - Prefix: return false to SKIP the original getter (full override)
    //   - Postfix: modify __result AFTER the original ran (partial override)

    [HarmonyPatch(typeof(FlightPlanState), "get_FlightDirection")]
    public static class FlightDirectionOverride
    {
        // ═══ OPTION A: Force ALL aircraft to Arrival ═══
        // [HarmonyPrefix]
        // static bool Prefix(ref EFlightDirection __result)
        // {
        //     AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [HOOK] get_FlightDirection() → overridden to Arrival");
        //     __result = EFlightDirection.Arrival;
        //     return false; // Skip original getter completely
        // }

        // ═══ OPTION B: Selective override based on CallSign ═══
        // [HarmonyPostfix]
        // static void Postfix(FlightPlanState __instance, ref EFlightDirection __result)
        // {
        //     if (__instance.CallSign == "CCA1234")
        //     {
        //         __result = EFlightDirection.Arrival;
        //         AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [HOOK] {__instance.CallSign} FlightDirection → Arrival");
        //     }
        // }

    }

    // ==================== HOOK: Override CallSign in 3D UI (Display-Only) ==================== DISABLED
    // AircraftButtonBaseView has: _aircraft (Aircraft), _textMeshProUGUI (TextMeshProUGUI)

    /*
    [HarmonyPatch]
    public static class CallSignOverride
    {
        private static readonly HashSet<string> _logged = new();

        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("ContextCross.View.AircraftButtonBaseView");
            if (type == null) return null;
            return AccessTools.PropertyGetter(type, "CallSign");
        }

        [HarmonyPrefix]
        static bool Prefix(object __instance, ref string __result)
        {
            try
            {
                var viewType = __instance.GetType();
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // Get _aircraft (Aircraft) — confirmed from property dump
                var aircraft = viewType.GetProperty("_aircraft", flags)?.GetValue(__instance);
                if (aircraft == null) return true;

                var fp = aircraft.GetType().GetProperty("FlightPlan", flags)?.GetValue(aircraft);
                if (fp == null) return true;

                var dir = (EFlightDirection?)fp.GetType().GetProperty("FlightDirection", flags)?.GetValue(fp);
                if (dir == null) return true;

                string originalCallSign = fp.GetType().GetProperty("CallSign", flags)?.GetValue(fp) as string ?? "???";
                __result = dir == EFlightDirection.Departure ? "ES1111" : "CA3115";

                // Log once per original callsign
                //if (_logged.Add(originalCallSign))
                //    AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [CallSignOverride] {originalCallSign} → {__result} (display-only)");

                return false;
            }
            catch
            {
                //AC27SkinPlugin.Logger.LogError($"[AC27Skin] [CallSignOverride] Error: {ex.Message}");
                return true;
            }
        }
    }
    */

    // ==================== HOOK: Override Taxi Speed (Method Parameter) ====================
    // Patches Dynamics.Approach2Roll(float taxiSpeed) — called when an aircraft
    // transitions from approach/landing to rolling (on the runway).
    // The taxiSpeed parameter controls how fast it rolls after landing.
    //
    // ── HOW TO USE ──
    //   Uncomment Prefix to override the taxiSpeed parameter.
    //   This only affects the Approach→Roll transition speed, not continuous taxi.

    [HarmonyPatch]
    public static class TaxiSpeedOverride
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("ContextCross.Dynamics.Dynamics");
            if (type == null) return null;
            return AccessTools.Method(type, "Approach2Roll", new[] { typeof(float) });
        }
    }

    // ==================== HOOK: Override Taxi Speed AFTER Dynamics.Update ====================
    // Patches Dynamics.Update(AircraftDynamicsInput, float) with a Postfix.
    // This runs AFTER ControlTargetTaxiSpeed() has already recalculated the target,
    // so our override sticks. Also SKIPS Pushback/Parking/Takeoff states to avoid
    // breaking pushback (which was endlessly looping when forced to 70 speed).
    [HarmonyPatch]
    public static class DynamicsUpdatePostfix
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("ContextCross.Dynamics.Dynamics");
            if (type == null) return null;
            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name == "Update" && m.GetParameters().Length == 2)
                    return m;
            }
            return null;
        }

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var dynType = __instance.GetType();

                // Check current state to skip non-taxi states (esp. Pushback!)
                var stateReactive = dynType.GetField("StateReactive", flags)?.GetValue(__instance);
                if (stateReactive == null) return;
                var valueProp = stateReactive.GetType().GetProperty("Value", flags);
                var currentState = valueProp?.GetValue(stateReactive);
                if (currentState == null) return;
                int stateInt = (int)currentState;

                // Only override for taxi states: ReadyForTaxi=5, TaxiingArrival=6, TaxiingDeparture=7, TaxiPausing=10, TaxiAligning=15
                if (stateInt != 5 && stateInt != 6 && stateInt != 7 && stateInt != 10 && stateInt != 15)
                    return;

                // All speed/acceleration overrides disabled — keep game defaults
            }
            catch { }
        }
    }

    // ==================== HOOK: Log on Aircraft Init ====================
    // Patches Aircraft.InitializeReactiveProperties (or constructor fallback)
    // to log initial TaxiSpeed/CallSign/FlightDirection.

    [HarmonyPatch]
    public static class AircraftLogPostfix
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("ContextCross.Aircrafts.Aircraft");
            if (type == null) return null;
            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                string n = m.Name;
                if ((n.Contains("Initialize") || n.Contains("initialize")) && m.GetParameters().Length <= 1)
                    return m;
            }
            return AccessTools.Constructor(type);
        }

        [HarmonyPostfix]
        static void Postfix(Aircraft __instance)
        {
            try
            {
                if (__instance == null || __instance.FlightPlan == null) return;
            }
            catch { }
        }
    }

    // ==================== HOOK: Override StripBtnView.Init (FlightStrip label) ==================== DISABLED
    /*
    [HarmonyPatch]
    public static class StripBtnViewInitPatch
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("ContextCross.View.StripBtnView");
            if (type == null) return null;
            return AccessTools.Method(type, "Init", new[] { typeof(Aircraft) });
        }

        [HarmonyPostfix]
        static void Postfix(object __instance, Aircraft aircraft)
        {
            try
            {
                if (aircraft == null || aircraft.FlightPlan == null) return;
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var viewType = __instance.GetType();
                var tmp = viewType.GetProperty("_textMeshProUGUI", flags)?.GetValue(__instance) as TMP_Text;
                if (tmp == null) return;
                string display = aircraft.FlightPlan.FlightDirection == EFlightDirection.Departure ? "ES1111" : "CA3115";
                tmp.text = display;
            }
            catch { }
        }
    }

    // ==================== HOOK: Override CallSignView.Init (Map label) ==================== DISABLED
    [HarmonyPatch]
    public static class CallSignViewInitPatch
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("ContextCross.View.CallSignView");
            if (type == null) return null;
            return AccessTools.Method(type, "Init", new[] { typeof(Aircraft) });
        }

        [HarmonyPostfix]
        static void Postfix(object __instance, Aircraft aircraft)
        {
            try
            {
                if (aircraft == null || aircraft.FlightPlan == null) return;
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var viewType = __instance.GetType();
                var tmp = viewType.GetProperty("_textMeshProUGUI", flags)?.GetValue(__instance) as TMP_Text;
                if (tmp == null) return;
                string display = aircraft.FlightPlan.FlightDirection == EFlightDirection.Departure ? "ES1111" : "CA3115";
                tmp.text = display;
            }
            catch { }
        }
    }
    */

    // ==================== Patch: Override ALL Loading Tips ====================
    // Hook 1: Prefix on OnStringLoaded(string newText) — intercept localization output
    // Hook 2: Postfix on Show(LoadingContext) — belt-and-suspenders, fires on every scene load
    public static class LoadingTipOverride
    {
        private static string FixedTipText => TextOverridesConfig.FixedTipText;

        public static bool OnStringLoadedPrefix(ref string newText)
        {
            if (newText != FixedTipText && !string.IsNullOrEmpty(newText))
            {
                newText = FixedTipText;
                AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [Tips] Override applied via OnStringLoaded!");
            }
            return true;
        }

        public static void ShowPostfix(object __instance)
        {
            try
            {
                var mb = __instance as MonoBehaviour;
                if (mb == null) return;
                var t = mb.GetType();

                // Disable _tipsTextEvent so localization doesn't undo our override
                var evtField = t.GetField("_tipsTextEvent", BindingFlags.Instance | BindingFlags.NonPublic);
                if (evtField != null)
                {
                    var locEvt = evtField.GetValue(mb) as MonoBehaviour;
                    if (locEvt != null && locEvt.enabled) { locEvt.enabled = false; AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [Tips] Show postfix: disabled _tipsTextEvent"); }
                }

                var tipsField = t.GetField("_tipsText", BindingFlags.Instance | BindingFlags.NonPublic);
                if (tipsField != null)
                {
                    var tipsText = tipsField.GetValue(mb) as TMP_Text;
                    if (tipsText != null)
                    {
                        string oldText = tipsText.text ?? "(null)";
                        if (oldText != FixedTipText)
                        {
                            tipsText.text = FixedTipText;
                            tipsText.ForceMeshUpdate(true, true);
                            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Tips] ShowPostfix: wrote! old='{(oldText.Length > 40 ? oldText.Substring(0, 40) + "..." : oldText)}' new='{FixedTipText.Substring(0, 20)}...' go='{mb.name}'");
                        }
                        else
                        {
                            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Tips] ShowPostfix: text already correct, go='{mb.name}'");
                        }
                    }
                    else
                    {
                        AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Tips] ShowPostfix: _tipsText field is NULL, go='{mb.name}'");
                    }
                }
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogError($"[AC27Skin] [Tips] ShowPostfix error: {ex}");
            }
        }
    }

}

