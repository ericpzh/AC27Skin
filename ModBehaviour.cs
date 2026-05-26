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
    public class ModBehaviour : MonoBehaviour
    {
        public ModBehaviour(IntPtr ptr) : base(ptr) { }

        private float _aircraftLogTimer;
        //private float _callSignTimer; // DISABLED — callsign overrides removed
        private float _tipsScanTimer;
        private int _guardTick;
        private int _lastScreenW;
        private int _lastScreenH;
        private int _frameCount;

        /// Cached reference to the active MainMenuView — set by UpdateLogoPatch postfix.
        /// Used by ResolutionMonitor to reapply logo scale on resolution change.
        internal static MonoBehaviour ActiveMainMenuView;

        // ── StartBtn immediate fix: cached LevelSelectView ──
        private MonoBehaviour _cachedLSV;
        private int _lsvScanSkip;       // throttle full scene scans



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

            // ── Resolution change monitor (LateUpdate would be ideal but Update works) ──
            // On resolution/DPM change, reapply logo to all active MainMenuViews.
            _frameCount++;
            if (_lastScreenW > 0 && (Screen.width != _lastScreenW || Screen.height != _lastScreenH))
            {
                OnResolutionChanged();
            }
            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;

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

            // ── StartBtn immediate fix: checks LevelSelectView's Start button text every frame ──
            TryFixStartBtnText();
        }

        void OnDestroy()
        {
            ActiveMainMenuView = null;
        }

        /// Fires when Screen.width/height changes (resolution or DPI change).
        /// Re-applies logo scale + text overrides to all active MainMenuViews.
        private void OnResolutionChanged()
        {
            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [ResMon] Resolution changed: {_lastScreenW}x{_lastScreenH} → {Screen.width}x{Screen.height} (frame={_frameCount})");

            // Find all active MainMenuView instances in loaded scenes
            // Using cached reference first, fallback to scene scanning
            var views = new List<MonoBehaviour>();
            if (ActiveMainMenuView != null && ActiveMainMenuView.isActiveAndEnabled)
                views.Add(ActiveMainMenuView);

            // Also scan all scenes for additional MainMenuViews
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb != null && mb.GetType().Name == "MainMenuView" && mb != ActiveMainMenuView)
                            views.Add(mb);
                    }
                }
            }

            if (views.Count == 0)
            {
                AC27SkinPlugin.Logger.LogInfo("[AC27Skin] [ResMon] No active MainMenuView found, skipping logo reapply");
                return;
            }

            foreach (var view in views)
            {
                try
                {
                    LogoReplacer.Replace(view);
                    TextReplacer.DisableAllLocalization(view.gameObject);
                    TextReplacer.ReplaceButtonTexts(view);
                    TextReplacer.ReplaceVersionText(view);
                }
                catch (Exception ex)
                {
                    AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [ResMon] Error reapplying to {view.name}: {ex.Message}");
                }
            }

            AC27SkinPlugin.Logger.LogInfo($"[AC27Skin] [ResMon] Reapplied logo+text on {views.Count} MainMenuView(s)");
        }

        /// <summary>
        /// Every-frame check: find active LevelSelectView, get its StartBtn,
        /// and immediately replace text on the very first frame the button becomes active.
        /// Replaces the old StartBtnWatcher (which relied on AddComponent lifecycle).
        /// </summary>
        private void TryFixStartBtnText()
        {
            // ── Resolve LevelSelectView (cached or scan) ──
            if (_cachedLSV == null || !_cachedLSV.gameObject.activeInHierarchy)
            {
                _cachedLSV = null;
                _lsvScanSkip++;
                if (_lsvScanSkip < 10) return; // throttle full scans (every 10 frames ≈ 0.17s)
                _lsvScanSkip = 0;

                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded) continue;
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                        {
                            if (mb != null && mb.GetType().Name == "LevelSelectView")
                            {
                                _cachedLSV = mb;
                                break;
                            }
                        }
                        if (_cachedLSV != null) break;
                    }
                    if (_cachedLSV != null) break;
                }
            }

            if (_cachedLSV == null) return;

            // ── Get StartBtn via hierarchy (IL2CPP field reflection returns null) ──
            try
            {
                // Path from hierarchy dump: LevelSelectView → LevelPart → Start
                var startXform = _cachedLSV.transform.Find("LevelPart/Start");
                if (startXform == null) return; // Start button not yet created or LevelPart inactive
                var startGo = startXform.gameObject;
                var startBtn = startXform.GetComponent<Button>();
                if (startBtn == null) return;

                if (!startGo.activeInHierarchy) return;

                // ── Button active — check and fix text immediately ──
                var allTMP = startBtn.GetComponentsInChildren<TextMeshProUGUI>(true);
                var map = LevelSelectState.AllTextSorted;
                foreach (var tmp in allTMP)
                {
                    if (tmp == null || string.IsNullOrEmpty(tmp.text)) continue;
                    string normalized = TextReplacer.NormalizeForMatch(tmp.text);
                    foreach (var kv in map)
                    {
                        if (normalized.Contains(kv.Value) && kv.Key != kv.Value) continue;
                        if (normalized.Contains(kv.Key))
                        {
                            string newText = normalized.Replace(kv.Key, kv.Value);
                            TextReplacer.SafeSetTMPText(tmp, newText);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AC27SkinPlugin.Logger.LogWarning($"[AC27Skin] [StartBtn] scan error: {ex.Message}");
            }
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


}
