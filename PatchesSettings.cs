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


    /// Per-frame Settings text guard. Runs in LateUpdate to catch text resets
    /// within the same render cycle. Uses 3 rapid passes then self-destructs.
    /// Pass schedule: frame 1, 5, 20 — near-instant first pass, safety nets after.
    public class SettingsDelayedUpdater : MonoBehaviour
    {
        public MonoBehaviour proxy;
        private int _frame;
        private int _pass;

        public SettingsDelayedUpdater(IntPtr ptr) : base(ptr) { }

        public void ResetTimer()
        {
            _frame = 0;
            _pass = 0;
        }

        // Pass schedule: check at frames 1, 5, 20
        static readonly int[] PassFrames = { 1, 5, 20 };

        void LateUpdate()
        {
            if (proxy == null) { Destroy(this); return; }
            _frame++;

            int targetPass = 0;
            for (int i = 0; i < PassFrames.Length; i++)
            {
                if (_frame >= PassFrames[i]) targetPass = i + 1;
            }

            if (targetPass <= _pass) return; // not time for next pass yet
            _pass = targetPass;

            try
            {
                int locsDisabled = TextReplacer.DisableAllLocalization(proxy.gameObject);
                int count = SettingsTextState.ApplyDetailed(proxy, false);

                var settingsField = proxy.GetType().GetField("settingsView",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (settingsField != null)
                {
                    var settingsView = settingsField.GetValue(proxy) as MonoBehaviour;
                    if (settingsView != null && settingsView != proxy)
                    {
                        locsDisabled += TextReplacer.DisableAllLocalization(settingsView.gameObject);
                        count += SettingsTextState.ApplyDetailed(settingsView, false);
                    }
                }

                if (count > 0 || locsDisabled > 0)
                    AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Settings] Delayed:pass{_pass} f{_frame} applied {count} texts, disabled {locsDisabled} locs");
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogError($"[AC27Skin] [Settings] Delayed:pass{_pass} error: {ex}");
            }

            if (_pass >= PassFrames.Length)
            {
                AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Settings] Delayed: finished {PassFrames.Length} passes ({_frame} frames)");
                Destroy(this);
            }
        }
    }

    // ==================== Minimal MonoBehaviour ====================


}
