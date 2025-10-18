#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace EditorPlus.AnimationPreview
{
    internal static class ProviderUtils
    {
        public static void ExtractAttributeData(object attrInstance, ref string label, ref string colorHex, ref int order, ref string previewName)
        {
            if (attrInstance == null) return;
            var atype = attrInstance.GetType();
            var pLabel = atype.GetProperty("Label"); if (pLabel != null) label = (pLabel.GetValue(attrInstance) as string) ?? label;
            var pColor = atype.GetProperty("ColorHex"); if (pColor != null) colorHex = pColor.GetValue(attrInstance) as string;
            var pOrder = atype.GetProperty("Order"); if (pOrder != null) order = (int)(pOrder.GetValue(attrInstance) ?? 0);
            var pPreview = atype.GetProperty("PreviewName"); if (pPreview != null) previewName = pPreview.GetValue(attrInstance) as string;
        }
    }
}
#endif
