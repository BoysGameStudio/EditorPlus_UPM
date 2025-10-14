#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EditorPlus.SceneTimeline
{
    // A lightweight, generic palette entry used by EditorPlus timeline preview tools.
    [Serializable]
    public struct PaletteEntry
    {
        public string Label;
        public int Index;
        public Color FillColor;
        public Color OutlineColor;
        public bool IsActive;
    }

    // A tiny event bus to publish palette updates from any host project into EditorPlus tools.
    public static class PaletteBus
    {
        // Raised when the scene timeline palette changes. Consumers receive a snapshot list.
        public static event Action<IReadOnlyList<PaletteEntry>> Changed;

        // Publish a new palette snapshot. Call this from your project code when the active track set changes.
        public static void Publish(IReadOnlyList<PaletteEntry> entries)
        {
            Changed?.Invoke(entries);
        }
    }

    // Receiver contract for components that want to react to a single track label's palette entry.
    public interface ITimelineTrackColorReceiver
    {
        string TrackLabel { get; }
        void ApplyTimelinePalette(PaletteEntry entry);
        void ClearTimelinePalette();
    }

    // Optional receiver for components that can consume the full layered palette.
    public interface ITimelineTrackLayerPaletteReceiver : ITimelineTrackColorReceiver
    {
        bool ApplyLayeredTimelinePalette(IReadOnlyDictionary<string, PaletteEntry> palette);
    }
}
#endif
