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


}
