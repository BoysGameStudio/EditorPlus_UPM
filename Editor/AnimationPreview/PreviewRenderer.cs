#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace EditorPlus.AnimationPreview
{
    internal static class PreviewRenderer
    {
        public static void DrawHitFramesPreview(UnityEngine.Object parentTarget, int frame)
        {
            if (parentTarget == null) return;

            try
            {
                if (Event.current.type != EventType.Repaint) return;

                var so = new SerializedObject(parentTarget);
                var prop = so.FindProperty("hitFrames");
                if (prop == null) return;
                for (int i = 0; i < prop.arraySize; i++)
                {
                    var elem = prop.GetArrayElementAtIndex(i);
                    if (elem == null) continue;
                    var frameProp = elem.FindPropertyRelative("frame");
                    if (frameProp == null) continue;
                    if (frameProp.intValue != frame) continue;

                    var shapeProp = elem.FindPropertyRelative("shape");
                    if (shapeProp != null && shapeProp.propertyType == SerializedPropertyType.Generic)
                    {
                        DrawShape3DConfigPreview(shapeProp, parentTarget as GameObject);
                    }
                    else
                    {
                        Handles.color = new Color(1f, 0.25f, 0.25f, 0.6f);
                        Handles.DrawWireDisc(Vector3.zero, Vector3.up, 0.5f);
                        Handles.Label(Vector3.up * 0.6f, "HitFrame");
                    }

                    break;
                }
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
    }
}
#endif
