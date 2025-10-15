#if UNITY_EDITOR
using UnityEngine;

namespace EditorPlus.AnimationPreview
{
    internal sealed class TimelineState
    {
        public float Zoom = 1f;
        public float HScroll = 0f;
        public int CursorFrame = 0;
        public Rect VisibleRect;
        public bool IsSeeking = false;
        public bool WasPlayingBeforeSeek = false;
        private int _totalFrames;

        public void Ensure(int totalFrames)
        {
            if (_totalFrames != totalFrames)
            {
                _totalFrames = totalFrames;
                HScroll = Mathf.Clamp(HScroll, 0f, Mathf.Max(0f, WidthInPixels(totalFrames) - VisibleRect.width + 1f));
                CursorFrame = Mathf.Clamp(CursorFrame, 0, Mathf.Max(0, totalFrames - 1));
            }
        }

        public float PixelsPerFrame => 6f * Zoom;
        public float WidthInPixels(int frames) => frames * PixelsPerFrame;
        public float FrameToPixelX(int frame) => frame * PixelsPerFrame - HScroll;
        public int PixelToFrame(float localX, int totalFrames)
        {
            if (totalFrames <= 0) return 0;
            float px = Mathf.Clamp(localX + HScroll, 0f, WidthInPixels(totalFrames));
            int frame = Mathf.RoundToInt(px / Mathf.Max(1e-6f, PixelsPerFrame));
            return Mathf.Clamp(frame, 0, Mathf.Max(0, totalFrames - 1));
        }
    }
}
#endif