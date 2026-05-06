using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace IyanKim.UVMaskTool.Editor
{
    internal sealed class UVMaskWindow : EditorWindow
    {
        private const string LanguagePrefsKey = "IyanKim.UVMaskTool.Language";
        private const double BrushSceneRepaintIntervalSeconds = 1d / 24d;
        private const double BrushWindowRepaintIntervalSeconds = 1d / 30d;
        private const int BrushGridResolution = 32;

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

        private enum FaceSelectionTool
        {
            Click,
            Brush
        }

        private Renderer targetRenderer;
        private Mesh currentMesh;
        private Mesh scenePreviewMesh;
        private Mesh sceneOverlayMesh;
        private Material sceneOverlayMaterial;
        private Texture2D scenePreviewTexture;
        private bool scenePreviewDirty = true;
        private int[] currentTriangles = Array.Empty<int>();
        private List<Vector2> currentUvs;
        private Vector2[] currentUvArray = Array.Empty<Vector2>();
        private Rect[] currentTriangleBounds = Array.Empty<Rect>();
        private int[] currentIslandByTriangle = Array.Empty<int>();
        private Vector2Int[] currentWireEdges = Array.Empty<Vector2Int>();
        private List<int>[] currentBrushTriangleBins = Array.Empty<List<int>>();
        private Rect currentBrushGridBounds;
        private int[] currentBrushTriangleVisitMarks = Array.Empty<int>();
        private List<UVIsland> islands = new List<UVIsland>();
        private readonly HashSet<int> selectedTriangleIndices = new HashSet<int>();
        private readonly List<int> selectableTriangleIndices = new List<int>();
        private readonly List<int> brushCandidateTriangleIndices = new List<int>();
        private readonly List<int> sceneOverlayTriangleIndexBuffer = new List<int>();
        private readonly List<Vector3> sceneOverlayVertices = new List<Vector3>();
        private readonly List<int> availableUvChannels = new List<int>();
        private readonly List<int> availableSubmeshIndices = new List<int>();
        private string[] materialSlotLabels = Array.Empty<string>();

        private ToolLanguage language = ToolLanguage.English;
        private SelectionMode selectionMode = SelectionMode.Island;
        private FaceSelectionTool faceSelectionTool = FaceSelectionTool.Click;
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
        private Texture2D previewSelectedOverlayTexture;
        private Texture2D previewWireframeOverlayTexture;
        private float previewTextureAlpha = 0.85f;
        private bool showPreviewTexture = true;
        private bool showWireframe = true;
        private bool isPanning;
        private bool isBrushPainting;
        private bool isBrushErasing;
        private float brushRadiusPixels = 18f;
        private Vector2 brushCursorPosition;
        private Vector2[] cachedPreviewScreenUvs = Array.Empty<Vector2>();
        private Vector2 cachedPreviewScreenUvPan;
        private Vector2 cachedPreviewScreenUvSize = Vector2.zero;
        private float cachedPreviewScreenUvZoom = -1f;
        private Vector2Int cachedPreviewOverlaySize = Vector2Int.zero;
        private Vector2 cachedPreviewOverlayPan;
        private float cachedPreviewOverlayZoom = -1f;
        private int cachedPreviewSelectionRevision = -1;
        private Color cachedPreviewSelectionColor = new Color(-1f, -1f, -1f, -1f);
        private Color cachedPreviewWireframeColor = new Color(-1f, -1f, -1f, -1f);
        private int brushTriangleVisitStamp = 1;
        private int selectionRevision;
        private int sceneOverlaySelectionRevision = -1;
        private bool pendingBrushSceneRepaint;
        private double nextAllowedBrushSceneRepaintTime;
        private double nextAllowedBrushWindowRepaintTime;
        private Vector2 lastBrushSamplePosition;

        private ResolutionPreset resolutionPreset = ResolutionPreset.R1024;
        private int customResolution = 1024;
        private Color backgroundColor = new Color(0f, 0f, 0f, 1f);
        private Color previewSelectedColor = new Color(1f, 0.67f, 0.15f, 0.72f);
        private Color selectedColor = Color.white;
        private Color wireframeColor = new Color(0f, 0f, 0f, 0.72f);
        private int padding;
        private AntiAliasing antiAliasing = AntiAliasing.Off;
        private bool includeAlpha = true;
        private bool sceneViewHighlight = true;
        private bool pauseSceneOverlayWhileBrushing;

        [MenuItem("Tools/UV Mask Tool/UV Island Mask Generator")]
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
            InvalidatePreviewOverlayCaches();
            DisposeScenePreviewMesh();
            ClearSceneMaterialPreview(true);
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

            using (new EditorGUI.DisabledScope(currentMesh == null || availableUvChannels.Count == 0))
            {
                var uvChannelLabels = BuildUvChannelLabels();
                var currentUvChannelIndex = Mathf.Max(0, availableUvChannels.IndexOf(uvChannel));
                EditorGUI.BeginChangeCheck();
                var nextUvChannelIndex = EditorGUILayout.Popup(L("UV Channel"), currentUvChannelIndex, uvChannelLabels);
                if (EditorGUI.EndChangeCheck() && nextUvChannelIndex >= 0 && nextUvChannelIndex < availableUvChannels.Count)
                {
                    uvChannel = availableUvChannels[nextUvChannelIndex];
                    RebuildCurrentUvChannel();
                    MarkScenePreviewDirty();
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
                if (availableUvChannels.Count > 0)
                {
                    EditorGUILayout.LabelField(L("Detected UV Channels"), string.Join(", ", BuildUvChannelLabels()));
                }
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

            EditorGUI.BeginChangeCheck();
            previewSelectedColor = EditorGUILayout.ColorField(L("Preview Color"), previewSelectedColor);
            if (EditorGUI.EndChangeCheck())
            {
                MarkScenePreviewDirty();
            }

            using (new EditorGUI.DisabledScope(!sceneViewHighlight))
            {
                if (GUILayout.Button(L("Refresh Scene Preview"), GUILayout.Width(180f)))
                {
                    RepaintSceneAndWindow();
                }
            }

            var previewHeight = Mathf.Clamp(position.height - 500f, 280f, 420f);
            var previewRect = GUILayoutUtility.GetRect(10f, previewHeight, GUILayout.ExpandWidth(true));
            var localRect = new Rect(0f, 0f, previewRect.width, previewRect.height);
            var transform = UVPreviewRenderer.CreateView(localRect, currentUvs, currentTriangles, previewZoom, previewPan);
            HandlePreviewInput(previewRect, transform);
            transform = UVPreviewRenderer.CreateView(localRect, currentUvs, currentTriangles, previewZoom, previewPan);
            var previewScreenUvs = GetPreviewScreenUvs(transform, localRect);
            var showBrushCursor = selectionMode == SelectionMode.Face
                && faceSelectionTool == FaceSelectionTool.Brush
                && (previewRect.Contains(Event.current.mousePosition) || isBrushPainting);
            UVPreviewRenderer.Draw(
                previewRect,
                currentTriangles,
                currentUvArray,
                previewScreenUvs,
                islands,
                selectedTriangleIndices,
                hoverTriangleIndex,
                hoverIslandId,
                currentWireEdges,
                showWireframe,
                wireframeColor,
                previewSelectedColor,
                null,
                null,
                showBrushCursor,
                brushCursorPosition,
                brushRadiusPixels,
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

            if (selectionMode == SelectionMode.Face)
            {
                var faceToolLabels = new[] { L("Click"), L("Brush") };
                faceSelectionTool = (FaceSelectionTool)EditorGUILayout.Popup(L("Face Tool"), (int)faceSelectionTool, faceToolLabels);

                if (faceSelectionTool == FaceSelectionTool.Brush)
                {
                    brushRadiusPixels = EditorGUILayout.Slider(L("Brush Size"), brushRadiusPixels, 4f, 96f);
                    pauseSceneOverlayWhileBrushing = EditorGUILayout.ToggleLeft(L("Pause Scene Overlay While Brushing"), pauseSceneOverlayWhileBrushing);
                }
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

            EditorGUI.BeginChangeCheck();
            sceneViewHighlight = EditorGUILayout.ToggleLeft(L("Scene Highlight"), sceneViewHighlight);
            if (EditorGUI.EndChangeCheck())
            {
                RepaintSceneAndWindow();
            }
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
            if (evt.type == EventType.MouseUp && (evt.button == 0 || evt.button == 1))
            {
                isBrushPainting = false;
                isBrushErasing = false;
            }

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
            brushCursorPosition = localMouse;

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

            if (evt.type == EventType.MouseMove || (evt.type == EventType.MouseDrag && !isBrushPainting))
            {
                var hoverUvCenter = transform.ScreenToUv(localMouse);
                var hoverCandidates = CollectBrushCandidateTriangles(hoverUvCenter, 0f);
                var nextHoverTriangle = UVSelectionController.PickTriangle(
                    localMouse,
                    transform,
                    currentTriangles,
                    currentUvs,
                    currentTriangleBounds,
                    hoverCandidates);
                var nextHoverIsland = selectionMode == SelectionMode.Island ? GetIslandIdForTriangle(nextHoverTriangle) : -1;

                if (hoverTriangleIndex != nextHoverTriangle || hoverIslandId != nextHoverIsland)
                {
                    hoverTriangleIndex = nextHoverTriangle;
                    hoverIslandId = nextHoverIsland;
                    Repaint();
                }
            }

            if (selectionMode == SelectionMode.Face && faceSelectionTool == FaceSelectionTool.Brush)
            {
                if (evt.type == EventType.MouseDown && (evt.button == 0 || evt.button == 1) && !evt.alt)
                {
                    isBrushPainting = true;
                    isBrushErasing = evt.button == 1;
                    lastBrushSamplePosition = localMouse;
                    var changed = ApplyBrushStroke(localMouse, transform);
                    if (changed)
                    {
                        NotifySelectionChanged();
                    }
                    evt.Use();
                    RepaintAfterBrushStroke(changed, true);
                    return;
                }

                if (evt.type == EventType.MouseDrag && isBrushPainting && !evt.alt)
                {
                    var sampleSpacing = GetBrushSampleSpacing();
                    var movedEnough = (localMouse - lastBrushSamplePosition).sqrMagnitude >= sampleSpacing * sampleSpacing;
                    var changed = movedEnough && ApplyBrushStrokeSegment(lastBrushSamplePosition, localMouse, transform);
                    if (movedEnough)
                    {
                        lastBrushSamplePosition = localMouse;
                    }
                    if (changed)
                    {
                        NotifySelectionChanged();
                    }
                    evt.Use();
                    RepaintAfterBrushStroke(changed, false);
                    return;
                }

                if (evt.type == EventType.MouseUp && (evt.button == 0 || evt.button == 1))
                {
                    FlushPendingBrushSceneRepaint();
                    evt.Use();
                    RepaintSceneAndWindow();
                    return;
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

                NotifySelectionChanged();
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
            if (!sceneViewHighlight
                || (pauseSceneOverlayWhileBrushing && isBrushPainting)
                || targetRenderer == null
                || currentMesh == null
                || currentTriangles == null
                || currentTriangles.Length == 0
                || selectedTriangleIndices.Count == 0)
            {
                return;
            }

            var sceneMesh = GetScenePreviewMesh();
            if (sceneMesh == null)
            {
                return;
            }

            if (TryDrawSceneOverlay(sceneMesh))
            {
                return;
            }

            var vertices = sceneMesh.vertices;
            var meshTriangles = currentTriangles;
            var matrix = GetScenePreviewDrawMatrix();
            var drawnTriangles = 0;
            var lineColor = previewSelectedColor;
            lineColor.a = Mathf.Max(lineColor.a, 0.9f);
            Handles.color = lineColor;

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
            isBrushPainting = false;
            isBrushErasing = false;
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
            NotifySelectionChanged();
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
            NotifySelectionChanged();
        }

        private void ClearSelection()
        {
            isBrushPainting = false;
            isBrushErasing = false;
            pendingBrushSceneRepaint = false;
            selectedTriangleIndices.Clear();
            hoverTriangleIndex = -1;
            hoverIslandId = -1;
            NotifySelectionChanged();
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

        private string[] BuildUvChannelLabels()
        {
            if (availableUvChannels.Count == 0)
            {
                return Array.Empty<string>();
            }

            var labels = new string[availableUvChannels.Count];
            for (var i = 0; i < availableUvChannels.Count; i++)
            {
                labels[i] = $"UV{availableUvChannels[i]}";
            }

            return labels;
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
            var material = GetSelectedScenePreviewMaterial();
            if (material == null)
            {
                return null;
            }

            var texturePropertyName = GetSelectedScenePreviewTexturePropertyName(material);
            if (string.IsNullOrEmpty(texturePropertyName))
            {
                return null;
            }

            var texture = material.GetTexture(texturePropertyName);
            if (texture == null)
            {
                return null;
            }

            return texture as Texture2D;
        }

        private void RebuildPreviewCache()
        {
            currentUvArray = currentUvs != null ? currentUvs.ToArray() : Array.Empty<Vector2>();
            currentTriangleBounds = UVPreviewRenderer.BuildTriangleBounds(currentTriangles, currentUvs);
            currentIslandByTriangle = UVPreviewRenderer.BuildIslandMembership(islands, currentTriangles != null ? currentTriangles.Length / 3 : 0);
            currentWireEdges = UVPreviewRenderer.BuildWireEdges(currentTriangles);
            cachedPreviewScreenUvs = Array.Empty<Vector2>();
            cachedPreviewScreenUvZoom = -1f;
            InvalidatePreviewOverlayCaches();

            selectableTriangleIndices.Clear();
            for (var triangleIndex = 0; triangleIndex < currentIslandByTriangle.Length; triangleIndex++)
            {
                if (currentIslandByTriangle[triangleIndex] >= 0)
                {
                    selectableTriangleIndices.Add(triangleIndex);
                }
            }

            RebuildBrushCandidateGrid();
        }

        private void ClearPreviewCache()
        {
            currentUvArray = Array.Empty<Vector2>();
            currentTriangleBounds = Array.Empty<Rect>();
            currentIslandByTriangle = Array.Empty<int>();
            currentWireEdges = Array.Empty<Vector2Int>();
            currentBrushTriangleBins = Array.Empty<List<int>>();
            currentBrushTriangleVisitMarks = Array.Empty<int>();
            brushCandidateTriangleIndices.Clear();
            cachedPreviewScreenUvs = Array.Empty<Vector2>();
            cachedPreviewScreenUvZoom = -1f;
            InvalidatePreviewOverlayCaches();
            selectableTriangleIndices.Clear();
        }

        private void MarkScenePreviewDirty()
        {
            scenePreviewDirty = true;
        }

        private void NotifySelectionChanged()
        {
            selectionRevision++;
            MarkScenePreviewDirty();
        }

        private void UpdatePreviewOverlayCaches(Vector2Int previewSize, Vector2[] screenUvs)
        {
            if (previewSize.x <= 0 || previewSize.y <= 0 || currentTriangles == null || currentTriangles.Length == 0 || currentUvArray == null || currentUvArray.Length == 0)
            {
                return;
            }

            var viewChanged = cachedPreviewOverlaySize != previewSize
                || Mathf.Abs(cachedPreviewOverlayZoom - previewZoom) > 0.0001f
                || cachedPreviewOverlayPan != previewPan;

            if (viewChanged)
            {
                cachedPreviewOverlaySize = previewSize;
                cachedPreviewOverlayZoom = previewZoom;
                cachedPreviewOverlayPan = previewPan;
                cachedPreviewSelectionRevision = -1;
                cachedPreviewSelectionColor = new Color(-1f, -1f, -1f, -1f);
                cachedPreviewWireframeColor = new Color(-1f, -1f, -1f, -1f);
            }

            if (cachedPreviewSelectionRevision != selectionRevision || cachedPreviewSelectionColor != previewSelectedColor)
            {
                RebuildPreviewSelectedOverlay(previewSize, screenUvs);
                cachedPreviewSelectionRevision = selectionRevision;
                cachedPreviewSelectionColor = previewSelectedColor;
            }

            if (showWireframe && cachedPreviewWireframeColor != wireframeColor)
            {
                RebuildPreviewWireframeOverlay(previewSize, screenUvs);
                cachedPreviewWireframeColor = wireframeColor;
            }
            else if (!showWireframe && previewWireframeOverlayTexture != null)
            {
                DestroyImmediate(previewWireframeOverlayTexture);
                previewWireframeOverlayTexture = null;
                cachedPreviewWireframeColor = new Color(-1f, -1f, -1f, -1f);
            }
            else if (showWireframe && previewWireframeOverlayTexture == null)
            {
                RebuildPreviewWireframeOverlay(previewSize, screenUvs);
                cachedPreviewWireframeColor = wireframeColor;
            }
        }

        private void RebuildPreviewSelectedOverlay(Vector2Int previewSize, Vector2[] screenUvs)
        {
            EnsurePreviewOverlayTexture(ref previewSelectedOverlayTexture, previewSize, "UVMaskTool_PreviewSelected");
            FillPreviewOverlayTexture(previewSelectedOverlayTexture, currentTriangles, screenUvs, selectedTriangleIndices, previewSelectedColor);
        }

        private void RebuildPreviewWireframeOverlay(Vector2Int previewSize, Vector2[] screenUvs)
        {
            EnsurePreviewOverlayTexture(ref previewWireframeOverlayTexture, previewSize, "UVMaskTool_PreviewWireframe");
            FillPreviewWireframeTexture(previewWireframeOverlayTexture, currentTriangles, currentWireEdges, screenUvs, wireframeColor);
        }

        private void EnsurePreviewOverlayTexture(ref Texture2D texture, Vector2Int size, string name)
        {
            if (texture != null && texture.width == size.x && texture.height == size.y)
            {
                return;
            }

            if (texture != null)
            {
                DestroyImmediate(texture);
            }

            texture = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false, true)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        private void FillPreviewOverlayTexture(Texture2D texture, int[] meshTriangles, Vector2[] screenUvs, HashSet<int> triangleIndices, Color fillColor)
        {
            if (texture == null)
            {
                return;
            }

            var pixels = new Color32[texture.width * texture.height];
            if (triangleIndices != null && triangleIndices.Count > 0 && fillColor.a > 0.001f)
            {
                var fill = (Color32)fillColor;
                foreach (var triangleIndex in triangleIndices)
                {
                    RasterizePreviewTriangle(texture.width, texture.height, pixels, meshTriangles, screenUvs, triangleIndex, fill);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }

        private void FillPreviewWireframeTexture(Texture2D texture, int[] meshTriangles, Vector2Int[] wireEdges, Vector2[] screenUvs, Color lineColor)
        {
            if (texture == null)
            {
                return;
            }

            var pixels = new Color32[texture.width * texture.height];
            if (lineColor.a > 0.001f)
            {
                var color = (Color32)lineColor;
                if (wireEdges != null && wireEdges.Length > 0)
                {
                    for (var i = 0; i < wireEdges.Length; i++)
                    {
                        var edge = wireEdges[i];
                        if (edge.x < 0 || edge.y < 0 || edge.x >= screenUvs.Length || edge.y >= screenUvs.Length)
                        {
                            continue;
                        }

                        RasterizePreviewLine(texture.width, texture.height, pixels, screenUvs[edge.x], screenUvs[edge.y], color, 1);
                    }
                }
                else if (meshTriangles != null)
                {
                    for (var i = 0; i + 2 < meshTriangles.Length; i += 3)
                    {
                        var a = meshTriangles[i];
                        var b = meshTriangles[i + 1];
                        var c = meshTriangles[i + 2];
                        if (a < 0 || b < 0 || c < 0 || a >= screenUvs.Length || b >= screenUvs.Length || c >= screenUvs.Length)
                        {
                            continue;
                        }

                        RasterizePreviewLine(texture.width, texture.height, pixels, screenUvs[a], screenUvs[b], color, 1);
                        RasterizePreviewLine(texture.width, texture.height, pixels, screenUvs[b], screenUvs[c], color, 1);
                        RasterizePreviewLine(texture.width, texture.height, pixels, screenUvs[c], screenUvs[a], color, 1);
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }

        private void InvalidatePreviewOverlayCaches()
        {
            cachedPreviewOverlaySize = Vector2Int.zero;
            cachedPreviewOverlayPan = Vector2.zero;
            cachedPreviewOverlayZoom = -1f;
            cachedPreviewSelectionRevision = -1;
            cachedPreviewSelectionColor = new Color(-1f, -1f, -1f, -1f);
            cachedPreviewWireframeColor = new Color(-1f, -1f, -1f, -1f);

            if (previewSelectedOverlayTexture != null)
            {
                DestroyImmediate(previewSelectedOverlayTexture);
                previewSelectedOverlayTexture = null;
            }

            if (previewWireframeOverlayTexture != null)
            {
                DestroyImmediate(previewWireframeOverlayTexture);
                previewWireframeOverlayTexture = null;
            }
        }

        private static void RasterizePreviewTriangle(int width, int height, Color32[] pixels, int[] meshTriangles, Vector2[] screenUvs, int triangleIndex, Color32 fill)
        {
            var baseIndex = triangleIndex * 3;
            if (triangleIndex < 0 || meshTriangles == null || baseIndex + 2 >= meshTriangles.Length)
            {
                return;
            }

            var ia = meshTriangles[baseIndex];
            var ib = meshTriangles[baseIndex + 1];
            var ic = meshTriangles[baseIndex + 2];
            if (ia < 0 || ib < 0 || ic < 0 || ia >= screenUvs.Length || ib >= screenUvs.Length || ic >= screenUvs.Length)
            {
                return;
            }

            var a = screenUvs[ia];
            var b = screenUvs[ib];
            var c = screenUvs[ic];
            var area = SignedArea(a, b, c);
            if (Mathf.Abs(area) <= 0.000001f)
            {
                return;
            }

            var minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, width - 1);
            var maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, width - 1);
            var minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, height - 1);
            var maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, height - 1);

            for (var y = minY; y <= maxY; y++)
            {
                var row = y * width;
                var py = y + 0.5f;
                for (var x = minX; x <= maxX; x++)
                {
                    var px = x + 0.5f;
                    if (PointInPreviewTriangle(px, py, a, b, c, area))
                    {
                        pixels[row + x] = fill;
                    }
                }
            }
        }

        private static void RasterizePreviewLine(int width, int height, Color32[] pixels, Vector2 from, Vector2 to, Color32 color, int thickness)
        {
            var dx = to.x - from.x;
            var dy = to.y - from.y;
            var steps = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            var iterations = Mathf.Max(1, Mathf.CeilToInt(steps));
            for (var i = 0; i <= iterations; i++)
            {
                var t = iterations == 0 ? 0f : i / (float)iterations;
                var x = Mathf.RoundToInt(Mathf.Lerp(from.x, to.x, t));
                var y = Mathf.RoundToInt(Mathf.Lerp(from.y, to.y, t));
                for (var oy = -thickness; oy <= thickness; oy++)
                {
                    var py = y + oy;
                    if (py < 0 || py >= height)
                    {
                        continue;
                    }

                    var row = py * width;
                    for (var ox = -thickness; ox <= thickness; ox++)
                    {
                        var px = x + ox;
                        if (px < 0 || px >= width)
                        {
                            continue;
                        }

                        pixels[row + px] = color;
                    }
                }
            }
        }

        private static bool PointInPreviewTriangle(float px, float py, Vector2 a, Vector2 b, Vector2 c, float area)
        {
            var point = new Vector2(px, py);
            var w0 = SignedArea(b, c, point) / area;
            var w1 = SignedArea(c, a, point) / area;
            var w2 = 1f - w0 - w1;
            const float epsilon = -0.00001f;
            return w0 >= epsilon && w1 >= epsilon && w2 >= epsilon;
        }

        private static float SignedArea(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }

        private bool ApplyBrushStroke(Vector2 localMouse, UVPreviewRenderer.ViewTransform transform)
        {
            var uvCenter = transform.ScreenToUv(localMouse);
            var uvRadius = UVSelectionController.GetBrushUvRadius(transform, brushRadiusPixels);
            var candidateTriangles = CollectBrushCandidateTriangles(uvCenter, uvRadius);
            return UVSelectionController.ApplyBrush(
                selectedTriangleIndices,
                uvCenter,
                uvRadius,
                currentTriangles,
                currentUvs,
                currentTriangleBounds,
                candidateTriangles,
                isBrushErasing);
        }

        private bool ApplyBrushStrokeSegment(Vector2 from, Vector2 to, UVPreviewRenderer.ViewTransform transform)
        {
            var delta = to - from;
            var distance = delta.magnitude;
            var spacing = GetBrushSampleSpacing();
            if (distance <= spacing)
            {
                return false;
            }

            var changed = false;
            var stepCount = Mathf.Max(1, Mathf.CeilToInt(distance / spacing));
            for (var step = 1; step <= stepCount; step++)
            {
                var t = step / (float)stepCount;
                changed |= ApplyBrushStroke(Vector2.LerpUnclamped(from, to, t), transform);
            }

            return changed;
        }

        private float GetBrushSampleSpacing()
        {
            return Mathf.Max(brushRadiusPixels * 0.35f, 3f);
        }

        private Vector2[] GetPreviewScreenUvs(UVPreviewRenderer.ViewTransform transform, Rect localRect)
        {
            if (currentUvArray == null || currentUvArray.Length == 0)
            {
                return Array.Empty<Vector2>();
            }

            var currentSize = localRect.size;
            if (cachedPreviewScreenUvs.Length == currentUvArray.Length
                && Mathf.Abs(cachedPreviewScreenUvZoom - previewZoom) <= 0.0001f
                && cachedPreviewScreenUvPan == previewPan
                && cachedPreviewScreenUvSize == currentSize)
            {
                return cachedPreviewScreenUvs;
            }

            cachedPreviewScreenUvs = new Vector2[currentUvArray.Length];
            for (var i = 0; i < currentUvArray.Length; i++)
            {
                cachedPreviewScreenUvs[i] = transform.UvToScreen(currentUvArray[i]);
            }

            cachedPreviewScreenUvZoom = previewZoom;
            cachedPreviewScreenUvPan = previewPan;
            cachedPreviewScreenUvSize = currentSize;
            return cachedPreviewScreenUvs;
        }

        private void RebuildBrushCandidateGrid()
        {
            currentBrushTriangleBins = Array.Empty<List<int>>();
            brushCandidateTriangleIndices.Clear();

            if (currentTriangleBounds == null || currentTriangleBounds.Length == 0)
            {
                currentBrushTriangleVisitMarks = Array.Empty<int>();
                return;
            }

            var hasBounds = false;
            var minX = 0f;
            var minY = 0f;
            var maxX = 0f;
            var maxY = 0f;
            for (var triangleIndex = 0; triangleIndex < currentTriangleBounds.Length; triangleIndex++)
            {
                if (currentIslandByTriangle[triangleIndex] < 0)
                {
                    continue;
                }

                var bounds = currentTriangleBounds[triangleIndex];
                if (!hasBounds)
                {
                    minX = bounds.xMin;
                    minY = bounds.yMin;
                    maxX = bounds.xMax;
                    maxY = bounds.yMax;
                    hasBounds = true;
                    continue;
                }

                minX = Mathf.Min(minX, bounds.xMin);
                minY = Mathf.Min(minY, bounds.yMin);
                maxX = Mathf.Max(maxX, bounds.xMax);
                maxY = Mathf.Max(maxY, bounds.yMax);
            }

            if (!hasBounds)
            {
                currentBrushTriangleVisitMarks = Array.Empty<int>();
                return;
            }

            currentBrushGridBounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            if (currentBrushGridBounds.width <= 0.000001f)
            {
                currentBrushGridBounds.width = 0.000001f;
            }

            if (currentBrushGridBounds.height <= 0.000001f)
            {
                currentBrushGridBounds.height = 0.000001f;
            }

            currentBrushTriangleBins = new List<int>[BrushGridResolution * BrushGridResolution];
            currentBrushTriangleVisitMarks = new int[currentTriangleBounds.Length];
            var scaleX = BrushGridResolution / currentBrushGridBounds.width;
            var scaleY = BrushGridResolution / currentBrushGridBounds.height;
            for (var triangleIndex = 0; triangleIndex < currentTriangleBounds.Length; triangleIndex++)
            {
                if (currentIslandByTriangle[triangleIndex] < 0)
                {
                    continue;
                }

                var bounds = currentTriangleBounds[triangleIndex];
                var minCellX = Mathf.Clamp((int)((bounds.xMin - currentBrushGridBounds.xMin) * scaleX), 0, BrushGridResolution - 1);
                var maxCellX = Mathf.Clamp((int)((bounds.xMax - currentBrushGridBounds.xMin) * scaleX), 0, BrushGridResolution - 1);
                var minCellY = Mathf.Clamp((int)((bounds.yMin - currentBrushGridBounds.yMin) * scaleY), 0, BrushGridResolution - 1);
                var maxCellY = Mathf.Clamp((int)((bounds.yMax - currentBrushGridBounds.yMin) * scaleY), 0, BrushGridResolution - 1);

                for (var cellY = minCellY; cellY <= maxCellY; cellY++)
                {
                    var rowOffset = cellY * BrushGridResolution;
                    for (var cellX = minCellX; cellX <= maxCellX; cellX++)
                    {
                        var binIndex = rowOffset + cellX;
                        var bin = currentBrushTriangleBins[binIndex];
                        if (bin == null)
                        {
                            bin = new List<int>(8);
                            currentBrushTriangleBins[binIndex] = bin;
                        }

                        bin.Add(triangleIndex);
                    }
                }
            }
        }

        private List<int> CollectBrushCandidateTriangles(Vector2 uvCenter, float uvRadius)
        {
            brushCandidateTriangleIndices.Clear();
            if (currentBrushTriangleBins == null || currentBrushTriangleBins.Length == 0 || currentTriangleBounds.Length == 0)
            {
                brushCandidateTriangleIndices.AddRange(selectableTriangleIndices);
                return brushCandidateTriangleIndices;
            }

            brushTriangleVisitStamp++;
            if (brushTriangleVisitStamp == int.MaxValue)
            {
                System.Array.Clear(currentBrushTriangleVisitMarks, 0, currentBrushTriangleVisitMarks.Length);
                brushTriangleVisitStamp = 1;
            }

            var scaleX = BrushGridResolution / currentBrushGridBounds.width;
            var scaleY = BrushGridResolution / currentBrushGridBounds.height;
            var minCellX = Mathf.Clamp((int)((uvCenter.x - uvRadius - currentBrushGridBounds.xMin) * scaleX), 0, BrushGridResolution - 1);
            var maxCellX = Mathf.Clamp((int)((uvCenter.x + uvRadius - currentBrushGridBounds.xMin) * scaleX), 0, BrushGridResolution - 1);
            var minCellY = Mathf.Clamp((int)((uvCenter.y - uvRadius - currentBrushGridBounds.yMin) * scaleY), 0, BrushGridResolution - 1);
            var maxCellY = Mathf.Clamp((int)((uvCenter.y + uvRadius - currentBrushGridBounds.yMin) * scaleY), 0, BrushGridResolution - 1);

            for (var cellY = minCellY; cellY <= maxCellY; cellY++)
            {
                var rowOffset = cellY * BrushGridResolution;
                for (var cellX = minCellX; cellX <= maxCellX; cellX++)
                {
                    var bin = currentBrushTriangleBins[rowOffset + cellX];
                    if (bin == null)
                    {
                        continue;
                    }

                    for (var i = 0; i < bin.Count; i++)
                    {
                        var triangleIndex = bin[i];
                        if (triangleIndex < 0 || triangleIndex >= currentBrushTriangleVisitMarks.Length)
                        {
                            continue;
                        }

                        if (currentBrushTriangleVisitMarks[triangleIndex] == brushTriangleVisitStamp)
                        {
                            continue;
                        }

                        currentBrushTriangleVisitMarks[triangleIndex] = brushTriangleVisitStamp;
                        brushCandidateTriangleIndices.Add(triangleIndex);
                    }
                }
            }

            return brushCandidateTriangleIndices;
        }

        private void RepaintAfterBrushStroke(bool selectionChanged, bool forceScene)
        {
            var now = EditorApplication.timeSinceStartup;
            if (now >= nextAllowedBrushWindowRepaintTime)
            {
                nextAllowedBrushWindowRepaintTime = now + BrushWindowRepaintIntervalSeconds;
                Repaint();
            }

            if (!selectionChanged || !sceneViewHighlight)
            {
                return;
            }

            pendingBrushSceneRepaint = true;
            if (pauseSceneOverlayWhileBrushing && isBrushPainting && !forceScene)
            {
                return;
            }

            if (forceScene || now >= nextAllowedBrushSceneRepaintTime)
            {
                nextAllowedBrushSceneRepaintTime = now + BrushSceneRepaintIntervalSeconds;
                pendingBrushSceneRepaint = false;
                SceneView.RepaintAll();
            }
        }

        private void FlushPendingBrushSceneRepaint()
        {
            Repaint();
            if (!pendingBrushSceneRepaint)
            {
                return;
            }

            pendingBrushSceneRepaint = false;
            nextAllowedBrushSceneRepaintTime = EditorApplication.timeSinceStartup + BrushSceneRepaintIntervalSeconds;
            SceneView.RepaintAll();
        }

        private void SyncSceneMaterialPreview()
        {
            if (!scenePreviewDirty)
            {
                return;
            }

            scenePreviewDirty = false;
            ClearSceneMaterialPreview();

            if (!sceneViewHighlight
                || targetRenderer == null
                || currentMesh == null
                || currentTriangles == null
                || currentTriangles.Length == 0
                || currentUvs == null
                || selectedTriangleIndices.Count == 0)
            {
                return;
            }

            var preview = BuildScenePreviewTexture();
            if (preview == null)
            {
                return;
            }
            SceneView.RepaintAll();
        }

        private void ClearSceneMaterialPreview(bool releaseTexture = false)
        {
            if (releaseTexture && scenePreviewTexture != null)
            {
                DestroyImmediate(scenePreviewTexture);
                scenePreviewTexture = null;
            }

            if (releaseTexture && sceneOverlayMaterial != null)
            {
                DestroyImmediate(sceneOverlayMaterial);
                sceneOverlayMaterial = null;
            }
        }

        private Texture2D BuildScenePreviewTexture()
        {
            var resolution = GetScenePreviewResolution();
            var overlayColor = previewSelectedColor;
            if (overlayColor.a <= 0.001f)
            {
                overlayColor.a = 0.72f;
            }

            var preview = UVRasterizer.Rasterize(
                currentMesh,
                currentTriangles,
                currentUvs,
                selectedTriangleIndices,
                resolution,
                Color.clear,
                overlayColor,
                1);
            if (scenePreviewTexture != null)
            {
                DestroyImmediate(scenePreviewTexture);
            }

            scenePreviewTexture = preview;
            scenePreviewTexture.name = "UVMaskTool_SceneOverlay";
            scenePreviewTexture.hideFlags = HideFlags.HideAndDontSave;
            return scenePreviewTexture;
        }

        private int GetScenePreviewResolution()
        {
            var sourceTexture = GetSelectedMaterialPreviewTexture();
            if (sourceTexture == null)
            {
                sourceTexture = previewTexture;
            }

            if (sourceTexture == null)
            {
                return 1024;
            }

            var maxDimension = Mathf.Max(sourceTexture.width, sourceTexture.height);
            return Mathf.Clamp(maxDimension, 256, 1024);
        }

        private bool TryDrawSceneOverlay(Mesh sceneMesh)
        {
            var material = GetSceneOverlayMaterial();
            var overlayMesh = GetSceneOverlayMesh(sceneMesh);
            if (material == null || overlayMesh == null)
            {
                return false;
            }

            material.SetTexture("_MainTex", Texture2D.whiteTexture);
            material.SetTextureScale("_MainTex", Vector2.one);
            material.SetTextureOffset("_MainTex", Vector2.zero);
            material.SetColor("_Color", previewSelectedColor);
            material.SetPass(0);
            Graphics.DrawMeshNow(overlayMesh, GetScenePreviewDrawMatrix(), 0);
            return true;
        }

        private Material GetSceneOverlayMaterial()
        {
            if (sceneOverlayMaterial != null)
            {
                return sceneOverlayMaterial;
            }

            var shader = Shader.Find("Hidden/IyanKim/UVMaskSceneOverlay");
            if (shader == null)
            {
                return null;
            }

            sceneOverlayMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            return sceneOverlayMaterial;
        }

        private Material GetSelectedScenePreviewMaterial()
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

            return materials[selectedSubmeshIndex];
        }

        private string GetSelectedScenePreviewTexturePropertyName(Material material)
        {
            if (material == null)
            {
                return null;
            }

            if (material.HasProperty("_MainTex"))
            {
                return "_MainTex";
            }

            if (material.HasProperty("_BaseMap"))
            {
                return "_BaseMap";
            }

            return null;
        }

        private Mesh GetSceneOverlayMesh(Mesh sceneMesh)
        {
            if (sceneMesh == null || currentTriangles == null || currentTriangles.Length == 0 || selectedTriangleIndices.Count == 0)
            {
                return null;
            }

            if (sceneOverlayMesh == null)
            {
                sceneOverlayMesh = new Mesh
                {
                    name = "UVMaskTool_SceneOverlayMesh",
                    hideFlags = HideFlags.HideAndDontSave
                };
                sceneOverlayMesh.MarkDynamic();
                sceneOverlaySelectionRevision = -1;
            }

            sceneMesh.GetVertices(sceneOverlayVertices);
            sceneOverlayMesh.SetVertices(sceneOverlayVertices);
            if (sceneOverlaySelectionRevision != selectionRevision)
            {
                sceneOverlayTriangleIndexBuffer.Clear();
                AppendSelectedTriangleIndices(sceneOverlayTriangleIndexBuffer);
                sceneOverlayMesh.SetTriangles(sceneOverlayTriangleIndexBuffer, 0, true);
                sceneOverlaySelectionRevision = selectionRevision;
            }
            sceneOverlayMesh.bounds = sceneMesh.bounds;
            return sceneOverlayMesh;
        }

        private void AppendSelectedTriangleIndices(List<int> destination)
        {
            foreach (var triangleIndex in selectedTriangleIndices)
            {
                var baseIndex = triangleIndex * 3;
                if (triangleIndex < 0 || baseIndex + 2 >= currentTriangles.Length)
                {
                    continue;
                }

                destination.Add(currentTriangles[baseIndex]);
                destination.Add(currentTriangles[baseIndex + 1]);
                destination.Add(currentTriangles[baseIndex + 2]);
            }
        }

        private Mesh GetScenePreviewMesh()
        {
            if (targetRenderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                if (scenePreviewMesh == null)
                {
                    scenePreviewMesh = new Mesh
                    {
                        name = "UVMaskTool_ScenePreview",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    scenePreviewMesh.MarkDynamic();
                }

                skinnedMeshRenderer.BakeMesh(scenePreviewMesh);
                return scenePreviewMesh;
            }

            return currentMesh;
        }

        private Matrix4x4 GetScenePreviewDrawMatrix()
        {
            if (targetRenderer is SkinnedMeshRenderer)
            {
                var transform = targetRenderer.transform;
                return Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            }

            return targetRenderer.localToWorldMatrix;
        }

        private void DisposeScenePreviewMesh()
        {
            if (scenePreviewMesh == null)
            {
                if (sceneOverlayMesh == null)
                {
                    return;
                }
            }

            if (scenePreviewMesh != null)
            {
                DestroyImmediate(scenePreviewMesh);
                scenePreviewMesh = null;
            }

            if (sceneOverlayMesh != null)
            {
                DestroyImmediate(sceneOverlayMesh);
                sceneOverlayMesh = null;
                sceneOverlaySelectionRevision = -1;
            }
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
                    case "Face Tool": return "Face 도구";
                    case "Click": return "클릭";
                    case "Brush": return "브러시";
                    case "Brush Size": return "브러시 크기";
                    case "Pause Scene Overlay While Brushing": return "브러시 중 Scene Overlay 일시 중지";
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
                    case "Face Tool": return "Faceツール";
                    case "Click": return "クリック";
                    case "Brush": return "ブラシ";
                    case "Brush Size": return "ブラシサイズ";
                    case "Pause Scene Overlay While Brushing": return "ブラシ中はScene Overlayを停止";
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
                case "Preview Color": return "Preview Color";
                case "Refresh Scene Preview": return "Refresh Scene Preview";
                case "Selection Mode": return "Selection Mode";
                case "Face Tool": return "Face Tool";
                case "Click": return "Click";
                case "Brush": return "Brush";
                case "Brush Size": return "Brush Size";
                case "Pause Scene Overlay While Brushing": return "Pause Scene Overlay While Brushing";
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
