#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_ugui")]
    public static class ManageUGUI
    {
        private static DefaultControls.Resources s_StandardResources;

        private static DefaultControls.Resources GetStandardResources()
        {
            if (s_StandardResources.standard == null)
            {
                s_StandardResources = new DefaultControls.Resources();
            }
            return s_StandardResources;
        }

        public static object HandleCommand(JObject @params)
        {
            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess) return new ErrorResponse(actionResult.ErrorMessage);
            string action = actionResult.Value;

            switch (action.ToLowerInvariant())
            {
                case "create_element":
                    return CreateElement(p);
                case "set_layout":
                case "modify_element":
                    return ModifyElement(p);
                case "ensure_canvas":
                    return EnsureCanvas(p);
                default:
                    return new ErrorResponse($"Unknown action '{action}' for 'manage_ugui' tool.");
            }
        }

        private static object CreateElement(ToolParams p)
        {
            var typeResult = p.GetRequired("type");
            if (!typeResult.IsSuccess) return new ErrorResponse(typeResult.ErrorMessage);
            string type = typeResult.Value;

            string name = p.Get("name");
            JToken parentToken = p.GetRaw("parent");

            // 1. Find Parent (Intelligent)
            GameObject parentGo = null;
            if (parentToken != null)
            {
                parentGo = MCPForUnity.Editor.Tools.GameObjects.ManageGameObjectCommon.FindObjectInternal(parentToken, "by_id_or_name_or_path");
            }
            
            // If no parent specified, check if current selection is a UI element
            if (parentGo == null)
            {
                var selected = Selection.activeGameObject;
                if (selected != null && selected.GetComponent<RectTransform>() != null)
                {
                    parentGo = selected;
                    McpLog.Info($"[ManageUGUI] Using selected object '{parentGo.name}' as parent.");
                }
            }

            // 2. Ensure Canvas if no parent or parent is not UI
            if (parentGo == null || (parentGo.GetComponentInParent<Canvas>() == null && parentGo.GetComponent<Canvas>() == null))
            {
                McpLog.Info("[ManageUGUI] No Canvas found in parent hierarchy. Ensuring Canvas exists.");
                parentGo = EnsureCanvasInternal(parentGo);
            }

            // 3. Create using DefaultControls
            GameObject uiGo = null;
            var resources = GetStandardResources();

            try {
                switch (type.ToLowerInvariant())
                {
                    case "button": uiGo = DefaultControls.CreateButton(resources); break;
                    case "image": uiGo = DefaultControls.CreateImage(resources); break;
                    case "text": uiGo = DefaultControls.CreateText(resources); break;
                    case "scrollview": case "scroll_view": uiGo = DefaultControls.CreateScrollView(resources); break;
                    case "slider": uiGo = DefaultControls.CreateSlider(resources); break;
                    case "toggle": uiGo = DefaultControls.CreateToggle(resources); break;
                    case "panel": uiGo = DefaultControls.CreatePanel(resources); break;
                    case "dropdown": uiGo = DefaultControls.CreateDropdown(resources); break;
                    case "inputfield": case "input_field": uiGo = DefaultControls.CreateInputField(resources); break;
                    case "scrollbar": uiGo = DefaultControls.CreateScrollbar(resources); break;
                    case "rawimage": case "raw_image": uiGo = DefaultControls.CreateRawImage(resources); break;
                    case "empty":
                        uiGo = new GameObject(name ?? "UI Element", typeof(RectTransform));
                        break;
                    default:
                        return new ErrorResponse($"Unsupported UI type '{type}'. Valid types: Button, Image, Text, ScrollView, Slider, Toggle, Panel, Dropdown, InputField, Scrollbar, RawImage, Empty.");
                }
            } catch (Exception e) {
                return new ErrorResponse($"Internal error creating {type}: {e.Message}");
            }

            if (uiGo == null)
            {
                return new ErrorResponse($"Failed to create UI element of type '{type}'.");
            }

            if (!string.IsNullOrEmpty(name))
            {
                uiGo.name = name;
            }

            // 4. Parenting and Reset
            uiGo.transform.SetParent(parentGo.transform, false);
            uiGo.transform.localScale = Vector3.one;
            uiGo.transform.localPosition = Vector3.zero;

            // Ensure RectTransform
            RectTransform rt = uiGo.GetComponent<RectTransform>();
            if (rt == null) rt = uiGo.AddComponent<RectTransform>();

            // 5. Apply Layout & Visuals
            ApplyLayoutProperties(rt, p);
            ApplyVisualProperties(uiGo, p);

            // 6. Special case: Button stretching child text
            if (type.ToLowerInvariant() == "button")
            {
                var rtChildText = uiGo.GetComponentInChildren<Text>()?.rectTransform;
                if (rtChildText == null) {
                    var tmproType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
                    if (tmproType != null) {
                        var tmpro = uiGo.GetComponentInChildren(tmproType);
                        if (tmpro != null) rtChildText = tmpro.GetComponent<RectTransform>();
                    }
                }

                if (rtChildText != null) {
                    ApplyAnchorPreset(rtChildText, "stretch_stretch");
                }
            }

            Undo.RegisterCreatedObjectUndo(uiGo, $"Create UI {type}");
            Selection.activeGameObject = uiGo;

            return new SuccessResponse($"Created UI {type} '{uiGo.name}' successfully.", GameObjectSerializer.GetGameObjectData(uiGo));
        }

        private static object ModifyElement(ToolParams p)
        {
            JToken targetToken = p.GetRaw("target");
            GameObject targetGo = MCPForUnity.Editor.Tools.GameObjects.ManageGameObjectCommon.FindObjectInternal(targetToken, "by_id_or_name_or_path");
            
            if (targetGo == null) return new ErrorResponse("Target UI element not found.");
            
            RectTransform rt = targetGo.GetComponent<RectTransform>();
            if (rt == null) return new ErrorResponse("Target element does not have a RectTransform. UI tools only work on UI elements.");

            Undo.RecordObject(targetGo, "Modify UGUI Element");
            Undo.RecordObject(rt, "Modify UI Layout");

            ApplyLayoutProperties(rt, p);
            ApplyVisualProperties(targetGo, p);

            EditorUtility.SetDirty(targetGo);
            EditorUtility.SetDirty(rt);
            return new SuccessResponse($"Element '{targetGo.name}' updated successfully.", GameObjectSerializer.GetGameObjectData(targetGo));
        }

        private static void ApplyLayoutProperties(RectTransform rt, ToolParams p)
        {
            // Anchor Preset
            string preset = p.Get("anchor_preset")?.ToLowerInvariant();
            if (!string.IsNullOrEmpty(preset))
            {
                ApplyAnchorPreset(rt, preset);
            }

            // Direct Transform Properties
            Vector2? sizeDelta = VectorParsing.ParseVector2(p.GetRaw("size_delta"));
            if (sizeDelta.HasValue) rt.sizeDelta = sizeDelta.Value;

            Vector2? pos = VectorParsing.ParseVector2(p.GetRaw("anchored_position"));
            if (pos.HasValue) rt.anchoredPosition = pos.Value;

            float? pivotX = p.GetFloat("pivot_x");
            float? pivotY = p.GetFloat("pivot_y");
            if (pivotX.HasValue || pivotY.HasValue)
            {
                rt.pivot = new Vector2(pivotX ?? rt.pivot.x, pivotY ?? rt.pivot.y);
            }

            // Layout Group Support
            string layoutGroup = p.Get("layout_group");
            if (!string.IsNullOrEmpty(layoutGroup))
            {
                ApplyLayoutGroup(rt.gameObject, layoutGroup, p);
            }
        }

        private static void ApplyLayoutGroup(GameObject go, string type, ToolParams p)
        {
            HorizontalOrVerticalLayoutGroup group = null;
            GridLayoutGroup grid = null;

            switch (type.ToLowerInvariant())
            {
                case "horizontal":
                    group = go.GetComponent<HorizontalLayoutGroup>() ?? go.AddComponent<HorizontalLayoutGroup>();
                    break;
                case "vertical":
                    group = go.GetComponent<VerticalLayoutGroup>() ?? go.AddComponent<VerticalLayoutGroup>();
                    break;
                case "grid":
                    grid = go.GetComponent<GridLayoutGroup>() ?? go.AddComponent<GridLayoutGroup>();
                    break;
                case "none":
                case "remove":
                    foreach (var lg in go.GetComponents<LayoutGroup>()) UnityEngine.Object.DestroyImmediate(lg);
                    return;
            }

            if (group != null)
            {
                float? spacing = p.GetFloat("spacing");
                if (spacing.HasValue) group.spacing = spacing.Value;

                string align = p.Get("child_alignment");
                if (!string.IsNullOrEmpty(align) && Enum.TryParse<TextAnchor>(align, true, out var result))
                    group.childAlignment = result;

                bool? forceExpandW = p.GetBool("child_force_expand_width");
                if (forceExpandW.HasValue) group.childForceExpandWidth = forceExpandW.Value;
                
                bool? forceExpandH = p.GetBool("child_force_expand_height");
                if (forceExpandH.HasValue) group.childForceExpandHeight = forceExpandH.Value;

                bool? controlW = p.GetBool("child_control_width");
                if (controlW.HasValue) group.childControlWidth = controlW.Value;

                bool? controlH = p.GetBool("child_control_height");
                if (controlH.HasValue) group.childControlHeight = controlH.Value;
            }

            if (grid != null)
            {
                Vector2? cellSize = VectorParsing.ParseVector2(p.GetRaw("cell_size"));
                if (cellSize.HasValue) grid.cellSize = cellSize.Value;

                Vector2? spacing = VectorParsing.ParseVector2(p.GetRaw("spacing"));
                if (spacing.HasValue) grid.spacing = spacing.Value;

                string align = p.Get("child_alignment");
                if (!string.IsNullOrEmpty(align) && Enum.TryParse<TextAnchor>(align, true, out var result))
                    grid.childAlignment = result;
            }
        }

        private static void ApplyVisualProperties(GameObject go, ToolParams p)
        {
            // Color Extraction (Shared)
            Color? mainColor = ParseColor(p.Get("color"));

            // Image / RawImage / Panel properties
            Image img = go.GetComponent<Image>();
            RawImage rawImg = go.GetComponent<RawImage>();
            
            if (img != null)
            {
                string spritePath = p.Get("sprite");
                if (!string.IsNullOrEmpty(spritePath))
                {
                    img.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetPathUtility.SanitizeAssetPath(spritePath));
                    // Auto-set preserved aspect if it's a new sprite and not explicitly disabled
                    if (img.sprite != null && !p.Has("preserve_aspect")) img.preserveAspect = true;
                }
                
                if (mainColor.HasValue) img.color = mainColor.Value;
                
                bool? raycast = p.GetBool("raycast_target");
                if (raycast.HasValue) img.raycastTarget = raycast.Value;
                
                bool? preserve = p.GetBool("preserve_aspect");
                if (preserve.HasValue) img.preserveAspect = preserve.Value;
            }
            else if (rawImg != null)
            {
                string texPath = p.Get("texture");
                if (!string.IsNullOrEmpty(texPath))
                {
                    rawImg.texture = AssetDatabase.LoadAssetAtPath<Texture>(AssetPathUtility.SanitizeAssetPath(texPath));
                }
                
                if (mainColor.HasValue) rawImg.color = mainColor.Value;
            }

            // Text properties
            string text = p.Get("text");
            int? fontSize = p.GetInt("font_size");
            string fontPath = p.Get("font");
            string align = p.Get("alignment");

            // Legacy Text
            Text txt = go.GetComponent<Text>();
            if (txt != null)
            {
                if (text != null) txt.text = text;
                if (fontSize.HasValue) txt.fontSize = fontSize.Value;
                if (!string.IsNullOrEmpty(fontPath))
                {
                    txt.font = AssetDatabase.LoadAssetAtPath<Font>(AssetPathUtility.SanitizeAssetPath(fontPath));
                }
                if (!string.IsNullOrEmpty(align))
                {
                    if (Enum.TryParse<TextAnchor>(align, true, out var result))
                        txt.alignment = result;
                }
                if (mainColor.HasValue) txt.color = mainColor.Value;
            }

            // TextMeshPro support via reflection
            var tmproType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
            if (tmproType != null)
            {
                var tmpro = go.GetComponent(tmproType);
                if (tmpro != null)
                {
                    if (text != null) SetPropertyValue(tmpro, "text", text);
                    if (fontSize.HasValue) SetPropertyValue(tmpro, "fontSize", (float)fontSize.Value);
                    if (mainColor.HasValue) SetPropertyValue(tmpro, "color", mainColor.Value);
                    
                    if (!string.IsNullOrEmpty(align))
                    {
                        // Map alignment to TMPro enum if possible
                        // TMPro values: Left, Center, Right, Justified, Flush, Geometry
                        // Or combined like TopLeft
                        object tmproAlign = MapTMPAlignment(align);
                        if (tmproAlign != null) SetPropertyValue(tmpro, "alignment", tmproAlign);
                    }

                    bool? autoSize = p.GetBool("auto_size");
                    if (autoSize.HasValue) SetPropertyValue(tmpro, "enableAutoSizing", autoSize.Value);
                }
            }
        }

        private static object MapTMPAlignment(string align)
        {
            var type = Type.GetType("TMPro.TextAlignmentOptions, Unity.TextMeshPro");
            if (type == null) return null;

            // Try direct parse
            if (Enum.TryParse(type, align, true, out var result)) return result;

            // Try mapping Legacy TextAnchor to TMP
            switch (align.ToLowerInvariant())
            {
                case "upperleft": case "topleft": return Enum.Parse(type, "TopLeft");
                case "uppercenter": case "topcenter": return Enum.Parse(type, "Top");
                case "upperright": case "topright": return Enum.Parse(type, "TopRight");
                case "middleleft": return Enum.Parse(type, "Left");
                case "middlecenter": case "center": return Enum.Parse(type, "Center");
                case "middleright": return Enum.Parse(type, "Right");
                case "lowerleft": case "bottomleft": return Enum.Parse(type, "BottomLeft");
                case "lowercenter": case "bottomcenter": return Enum.Parse(type, "Bottom");
                case "lowerright": case "bottomright": return Enum.Parse(type, "BottomRight");
            }

            return null;
        }

        private static void SetPropertyValue(object obj, string propertyName, object value)
        {
            var prop = obj.GetType().GetProperty(propertyName);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(obj, value);
            }
        }

        private static Color? ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;

            // Handle named colors
            switch (hex.ToLowerInvariant())
            {
                case "white": return Color.white;
                case "black": return Color.black;
                case "red": return Color.red;
                case "green": return Color.green;
                case "blue": return Color.blue;
                case "yellow": return Color.yellow;
                case "cyan": return Color.cyan;
                case "magenta": return Color.magenta;
                case "gray": case "grey": return Color.gray;
                case "clear": case "transparent": return Color.clear;
            }

            if (ColorUtility.TryParseHtmlString(hex, out Color color)) return color;
            if (ColorUtility.TryParseHtmlString("#" + hex, out color)) return color;
            return null;
        }

        private static object EnsureCanvas(ToolParams p)
        {
            GameObject canvasGo = EnsureCanvasInternal(null);
            return new SuccessResponse("Canvas and EventSystem ensured.", GameObjectSerializer.GetGameObjectData(canvasGo));
        }

        private static GameObject EnsureCanvasInternal(GameObject parent = null)
        {
            Canvas existing = null;
            if (parent != null) existing = parent.GetComponentInParent<Canvas>();
            
            if (existing == null) 
            {
                // Intelligent Search: Prioritize "Main" or active Canvases
                var allCanvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                
                Canvas bestMatch = null;
                int bestScore = -1;

                foreach (var c in allCanvases)
                {
                    if (!c.gameObject.activeInHierarchy) continue;

                    int score = 0;
                    if (c.name.Contains("Main", StringComparison.OrdinalIgnoreCase)) score += 100;
                    if (c.name.Contains("UI", StringComparison.OrdinalIgnoreCase)) score += 50;
                    score += c.transform.childCount;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = c;
                    }
                }
                existing = bestMatch;
            }

            if (existing != null) return existing.gameObject;

            // Create Canvas
            GameObject canvasGo = new GameObject("Canvas");
            canvasGo.layer = LayerMask.NameToLayer("UI");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(canvasGo, "Create Canvas");

            // Create EventSystem
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
            {
                GameObject esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
                Undo.RegisterCreatedObjectUndo(esGo, "Create EventSystem");
            }

            return canvasGo;
        }

        private static void ApplyAnchorPreset(RectTransform rt, string preset)
        {
            switch (preset)
            {
                case "stretch_stretch": 
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; 
                    rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; 
                    break;
                case "top_left": rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); break;
                case "top_center": rt.anchorMin = new Vector2(0.5f, 1); rt.anchorMax = new Vector2(0.5f, 1); rt.pivot = new Vector2(0.5f, 1); break;
                case "top_right": rt.anchorMin = new Vector2(1, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 1); break;
                case "middle_left": rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f); break;
                case "middle_center": rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); break;
                case "middle_right": rt.anchorMin = new Vector2(1, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(1, 0.5f); break;
                case "bottom_left": rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero; break;
                case "bottom_center": rt.anchorMin = new Vector2(0.5f, 0); rt.anchorMax = new Vector2(0.5f, 0); rt.pivot = new Vector2(0.5f, 0); break;
                case "bottom_right": rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(1, 0); break;
                
                case "horiz_stretch_top": 
                    rt.anchorMin = new Vector2(0, 1); rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 1); 
                    rt.offsetMin = new Vector2(0, rt.offsetMin.y); rt.offsetMax = new Vector2(0, rt.offsetMax.y);
                    break;
                case "horiz_stretch_middle": 
                    rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); 
                    rt.offsetMin = new Vector2(0, rt.offsetMin.y); rt.offsetMax = new Vector2(0, rt.offsetMax.y);
                    break;
                case "horiz_stretch_bottom": 
                    rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0); 
                    rt.offsetMin = new Vector2(0, rt.offsetMin.y); rt.offsetMax = new Vector2(0, rt.offsetMax.y);
                    break;
                case "vert_stretch_left": 
                    rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 0.5f); 
                    rt.offsetMin = new Vector2(rt.offsetMin.x, 0); rt.offsetMax = new Vector2(rt.offsetMax.x, 0);
                    break;
                case "vert_stretch_center": 
                    rt.anchorMin = new Vector2(0.5f, 0); rt.anchorMax = new Vector2(0.5f, 1); rt.pivot = new Vector2(0.5f, 0.5f); 
                    rt.offsetMin = new Vector2(rt.offsetMin.x, 0); rt.offsetMax = new Vector2(rt.offsetMax.x, 0);
                    break;
                case "vert_stretch_right": 
                    rt.anchorMin = new Vector2(1, 0); rt.anchorMax = Vector2.one; rt.pivot = new Vector2(1, 0.5f); 
                    rt.offsetMin = new Vector2(rt.offsetMin.x, 0); rt.offsetMax = new Vector2(rt.offsetMax.x, 0);
                    break;
            }
            
            // --- Robustness Fix ---
            EditorUtility.SetDirty(rt);
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
    }
}

