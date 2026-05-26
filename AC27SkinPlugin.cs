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

}
