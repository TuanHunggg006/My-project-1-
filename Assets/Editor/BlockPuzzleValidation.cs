#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Run from Tools/Block Puzzle or with -executeMethod BlockPuzzleValidation.RunBatch.
// All board changes are in Play Mode; the saved scene and personal best stay intact.
[InitializeOnLoad]
public static class BlockPuzzleValidation
{
    private const string RunningKey = "BlockPuzzleValidation.Running";
    private const string ConnectedKey = "BlockPuzzleValidation.Connected";
    private const string BackgroundKey = "BlockPuzzleValidation.Background";
    private const string TestBestKey = "BlockPuzzle.ValidationOnly.BestScore";
    private static int stage;
    private static double nextStep;
    private static double readinessDeadline;
    private static GridDropBoard board;
    private static PolyominoShapeGenerator generator;
    private static DraggableGeneratedShape testShape;
    private static Vector2 pointer;
    private static int assertions;
    private static bool visualCaptured;
    private static bool runtimeError;
    private static readonly List<string> report = new List<string>();

    static BlockPuzzleValidation()
    {
        if (SessionState.GetBool(RunningKey, false))
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += ObserveLog;
            nextStep = EditorApplication.timeSinceStartup + 1.5;
            readinessDeadline = EditorApplication.timeSinceStartup + 30;
        }
    }

    [MenuItem("Tools/Block Puzzle/Validate shapes and scoring")]
    public static void ValidateRules()
    {
        assertions = 0;
        GameObject owner = new GameObject("CatalogValidation");
        owner.SetActive(false);
        try
        {
            PolyominoShapeGenerator catalog = owner.AddComponent<PolyominoShapeGenerator>();
            int[] counts = { 0, 1, 2, 6, 19, 2, 2 };
            for (int size = 1; size <= 6; size++)
            {
                List<HashSet<Vector2Int>> shapes = GetCatalog(catalog, size);
                Require(shapes.Count == counts[size], $"Catalog size {size}: orientation count");
                HashSet<string> unique = new HashSet<string>();
                foreach (HashSet<Vector2Int> shape in shapes)
                {
                    Require(shape.Count == size, "Cell count");
                    int maxX = 0, maxY = 0, minX = int.MaxValue, minY = int.MaxValue;
                    foreach (Vector2Int cell in shape)
                    {
                        maxX = Math.Max(maxX, cell.x); maxY = Math.Max(maxY, cell.y);
                        minX = Math.Min(minX, cell.x); minY = Math.Min(minY, cell.y);
                    }
                    Require(minX == 0 && minY == 0, "Normalized coordinates");
                    if (size == 5)
                        Require(maxX == 0 || maxY == 0, "No five-cell L/P or irregular shapes");
                    if (size == 6)
                        Require((maxX + 1) * (maxY + 1) == 6 && maxX > 0 && maxY > 0,
                            "Six cells form a full 2x3 rectangle");
                    Require(Connected(shape), "Connected shape");
                    string key = (string)Invoke(catalog, "BuildKey", shape);
                    Require(unique.Add(key), "No duplicate orientation");
                }
            }
            for (int rows = 0; rows <= 8; rows++)
                for (int columns = 0; columns <= 8; columns++)
                {
                    int lines = rows + columns;
                    int multiplier = lines == 0 ? 0 : 1 + lines / 2;
                    Require(GridDropBoard.CalculateLineMultiplier(rows, columns) == multiplier, "Multiplier");
                    Require(GridDropBoard.CalculateLineScore(rows, columns) == lines * 100 * multiplier, "Score");
                }
            Require(GridDropBoard.CalculateLineScore(1, 0) == 100, "One row = 100");
            Require(GridDropBoard.CalculateLineScore(1, 1) == 400, "Cross = 400");
            Require(GridDropBoard.CalculateLineScore(2, 0) == 400, "Two parallel lines = 400");
            Require(GridDropBoard.CalculateLineScore(2, 2) == 1200, "Two rows + two columns = 1200");
            Debug.Log($"BLOCK_PUZZLE_RULES_PASS: {assertions} assertions, 32 approved orientations.");
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    public static void RunBatch()
    {
        ValidateRules();
        SessionState.SetBool(RunningKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        GridDropBoard editBoard = UnityEngine.Object.FindObjectOfType<GridDropBoard>();
        Require(editBoard != null, "Board exists in Main scene");
        Set(editBoard, "createInitialBlocks", false);
        Set(editBoard, "bestScoreKey", TestBestKey);
        PlayerPrefs.DeleteKey(TestBestKey);
        SetPortraitGameView();
        EditorApplication.EnterPlaymode();
    }

    // Tests the current scene without saving it or closing the user's Editor.
    public static void RunConnected()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Start validation from Edit Mode.");
        SessionState.SetBool(ConnectedKey, true);
        SessionState.SetBool(BackgroundKey, Application.runInBackground);
        SessionState.SetBool(RunningKey, true);
        EditorApplication.EnterPlaymode();
    }

    private static void SetPortraitGameView()
    {
        Assembly editorAssembly = typeof(Editor).Assembly;
        Type sizesType = editorAssembly.GetType("UnityEditor.GameViewSizes");
        Type singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        object sizes = singleton.GetProperty("instance").GetValue(null);
        object groupType = Enum.Parse(editorAssembly.GetType("UnityEditor.GameViewSizeGroupType"), "Standalone");
        object group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { groupType });
        Type viewSizeType = editorAssembly.GetType("UnityEditor.GameViewSize");
        object fixedResolution = Enum.Parse(editorAssembly.GetType("UnityEditor.GameViewSizeType"), "FixedResolution");
        object viewSize = Activator.CreateInstance(viewSizeType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new[] { fixedResolution, (object)540, 1170, "Block Puzzle Validation" }, null);
        group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { viewSize });
        int index = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null) - 1;
        Type gameViewType = editorAssembly.GetType("UnityEditor.GameView");
        EditorWindow gameView = EditorWindow.GetWindow(gameViewType);
        gameViewType.GetProperty("selectedSizeIndex").SetValue(gameView, index);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < nextStep)
            return;
        try
        {
            switch (stage)
            {
                case 0:
                    board = UnityEngine.Object.FindObjectOfType<GridDropBoard>();
                    generator = UnityEngine.Object.FindObjectOfType<PolyominoShapeGenerator>();
                    Require(board != null && generator != null, "Runtime scene references");
                    if (Get<RectTransform[]>(board, "gridCells") == null)
                    {
                        Require(EditorApplication.timeSinceStartup < readinessDeadline, "Runtime Start completes before timeout");
                        nextStep = EditorApplication.timeSinceStartup + 0.2;
                        return;
                    }
                    if (SessionState.GetBool(ConnectedKey, false))
                    {
                        Application.runInBackground = true;
                        Set(board, "bestScoreKey", TestBestKey);
                        Set(board, "<BestScore>k__BackingField", 0);
                        PlayerPrefs.DeleteKey(TestBestKey);
                        RectTransform[] placed = Get<RectTransform[]>(board, "placedBlocks");
                        for (int i = 0; i < placed.Length; i++)
                        {
                            if (placed[i] != null) UnityEngine.Object.Destroy(placed[i].gameObject);
                            placed[i] = null;
                        }
                        Array.Clear(Get<bool[]>(board, "occupiedCells"), 0, placed.Length);
                        Get<BlockPuzzleScoreDisplay>(board, "scoreDisplay").Initialize(
                            Get<RectTransform>(board, "gridContainer"), (Sprite)Invoke(board, "GetGlowSprite"), 0, 0);
                    }
                    Require(board.Score == 0, "Initial score is zero");
                    Require(Get<RectTransform[]>(board, "gridCells").Length == 64, "64-cell grid");
                    Require(UnityEngine.Object.FindObjectOfType<BlockPuzzleScoreDisplay>() != null, "HUD automatically created");
                    ValidatePlacement();
                    Capture("initial.png");
                    PrepareLines(new[] { 0 }, new[] { 0 }, new HashSet<int> { 0 });
                    testShape = CreateShape(new HashSet<Vector2Int> { Vector2Int.zero });
                    pointer = CellScreen(0);
                    testShape.BeginDrag(Event(pointer));
                    testShape.Drag(Event(pointer));
                    break;
                case 1:
                    Require(Get<List<Image>>(board, "previewImages").Count == 1, "Ghost preview appears");
                    Require(Get<List<Image>>(board, "previewImages")[0].gameObject.activeSelf, "Ghost visible");
                    Require(ActiveLinePreviews() == 15, "Hover highlights cross: 15 unique cells");
                    Require(board.Score == 0 && board.GetFilledRatio() == 14f / 64f,
                        "Hover does not score or change occupancy");
                    board.HidePlacementPreview(testShape);
                    Require(ActiveLinePreviews() == 0, "Invalid/outside hover removes all glow");
                    board.ShowPlacementPreview(testShape, new List<int> { 18 });
                    Require(ActiveLinePreviews() == 0, "Valid non-completing hover has no line glow");
                    board.ShowPlacementPreview(testShape, new List<int> { 0 });
                    Require(ActiveLinePreviews() == 15, "Hover returns to completing cross");
                    Capture("drag-preview.png");
                    testShape.EndDrag(Event(pointer));
                    Require(Get<bool[]>(board, "occupiedCells")[0], "Placement reserves occupied cell immediately");
                    Require(board.IsClearingLines, "Placement animation locks the board");
                    break;
                case 2:
                    Require(board.Score == 400, "One horizontal + one vertical scores once");
                    Require(Get<bool[]>(board, "occupiedCells")[0] == false, "Intersection cleared");
                    BlockClearSparks sparks = UnityEngine.Object.FindObjectOfType<BlockClearSparks>();
                    Require(sparks != null && sparks.GetComponent<CanvasRenderer>() != null,
                        "Fragment mesh has a CanvasRenderer");
                    sparks.SetProgress(0.2f);
                    Canvas.ForceUpdateCanvases();
                    Mesh sparkMesh = sparks.canvasRenderer.GetMesh();
                    Require(sparkMesh.vertexCount == 15 * 3 * 4, "Cross renders 45 square fragments");
                    UnityEngine.Object.Destroy(sparkMesh);
                    if (!visualCaptured)
                    {
                        Capture("cross-glow.png");
                        board.StartCoroutine(CaptureDisappearance());
                        visualCaptured = true;
                    }
                    break;
                case 3:
                    Require(!board.IsClearingLines, "Clear lock releases");
                    Require(board.GetFilledRatio() == 0f, "Cross leaves an empty board");
                    Require(ActiveLinePreviews() == 0 && Get<List<RectTransform>>(board, "glowVisuals").Count == 0,
                        "Committed effects and preview completely cleaned up");
                    PrepareLines(new[] { 0, 1 }, new[] { 0, 1 }, new HashSet<int> { 0, 1, 8, 9 });
                    testShape = CreateShape(new HashSet<Vector2Int> {
                        new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(0,1), new Vector2Int(1,1) });
                    Require(board.TryBeginDrag(testShape), "Start four-line preview");
                    board.ShowPlacementPreview(testShape, new List<int> { 0, 1, 8, 9 });
                    Require(ActiveLinePreviews() == 28, "Four-line hover deduplicates intersections");
                    Capture("four-line-preview.png");
                    board.EndDragPreview(testShape);
                    Require(ActiveLinePreviews() == 0, "Cancel removes four-line glow");
                    Require(board.TryPlaceShape(testShape, Vector2Int.zero, CellScreen(0), null), "Place 2x2 at intersection");
                    break;
                case 4:
                    Require(board.Score == 1600, "Four-line combo adds exactly 1200");
                    Require(Get<List<RectTransform>>(board, "clearingBlocks").Count == 28,
                        "Two rows + two columns clear 28 unique cells, intersections once");
                    Capture("four-line-combo.png");
                    break;
                case 5:
                    Require(!board.IsClearingLines && board.GetFilledRatio() == 0f, "Four-line cleanup");
                    Require(board.BestScore == 1600 && PlayerPrefs.GetInt(TestBestKey) == 1600, "Best score saved");
                    // Invalid release returns smoothly and remains available in the tray.
                    testShape = CreateShape(new HashSet<Vector2Int> { Vector2Int.zero });
                    Transform originalParent = testShape.transform.parent;
                    Set(testShape, "originalParent", originalParent);
                    testShape.BeginDrag(Event(CellScreen(0)));
                    testShape.EndDrag(Event(new Vector2(-500f, -500f)));
                    Require(testShape.IsInteracting, "Invalid release starts return animation");
                    break;
                case 6:
                    Require(!testShape.IsInteracting, "Return animation ends");
                    Require(testShape.transform.parent == Get<Transform>(testShape, "originalParent"), "Returned to original tray parent");
                    Require(testShape.GetComponent<CanvasGroup>().blocksRaycasts, "Returned piece accepts input");
                    Capture("score-final.png");
                    Require(!runtimeError, "No runtime errors");
                    break;
                case 7:
                    report.Add($"PASS: {assertions} runtime assertions.");
                    report.Add("PASS: catalog, scoring, empty/outside/occupied/edge placements, ghost preview, real drag/release,");
                    report.Add("cross clear, four-line combo, intersection deduplication, animation lock, invalid return and best score.");
                    File.WriteAllLines("Development/Validation/" + OutputPrefix + "results.txt", report);
                    Debug.Log("BLOCK_PUZZLE_RUNTIME_PASS: " + assertions);
                    Finish(0);
                    return;
            }
            stage++;
            nextStep = EditorApplication.timeSinceStartup +
                (stage == 2 || stage == 4 ? 0.2 : stage == 3 || stage == 5 ? 1.3 : 0.35);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            File.WriteAllText("Development/Validation/" + OutputPrefix + "results.txt", "FAIL at stage " + stage + "\n" + exception);
            Finish(1);
        }
    }

    private static void ValidatePlacement()
    {
        List<int> targets = new List<int>();
        RectTransform[] grid = Get<RectTransform[]>(board, "gridCells");
        bool[] occupancy = Get<bool[]>(board, "occupiedCells");
        for (int size = 1; size <= 6; size++)
            foreach (HashSet<Vector2Int> cells in GetCatalog(generator, size))
            {
                DraggableGeneratedShape shape = CreateShape(cells);
                foreach (Vector2Int grabbed in cells)
                    for (int anchor = 0; anchor < 64; anchor++)
                    {
                        bool expected = true;
                        foreach (Vector2Int cell in cells)
                        {
                            int x = anchor % 8 + cell.x - grabbed.x;
                            int y = anchor / 8 + cell.y - grabbed.y;
                            if (x < 0 || x >= 8 || y < 0 || y >= 8) expected = false;
                        }
                        bool fits = board.TryGetPlacement(shape, grabbed, CellScreen(anchor), null, targets, out _);
                        Require(fits == expected, "All orientations, grabbed cells and board edges");
                        if (fits)
                        {
                            Require(targets.Count == size, "Preview cell count");
                            occupancy[targets[0]] = true;
                            Require(!board.TryGetPlacement(shape, grabbed, CellScreen(anchor), null, targets, out _),
                                "Occupied target rejected");
                            Array.Clear(occupancy, 0, occupancy.Length);
                        }
                    }
                Require(!board.TryGetPlacement(shape, Vector2Int.zero, new Vector2(-500f, -500f), null, targets, out _),
                    "Outside board rejected");
                UnityEngine.Object.Destroy(shape.gameObject);
            }
        Require(!board.CanPlaceShape(new Vector2Int[0]), "Empty shape rejected");
        report.Add("PASS: exhaustive placement validation for all 32 catalog orientations and every grabbed cell/anchor.");
    }

    private static DraggableGeneratedShape CreateShape(HashSet<Vector2Int> cells)
    {
        GameObject owner = new GameObject("ValidationShape", typeof(RectTransform));
        owner.transform.SetParent(Get<RectTransform>(generator, "previewRoot"), false);
        RectTransform shapeRect = owner.GetComponent<RectTransform>();
        shapeRect.pivot = new Vector2(0f, 1f);
        DraggableGeneratedShape shape = owner.AddComponent<DraggableGeneratedShape>();
        shape.Initialize(board, null);
        Vector2 size = Get<RectTransform[]>(board, "gridCells")[0].rect.size;
        foreach (Vector2Int cell in cells)
        {
            GameObject visual = UnityEngine.Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BlockBlue.prefab"), owner.transform, false);
            RectTransform rect = visual.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(cell.x * size.x, -cell.y * size.y);
            shape.RegisterCell(rect, cell);
        }
        shapeRect.localScale = Vector3.one * 0.55f;
        return shape;
    }

    private static void PrepareLines(int[] fullRows, int[] fullColumns, HashSet<int> holes)
    {
        bool[] occupancy = Get<bool[]>(board, "occupiedCells");
        RectTransform[] placed = Get<RectTransform[]>(board, "placedBlocks");
        RectTransform[] grid = Get<RectTransform[]>(board, "gridCells");
        for (int index = 0; index < 64; index++)
        {
            bool fill = (Array.IndexOf(fullRows, index / 8) >= 0 || Array.IndexOf(fullColumns, index % 8) >= 0)
                && !holes.Contains(index);
            occupancy[index] = fill;
            if (!fill) continue;
            GameObject visual = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                index % 2 == 0 ? "Assets/Prefabs/BlockBlue.prefab" : "Assets/Prefabs/BlockPurple.prefab"));
            placed[index] = visual.GetComponent<RectTransform>();
            Invoke(board, "PlaceCellVisual", placed[index], grid[index], index);
        }
    }

    private static Vector2 CellScreen(int index)
    {
        RectTransform rect = Get<RectTransform[]>(board, "gridCells")[index];
        return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
    }

    private static PointerEventData Event(Vector2 position)
    {
        return new PointerEventData(EventSystem.current) { pointerId = -1, position = position };
    }

    private static void Capture(string name)
    {
        Canvas.ForceUpdateCanvases();
        ScreenCapture.CaptureScreenshot("Development/Validation/" + OutputPrefix + name);
    }

    private static string OutputPrefix => SessionState.GetBool(ConnectedKey, false) ? "effects-" : "";

    private static IEnumerator CaptureDisappearance()
    {
        for (int i = 0; i < 8; i++)
        {
            Capture("disappear-" + i + ".png");
            yield return new WaitForSecondsRealtime(0.04f);
        }
    }

    private static int ActiveLinePreviews()
    {
        int count = 0;
        foreach (Image image in Get<List<Image>>(board, "linePreviewImages"))
            if (image.gameObject.activeSelf) count++;
        return count;
    }

    private static List<HashSet<Vector2Int>> GetCatalog(PolyominoShapeGenerator catalog, int size)
    {
        return (List<HashSet<Vector2Int>>)Invoke(catalog, "BuildAllShapes", size);
    }

    private static bool Connected(HashSet<Vector2Int> cells)
    {
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        foreach (Vector2Int cell in cells) { queue.Enqueue(cell); visited.Add(cell); break; }
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            foreach (Vector2Int direction in directions)
            {
                Vector2Int next = current + direction;
                if (cells.Contains(next) && visited.Add(next)) queue.Enqueue(next);
            }
        }
        return visited.Count == cells.Count;
    }

    private static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception("Validation failed: " + message);
    }

    private static T Get<T>(object owner, string name)
    {
        return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    }

    private static void Set(object owner, string name, object value)
    {
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    }

    private static object Invoke(object owner, string name, params object[] arguments)
    {
        return owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, arguments);
    }

    private static void ObserveLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            runtimeError = true;
    }

    private static void Finish(int code)
    {
        SessionState.SetBool(RunningKey, false);
        PlayerPrefs.DeleteKey(TestBestKey);
        PlayerPrefs.Save();
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= ObserveLog;
        if (SessionState.GetBool(ConnectedKey, false))
        {
            SessionState.SetBool(ConnectedKey, false);
            Application.runInBackground = SessionState.GetBool(BackgroundKey, false);
            EditorApplication.ExitPlaymode();
        }
        else EditorApplication.Exit(code);
    }
}
#endif
