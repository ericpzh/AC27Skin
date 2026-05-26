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

}
