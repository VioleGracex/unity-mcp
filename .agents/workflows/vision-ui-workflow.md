---
description: How to recreate a UI from a reference image using Unity UGUI and existing assets.
---

# Workflow: Vision-to-UI Recreation

Follow these steps when a user provides a reference image and asks to recreate it in Unity using UGUI.

## 1. Scene & Asset Analysis
Before building, understand the target environment and available tools:
- **List UI Assets**: Search for sprites and fonts that match the visual style.
  - `find_ui_sprites searchPattern="Button"`
  - `manage_asset action="search" search_pattern="t:Font"`
- **Check Canvas**: Ensure a Canvas exists with a proper `CanvasScaler` (1920x1080 is default).
  - `manage_ugui action="ensure_canvas"`

## 2. Visual Decomposition
Analyze the reference image and identify the hierarchy:
- **Background/Container**: Large panels or full-screen images.
- **Layout Groups**: Items arranged in rows (Horizontal) or columns (Vertical).
- **Core Elements**: Buttons, Icons (Image), Labels (Text), Input Fields.
- **Properties**: Estimate colors (Hex or names), font sizes, and anchor positions.

## 3. Implementation (Bottom-Up or Top-Down)
**IMPORTANT: ALWAYS use `manage_ugui` for UI elements.** NEVER use `manage_gameobject` to create UI, as it defaults to a standard `Transform` instead of a `RectTransform`.

### Step A: Create the Root Container
The tool now supports intelligent parenting. If no parent is specified, it will try to use the selected UI element or default to the Canvas.
```json
{
  "action": "create_element",
  "type": "Panel",
  "name": "MainContainer",
  "color": "#2D2D2D",
  "anchor_preset": "middle_center",
  "size_delta": {"x": 800, "y": 600}
}
```

### Step B: Add Visual Elements with Assets
```json
{
  "action": "create_element",
  "type": "Image",
  "name": "HeaderIcon",
  "parent": "MainContainer",
  "sprite": "Assets/UI/Icons/Search_Icon.png",
  "anchor_preset": "top_left",
  "anchored_position": {"x": 20, "y": -20},
  "size_delta": {"x": 40, "y": 40}
}
```

### Step C: Using Layout Groups
You can now add layout groups directly during creation or via `modify_element`.
```json
{
  "action": "modify_element",
  "target": "MainContainer",
  "layout_group": "vertical",
  "spacing": 10,
  "child_alignment": "MiddleCenter",
  "child_control_width": true,
  "child_force_expand_width": true
}
```

## 4. Refinement
- **Iterative Tweaks**: Use `action="modify_element"` to adjust spacing or colors.
- **Color Extraction**: Use vision to get exact hex codes. The tool supports `#RRGGBB` or common names like `white`, `black`, `red`.
- **Validation**: Use `manage_camera action="screenshot"` to verify the result.

## 5. Tips for Success
- **RectTransform Only**: `manage_ugui` enforces `RectTransform`. If you accidentally use another tool, use `manage_ugui` to "fix" it by targeting the object with a modification.
- **TMP Support**: Use `fontSize`, `text`, and `alignment`. The tool automatically maps legacy alignment names to TextMeshPro equivalents.
- **Preserve Aspect**: By default, `manage_ugui` enables `preserveAspect` when assigning a new sprite to an image.
