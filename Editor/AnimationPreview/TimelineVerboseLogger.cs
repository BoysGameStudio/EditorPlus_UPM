#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Quantum;

/// <summary>
/// Small editor utility to print timeline track diagnostics for the currently selected object.
/// Use Tools -> EditorPlus -> Timeline Verbose Log to emit logs.
/// This is temporary debug tooling and can be removed after diagnosis.
/// </summary>
public static class TimelineVerboseLogger
{
    [MenuItem("Tools/EditorPlus/Timeline Verbose Log")]
    public static void LogSelectedTracks()
    {
        var obj = Selection.activeObject;
        Debug.Log($"[TimelineVerbose] Selection => Type={(obj!=null?obj.GetType().FullName:"(null)")} Name={(obj!=null?obj.name:"(null)")}");
        if (obj == null)
        {
            return;
        }

        try
        {
            // Handle common Quantum types explicitly (no reflection, no string member access)
            if (obj is AttackActionData attack)
            {
                Debug.Log($"[TimelineVerbose] Type=AttackActionData");

                if (attack.hitFrames != null)
                {
                    Debug.Log($"[TimelineVerbose] hitFrames length={attack.hitFrames.Length}");
                    for (int i = 0; i < attack.hitFrames.Length; i++)
                    {
                        var hf = attack.hitFrames[i];
                        if (hf == null)
                        {
                            Debug.Log($"[TimelineVerbose] hitFrames[{i}] is null");
                        }
                        else
                        {
                            Debug.Log($"[TimelineVerbose] hitFrames[{i}] frame={hf.frame}");
                        }
                    }
                }

                if (attack.projectileFrames != null)
                {
                    Debug.Log($"[TimelineVerbose] projectileFrames length={attack.projectileFrames.Length}");
                    for (int i = 0; i < attack.projectileFrames.Length; i++)
                    {
                        var pf = attack.projectileFrames[i];
                        if (pf == null) Debug.Log($"[TimelineVerbose] projectileFrames[{i}] is null"); else Debug.Log($"[TimelineVerbose] projectileFrames[{i}] frame={pf.frame}");
                    }
                }

                if (attack.childActorFrames != null)
                {
                    Debug.Log($"[TimelineVerbose] childActorFrames length={attack.childActorFrames.Length}");
                    for (int i = 0; i < attack.childActorFrames.Length; i++)
                    {
                        var cf = attack.childActorFrames[i];
                        if (cf == null) Debug.Log($"[TimelineVerbose] childActorFrames[{i}] is null"); else Debug.Log($"[TimelineVerbose] childActorFrames[{i}] frame={cf.Frame}");
                    }
                }

                if (attack.affectFrames != null)
                {
                    Debug.Log($"[TimelineVerbose] affectFrames length={attack.affectFrames.Length}");
                    for (int i = 0; i < attack.affectFrames.Length; i++)
                    {
                        var af = attack.affectFrames[i];
                        if (af == null) Debug.Log($"[TimelineVerbose] affectFrames[{i}] is null"); else Debug.Log($"[TimelineVerbose] affectFrames[{i}] frame={af.Frame}");
                    }
                }

                return;
            }

            if (obj is ActiveActionData activeAction)
            {
                Debug.Log($"[TimelineVerbose] Type=ActiveActionData");
                Debug.Log($"[TimelineVerbose] MoveInterruptionLockEndFrame={activeAction.MoveInterruptionLockEndFrame} ActionMovementStartFrame={activeAction.ActionMovementStartFrame}");

                if (activeAction.NonHitLockableFrames != null)
                {
                    Debug.Log($"[TimelineVerbose] NonHitLockableFrames={string.Join(",", activeAction.NonHitLockableFrames)}");
                }

                if (activeAction.IFrames != null)
                {
                    Debug.Log($"[TimelineVerbose] IFrames start={activeAction.IFrames.IntraActionStartFrame} end={activeAction.IFrames.IntraActionEndFrame}");
                }

                if (activeAction.AffectWindow != null)
                {
                    Debug.Log($"[TimelineVerbose] AffectWindow start={activeAction.AffectWindow.IntraActionStartFrame} end={activeAction.AffectWindow.IntraActionEndFrame}");
                }

                return;
            }

            // Try common Quantum interfaces that expose frame lists without reflection
            if (obj is IOffensiveAction offensive)
            {
                try
                {
                    var hfArr = offensive.GetHitFrames();
                    if (hfArr != null)
                    {
                        Debug.Log($"[TimelineVerbose] IOffensiveAction.GetHitFrames length={hfArr.Length}");
                        for (int i = 0; i < hfArr.Length; i++)
                        {
                            var hf = hfArr[i];
                            if (hf == null) Debug.Log($"[TimelineVerbose] HitFrame[{i}] is null"); else Debug.Log($"[TimelineVerbose] HitFrame[{i}] frame={hf.frame}");
                        }
                        return;
                    }
                }
                catch { }
            }

            Debug.Log($"[TimelineVerbose] No typed handlers matched for {obj.GetType().FullName}. This tool intentionally avoids reflection/string access.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TimelineVerbose] Inspect error: {ex.Message}");
        }
    }
}

#endif
