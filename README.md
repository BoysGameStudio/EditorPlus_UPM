Editor Plus

A lightweight, editor-only toolkit. Currently includes:

- EditorPlus.UniversalAnimPlayer: a minimal animation preview driver used by inspector tools.
- ShowActionTimelineAttribute: a runtime-safe attribute for marking timeline fields; editor drawers live under Editor.
- SceneTimeline preview helpers: generic palette types, a small palette event bus, a preview stage, and a renderer binder.

Structure
- EditorPlus/
  - Editor/                     # Editor-only asmdef & sources
    - EditorPlus.Editor.asmdef
    - UniversalAnimPlayer.cs
    - Drawers/ShowActionTimelineDrawer.cs
  - ClipTimelinePreviewStage.cs
    - TimelineTrackPreviewBinder.cs
    - SceneTimelineTypes.cs
    - OdinInspectorUtils.cs
  - Runtime/                    # Runtime-safe attributes and types
    - EditorPlus.Runtime.asmdef
    - ShowActionTimelineAttribute.cs
  - package.json
  - README.md

Notes
- UniversalAnimPlayer is editor-only (in Editor folder) and wrapped with UNITY_EDITOR; it will not be included in player builds.
- ShowActionTimelineAttribute lives in Runtime so shipping builds compile even if fields keep the attribute. The corresponding drawers and tools are in Editor.
- SceneTimeline palette: call EditorPlus.SceneTimeline.PaletteBus.Publish(List<PaletteEntry>) from your own editor tooling to update colors/indices used by the outline preview. No dependency on your game assemblies is required.
- Keep gameplay/simulation logic out of EditorPlus to preserve determinism and isolation. Only editor-facing utilities should live here.
