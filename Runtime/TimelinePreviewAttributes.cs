// Odin AnimationClip Timeline Drawer (Field Attribute Version)
// Field-level [ShowActionTimeline] attribute you can put directly on an AnimationClip field.
// Keep this file runtime-safe (no UnityEditor references) so builds won't break when attribute is present.

using System;

/// <summary>
/// Put this attribute directly on an AnimationClip field to render a timeline under it.
/// Example:
/// [TimelinePreview(Height = 240)] public AnimationClip Animation;
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class TimelinePreviewAttribute : Attribute
{
    /// <summary>Timeline drawing height.</summary>
    public float Height { get; set; } = 260f;
}

/// <summary>
/// Mark any member as a track to be drawn on the timeline. Works on fields or properties.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class TimelineTrackAttribute : Attribute // Rename to TimelineTrackInfoAttribute if you want even shorter
{
    public string Label { get; }
    public string ColorHex { get; }
    public int Order { get; }

    public TimelineTrackAttribute(string label = null, string colorHex = null, int order = 0)
    {
        Label = label;
        ColorHex = colorHex;
        Order = order;
    }
}

