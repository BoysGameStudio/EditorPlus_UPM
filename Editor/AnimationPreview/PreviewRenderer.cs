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
                                // hf.shape is a Shape3DConfig in Quantum packages; draw via reflection-free path if possible
                                DrawShape3DConfigPreviewFromReflection(hf.shape, ctx);
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

                // 3) Reflection fallback for unknown POCO/Odin types - conservative and scoped
                try
                {
                    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    Array arr = null;

                    var t = parentTarget.GetType();
                    var fi = t.GetField("hitFrames", flags);
                    if (fi != null) arr = fi.GetValue(parentTarget) as Array;
                    else
                    {
                        var pi = t.GetProperty("hitFrames", flags);
                        if (pi != null && pi.CanRead) arr = pi.GetValue(parentTarget, null) as Array;
                    }

                    if (arr == null)
                    {
                        // scan fields/properties for an array with elements that have 'frame'
                        foreach (var field in t.GetFields(flags))
                        {
                            try
                            {
                                var val = field.GetValue(parentTarget);
                                if (val is Array a)
                                {
                                    var et = a.GetType().GetElementType();
                                    if (et != null && (et.GetField("frame", flags) != null || et.GetProperty("frame", flags) != null))
                                    {
                                        arr = a; break;
                                    }
                                }
                            }
                            catch { }
                        }

                        if (arr == null)
                        {
                            foreach (var pinfo in t.GetProperties(flags))
                            {
                                try
                                {
                                    if (!pinfo.CanRead) continue;
                                    var val = pinfo.GetValue(parentTarget, null);
                                    if (val is Array a)
                                    {
                                        var et = a.GetType().GetElementType();
                                        if (et != null && (et.GetField("frame", flags) != null || et.GetProperty("frame", flags) != null))
                                        {
                                            arr = a; break;
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    if (arr != null)
                    {
                        for (int i = 0; i < arr.Length; i++)
                        {
                            var elem = arr.GetValue(i);
                            if (elem == null) continue;
                            int ef = -1;

                            var fField = elem.GetType().GetField("frame", flags);
                            if (fField != null)
                            {
                                var fv = fField.GetValue(elem);
                                if (fv is int iv) ef = iv;
                                else if (fv is long lv) ef = (int)lv;
                                else if (fv != null) { try { ef = Convert.ToInt32(fv); } catch { ef = -1; } }
                            }
                            else
                            {
                                var fProp = elem.GetType().GetProperty("frame", flags);
                                if (fProp != null)
                                {
                                    var pv = fProp.GetValue(elem, null);
                                    if (pv is int piv) ef = piv;
                                    else if (pv is long plv) ef = (int)plv;
                                    else if (pv != null) { try { ef = Convert.ToInt32(pv); } catch { ef = -1; } }
                                }
                            }

                            if (ef != frame) continue;

                            object shapeObj = null;
                            var shapeField = elem.GetType().GetField("shape", flags);
                            if (shapeField != null) shapeObj = shapeField.GetValue(elem);
                            else
                            {
                                var shapeProp = elem.GetType().GetProperty("shape", flags);
                                if (shapeProp != null && shapeProp.CanRead) shapeObj = shapeProp.GetValue(elem, null);
                            }

                            if (shapeObj != null)
                            {
                                var ctx = ResolvePreviewRoot(parentTarget);
                                DrawShape3DConfigPreviewFromReflection(shapeObj, ctx);
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
                catch { }
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

        // Reflection-based renderer for POCO/Odin shape objects. Attempts to read the same fields as
        // DrawShape3DConfigPreview (ShapeType, PositionOffset, RotationOffset, SphereRadius, BoxExtents,
        // CapsuleRadius, CapsuleHeight) and draws approximate shapes at the computed world position.
        private static void DrawShape3DConfigPreviewFromReflection(object shapeObj, GameObject context)
        {
            try
            {
                if (shapeObj == null) return;
                // Try a variety of common names for shape type / position / rotation / sizes to be tolerant
                int shapeType = GetFirstIntFromNames(shapeObj, new[] { "ShapeType", "shapeType", "Type", "type" }, 0);

                Vector3 pos = GetFirstVector3FromNames(shapeObj, new[] { "PositionOffset", "positionOffset", "Center", "center", "Position", "position", "Offset", "offset" }, Vector3.zero);
                Quaternion rot = Quaternion.identity;
                if (TryGetQuaternionFromNames(shapeObj, new[] { "RotationOffset", "rotationOffset", "Rotation", "rotation", "Rot", "rot" }, out var q)) rot = q;
                else if (TryGetVector3FromNames(shapeObj, new[] { "RotationOffset", "rotationOffset", "Rotation", "rotation" }, out var rotVec)) rot = Quaternion.Euler(rotVec);

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
                        float r = GetFirstFloatFromNames(shapeObj, new[] { "SphereRadius", "sphereRadius", "Radius", "radius", "r" }, 0.5f);
                        r = Mathf.Max(0.001f, r);
                        Handles.DrawWireDisc(worldPos, worldRot * Vector3.up, r);
                        Handles.DrawWireDisc(worldPos, worldRot * Vector3.right, r);
                        Handles.DrawWireDisc(worldPos, worldRot * Vector3.forward, r);
                        Handles.Label(worldPos + Vector3.up * (r + 0.1f), "HitFrame(Sphere)");
                        break;
                    }
                    case 2: // Box
                    {
                        Vector3 ext = GetFirstVector3FromNames(shapeObj, new[] { "BoxExtents", "BoxSize", "Box_Size", "Size", "size", "Extents", "extents" }, Vector3.one * 0.5f);
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
                        float radius = GetFirstFloatFromNames(shapeObj, new[] { "CapsuleRadius", "capsuleRadius", "Radius", "radius", "r" }, 0.25f);
                        float height = GetFirstFloatFromNames(shapeObj, new[] { "CapsuleHeight", "capsuleHeight", "Height", "height", "h" }, 1f);
                        radius = Mathf.Max(0.001f, radius);
                        height = Mathf.Max(0f, height);
                        float half = Mathf.Max(0f, (height - 2f * radius) * 0.5f);
                        Vector3 up = worldRot * Vector3.up;
                        var top = worldPos + up * half;
                        var bot = worldPos - up * half;
                        Handles.DrawWireDisc(top, worldRot * Vector3.up, radius);
                        Handles.DrawWireDisc(bot, worldRot * Vector3.up, radius);
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

        #region Reflection Helpers
        private static int GetIntFromReflected(object obj, string name, int fallback)
        {
            try
            {
                var f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    var v = f.GetValue(obj);
                    if (v is int i) return i;
                    if (v is Enum e) return Convert.ToInt32(e);
                    if (v != null) { try { return Convert.ToInt32(v); } catch { } }
                }
                var p = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null)
                {
                    var v = p.GetValue(obj, null);
                    if (v is int ip) return ip;
                    if (v is Enum e2) return Convert.ToInt32(e2);
                    if (v != null) { try { return Convert.ToInt32(v); } catch { } }
                }
            }
            catch { }
            return fallback;
        }

        private static float GetFloatFromReflected(object obj, string name, float fallback)
        {
            try
            {
                var f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    var v = f.GetValue(obj);
                    if (v is float fv) return fv;
                    if (v is double dv) return (float)dv;
                    if (v is int iv) return iv;
                    if (v != null) { try { return Convert.ToSingle(v); } catch { } }
                }
                var p = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null)
                {
                    var v = p.GetValue(obj, null);
                    if (v is float fp) return fp;
                    if (v is double dp) return (float)dp;
                    if (v is int ip) return ip;
                    if (v != null) { try { return Convert.ToSingle(v); } catch { } }
                }
            }
            catch { }
            return fallback;
        }

        private static Vector3 GetVector3FromReflected(object obj, string name, Vector3 fallback)
        {
            if (TryGetVector3FromReflected(obj, name, out var v)) return v;
            return fallback;
        }

        private static bool TryGetVector3FromReflected(object obj, string name, out Vector3 result)
        {
            result = Vector3.zero;
            try
            {
                var f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    var v = f.GetValue(obj);
                    if (v is Vector3 vv) { result = vv; return true; }
                    // try FPVector3 or similar with x,y,z
                    if (v != null)
                    {
                        var tx = v.GetType().GetField("x", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var ty = v.GetType().GetField("y", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var tz = v.GetType().GetField("z", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (tx != null && ty != null && tz != null)
                        {
                            try
                            {
                                var xv = tx.GetValue(v); var yv = ty.GetValue(v); var zv = tz.GetValue(v);
                                float xf, yf, zf;
                                if (!TryConvertNumeric(xv, out xf)) xf = 0f;
                                if (!TryConvertNumeric(yv, out yf)) yf = 0f;
                                if (!TryConvertNumeric(zv, out zf)) zf = 0f;
                                result = new Vector3(xf, yf, zf);
                                return true;
                            }
                            catch { }
                        }
                        // last resort: try to find a ToUnityVector3 / ToUnityVector3 extension method
                        if (TryInvokeToUnityVector3(v, out var outV)) { result = outV; return true; }
                    }
                }
                var p = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null)
                {
                    var v = p.GetValue(obj, null);
                    if (v is Vector3 vp) { result = vp; return true; }
                    if (v != null)
                    {
                        var tx = v.GetType().GetField("x", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var ty = v.GetType().GetField("y", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var tz = v.GetType().GetField("z", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (tx != null && ty != null && tz != null)
                        {
                            try
                            {
                                var xv = tx.GetValue(v); var yv = ty.GetValue(v); var zv = tz.GetValue(v);
                                float xf, yf, zf;
                                if (!TryConvertNumeric(xv, out xf)) xf = 0f;
                                if (!TryConvertNumeric(yv, out yf)) yf = 0f;
                                if (!TryConvertNumeric(zv, out zf)) zf = 0f;
                                result = new Vector3(xf, yf, zf);
                                return true;
                            }
                            catch { }
                        }
                        if (TryInvokeToUnityVector3(v, out var outV2)) { result = outV2; return true; }
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool TryConvertNumeric(object val, out float f)
        {
            f = 0f;
            if (val == null) return false;
            try
            {
                if (val is float ff) { f = ff; return true; }
                if (val is double dd) { f = (float)dd; return true; }
                if (val is int ii) { f = ii; return true; }
                if (val is long ll) { f = ll; return true; }
                // If it's a Photon.Deterministic.FP or similar, try to call ToFloat or toString/convert
                var t = val.GetType();
                var m = t.GetMethod("ToFloat", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null)
                {
                    var res = m.Invoke(val, null);
                    if (res is float rf) { f = rf; return true; }
                    if (res is double rd) { f = (float)rd; return true; }
                }
                // try property 'RawValue' or 'Value'
                var pv = t.GetProperty("RawValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pv != null)
                {
                    var rv = pv.GetValue(val, null);
                    if (rv != null) { f = Convert.ToSingle(rv); return true; }
                }
                pv = t.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pv != null)
                {
                    var rv = pv.GetValue(val, null);
                    if (rv != null) { f = Convert.ToSingle(rv); return true; }
                }
                // last resort: try Convert.ToSingle
                f = Convert.ToSingle(val);
                return true;
            }
            catch { return false; }
        }

        private static bool TryInvokeToUnityVector3(object value, out Vector3 result)
        {
            result = Vector3.zero;
            if (value == null) return false;
            try
            {
                var valType = value.GetType();
                // search loaded assemblies for methods named ToUnityVector3 that accept this param type (or a compatible type)
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types = null;
                    try { types = asm.GetTypes(); } catch { continue; }
                    foreach (var t in types)
                    {
                        foreach (var mi in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (mi.Name != "ToUnityVector3") continue;
                            var ps = mi.GetParameters();
                            if (ps.Length != 1) continue;
                            if (ps[0].ParameterType.IsAssignableFrom(valType) || ps[0].ParameterType.Name == valType.Name)
                            {
                                var outObj = mi.Invoke(null, new object[] { value });
                                if (outObj is Vector3 v3) { result = v3; return true; }
                                // some ToUnityVector3 return Vector3Int or FPVector3 conversion - try to convert
                                if (outObj is Vector3Int v3i) { result = (Vector3)v3i; return true; }
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool TryGetQuaternionFromReflected(object obj, string name, out Quaternion result)
        {
            result = Quaternion.identity;
            try
            {
                var f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    var v = f.GetValue(obj);
                    if (v is Quaternion q) { result = q; return true; }
                    if (v != null)
                    {
                        var tx = v.GetType().GetField("x", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var ty = v.GetType().GetField("y", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var tz = v.GetType().GetField("z", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var tw = v.GetType().GetField("w", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (tx != null && ty != null && tz != null && tw != null)
                        {
                            try
                            {
                                var xv = tx.GetValue(v); var yv = ty.GetValue(v); var zv = tz.GetValue(v); var wv = tw.GetValue(v);
                                float xf = xv is float fxf ? fxf : (xv != null ? Convert.ToSingle(xv) : 0f);
                                float yf = yv is float fyf ? fyf : (yv != null ? Convert.ToSingle(yv) : 0f);
                                float zf = zv is float fzf ? fzf : (zv != null ? Convert.ToSingle(zv) : 0f);
                                float wf = wv is float fwf ? fwf : (wv != null ? Convert.ToSingle(wv) : 1f);
                                result = new Quaternion(xf, yf, zf, wf);
                                return true;
                            }
                            catch { }
                        }
                    }
                }
                var p = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null)
                {
                    var v = p.GetValue(obj, null);
                    if (v is Quaternion qp) { result = qp; return true; }
                }
            }
            catch { }
            return false;
        }
        #endregion

        #region Name-fallback helpers
        private static int GetFirstIntFromNames(object obj, string[] names, int fallback)
        {
            foreach (var n in names)
            {
                var v = GetIntFromReflected(obj, n, int.MinValue);
                if (v != int.MinValue) return v;
            }
            return fallback;
        }

        private static float GetFirstFloatFromNames(object obj, string[] names, float fallback)
        {
            foreach (var n in names)
            {
                var v = GetFloatFromReflected(obj, n, float.NaN);
                if (!float.IsNaN(v)) return v;
            }
            return fallback;
        }

        private static Vector3 GetFirstVector3FromNames(object obj, string[] names, Vector3 fallback)
        {
            foreach (var n in names)
            {
                var v = GetVector3FromReflected(obj, n, fallback);
                if (v != fallback) return v;
            }
            return fallback;
        }

        private static bool TryGetVector3FromNames(object obj, string[] names, out Vector3 result)
        {
            result = Vector3.zero;
            foreach (var n in names)
            {
                if (TryGetVector3FromReflected(obj, n, out result)) return true;
            }
            return false;
        }

        private static bool TryGetQuaternionFromNames(object obj, string[] names, out Quaternion result)
        {
            result = Quaternion.identity;
            foreach (var n in names)
            {
                if (TryGetQuaternionFromReflected(obj, n, out result)) return true;
            }
            return false;
        }
        #endregion

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
        private static GameObject ResolvePreviewRoot(UnityEngine.Object parentTarget)
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
