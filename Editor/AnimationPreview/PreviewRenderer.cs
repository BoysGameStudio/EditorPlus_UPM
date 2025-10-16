#if UNITY_EDITOR
using System;
using System.Reflection;
using Quantum;
using Quantum.Physics3D;
using UnityEngine;
using UnityEditor;

namespace EditorPlus.AnimationPreview
{
    internal static class PreviewRenderer
    {
        public static void DrawHitFramesPreview(UnityEngine.Object parentTarget, int frame)
        {
            if (parentTarget == null) return;

            // Only draw during repaint
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            try
            {
                // 1) If the asset is Unity-serialized (ScriptableObject / MonoBehaviour), try SerializedProperty first
                var so = new SerializedObject(parentTarget);
                var hitFramesProp = so.FindProperty("hitFrames");
                if (hitFramesProp != null && hitFramesProp.isArray)
                {
                    for (int idx = 0; idx < hitFramesProp.arraySize; idx++)
                    {
                        var elem = hitFramesProp.GetArrayElementAtIndex(idx);
                        if (elem == null) continue;
                        var frameProp = elem.FindPropertyRelative("frame");
                        if (frameProp == null) continue;
                        var f = GetIntSafe(frameProp, int.MinValue);
                        if (f == int.MinValue) continue;
                        if (f != frame) continue;

                        var shapeProp = elem.FindPropertyRelative("shape");
                        if (shapeProp != null && shapeProp.propertyType == SerializedPropertyType.Generic)
                        {
                            var ctx = ResolvePreviewRoot(parentTarget);
                            DrawShape3DConfigPreview(shapeProp, ctx);
                        }
                        else
                        {
                            Handles.color = new Color(1f, 0.25f, 0.25f, 0.6f);
                            Handles.DrawWireDisc(Vector3.zero, Vector3.up, 0.5f);
                            Handles.Label(Vector3.up * 0.6f, "HitFrame");
                        }

                        // we only draw the first matching frame
                        return;
                    }
                }

                // 2) If we can directly reference Quantum types (typed-first), prefer that (less fragile)
                try
                {
                    if (parentTarget is AttackActionData attack && attack.hitFrames != null)
                    {
                        foreach (var hf in attack.hitFrames)
                        {
                            if (hf == null) continue;
                            if (hf.frame != frame) continue;

                            if (hf.shape != null)
                            {
                                var ctx = ResolvePreviewRoot(parentTarget);
                                // hf.shape is a Shape3DConfig in Quantum packages; draw using direct API
                                DrawShape3DConfigPreview(hf.shape, ctx);
                            }
                            else
                            {
                                Handles.color = new Color(1f, 0.25f, 0.25f, 0.6f);
                                Handles.DrawWireDisc(Vector3.zero, Vector3.up, 0.5f);
                                Handles.Label(Vector3.up * 0.6f, "HitFrame");
                            }

                            return;
                        }
                    }
                }
                catch { /* typed path may fail in some editor contexts; fallback to reflection below */ }

                // 3) No reflection fallback: AnimationPreview is project-specific and should reference
                // known Quantum/Project types directly. If we couldn't draw via SerializedProperty or
                // typed-access above, give up gracefully.
            }
            catch { }
        }

        public static void DrawShape3DConfigPreview(SerializedProperty shapeProp, GameObject context)
        {
            try
            {
                var shapeTypeProp = shapeProp.FindPropertyRelative("ShapeType");
                if (shapeTypeProp == null) return;

                int shapeType = shapeTypeProp.enumValueIndex;

                Vector3 pos = Vector3.zero;
                var posProp = shapeProp.FindPropertyRelative("PositionOffset");
                if (posProp != null && posProp.propertyType == SerializedPropertyType.Vector3)
                {
                    pos = posProp.vector3Value;
                }

                Quaternion rot = Quaternion.identity;
                var rotProp = shapeProp.FindPropertyRelative("RotationOffset");
                if (rotProp != null)
                {
                    if (rotProp.propertyType == SerializedPropertyType.Vector3)
                    {
                        rot = Quaternion.Euler(rotProp.vector3Value);
                    }
                    else if (rotProp.propertyType == SerializedPropertyType.Quaternion)
                    {
                        try { rot = rotProp.quaternionValue; } catch { rot = Quaternion.identity; }
                    }
                }

                Vector3 worldPos = pos;
                Quaternion worldRot = rot;
                if (context != null)
                {
                    var t = context.transform;
                    worldPos = t.TransformPoint(pos);
                    worldRot = t.rotation * rot;
                }

                Handles.color = new Color(1f, 0.25f, 0.25f, 0.6f);

                switch (shapeType)
                {
                    case 1: // Sphere
                    {
                        var radiusProp = shapeProp.FindPropertyRelative("SphereRadius");
                        float r = 0.5f;
                        if (radiusProp != null) r = Mathf.Max(0.001f, GetFloatSafe(radiusProp, 0.5f));
                        Handles.DrawWireDisc(worldPos, worldRot * Vector3.up, r);
                        Handles.DrawWireDisc(worldPos, worldRot * Vector3.right, r);
                        Handles.DrawWireDisc(worldPos, worldRot * Vector3.forward, r);
                        Handles.Label(worldPos + Vector3.up * (r + 0.1f), "HitFrame(Sphere)");
                        break;
                    }

                    case 2: // Box
                    {
                        var extentsProp = shapeProp.FindPropertyRelative("BoxExtents");
                        Vector3 ext = Vector3.one * 0.5f;
                        if (extentsProp != null && extentsProp.propertyType == SerializedPropertyType.Vector3) ext = extentsProp.vector3Value;
                        var verts = new Vector3[8];
                        var half = ext;
                        verts[0] = worldPos + worldRot * new Vector3(-half.x, -half.y, -half.z);
                        verts[1] = worldPos + worldRot * new Vector3(half.x, -half.y, -half.z);
                        verts[2] = worldPos + worldRot * new Vector3(half.x, -half.y, half.z);
                        verts[3] = worldPos + worldRot * new Vector3(-half.x, -half.y, half.z);
                        verts[4] = worldPos + worldRot * new Vector3(-half.x, half.y, -half.z);
                        verts[5] = worldPos + worldRot * new Vector3(half.x, half.y, -half.z);
                        verts[6] = worldPos + worldRot * new Vector3(half.x, half.y, half.z);
                        verts[7] = worldPos + worldRot * new Vector3(-half.x, half.y, half.z);

                        Handles.DrawLine(verts[0], verts[1]); Handles.DrawLine(verts[1], verts[2]); Handles.DrawLine(verts[2], verts[3]); Handles.DrawLine(verts[3], verts[0]);
                        Handles.DrawLine(verts[4], verts[5]); Handles.DrawLine(verts[5], verts[6]); Handles.DrawLine(verts[6], verts[7]); Handles.DrawLine(verts[7], verts[4]);
                        Handles.DrawLine(verts[0], verts[4]); Handles.DrawLine(verts[1], verts[5]); Handles.DrawLine(verts[2], verts[6]); Handles.DrawLine(verts[3], verts[7]);
                        Handles.Label(worldPos + worldRot * Vector3.up * (half.y + 0.1f), "HitFrame(Box)");
                        break;
                    }

                    case 3: // Capsule
                    {
                        var radiusProp = shapeProp.FindPropertyRelative("CapsuleRadius");
                        var heightProp = shapeProp.FindPropertyRelative("CapsuleHeight");
                        float radius = 0.25f; float height = 1f;
                        if (radiusProp != null) radius = Mathf.Max(0.001f, GetFloatSafe(radiusProp, 0.25f));
                        if (heightProp != null) height = Mathf.Max(0f, GetFloatSafe(heightProp, 1f));
                        float half = Mathf.Max(0f, (height - 2f * radius) * 0.5f);
                        Vector3 up = worldRot * Vector3.up;
                        var top = worldPos + up * half;
                        var bot = worldPos - up * half;
                        Handles.DrawWireDisc(top, worldRot * Vector3.up, radius);
                        Handles.DrawWireDisc(bot, worldRot * Vector3.up, radius);
                        // draw simple connecting lines (approx cylinder)
                        Handles.DrawLine(top + worldRot * Vector3.right * radius, bot + worldRot * Vector3.right * radius);
                        Handles.DrawLine(top - worldRot * Vector3.right * radius, bot - worldRot * Vector3.right * radius);
                        Handles.DrawLine(top + worldRot * Vector3.forward * radius, bot + worldRot * Vector3.forward * radius);
                        Handles.DrawLine(top - worldRot * Vector3.forward * radius, bot - worldRot * Vector3.forward * radius);
                        Handles.Label(worldPos + up * (half + radius + 0.05f), "HitFrame(Capsule)");
                        break;
                    }

                    default:
                    {
                        Handles.DrawWireDisc(worldPos, Vector3.up, 0.5f);
                        Handles.Label(worldPos + Vector3.up * 0.6f, "HitFrame");
                        break;
                    }
                }
            }
            catch { }
        }

        // Direct drawer for Quantum.Shape3DConfig (no reflection)
        public static void DrawShape3DConfigPreview(Shape3DConfig config, GameObject context)
        {
            try
            {
                // If config is default/empty, draw nothing
                if (config.Equals(default(Shape3DConfig))) return;

#if QUANTUM_ENABLE_PHYSICS3D && !QUANTUM_DISABLE_PHYSICS3D
                // Prefer Quantum's editor gizmo if available
                Vector3 position = context != null ? context.transform.position : Vector3.zero;
                Quaternion rotation = context != null ? context.transform.rotation : Quaternion.identity;
                var entry = new Quantum.QuantumGizmoEntry(new Color(1f, 0.25f, 0.25f, 0.6f));
                Quantum.QuantumUnityRuntime.DrawShape3DConfigGizmo(config, position, rotation, entry);
#else
                // Fallback: draw a simple conservative placeholder at the preview root
                Handles.color = new Color(1f, 0.25f, 0.25f, 0.6f);
                Vector3 worldPos = context != null ? context.transform.position : Vector3.zero;
                Handles.DrawWireDisc(worldPos, Vector3.up, 0.5f);
                Handles.Label(worldPos + Vector3.up * 0.6f, "HitFrame");
#endif
            }
            catch { }
        }

        private static float GetFloatSafe(SerializedProperty prop, float fallback)
        {
            if (prop == null) return fallback;
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Float:
                        return prop.floatValue;
                    case SerializedPropertyType.Integer:
                        return prop.intValue;
                    case SerializedPropertyType.Enum:
                        return prop.enumValueIndex;
                    default:
                        return fallback;
                }
            }
            catch { return fallback; }
        }

        private static int GetIntSafe(SerializedProperty prop, int fallback)
        {
            if (prop == null) return fallback;
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        return prop.intValue;
                    case SerializedPropertyType.Enum:
                        return prop.enumValueIndex;
                    case SerializedPropertyType.Float:
                        return Mathf.RoundToInt(prop.floatValue);
                    case SerializedPropertyType.String:
                        if (int.TryParse(prop.stringValue, out var r)) return r;
                        return fallback;
                    case SerializedPropertyType.Generic:
                    {
                        var raw = prop.FindPropertyRelative("RawValue") ?? prop.FindPropertyRelative("Value") ?? prop.FindPropertyRelative("m_RawValue") ?? prop.FindPropertyRelative("m_Value");
                        if (raw != null)
                        {
                            switch (raw.propertyType)
                            {
                                case SerializedPropertyType.Integer: return raw.intValue;
                                case SerializedPropertyType.Float: return Mathf.RoundToInt(raw.floatValue);
                                case SerializedPropertyType.Enum: return raw.enumValueIndex;
                                case SerializedPropertyType.String:
                                    if (int.TryParse(raw.stringValue, out var rr)) return rr;
                                    break;
                            }
                        }
                        return fallback;
                    }
                    default:
                        return fallback;
                }
            }
            catch { return fallback; }
        }

        // Try to find a GameObject that serves as the preview root for the given target.
        // Uses conservative reflection (editor-only) to avoid hard dependencies. Returns null if not found.
        public static GameObject ResolvePreviewRoot(UnityEngine.Object parentTarget)
        {
            try
            {
                if (parentTarget == null) return null;
                if (parentTarget is GameObject go) return go;

                var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var t = parentTarget.GetType();
                string[] names = new[] { "_tempPreviewGO", "tempPreviewGO", "_previewGO", "previewGO", "_previewRoot", "previewRoot" };

                foreach (var name in names)
                {
                    try
                    {
                        var fi2 = t.GetField(name, flags | BindingFlags.Instance);
                        if (fi2 != null)
                        {
                            var v = fi2.GetValue(parentTarget);
                            if (v is GameObject gg) return gg;
                        }
                        var fsi = t.GetField(name, flags | BindingFlags.Static);
                        if (fsi != null)
                        {
                            var vs = fsi.GetValue(null);
                            if (vs is GameObject sgg) return sgg;
                        }
                        var pi = t.GetProperty(name, flags | BindingFlags.Instance);
                        if (pi != null && pi.CanRead)
                        {
                            var pv = pi.GetValue(parentTarget, null);
                            if (pv is GameObject pg) return pg;
                        }
                    }
                    catch { }
                }

                // Search instance fields/properties for a GameObject or Component reference
                try
                {
                    foreach (var field in t.GetFields(flags))
                    {
                        try
                        {
                            var val = field.GetValue(parentTarget);
                            if (val is GameObject ggo) return ggo;
                            if (val is Component comp) return comp.gameObject;
                        }
                        catch { }
                    }

                    foreach (var pinfo in t.GetProperties(flags))
                    {
                        try
                        {
                            if (!pinfo.CanRead) continue;
                            var val = pinfo.GetValue(parentTarget, null);
                            if (val is GameObject ggo) return ggo;
                            if (val is Component comp) return comp.gameObject;
                        }
                        catch { }
                    }
                }
                catch { }

                // Last resort: named temporary preview object created by preview host
                try { return GameObject.Find("__DashTempPreview__"); } catch { }
                return null;
            }
            catch { }
            return null;
        }

    }
}
#endif
