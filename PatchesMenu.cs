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
                ModBehaviour.ActiveMainMenuView = __instance; // cache for ResolutionMonitor
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
        // BaseLogoScale calibrated at 1920x1080 (screenRatio=1.0, canvasScale≈1.0).
        // Since this canvas has NO CanvasScaler, we must multiply by screenRatio to
        // maintain consistent visual proportion across different resolutions.
        // At 50% UI → scaleFactor≈0.5 → everything is HALF size → need 2× localScale.
        // Formula: dynamicScale = BaseLogoScale * screenRatio / max(canvasScale, 0.3f)
        private const float BaseLogoScale = 3.1f;

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

                // Dynamic scale: scale proportional to screen width (no CanvasScaler, so UI is in pixel coords)
                // screenRatio maintains the same visual proportion across resolutions:
                //   At 1080p (1920 wide): ratio=1.0 → dynamicScale=3.1
                //   At 4K (3840 wide):   ratio=2.0 → dynamicScale=6.2 (same % of screen width)
                // Also divide by CanvasScaler.scaleFactor to compensate UI scale setting.
                // BaseLogoScale=3.1 calibrated at 1920x1080, canvasScale≈1.0.
                float screenRatio = (float)Screen.width / 1920f;
                float canvasScale = GetCanvasScaleFactor(logoHolder);
                float dynamicScale = BaseLogoScale * screenRatio / Mathf.Max(canvasScale, 0.3f);
                var scale = new Vector3(dynamicScale, dynamicScale, 1f);

                // Log once per change or every 300 frames (reduce spam)
                bool scaleChanged = Mathf.Abs(dynamicScale - _lastLogScale) > 0.01f;
                _lastLogScale = dynamicScale;
                _diagFrame++;
                if (scaleChanged || _diagFrame % 300 == 0)
                    DumpCanvasScalerInfo(logoHolder, screenRatio, canvasScale, dynamicScale);

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

        /// Get the Canvas scaleFactor from the parent Canvas. 
        /// Logs full CanvasScaler diagnostic info when called from DumpCanvasScalerInfo.
        private static float GetCanvasScaleFactor(Transform logoHolder)
        {
            try
            {
                var canvas = logoHolder.GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    float sf = canvas.scaleFactor;
                    if (sf > 0.01f)
                        return sf;
                }
            }
            catch { }
            return 1.0f;
        }

        /// Log comprehensive CanvasScaler + screen info for scale diagnosis
        private static void DumpCanvasScalerInfo(Transform logoHolder, float screenRatio, float canvasScale, float dynamicScale)
        {
            try
            {
                var canvas = logoHolder.GetComponentInParent<Canvas>();
                string scalerInfo = "(no CanvasScaler)";
                if (canvas != null)
                {
                    var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                    if (scaler != null)
                    {
                        scalerInfo = $"uiScaleMode={scaler.uiScaleMode} refRes={scaler.referenceResolution} matchWH={scaler.matchWidthOrHeight:F2} scaleFactor({scaler.scaleFactor:F2})";
                    }
                    else
                    {
                        scalerInfo = $"canvas.scaleFactor={canvas.scaleFactor:F2} (no CanvasScaler component)";
                    }
                }
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LogoDiag] Screen={Screen.width}x{Screen.height} dpi={Screen.dpi:F0} " +
                    $"screenRatio={screenRatio:F2} canvasScale={canvasScale:F3} dynamicScale={dynamicScale:F3}");
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LogoDiag] Canvas: {scalerInfo}");
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [LogoDiag] Error: {ex.Message}");
            }
        }
        
        private static int _lastReplacedLogoChildCount = -1;
        private static float _lastLogScale = -1f;
        private static int _diagFrame;
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
                ModBehaviour.ActiveMainMenuView = __instance; // cache for ResolutionMonitor

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

                // 3. Version text FIRST (see original "游戏版本" before button overrides touch it)
                bool ver = TextReplacer.ReplaceVersionText(__instance);
                // 4. Button text replacement (skips VersionText)
                int btns = TextReplacer.ReplaceButtonTexts(__instance);
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

                // Version text FIRST (see original "游戏版本" before button overrides touch it)
                bool ver = TextReplacer.ReplaceVersionText(__instance);
                int btns = TextReplacer.ReplaceButtonTexts(__instance);
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

                // Dump all TMP text for diagnosis
                var allTmp = __instance.GetComponentsInChildren<TextMeshProUGUI>(true);
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LiveryMod]   TMP count={allTmp.Length}");
                foreach (var t in allTmp)
                {
                    if (t != null && !string.IsNullOrEmpty(t.text))
                        AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [LiveryMod]     '{t.name}' = \"{t.text.Replace("\r","").Replace("\n","\\n")}\"");
                }

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

}
