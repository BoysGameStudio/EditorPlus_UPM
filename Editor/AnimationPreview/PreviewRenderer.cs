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
        // Typed-first, non-reflective implementation:
        // - If parentTarget is GameObject or Component, use that.
        // - Otherwise, check common serialized property names (previewRoot, previewGO, tempPreviewGO, etc.) using SerializedObject.
        // - As a last resort, try the temporary preview GameObject name if created by the preview host.
        // This avoids iterating fields/properties via reflection while preserving common serialized fallbacks.
        public static GameObject ResolvePreviewRoot(UnityEngine.Object parentTarget)
        {
            if (parentTarget == null) return null;

            // Direct cases
            if (parentTarget is GameObject go) return go;
            if (parentTarget is Component comp) return comp.gameObject;

            try
            {
                var so = new SerializedObject(parentTarget);
                // Common property names that users/projects tend to use for preview roots
                string[] propNames = new[] { "previewRoot", "_previewRoot", "previewGO", "_previewGO", "tempPreviewGO", "_tempPreviewGO", "previewObject", "_previewObject" };

                foreach (var name in propNames)
                {
                    try
                    {
                        var prop = so.FindProperty(name);
                        if (prop == null) continue;

                        // If it's an object reference, return it if suitable
                        if (prop.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            var obj = prop.objectReferenceValue;
                            if (obj is GameObject g) return g;
                            if (obj is Component c) return c.gameObject;
                        }

                        // Some serialized wrappers may expose a child field holding the reference
                        var candidate = prop.FindPropertyRelative("gameObject") ?? prop.FindPropertyRelative("m_GameObject") ?? prop.FindPropertyRelative("objectReferenceValue");
                        if (candidate != null && candidate.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            var obj2 = candidate.objectReferenceValue;
                            if (obj2 is GameObject g2) return g2;
                            if (obj2 is Component c2) return c2.gameObject;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // Last resort: named temporary preview object created by preview host
            try { return GameObject.Find("__DashTempPreview__"); } catch { }
            return null;
        }

    }
}
#endif
