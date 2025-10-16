#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace EditorPlus.AnimationPreview
{
    internal static class RulerRenderer
    {
        public static void DrawRuler(Rect rect, TimelineState st, float fps, int totalFrames)
        {
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.15f));

            float rulerStartX = rect.x + TimelineContext.TimelineLabelWidth; // Align with track content area
            float rulerWidth = rect.width - TimelineContext.TimelineLabelWidth;

            float ppf = st.PixelsPerFrame;
            int step = 1;
            if (ppf < 3) step = 5;
            if (ppf < 1) step = 10;
            if (ppf < 0.5f) step = 20;
            if (ppf < 0.25f) step = 50;

            int start = Mathf.Max(0, st.PixelToFrame(0, totalFrames));
            int end = Mathf.Min(totalFrames, st.PixelToFrame(rulerWidth, totalFrames) + 1);

            Handles.BeginGUI();
            for (int f = AlignTo(start, step); f <= end; f += step)
            {
                float x = rulerStartX + st.FrameToPixelX(f);
                float h = (f % (step * 5) == 0) ? rect.height : rect.height * 0.6f;
                Handles.color = new Color(1, 1, 1, 0.2f);
                Handles.DrawLine(new Vector3(x, rect.yMax, 0), new Vector3(x, rect.yMax - h, 0));
                if (f % (step * 5) == 0)
                {
                    var label = f.ToString();
                    var size = EditorStyles.miniLabel.CalcSize(new GUIContent(label));
                    GUI.Label(new Rect(x + 2, rect.yMax - size.y - 1, size.x, size.y), label, EditorStyles.miniLabel);
                }
            }
            Handles.EndGUI();
        }

        private static int AlignTo(int v, int step) => (v % step == 0) ? v : (v + (step - (v % step)));
    }
}
#endif
