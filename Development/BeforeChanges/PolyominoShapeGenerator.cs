using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PolyominoShapeGenerator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BlockPrefabPalette blockPalette;
    [SerializeField] private RectTransform previewRoot;
    [SerializeField] private GridDropBoard dropBoard;
    [SerializeField] private RectTransform gridContainer;
    [SerializeField] private Button regenerateButton;
    [SerializeField] private TMP_Text resultText;

    [Header("Suggestion batch")]
    [Min(1)]
    [SerializeField] private int suggestionsPerBatch = 3;
    [Range(1, 6)]
    [SerializeField] private int maximumShapeSize = 5;
    [SerializeField] private bool generateOnStart = true;

    [Header("Small preview")]
    [Range(0.1f, 1f)]
    [SerializeField] private float previewScale = 0.55f;
    [SerializeField] private float cellGap = 0f;
    [SerializeField] private bool fitSuggestionsToViewport = true;
    [SerializeField] private float previewPadding = 10f;
    [SerializeField]
    private Vector2 previewSlotSize =
        new Vector2(220f, 220f);
    [Tooltip("Dùng khi chưa đọc được kích thước ô trên Grid.")]
    [SerializeField] private float fallbackCellSize = 45f;

    private readonly List<GameObject> spawnedPreviews =
        new List<GameObject>();

    private readonly Dictionary<int, List<HashSet<Vector2Int>>>
        shapeCache =
            new Dictionary<int, List<HashSet<Vector2Int>>>();

    private int remainingSuggestions;
    private float boardCellWidth;
    private float boardCellHeight;
    private Coroutine nextBatchCoroutine;

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.right,
        Vector2Int.left,
        Vector2Int.up,
        Vector2Int.down
    };

    private void OnEnable()
    {
        if (regenerateButton != null)
        {
            regenerateButton.onClick.AddListener(
                GenerateSuggestionBatch
            );
        }
    }

    private void OnDisable()
    {
        if (regenerateButton != null)
        {
            regenerateButton.onClick.RemoveListener(
                GenerateSuggestionBatch
            );
        }
    }

    private IEnumerator Start()
    {
        yield return null;

        if (dropBoard != null)
            dropBoard.InitializeBoard();

        UpdateBoardCellSize();

        if (generateOnStart)
            GenerateSuggestionBatch();
    }

    [ContextMenu("Generate 3 Suggestions")]
    public void GenerateSuggestionBatch()
    {
        if (!ValidateReferences())
            return;

        dropBoard.InitializeBoard();
        UpdateBoardCellSize();
        ClearOldPreviews();

        remainingSuggestions = 0;

        for (int i = 0; i < suggestionsPerBatch; i++)
        {
            HashSet<Vector2Int> shape =
                PickPlaceableShape();

            if (shape == null)
                break;

            if (CreateShapePreview(shape, i))
                remainingSuggestions++;
        }

        ResizePreviewRoot();

        if (remainingSuggestions == 0)
        {
            SetResultText("Không còn shape nào đặt được.");
            Debug.Log("Game Over: bảng không còn chỗ trống.");
            return;
        }

        SetResultText(
            $"Lượt mới: {remainingSuggestions} shape."
        );
    }

    public void NotifySuggestionPlaced()
    {
        remainingSuggestions = Mathf.Max(
            0,
            remainingSuggestions - 1
        );

        SetResultText(
            $"Còn {remainingSuggestions} shape trong lượt."
        );

        if (remainingSuggestions > 0 ||
            nextBatchCoroutine != null)
        {
            return;
        }

        nextBatchCoroutine = StartCoroutine(
            GenerateNextBatchAfterFrame()
        );
    }

    private IEnumerator GenerateNextBatchAfterFrame()
    {
        yield return null;

        while (dropBoard != null &&
               dropBoard.IsClearingLines)
        {
            yield return null;
        }

        nextBatchCoroutine = null;
        GenerateSuggestionBatch();
    }

    private HashSet<Vector2Int> PickPlaceableShape()
    {
        float filledRatio = dropBoard.GetFilledRatio();
        int adaptiveMaximum = maximumShapeSize;

        if (filledRatio >= 0.75f)
            adaptiveMaximum = Mathf.Min(adaptiveMaximum, 2);
        else if (filledRatio >= 0.55f)
            adaptiveMaximum = Mathf.Min(adaptiveMaximum, 3);
        else if (filledRatio >= 0.30f)
            adaptiveMaximum = Mathf.Min(adaptiveMaximum, 4);

        adaptiveMaximum = Mathf.Clamp(adaptiveMaximum, 1, 6);
        List<int> sizesToTry = new List<int>();

        for (int size = 1; size <= adaptiveMaximum; size++)
            sizesToTry.Add(size);

        Shuffle(sizesToTry);

        foreach (int size in sizesToTry)
        {
            List<HashSet<Vector2Int>> shapes =
                GetShapesOfSize(size);

            List<int> indices = new List<int>();

            for (int i = 0; i < shapes.Count; i++)
                indices.Add(i);

            Shuffle(indices);

            foreach (int index in indices)
            {
                HashSet<Vector2Int> candidate = shapes[index];

                if (dropBoard.CanPlaceShape(candidate))
                {
                    return new HashSet<Vector2Int>(candidate);
                }
            }
        }

        return null;
    }

    private List<HashSet<Vector2Int>> GetShapesOfSize(
        int cellCount)
    {
        if (!shapeCache.TryGetValue(
                cellCount,
                out List<HashSet<Vector2Int>> shapes))
        {
            shapes = BuildAllShapes(cellCount);
            shapeCache.Add(cellCount, shapes);
        }

        return shapes;
    }

    private List<HashSet<Vector2Int>> BuildAllShapes(
        int cellCount)
    {
        List<HashSet<Vector2Int>> currentShapes =
            new List<HashSet<Vector2Int>>
            {
                new HashSet<Vector2Int>
                {
                    Vector2Int.zero
                }
            };

        for (int currentSize = 2;
             currentSize <= cellCount;
             currentSize++)
        {
            Dictionary<string, HashSet<Vector2Int>> uniqueShapes =
                new Dictionary<string, HashSet<Vector2Int>>();

            foreach (HashSet<Vector2Int> shape in currentShapes)
            {
                foreach (Vector2Int cell in shape)
                {
                    foreach (Vector2Int direction in Directions)
                    {
                        Vector2Int newCell = cell + direction;

                        if (shape.Contains(newCell))
                            continue;

                        HashSet<Vector2Int> candidate =
                            new HashSet<Vector2Int>(shape)
                            {
                                newCell
                            };

                        candidate = Normalize(candidate);
                        string key = BuildKey(candidate);

                        if (!uniqueShapes.ContainsKey(key))
                            uniqueShapes.Add(key, candidate);
                    }
                }
            }

            currentShapes =
                new List<HashSet<Vector2Int>>(
                    uniqueShapes.Values
                );
        }

        return currentShapes;
    }

    private bool CreateShapePreview(
        HashSet<Vector2Int> shape,
        int shapeIndex)
    {
        GameObject selectedPrefab =
            blockPalette.GetRandomPrefab();

        if (selectedPrefab == null)
            return false;

        GameObject previewObject = new GameObject(
            $"Suggestion_{shapeIndex + 1}",
            typeof(RectTransform)
        );

        RectTransform previewRect =
            previewObject.GetComponent<RectTransform>();

        previewRect.SetParent(previewRoot, false);
        previewRect.anchorMin = new Vector2(0f, 1f);
        previewRect.anchorMax = new Vector2(0f, 1f);
        previewRect.pivot = new Vector2(0f, 1f);
        DraggableGeneratedShape draggableShape =
            previewObject.AddComponent<DraggableGeneratedShape>();

        draggableShape.Initialize(dropBoard, this);

        int maxX = 0;
        int maxY = 0;

        foreach (Vector2Int cell in shape)
        {
            maxX = Mathf.Max(maxX, cell.x);
            maxY = Mathf.Max(maxY, cell.y);
        }

        float shapeWidth =
            (maxX + 1) * boardCellWidth + maxX * cellGap;

        float shapeHeight =
            (maxY + 1) * boardCellHeight + maxY * cellGap;

        previewRect.sizeDelta =
            new Vector2(shapeWidth, shapeHeight);

        Vector2 slotSize = GetEffectiveSlotSize();

        float widthScale =
            (slotSize.x - previewPadding * 2f) / shapeWidth;

        float heightScale =
            (slotSize.y - previewPadding * 2f) / shapeHeight;

        float shapePreviewScale = Mathf.Max(
            0.05f,
            Mathf.Min(previewScale, widthScale, heightScale)
        );

        previewRect.localScale =
            Vector3.one * shapePreviewScale;

        float displayedWidth = shapeWidth * shapePreviewScale;
        float displayedHeight = shapeHeight * shapePreviewScale;

        float x =
            shapeIndex * slotSize.x +
            (slotSize.x - displayedWidth) / 2f;

        float y =
            (slotSize.y - displayedHeight) / 2f;

        previewRect.anchoredPosition = new Vector2(x, -y);

        foreach (Vector2Int cell in shape)
        {
            GameObject square = Instantiate(
                selectedPrefab,
                previewRect,
                false
            );

            square.name = $"Cell_{cell.x}_{cell.y}";

            RectTransform squareRect =
                square.GetComponent<RectTransform>();

            if (squareRect == null)
            {
                Debug.LogError(
                    "Prefab màu phải là UI và có RectTransform."
                );
                Destroy(square);
                continue;
            }

            squareRect.anchorMin = new Vector2(0f, 1f);
            squareRect.anchorMax = new Vector2(0f, 1f);
            squareRect.pivot = new Vector2(0f, 1f);
            squareRect.localScale = Vector3.one;
            squareRect.sizeDelta = new Vector2(
                boardCellWidth,
                boardCellHeight
            );

            squareRect.anchoredPosition = new Vector2(
                cell.x * (boardCellWidth + cellGap),
                -cell.y * (boardCellHeight + cellGap)
            );

            Graphic[] graphics =
                square.GetComponentsInChildren<Graphic>(true);

            foreach (Graphic graphic in graphics)
                graphic.raycastTarget = true;

            GeneratedShapeDragHandle dragHandle =
                square.GetComponent<GeneratedShapeDragHandle>();

            if (dragHandle == null)
            {
                dragHandle = square.AddComponent<
                    GeneratedShapeDragHandle>();
            }

            dragHandle.Initialize(draggableShape);
            draggableShape.RegisterCell(squareRect, cell);
        }

        spawnedPreviews.Add(previewObject);
        return true;
    }

    private void ResizePreviewRoot()
    {
        Vector2 slotSize = GetEffectiveSlotSize();

        previewRoot.sizeDelta = new Vector2(
            suggestionsPerBatch * slotSize.x,
            slotSize.y
        );
    }

    private Vector2 GetEffectiveSlotSize()
    {
        if (!fitSuggestionsToViewport ||
            previewRoot == null ||
            previewRoot.parent == null)
        {
            return previewSlotSize;
        }

        RectTransform viewport =
            previewRoot.parent as RectTransform;

        if (viewport == null ||
            viewport.rect.width <= 0f ||
            viewport.rect.height <= 0f)
        {
            return previewSlotSize;
        }

        return new Vector2(
            viewport.rect.width /
                Mathf.Max(1, suggestionsPerBatch),
            viewport.rect.height
        );
    }

    private void UpdateBoardCellSize()
    {
        boardCellWidth = fallbackCellSize;
        boardCellHeight = fallbackCellSize;

        if (gridContainer == null)
            return;

        Canvas.ForceUpdateCanvases();

        for (int i = 0; i < gridContainer.childCount; i++)
        {
            Transform child = gridContainer.GetChild(i);

            if (!child.name.StartsWith(
                    "Square_",
                    StringComparison.Ordinal))
            {
                continue;
            }

            RectTransform gridSquare = child as RectTransform;

            if (gridSquare == null)
                continue;

            boardCellWidth = gridSquare.rect.width;
            boardCellHeight = gridSquare.rect.height;
            return;
        }
    }

    private bool ValidateReferences()
    {
        if (blockPalette == null ||
            !blockPalette.HasValidPrefab)
        {
            Debug.LogError(
                "Chưa gán Block Prefab Palette hoặc danh sách màu trống."
            );
            return false;
        }

        if (previewRoot == null || dropBoard == null)
        {
            Debug.LogError(
                "Chưa gán Preview Root hoặc Drop Board."
            );
            return false;
        }

        return true;
    }

    private HashSet<Vector2Int> Normalize(
        IEnumerable<Vector2Int> cells)
    {
        int minX = int.MaxValue;
        int minY = int.MaxValue;

        foreach (Vector2Int cell in cells)
        {
            minX = Mathf.Min(minX, cell.x);
            minY = Mathf.Min(minY, cell.y);
        }

        HashSet<Vector2Int> normalized =
            new HashSet<Vector2Int>();

        foreach (Vector2Int cell in cells)
        {
            normalized.Add(new Vector2Int(
                cell.x - minX,
                cell.y - minY
            ));
        }

        return normalized;
    }

    private string BuildKey(IEnumerable<Vector2Int> cells)
    {
        List<Vector2Int> orderedCells =
            new List<Vector2Int>(cells);

        orderedCells.Sort((first, second) =>
        {
            int compareY = first.y.CompareTo(second.y);

            return compareY != 0
                ? compareY
                : first.x.CompareTo(second.x);
        });

        List<string> parts = new List<string>();

        foreach (Vector2Int cell in orderedCells)
            parts.Add($"{cell.x},{cell.y}");

        return string.Join(";", parts);
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int randomIndex = UnityEngine.Random.Range(0, i + 1);
            T temporary = list[i];
            list[i] = list[randomIndex];
            list[randomIndex] = temporary;
        }
    }

    private void ClearOldPreviews()
    {
        foreach (GameObject preview in spawnedPreviews)
        {
            if (preview == null)
                continue;

            if (Application.isPlaying)
                Destroy(preview);
            else
                DestroyImmediate(preview);
        }

        spawnedPreviews.Clear();
    }

    private void SetResultText(string message)
    {
        if (resultText != null)
            resultText.text = message;
    }
}
