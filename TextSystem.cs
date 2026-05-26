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
                // Skip VersionText — it's handled separately by ReplaceVersionText
                if (tmp.gameObject.name.IndexOf("VersionText", StringComparison.OrdinalIgnoreCase) >= 0) continue;
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
                if (idx < 0) idx = tmp.text.IndexOf("游戏版本"); // fallback
                if (idx < 0) idx = tmp.text.IndexOf("演练版本"); // post-override: "游戏版本"→"演练版本"
                if (idx >= 0)
                {
                    // Keep prefix text before version, add version + newline + company
                    string prefix;
                    if (tmp.text.IndexOf("游戏版本") >= 0 || tmp.text.IndexOf("演练版本") >= 0)
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
                // Skip VersionText — it's handled separately by ReplaceVersionText
                if (t.gameObject.name.IndexOf("VersionText", StringComparison.OrdinalIgnoreCase) >= 0) continue;
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
                bool ver = TextReplacer.ReplaceVersionText(menuView);
                int btns = TextReplacer.ReplaceButtonTexts(menuView);
                int co = TextReplacer.ReplaceCompanyText(menuView);
                int txts = TextReplacer.ReplaceTexts(menuView);
                LogoReplacer.Replace(menuView);

                int total = btns + co + txts + (ver ? 1 : 0);
                if (total > 0 || locsDisabled > 0)
                    AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [Delay:{_attempt}] btns={btns} co={co} txts={txts} ver={ver} locsDisabled={locsDisabled}");
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

    public static class TextOverridesConfig
    {
        private static bool _loaded;
        private static string _fixedTipText;
        private static string _companyFrom = "CCC Games";
        private static string _companyTo;
        private static string _versionText;
        private static Dictionary<string, string> _allText = new();

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
