# Tuning Window

Edit all your game's tuning values in one window. Mark ScriptableObject fields with `[Tunable]` and they show
up in **Window ▸ Tuning**, grouped into tabs and folders. You can edit them during play and keep the changes
afterwards, and Save and Revert apply to all of them at once.

## Install

Pick one:

- **From disk:** *Window ▸ Package Manager ▸ + ▸ Install package from disk…*, then pick this folder's
  `package.json`.
- **From a tarball:** *Window ▸ Package Manager ▸ + ▸ Install package from tarball…*, then pick
  `com.gamearchitecture.tuning-1.0.0.tgz`.
- **Embedded:** copy the whole `com.gamearchitecture.tuning` folder into your project's `Packages/` folder.

The package requires Unity 2021.3 or newer, and has been tested on Unity 6000.3.

## Use

1. Put `[Tunable("Tab", "Folder/Subfolder")]` on serialized fields of a ScriptableObject. Fields inside
   `[Serializable]` classes that the ScriptableObject contains work too.
2. Create an asset of that ScriptableObject.
3. Open **Window ▸ Tuning**.

```csharp
using GameArchitecture.Tuning;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Car Config")]
public class CarConfig : ScriptableObject
{
    [Tunable("Car", "Engine")]
    [Tooltip("Top speed in km/h.")]           // tooltips show in the window
    public float topSpeedKmh = 120f;

    [Tunable("Car", "Engine/Gears")]          // nested folders
    public AnimationCurve gearSpacing = AnimationCurve.Linear(0, 0, 1, 1);

    [Tunable("Car", "Engine", ReadOnly = true)] // shown, but not editable
    public float[] gearTopSpeeds;              // e.g. filled in OnValidate

    public HandlingSettings handling = new();  // [Tunable] fields inside are found too
}
```

### In the window

- **Tabs** come from the first argument, and **folders** (which can be folded) from the second.
- The **search box** filters by field or folder name.
- **Save** writes every changed asset to disk. **Revert** undoes everything since the last save, or since the
  window was opened. A `*` on a tab means it has unsaved changes.
- **Play mode:** edits apply immediately and are kept after you stop playing, because they're assets, not scene
  objects.
- **Refresh** searches the project again. The window also refreshes by itself when assets change.

### Tables and read-only fields

- **Arrays and lists are drawn as tables**: one row per element, and one column per field of the element type.
  - Plain values get a `#` column and a `Value` column.
  - Column headers use `[InspectorName("…")]` on the element's fields if present, and `[Tooltip]` shows when you
    hover a header.
  - Editable tables have − and + buttons to remove and add rows.
- **`ReadOnly = true`** shows a field without letting it be edited. In tables, read-only values show as plain
  text. This is useful for previews you work out in `OnValidate`.
- **The Inspector looks the same.** ScriptableObjects with `[Tunable]` fields get tables and read-only fields in
  the Inspector too, through a fallback editor. ScriptableObjects without `[Tunable]` fields, or with their own
  custom editor, are left alone.

```csharp
[Serializable]
public struct GearInfo
{
    public int gear;
    [InspectorName("Top km/h"), Tooltip("Top speed in this gear.")] public float topSpeedKmh;
}

[Tunable("Car", "Engine", ReadOnly = true)] public GearInfo[] gearOverview;

private void OnValidate() { /* fill gearOverview from the other settings */ }
```

## Notes

- Only ScriptableObject assets are scanned, not MonoBehaviours in scenes.
- `[Tunable]` is a plain attribute and doesn't change how Unity serializes the field. Removing the package only
  stops the window and Inspector features; your data is untouched (code using `[Tunable]` won't compile until the
  attributes are removed).
- Runtime assembly: `GameArchitecture.Tuning`, which only contains the attribute. Editor assembly:
  `GameArchitecture.Tuning.Editor`.
