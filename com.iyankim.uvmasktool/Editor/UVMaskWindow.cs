using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace IyanKim.UVMaskTool.Editor
{
    internal sealed class UVMaskWindow : EditorWindow
    {
        private const string LanguagePrefsKey = "IyanKim.UVMaskTool.Language";

        private static readonly string[] LanguageLabels =
        {
            "English", "한국어", "日本語"
        };

        private enum ToolLanguage
        {
            English,
            Korean,
            Japanese
        }

        private enum ResolutionPreset
        {
            R256 = 256,
            R512 = 512,
            R1024 = 1024,
            R2048 = 2048,
            R4096 = 4096,
            Custom = -1
        }

        private enum AntiAliasing
        {
            Off = 1,
            SSAA2x = 2,
            SSAA4x = 4
        }

        private enum SelectionMode
        {
            Island,
            Face
        }

        private Renderer targetRenderer;
        private Mesh currentMesh;
        private int[] currentTriangles = Array.Empty<int>();
        private List<Vector2> currentUvs;
        private Vector2[] currentUvArray = Array.Empty<Vector2>();
        private Rect[] currentTriangleBounds = Array.Empty<Rect>();
        private int[] currentIslandByTriangle = Array.Empty<int>();
        private Vector2Int[] currentWireEdges = Array.Empty<Vector2Int>();
        private List<UVIsland> islands = new List<UVIsland>();
        private readonly HashSet<int> selectedTriangleIndices = new HashSet<int>();
        private readonly List<int> selectableTriangleIndices = new List<int>();
        private readonly List<int> availableUvChannels = new List<int>();
        private readonly List<int> availableSubmeshIndices = new List<int>();
        private string[] materialSlotLabels = Array.Empty<string>();

        private ToolLanguage language = ToolLanguage.English;
        private SelectionMode selectionMode = SelectionMode.Island;
        private int selectedMaterialSlotIndex;
        private int selectedSubmeshIndex = -1;
        private int uvChannel;
        private int hoverTriangleIndex = -1;
        private int hoverIslandId = -1;
        private string statusMessage;
        private MessageType statusType = MessageType.Info;
        private Vector2 scrollPosition;
        private Vector2 previewPan;
        private float previewZoom = 1f;
        private Texture2D previewTexture;
        private float previewTextureAlpha = 0.85f;
        private bool showPreviewTexture = true;
        private bool showWireframe = true;
        private bool isPanning;

        private ResolutionPreset resolutionPreset = ResolutionPreset.R1024;
        private int customResolution = 1024;
        private Color backgroundColor = new Color(0f, 0f, 0f, 1f);
        private Color selectedColor = Color.white;
        private Color wireframeColor = new Color(0.88f, 0.88f, 0.88f, 0.58f);
        private int padding;
        private AntiAliasing antiAliasing = AntiAliasing.Off;
        private bool includeAlpha = true;
        private bool sceneViewHighlight = true;

        [MenuItem("Iyan-Kim/Tools/UV Island Mask Generator")]
        public static void Open()
        {
            var window = GetWindow<UVMaskWindow>("UV Mask Generator");
            window.minSize = new Vector2(520f, 680f);
            if (window.position.width < 620f || window.position.height < 900f)
            {
                window.position = new Rect(window.position.x, window.position.y, 640f, 920f);
            }

            window.Show();
        }

        private void OnEnable()
        {
            language = (ToolLanguage)Mathf.Clamp(
                EditorPrefs.GetInt(LanguagePrefsKey, (int)ToolLanguage.English),
                (int)ToolLanguage.English,
                (int)ToolLanguage.Japanese);
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnGUI()
        {
            titleContent = new GUIContent(L("Window Title"));

            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 150f;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(L("Window Title"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(L("HelpText"), MessageType.Info);
            EditorGUILayout.Space(10);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawInputSection();
            EditorGUILayout.Space(10f);
            DrawPreviewSection();
            EditorGUILayout.Space(10f);
            DrawSelectionSection();
            EditorGUILayout.Space(10f);
            DrawExportSection();
            EditorGUILayout.Space(10f);
            DrawLanguageSelector();
            EditorGUILayout.Space(10f);
            EditorGUILayout.EndScrollView();

            EditorGUIUtility.labelWidth = previousLabelWidth;
        }

        private void DrawInputSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(L("Input"), EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            targetRenderer = (Renderer)EditorGUILayout.ObjectField(L("Renderer"), targetRenderer, typeof(Renderer), true);
            if (EditorGUI.EndChangeCheck())
            {
                RebuildFromRenderer();
            }

            using (new EditorGUI.DisabledScope(targetRenderer == null || availableSubmeshIndices.Count == 0))
            {
                EditorGUI.BeginChangeCheck();
                selectedMaterialSlotIndex = EditorGUILayout.Popup(L("Material Slot"), selectedMaterialSlotIndex, materialSlotLabels);
                if (EditorGUI.EndChangeCheck())
                {
                    selectedSubmeshIndex = availableSubmeshIndices[selectedMaterialSlotIndex];
                    RefreshUvChannelsForCurrentMaterial(false);
                }
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }

            if (currentMesh != null)
            {
                EditorGUILayout.LabelField(L("Mesh"), currentMesh.name);
                EditorGUILayout.LabelField(L("Vertices"), currentMesh.vertexCount.ToString());
                EditorGUILayout.LabelField(L("Triangles"), (currentMesh.triangles.Length / 3).ToString());
                EditorGUILayout.LabelField(L("Material Triangles"), (currentTriangles.Length / 3).ToString());
                EditorGUILayout.LabelField(L("Islands"), islands.Count.ToString());
                EditorGUILayout.LabelField(L("Selectable Faces"), GetSelectableTriangleCount().ToString());
                if (!currentMesh.isReadable)
                {
                    EditorGUILayout.HelpBox(L("ReadWriteWarning"), MessageType.Warning);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawLanguageSelector()
        {
            EditorGUILayout.LabelField("Language / 한국어 / 日本語", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            language = (ToolLanguage)EditorGUILayout.Popup((int)language, LanguageLabels);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetInt(LanguagePrefsKey, (int)language);
                Repaint();
            }
        }

        private void DrawPreviewSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(L("UV Preview"), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L("Preview Texture"), GUILayout.Width(98f));
                previewTexture = (Texture2D)EditorGUILayout.ObjectField(previewTexture, typeof(Texture2D), false, GUILayout.Width(220f));
                using (new EditorGUI.DisabledScope(GetSelectedMaterialPreviewTexture() == null))
                {
                    if (GUILayout.Button(L("Use Material Texture"), GUILayout.Width(150f)))
                    {
                        previewTexture = GetSelectedMaterialPreviewTexture();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L("Reset View"), GUILayout.Width(110f)))
                {
                    previewZoom = 1f;
                    previewPan = Vector2.zero;
                    Repaint();
                }

                EditorGUILayout.LabelField(L("Zoom"), GUILayout.Width(46f));
                previewZoom = EditorGUILayout.Slider(previewZoom, 0.1f, 12f);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                showPreviewTexture = EditorGUILayout.ToggleLeft(L("Show Texture"), showPreviewTexture, GUILayout.Width(130f));
                using (new EditorGUI.DisabledScope(!showPreviewTexture))
                {
                    EditorGUILayout.LabelField(L("Texture Opacity"), GUILayout.Width(98f));
                    previewTextureAlpha = EditorGUILayout.Slider(previewTextureAlpha, 0f, 1f);
                }
            }

            showWireframe = EditorGUILayout.ToggleLeft(L("Wireframe"), showWireframe);
            using (new EditorGUI.DisabledScope(!showWireframe))
            {
                wireframeColor = EditorGUILayout.ColorField(L("UV Wireframe Color"), wireframeColor);
            }

            var previewHeight = Mathf.Clamp(position.height - 500f, 280f, 420f);
            var previewRect = GUILayoutUtility.GetRect(10f, previewHeight, GUILayout.ExpandWidth(true));
            var localRect = new Rect(0f, 0f, previewRect.width, previewRect.height);
            var transform = UVPreviewRenderer.CreateView(localRect, currentUvs, currentTriangles, previewZoom, previewPan);

            HandlePreviewInput(previewRect, transform);
            UVPreviewRenderer.Draw(
                previewRect,
                currentTriangles,
                currentUvArray,
                islands,
                selectedTriangleIndices,
                hoverTriangleIndex,
                hoverIslandId,
                currentWireEdges,
                showWireframe,
                wireframeColor,
                showPreviewTexture ? previewTexture : null,
                previewTextureAlpha,
                transform);
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectionSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(L("Selection Info"), EditorStyles.boldLabel);

            var selectionModeLabels = new[] { L("Island"), L("Face") };
            EditorGUI.BeginChangeCheck();
            var nextSelectionMode = (SelectionMode)EditorGUILayout.Popup(L("Selection Mode"), (int)selectionMode, selectionModeLabels);
            if (EditorGUI.EndChangeCheck())
            {
                SetSelectionMode(nextSelectionMode);
            }

            EditorGUILayout.LabelField(
                selectionMode == SelectionMode.Island ? L("Selected Islands") : L("Selected Faces"),
                selectionMode == SelectionMode.Island ? GetSelectedIslandCount().ToString() : selectedTriangleIndices.Count.ToString());

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(islands.Count == 0))
                {
                    if (GUILayout.Button(L("Select All")))
                    {
                        SelectAll();
                        RepaintSceneAndWindow();
                    }

                    if (GUILayout.Button(L("Invert")))
                    {
                        InvertSelection();
                        RepaintSceneAndWindow();
                    }
                }

                using (new EditorGUI.DisabledScope(selectedTriangleIndices.Count == 0))
                {
                    if (GUILayout.Button(L("Clear")))
                    {
                        ClearSelection();
                        RepaintSceneAndWindow();
                    }
                }
            }

            sceneViewHighlight = EditorGUILayout.ToggleLeft(L("Scene Highlight"), sceneViewHighlight);
            EditorGUILayout.EndVertical();
        }

        private void DrawExportSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(L("Export Settings"), EditorStyles.boldLabel);
            resolutionPreset = (ResolutionPreset)EditorGUILayout.EnumPopup(L("Resolution"), resolutionPreset);
            if (resolutionPreset == ResolutionPreset.Custom)
            {
                customResolution = EditorGUILayout.IntField(L("Custom Resolution"), customResolution);
            }

            includeAlpha = EditorGUILayout.Toggle(L("Include Alpha"), includeAlpha);
            backgroundColor = EditorGUILayout.ColorField(L("Background Color"), backgroundColor);
            selectedColor = EditorGUILayout.ColorField(L("Selected Color"), selectedColor);
            padding = EditorGUILayout.IntSlider(L("Padding"), padding, -10, 10);
            antiAliasing = (AntiAliasing)EditorGUILayout.EnumPopup(L("Anti-Aliasing"), antiAliasing);

            var resolution = GetOutputResolution();
            var aaScale = (int)antiAliasing;
            var effectiveResolution = resolution * aaScale;
            if (resolution > 4096 || effectiveResolution > 8192)
            {
                EditorGUILayout.HelpBox(
                    string.Format(L("LargeExportWarning"), resolution, effectiveResolution),
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(!CanExport(resolution)))
            {
                if (GUILayout.Button(L("Export PNG"), GUILayout.Height(32f)))
                {
                    ExportMask(resolution, aaScale);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void HandlePreviewInput(Rect previewRect, UVPreviewRenderer.ViewTransform transform)
        {
            var evt = Event.current;
            if (!previewRect.Contains(evt.mousePosition))
            {
                if ((hoverIslandId != -1 || hoverTriangleIndex != -1) && evt.type == EventType.MouseMove)
                {
                    hoverTriangleIndex = -1;
                    hoverIslandId = -1;
                    Repaint();
                }
                return;
            }

            var localMouse = evt.mousePosition - previewRect.position;

            if (evt.type == EventType.ScrollWheel)
            {
                var uvBeforeZoom = transform.ScreenToUv(localMouse);
                var zoomMultiplier = evt.delta.y > 0f ? 0.9f : 1.1f;
                previewZoom = Mathf.Clamp(previewZoom * zoomMultiplier, 0.1f, 12f);

                var localRect = new Rect(0f, 0f, previewRect.width, previewRect.height);
                var nextTransform = UVPreviewRenderer.CreateView(localRect, currentUvs, currentTriangles, previewZoom, previewPan);
                var screenAfterZoom = nextTransform.UvToScreen(uvBeforeZoom);
                previewPan += localMouse - screenAfterZoom;
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDown && (evt.button == 2 || (evt.button == 0 && evt.alt)))
            {
                isPanning = true;
                evt.Use();
                return;
            }

            if (evt.type == EventType.MouseDrag && isPanning)
            {
                previewPan += evt.delta;
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseUp && isPanning)
            {
                isPanning = false;
                evt.Use();
                return;
            }

            if (currentMesh == null || currentUvs == null || islands.Count == 0)
            {
                return;
            }

            if (evt.type == EventType.MouseMove || evt.type == EventType.MouseDrag)
            {
                var nextHoverTriangle = UVSelectionController.PickTriangle(
                    localMouse,
                    transform,
                    currentTriangles,
                    currentUvs,
                    currentTriangleBounds);
                var nextHoverIsland = selectionMode == SelectionMode.Island ? GetIslandIdForTriangle(nextHoverTriangle) : -1;

                if (hoverTriangleIndex != nextHoverTriangle || hoverIslandId != nextHoverIsland)
                {
                    hoverTriangleIndex = nextHoverTriangle;
                    hoverIslandId = nextHoverIsland;
                    Repaint();
                }
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
            {
                var pickedTriangle = UVSelectionController.PickTriangle(
                    localMouse,
                    transform,
                    currentTriangles,
                    currentUvs,
                    currentTriangleBounds);

                if (selectionMode == SelectionMode.Face)
                {
                    UVSelectionController.ApplyClick(
                        selectedTriangleIndices,
                        pickedTriangle,
                        evt.shift,
                        evt.control || evt.command);
                }
                else
                {
                    var pickedIsland = GetIslandForTriangle(pickedTriangle);
                    if (pickedIsland != null)
                    {
                        UVSelectionController.ApplyGroupClick(
                            selectedTriangleIndices,
                            pickedIsland.triangleIndices,
                            evt.shift,
                            evt.control || evt.command);
                    }
                }

                evt.Use();
                RepaintSceneAndWindow();
            }
        }

        private void RebuildFromRenderer()
        {
            currentMesh = ExtractMesh(targetRenderer);
            currentTriangles = Array.Empty<int>();
            currentUvs = null;
            ClearPreviewCache();
            availableUvChannels.Clear();
            availableSubmeshIndices.Clear();
            materialSlotLabels = Array.Empty<string>();
            selectedMaterialSlotIndex = 0;
            selectedSubmeshIndex = -1;
            islands.Clear();
            ClearSelection();

            if (targetRenderer == null)
            {
                statusMessage = L("SelectRendererMessage");
                statusType = MessageType.Info;
                RepaintSceneAndWindow();
                return;
            }

            if (currentMesh == null)
            {
                statusMessage = L("NoMeshMessage");
                statusType = MessageType.Error;
                RepaintSceneAndWindow();
                return;
            }

            RefreshMaterialSlots();
            if (availableSubmeshIndices.Count == 0)
            {
                statusMessage = L("NoMaterialSlotsMessage");
                statusType = MessageType.Warning;
                RepaintSceneAndWindow();
                return;
            }

            selectedMaterialSlotIndex = Mathf.Clamp(selectedMaterialSlotIndex, 0, availableSubmeshIndices.Count - 1);
            selectedSubmeshIndex = availableSubmeshIndices[selectedMaterialSlotIndex];
            currentTriangles = currentMesh.GetTriangles(selectedSubmeshIndex);

            RefreshUvChannelsForCurrentMaterial(false);
        }

        private void RefreshUvChannelsForCurrentMaterial(bool preserveCurrentChannel)
        {
            currentTriangles = selectedSubmeshIndex >= 0 && selectedSubmeshIndex < currentMesh.subMeshCount
                ? currentMesh.GetTriangles(selectedSubmeshIndex)
                : Array.Empty<int>();

            currentUvs = null;
            ClearPreviewCache();
            islands.Clear();
            ClearSelection();
            availableUvChannels.Clear();
            availableUvChannels.AddRange(UVIslandDetector.FindAvailableUvChannels(currentMesh, currentTriangles));

            if (availableUvChannels.Count == 0)
            {
                statusMessage = L("NoUvChannelsMessage");
                statusType = MessageType.Warning;
                RepaintSceneAndWindow();
                return;
            }

            var nextChannel = preserveCurrentChannel && availableUvChannels.Contains(uvChannel)
                ? uvChannel
                : UVIslandDetector.FindBestUvChannel(currentMesh, currentTriangles, availableUvChannels);
            uvChannel = availableUvChannels.Contains(nextChannel) ? nextChannel : availableUvChannels[0];
            RebuildCurrentUvChannel();
        }

        private void RebuildCurrentUvChannel()
        {
            currentUvs = null;
            ClearPreviewCache();
            islands.Clear();
            ClearSelection();

            try
            {
                currentUvs = UVIslandDetector.ReadUVs(currentMesh, uvChannel);
                currentTriangles = selectedSubmeshIndex >= 0 && selectedSubmeshIndex < currentMesh.subMeshCount
                    ? currentMesh.GetTriangles(selectedSubmeshIndex)
                    : Array.Empty<int>();
                islands = UVIslandDetector.GenerateIslands(currentMesh, currentUvs, currentTriangles);
                RebuildPreviewCache();
                statusMessage = islands.Count > 0
                    ? string.Format(L("GeneratedMessage"), islands.Count, currentMesh.name, selectedSubmeshIndex)
                    : L("NoIslandsMessage");
                statusType = islands.Count > 0 ? MessageType.Info : MessageType.Warning;
            }
            catch (Exception exception)
            {
                currentUvs = null;
                ClearPreviewCache();
                islands.Clear();
                statusMessage = exception.Message;
                statusType = MessageType.Error;
            }

            RepaintSceneAndWindow();
        }

        private void ExportMask(int resolution, int aaScale)
        {
            if (!CanExport(resolution))
            {
                return;
            }

            var effectiveResolution = resolution * aaScale;
            if (effectiveResolution > 8192)
            {
                var proceed = EditorUtility.DisplayDialog(
                    L("Large Export Title"),
                    string.Format(L("Large Export Dialog"), effectiveResolution),
                    L("Export"),
                    L("Cancel"));
                if (!proceed)
                {
                    return;
                }
            }

            try
            {
                var bg = includeAlpha ? backgroundColor : ForceOpaque(backgroundColor);
                var fg = includeAlpha ? selectedColor : ForceOpaque(selectedColor);
                var texture = UVRasterizer.Rasterize(
                    currentMesh,
                    currentTriangles,
                    currentUvs,
                    selectedTriangleIndices,
                    resolution,
                    bg,
                    fg,
                    aaScale);

                if (padding != 0)
                {
                    UVPaddingProcessor.Apply(texture, padding, bg, fg);
                }

                var defaultName = $"{SanitizeFileName(currentMesh.name)}_Mat{selectedSubmeshIndex}_Mask.png";
                UVExporter.ExportPng(texture, defaultName);
                DestroyImmediate(texture);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog(L("Window Title"), exception.Message, "OK");
            }
        }

        private bool CanExport(int resolution)
        {
            return currentMesh != null
                && currentTriangles != null
                && currentTriangles.Length > 0
                && currentUvs != null
                && islands.Count > 0
                && selectedTriangleIndices.Count > 0
                && resolution > 0
                && resolution <= 8192;
        }

        private int GetOutputResolution()
        {
            return resolutionPreset == ResolutionPreset.Custom
                ? Mathf.Clamp(customResolution, 1, 8192)
                : (int)resolutionPreset;
        }

        private void RefreshMaterialSlots()
        {
            availableSubmeshIndices.Clear();
            if (currentMesh == null)
            {
                materialSlotLabels = Array.Empty<string>();
                return;
            }

            var labels = new List<string>();
            var materials = targetRenderer != null ? targetRenderer.sharedMaterials : Array.Empty<Material>();
            for (var submeshIndex = 0; submeshIndex < currentMesh.subMeshCount; submeshIndex++)
            {
                var triangles = currentMesh.GetTriangles(submeshIndex);
                if (triangles == null || triangles.Length == 0)
                {
                    continue;
                }

                availableSubmeshIndices.Add(submeshIndex);
                var materialName = submeshIndex < materials.Length && materials[submeshIndex] != null
                    ? materials[submeshIndex].name
                    : L("No Material");
                labels.Add($"{submeshIndex}: {materialName}");
            }

            materialSlotLabels = labels.ToArray();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!sceneViewHighlight || targetRenderer == null || currentMesh == null || currentTriangles == null || currentTriangles.Length == 0 || selectedTriangleIndices.Count == 0)
            {
                return;
            }

            var vertices = currentMesh.vertices;
            var meshTriangles = currentTriangles;
            var matrix = targetRenderer.localToWorldMatrix;
            var drawnTriangles = 0;
            Handles.color = new Color(1f, 0.62f, 0.1f, 0.85f);

            foreach (var triangleIndex in selectedTriangleIndices)
            {
                var baseIndex = triangleIndex * 3;
                if (triangleIndex < 0 || baseIndex + 2 >= meshTriangles.Length)
                {
                    continue;
                }

                var a = matrix.MultiplyPoint3x4(vertices[meshTriangles[baseIndex]]);
                var b = matrix.MultiplyPoint3x4(vertices[meshTriangles[baseIndex + 1]]);
                var c = matrix.MultiplyPoint3x4(vertices[meshTriangles[baseIndex + 2]]);
                Handles.DrawAAPolyLine(3f, a, b, c, a);

                drawnTriangles++;
                if (drawnTriangles > 5000)
                {
                    return;
                }
            }
        }

        private void SetSelectionMode(SelectionMode nextMode)
        {
            if (selectionMode == nextMode)
            {
                return;
            }

            selectionMode = nextMode;
            ClearSelection();
            RepaintSceneAndWindow();
        }

        private void SelectAll()
        {
            selectedTriangleIndices.Clear();
            for (var i = 0; i < selectableTriangleIndices.Count; i++)
            {
                selectedTriangleIndices.Add(selectableTriangleIndices[i]);
            }
        }

        private void InvertSelection()
        {
            var nextSelection = new HashSet<int>();
            if (selectionMode == SelectionMode.Face)
            {
                for (var i = 0; i < selectableTriangleIndices.Count; i++)
                {
                    var triangleIndex = selectableTriangleIndices[i];
                    if (!selectedTriangleIndices.Contains(triangleIndex))
                    {
                        nextSelection.Add(triangleIndex);
                    }
                }
            }
            else
            {
                for (var i = 0; i < islands.Count; i++)
                {
                    var triangleIndices = islands[i].triangleIndices;
                    var isFullySelected = triangleIndices.Count > 0;
                    for (var t = 0; t < triangleIndices.Count; t++)
                    {
                        if (!selectedTriangleIndices.Contains(triangleIndices[t]))
                        {
                            isFullySelected = false;
                            break;
                        }
                    }

                    if (isFullySelected)
                    {
                        continue;
                    }

                    for (var t = 0; t < triangleIndices.Count; t++)
                    {
                        nextSelection.Add(triangleIndices[t]);
                    }
                }
            }

            selectedTriangleIndices.Clear();
            foreach (var triangleIndex in nextSelection)
            {
                selectedTriangleIndices.Add(triangleIndex);
            }
        }

        private void ClearSelection()
        {
            selectedTriangleIndices.Clear();
            hoverTriangleIndex = -1;
            hoverIslandId = -1;
        }

        private int GetSelectableTriangleCount()
        {
            return selectableTriangleIndices.Count;
        }

        private int GetSelectedIslandCount()
        {
            var count = 0;
            for (var i = 0; i < islands.Count; i++)
            {
                var triangleIndices = islands[i].triangleIndices;
                if (triangleIndices.Count == 0)
                {
                    continue;
                }

                var isFullySelected = true;
                for (var t = 0; t < triangleIndices.Count; t++)
                {
                    if (!selectedTriangleIndices.Contains(triangleIndices[t]))
                    {
                        isFullySelected = false;
                        break;
                    }
                }

                if (isFullySelected)
                {
                    count++;
                }
            }

            return count;
        }

        private UVIsland GetIslandForTriangle(int triangleIndex)
        {
            var islandId = GetIslandIdForTriangle(triangleIndex);
            return islandId >= 0 && islandId < islands.Count ? islands[islandId] : null;
        }

        private int GetIslandIdForTriangle(int triangleIndex)
        {
            if (triangleIndex < 0 || currentIslandByTriangle == null || triangleIndex >= currentIslandByTriangle.Length)
            {
                return -1;
            }

            return currentIslandByTriangle[triangleIndex];
        }

        private Texture2D GetSelectedMaterialPreviewTexture()
        {
            if (targetRenderer == null || selectedSubmeshIndex < 0)
            {
                return null;
            }

            var materials = targetRenderer.sharedMaterials;
            if (materials == null || selectedSubmeshIndex >= materials.Length)
            {
                return null;
            }

            var material = materials[selectedSubmeshIndex];
            if (material == null || material.mainTexture == null)
            {
                return null;
            }

            return material.mainTexture as Texture2D;
        }

        private void RebuildPreviewCache()
        {
            currentUvArray = currentUvs != null ? currentUvs.ToArray() : Array.Empty<Vector2>();
            currentTriangleBounds = UVPreviewRenderer.BuildTriangleBounds(currentTriangles, currentUvs);
            currentIslandByTriangle = UVPreviewRenderer.BuildIslandMembership(islands, currentTriangles != null ? currentTriangles.Length / 3 : 0);
            currentWireEdges = UVPreviewRenderer.BuildWireEdges(currentTriangles);

            selectableTriangleIndices.Clear();
            for (var triangleIndex = 0; triangleIndex < currentIslandByTriangle.Length; triangleIndex++)
            {
                if (currentIslandByTriangle[triangleIndex] >= 0)
                {
                    selectableTriangleIndices.Add(triangleIndex);
                }
            }
        }

        private void ClearPreviewCache()
        {
            currentUvArray = Array.Empty<Vector2>();
            currentTriangleBounds = Array.Empty<Rect>();
            currentIslandByTriangle = Array.Empty<int>();
            currentWireEdges = Array.Empty<Vector2Int>();
            selectableTriangleIndices.Clear();
        }

        private static Mesh ExtractMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                return skinnedMeshRenderer.sharedMesh;
            }

            if (renderer is MeshRenderer meshRenderer)
            {
                var meshFilter = meshRenderer.GetComponent<MeshFilter>();
                return meshFilter != null ? meshFilter.sharedMesh : null;
            }

            return null;
        }

        private string L(string key)
        {
            if (language == ToolLanguage.Korean)
            {
                switch (key)
                {
                    case "Language": return "언어";
                    case "Input": return "입력";
                    case "Renderer": return "렌더러";
                    case "UV Channel": return "UV 채널";
                    case "Detected UV Channels": return "감지된 UV 채널";
                    case "Mesh": return "메시";
                    case "Vertices": return "버텍스";
                    case "Triangles": return "삼각형";
                    case "Islands": return "아일랜드";
                    case "UV Preview": return "UV 미리보기";
                    case "Reset View": return "뷰 초기화";
                    case "Zoom": return "줌";
                    case "Selection Info": return "선택 정보";
                    case "Selected Islands": return "선택된 아일랜드";
                    case "Select All": return "전체 선택";
                    case "Invert": return "반전";
                    case "Clear": return "해제";
                    case "Scene Highlight": return "Scene View에서 선택 삼각형 강조";
                    case "Export Settings": return "내보내기 설정";
                    case "Resolution": return "해상도";
                    case "Custom Resolution": return "사용자 해상도";
                    case "Include Alpha": return "알파 포함";
                    case "Background Color": return "배경 색상";
                    case "Selected Color": return "선택 색상";
                    case "Padding": return "패딩";
                    case "Anti-Aliasing": return "안티앨리어싱";
                    case "Export PNG": return "PNG 내보내기";
                    case "Export": return "내보내기";
                    case "Cancel": return "취소";
                    case "None": return "없음";
                    case "Window Title": return "UV 아일랜드 마스크 생성기";
                    case "HelpText": return "Renderer와 Material Slot을 선택하고 UV island를 클릭해 마스크 PNG를 생성합니다.\nUV 채널은 선택된 material slot 기준으로 자동 선택됩니다.";
                    case "Large Export Title": return "큰 UV 마스크 내보내기";
                    case "ReadWriteWarning": return "Read/Write가 비활성화되어 있습니다. 아일랜드 감지나 내보내기가 실패하면 모델 import 설정에서 Read/Write를 활성화하세요.";
                    case "SelectRendererMessage": return "SkinnedMeshRenderer 또는 MeshRenderer를 선택하세요.";
                    case "NoMeshMessage": return "메시를 찾을 수 없습니다. MeshRenderer 오브젝트에는 shared mesh가 있는 MeshFilter가 필요합니다.";
                    case "NoUvChannelsMessage": return "사용 가능한 UV 채널이 없습니다. 비어 있거나 모든 삼각형 면적이 0인 UV 채널은 숨깁니다.";
                    case "NoIslandsMessage": return "유효한 UV 아일랜드가 없습니다. 면적이 0인 UV 삼각형은 제외됩니다.";
                    case "GeneratedMessage": return "{1}의 material {2}에서 UV 아일랜드 {0}개를 생성했습니다.";
                    case "LargeExportWarning": return "큰 내보내기: {0}x{0}, 작업 버퍼 {1}x{1}. 메모리를 많이 사용할 수 있습니다.";
                    case "Large Export Dialog": return "작업 버퍼가 {0}x{0}입니다. 시간이 걸리고 메모리를 많이 사용할 수 있습니다.";
                }
            }

            if (language == ToolLanguage.Japanese)
            {
                switch (key)
                {
                    case "Language": return "言語";
                    case "Input": return "入力";
                    case "Renderer": return "レンダラー";
                    case "UV Channel": return "UVチャンネル";
                    case "Detected UV Channels": return "検出されたUVチャンネル";
                    case "Mesh": return "メッシュ";
                    case "Vertices": return "頂点";
                    case "Triangles": return "三角形";
                    case "Islands": return "アイランド";
                    case "UV Preview": return "UVプレビュー";
                    case "Reset View": return "ビューをリセット";
                    case "Zoom": return "ズーム";
                    case "Selection Info": return "選択情報";
                    case "Selected Islands": return "選択中のアイランド";
                    case "Select All": return "すべて選択";
                    case "Invert": return "反転";
                    case "Clear": return "解除";
                    case "Scene Highlight": return "Scene Viewで選択三角形を強調表示";
                    case "Export Settings": return "エクスポート設定";
                    case "Resolution": return "解像度";
                    case "Custom Resolution": return "カスタム解像度";
                    case "Include Alpha": return "アルファを含める";
                    case "Background Color": return "背景色";
                    case "Selected Color": return "選択色";
                    case "Padding": return "パディング";
                    case "Anti-Aliasing": return "アンチエイリアス";
                    case "Export PNG": return "PNGを書き出し";
                    case "Export": return "書き出し";
                    case "Cancel": return "キャンセル";
                    case "None": return "なし";
                    case "Window Title": return "UV Island Mask Generator";
                    case "HelpText": return "RendererとMaterial Slotを選択し、UVアイランドをクリックしてマスクPNGを書き出します。\nUVチャンネルは選択したmaterial slotから自動選択されます。";
                    case "Large Export Title": return "大きいUVマスクの書き出し";
                    case "ReadWriteWarning": return "Read/Writeが無効です。アイランド検出または書き出しに失敗する場合は、モデルのインポート設定でRead/Writeを有効にしてください。";
                    case "SelectRendererMessage": return "SkinnedMeshRendererまたはMeshRendererを選択してください。";
                    case "NoMeshMessage": return "メッシュが見つかりません。MeshRendererオブジェクトにはshared meshを持つMeshFilterが必要です。";
                    case "NoUvChannelsMessage": return "使用可能なUVチャンネルがありません。空、またはすべての三角形面積が0のUVチャンネルは表示しません。";
                    case "NoIslandsMessage": return "有効なUVアイランドが見つかりません。面積0のUV三角形は無視されます。";
                    case "GeneratedMessage": return "{1} の material {2} から {0} 個のUVアイランドを生成しました。";
                    case "LargeExportWarning": return "大きい書き出し: {0}x{0}, 作業バッファ {1}x{1}。多くのメモリを使用する可能性があります。";
                    case "Large Export Dialog": return "作業バッファは {0}x{0} です。時間がかかり、多くのメモリを使用する可能性があります。";
                }
            }

            switch (key)
            {
                case "Window Title": return "UV Island Mask Generator";
                case "HelpText": return "Select a Renderer and material slot, choose Island or Face mode, optionally overlay a texture, then click UV selections to export a mask PNG.\nThe UV channel is selected automatically from the chosen material slot.";
                case "Material Slot": return "Material Slot";
                case "Material Triangles": return "Material Triangles";
                case "Selectable Faces": return "Selectable Faces";
                case "Preview Texture": return "Preview Texture";
                case "Use Material Texture": return "Use Material Texture";
                case "Show Texture": return "Show Texture";
                case "Texture Opacity": return "Texture Opacity";
                case "Wireframe": return "Wireframe";
                case "UV Wireframe Color": return "UV Wireframe Color";
                case "Selection Mode": return "Selection Mode";
                case "Island": return "Island";
                case "Face": return "Face";
                case "Selected Faces": return "Selected Faces";
                case "No Material": return "No Material";
                case "ReadWriteWarning": return "Read/Write is disabled. If island detection or export fails, enable Read/Write in the model import settings.";
                case "SelectRendererMessage": return "Select a SkinnedMeshRenderer or MeshRenderer.";
                case "NoMeshMessage": return "No mesh was found. MeshRenderer objects need a MeshFilter with a shared mesh.";
                case "NoMaterialSlotsMessage": return "No material slots with triangles were found.";
                case "NoUvChannelsMessage": return "No usable UV channels were found. Empty UV channels and channels with only zero-area UV triangles are hidden.";
                case "NoIslandsMessage": return "No valid UV islands were found. Degenerate UV triangles are ignored.";
                case "GeneratedMessage": return "Generated {0} UV islands from {1}, material {2}.";
                case "LargeExportWarning": return "Large export: {0}x{0}, working buffer {1}x{1}. This can use substantial memory.";
                case "Large Export Title": return "Large UV Mask Export";
                case "Large Export Dialog": return "The working buffer will be {0}x{0}. This may take a while and use a lot of memory.";
                default: return key;
            }
        }

        private static Color ForceOpaque(Color color)
        {
            color.a = 1f;
            return color;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "UV_Island";
            }

            var invalid = System.IO.Path.GetInvalidFileNameChars();
            for (var i = 0; i < invalid.Length; i++)
            {
                value = value.Replace(invalid[i], '_');
            }

            return value;
        }

        private void RepaintSceneAndWindow()
        {
            Repaint();
            SceneView.RepaintAll();
        }
    }
}
