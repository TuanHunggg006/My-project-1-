using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GridDropBoard : MonoBehaviour
{
    [Header("Board")]
    [SerializeField] private RectTransform gridContainer;
    [SerializeField] private RectTransform placedBlocksRoot;
    [SerializeField] private int rows = 8;
    [SerializeField] private int columns = 8;

    [Header("Colors")]
    [SerializeField] private BlockPrefabPalette blockPalette;

    [Header("Random blocks at game start")]
    [SerializeField] private bool createInitialBlocks = true;
    [Range(0, 64)]
    [SerializeField] private int minimumInitialBlocks = 2;
    [Range(0, 64)]
    [SerializeField] private int maximumInitialBlocks = 3;

    [Header("Line clear effect")]
    [Range(0.15f, 1f)]
    [SerializeField] private float clearDuration = 0.36f;
    [Range(0f, 0.15f)]
    [SerializeField] private float clearStagger = 0.008f;
#pragma warning disable 0414 // Preserve the old serialized settings for existing scenes/prefabs.
    [HideInInspector]
    [SerializeField] private float clearPopScale = 1.2f;
    [SerializeField, HideInInspector] private float clearRotation = 0f;
#pragma warning restore 0414
    [Range(0f, 1f)]
    [SerializeField] private float clearFlashStrength = 0.4f;

    [Header("Placement")]
    [SerializeField, Min(0.01f)] private float placementDuration = 0.12f;
    [SerializeField, Range(0.1f, 0.8f)] private float previewAlpha = 0.35f;

    [Header("Score")]
    [SerializeField, Min(1)] private int pointsPerLine = 100;
    [SerializeField] private BlockPuzzleScoreDisplay scoreDisplay;
    [SerializeField] private string bestScoreKey = "BlockPuzzle.BestScore";

    [Header("Line glow")]
    [SerializeField] private Color lineGlowColor = new Color(0.6f, 0.85f, 1f, 1f);
    [SerializeField, Min(0.01f)] private float lineFlashDuration = 0.09f;

    private RectTransform[] gridCells;
    private bool[] occupiedCells;
    private RectTransform[] placedBlocks;
    private bool boardInitialized;
    private bool initialBlocksCreated;
    private int activeClearAnimations;
    private int activePlacementAnimations;
    private bool scoreInitialized;
    private readonly List<Image> previewImages = new List<Image>();
    private readonly List<Image> linePreviewImages = new List<Image>();
    private readonly List<Image> linePreviewHalos = new List<Image>();
    private readonly List<int> previewFullRows = new List<int>();
    private readonly List<int> previewFullColumns = new List<int>();
    private readonly HashSet<int> previewLineCells = new HashSet<int>();
    private readonly Dictionary<Sprite, Color> spriteEffectColors = new Dictionary<Sprite, Color>();
    private Sprite placedLineSprite;
    private Color placedLineTint = Color.white;
    private Color placedLineEffectColor;
    private readonly List<int> placementTargets = new List<int>();
    private readonly List<RectTransform> clearingBlocks = new List<RectTransform>();
    private readonly List<RectTransform> glowVisuals = new List<RectTransform>();
    private DraggableGeneratedShape previewOwner;
    private bool resumePlacementResolution;
    private bool ownsScoreDisplay;
    private Sprite glowSprite;
    private Texture2D glowTexture;

    public int Score { get; private set; }
    public int BestScore { get; private set; }

    public bool IsClearingLines => activeClearAnimations > 0 || activePlacementAnimations > 0;

    private IEnumerator Start()
    {
        yield return null;
        InitializeBoard();
        EnsureScoreDisplay();
    }

    public void InitializeBoard()
    {
        RefreshGridCells();

        boardInitialized =
            gridContainer != null &&
            placedBlocksRoot != null &&
            gridCells != null &&
            gridCells.Length == rows * columns &&
            gridCells.Length > 0 &&
            Array.TrueForAll(gridCells, cell => cell != null);

        if (!boardInitialized || initialBlocksCreated)
            return;

        initialBlocksCreated = true;

        if (createInitialBlocks)
            SpawnInitialRandomBlocks();
    }

    public float GetFilledRatio()
    {
        if (!EnsureGridReady())
            return 1f;

        int occupiedCount = 0;

        foreach (bool occupied in occupiedCells)
        {
            if (occupied)
                occupiedCount++;
        }

        return occupiedCount / (float)occupiedCells.Length;
    }

    public bool CanPlaceShape(
        IEnumerable<Vector2Int> shapeCells)
    {
        if (!EnsureGridReady() || shapeCells == null)
            return false;

        List<Vector2Int> cells =
            new List<Vector2Int>(shapeCells);
        if (cells.Count == 0)
            return false;

        for (int anchorRow = 0;
             anchorRow < rows;
             anchorRow++)
        {
            for (int anchorColumn = 0;
                 anchorColumn < columns;
                 anchorColumn++)
            {
                bool fits = true;

                foreach (Vector2Int cell in cells)
                {
                    int targetColumn = anchorColumn + cell.x;
                    int targetRow = anchorRow + cell.y;

                    if (targetColumn < 0 ||
                        targetColumn >= columns ||
                        targetRow < 0 ||
                        targetRow >= rows)
                    {
                        fits = false;
                        break;
                    }

                    int targetIndex =
                        targetRow * columns + targetColumn;

                    if (gridCells[targetIndex] == null || occupiedCells[targetIndex])
                    {
                        fits = false;
                        break;
                    }
                }

                if (fits)
                    return true;
            }
        }

        return false;
    }

    public bool TryPlaceShape(
        DraggableGeneratedShape shape,
        Vector2Int grabbedCell,
        Vector2 pointerScreenPosition,
        Camera eventCamera)
    {
        if (!TryGetPlacement(shape, grabbedCell, pointerScreenPosition,
                eventCamera, placementTargets, out _))
            return false;

        EndDragPreview(shape);
        Image lineSource = shape.Cells[0].RectTransform.GetComponent<Image>();
        placedLineSprite = lineSource != null ? lineSource.sprite : null;
        placedLineTint = lineSource != null ? lineSource.color : Color.white;
        placedLineEffectColor = GetEffectColor(lineSource);
        for (int i = 0; i < shape.Cells.Count; i++)
        {
            int targetIndex = placementTargets[i];
            occupiedCells[targetIndex] = true;
            RectTransform block = shape.Cells[i].RectTransform;
            Vector3 startCenter = block.TransformPoint(block.rect.center);
            Vector3 startScale = block.lossyScale;
            PlaceCellVisual(block, gridCells[targetIndex], targetIndex);
            placedBlocks[targetIndex] = block;
            Vector3 targetPosition = block.localPosition;
            block.position = startCenter;
            Vector3 parentScale = placedBlocksRoot.lossyScale;
            block.localScale = new Vector3(startScale.x / parentScale.x,
                startScale.y / parentScale.y, 1f);
            activePlacementAnimations++;
            StartCoroutine(PlayPlacementEffect(block, targetPosition));
        }

        // Keep the predicted line lit while the piece settles into its cells.
        ShowLinePreview(null, placedLineSprite, placedLineTint, placedLineEffectColor);
        StartCoroutine(ResolvePlacement());
        Destroy(shape.gameObject);
        return true;
    }

    // Preview and release share the same validation, including boundaries and occupancy.
    public bool TryGetPlacement(DraggableGeneratedShape shape, Vector2Int grabbedCell,
        Vector2 pointerScreenPosition, Camera eventCamera, List<int> targetIndices,
        out Vector3 snappedCenter)
    {
        snappedCenter = Vector3.zero;
        targetIndices.Clear();
        if (shape == null || shape.Cells.Count == 0 || IsClearingLines || !EnsureGridReady())
            return false;

        if (!RectTransformUtility.RectangleContainsScreenPoint(
                gridContainer,
                pointerScreenPosition,
                eventCamera))
        {
            return false;
        }

        int anchorIndex = FindNearestGridCell(
            pointerScreenPosition,
            eventCamera
        );

        if (anchorIndex < 0)
            return false;

        int anchorRow = anchorIndex / columns;
        int anchorColumn = anchorIndex % columns;
        snappedCenter = gridCells[anchorIndex].TransformPoint(gridCells[anchorIndex].rect.center);

        foreach (DraggableGeneratedShape.CellData cell
                 in shape.Cells)
        {
            int targetColumn =
                anchorColumn +
                cell.Coordinate.x -
                grabbedCell.x;

            int targetRow =
                anchorRow +
                cell.Coordinate.y -
                grabbedCell.y;

            if (targetColumn < 0 || targetColumn >= columns ||
                targetRow < 0 || targetRow >= rows)
            {
                return false;
            }

            int targetIndex =
                targetRow * columns + targetColumn;

            if (cell.RectTransform == null || gridCells[targetIndex] == null ||
                occupiedCells[targetIndex] || targetIndices.Contains(targetIndex))
            {
                return false;
            }

            targetIndices.Add(targetIndex);
        }

        return true;
    }

    public Vector3 GetDragScale(RectTransform dragParent)
    {
        Vector3 boardScale = gridContainer.lossyScale;
        Vector3 parentScale = dragParent.lossyScale;
        return new Vector3(boardScale.x / parentScale.x, boardScale.y / parentScale.y, 1f);
    }

    public bool TryBeginDrag(DraggableGeneratedShape shape)
    {
        if (IsClearingLines || (previewOwner != null && previewOwner != shape))
            return false;
        previewOwner = shape;
        return true;
    }

    public void ShowPlacementPreview(DraggableGeneratedShape shape, List<int> targets)
    {
        if (previewOwner != shape)
            return;
        while (previewImages.Count < targets.Count)
        {
            GameObject visual = new GameObject("PlacementGhost", typeof(RectTransform), typeof(Image));
            visual.transform.SetParent(placedBlocksRoot, false);
            Image image = visual.GetComponent<Image>();
            image.raycastTarget = false;
            previewImages.Add(image);
        }
        for (int i = 0; i < previewImages.Count; i++)
        {
            Image ghost = previewImages[i];
            ghost.gameObject.SetActive(i < targets.Count);
            if (i >= targets.Count)
                continue;
            Image source = shape.Cells[i].RectTransform.GetComponent<Image>();
            ghost.sprite = source != null ? source.sprite : null;
            Color color = source != null ? source.color : Color.white;
            color.a = previewAlpha;
            ghost.color = color;
            ghost.rectTransform.sizeDelta = gridCells[targets[i]].rect.size;
            ghost.rectTransform.position = gridCells[targets[i]].TransformPoint(gridCells[targets[i]].rect.center);
            ghost.transform.SetAsLastSibling();
        }
        Image lineSource = shape.Cells[0].RectTransform.GetComponent<Image>();
        ShowLinePreview(targets, lineSource != null ? lineSource.sprite : null,
            lineSource != null ? lineSource.color : Color.white, GetEffectColor(lineSource));
    }

    public void HidePlacementPreview(DraggableGeneratedShape shape)
    {
        if (previewOwner != shape)
            return;
        foreach (Image ghost in previewImages)
            if (ghost != null)
                ghost.gameObject.SetActive(false);
        HideLinePreview();
    }

    public void EndDragPreview(DraggableGeneratedShape shape)
    {
        HidePlacementPreview(shape);
        if (previewOwner == shape)
            previewOwner = null;
    }

    private void ShowLinePreview(IList<int> candidate, Sprite sprite, Color tint, Color glowColor)
    {
        CollectCompletedLines(candidate, previewFullRows, previewFullColumns, previewLineCells);
        while (linePreviewImages.Count < previewLineCells.Count)
        {
            Image halo = CreateGlow("PredictedLineHalo", Vector3.zero, Vector2.one, glowColor);
            // Pooled previews have a different lifetime from a committed clear effect.
            glowVisuals.Remove(halo.rectTransform);
            linePreviewHalos.Add(halo);
            GameObject visual = new GameObject("PredictedLineCell", typeof(RectTransform), typeof(Image));
            visual.transform.SetParent(placedBlocksRoot, false);
            Image image = visual.GetComponent<Image>();
            image.raycastTarget = false;
            linePreviewImages.Add(image);
        }
        int slot = 0;
        foreach (int index in previewLineCells)
        {
            RectTransform cell = gridCells[index];
            Vector3 center = cell.TransformPoint(cell.rect.center);
            Image halo = linePreviewHalos[slot];
            Image image = linePreviewImages[slot++];
            halo.gameObject.SetActive(true);
            halo.rectTransform.position = center;
            halo.rectTransform.sizeDelta = cell.rect.size * 1.65f;
            halo.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0.65f);
            halo.transform.SetAsLastSibling();
            image.gameObject.SetActive(true);
            image.sprite = sprite;
            image.color = tint;
            image.rectTransform.position = center;
            image.rectTransform.sizeDelta = cell.rect.size;
            image.transform.SetAsLastSibling();
        }
        for (int i = slot; i < linePreviewImages.Count; i++)
        {
            linePreviewImages[i].gameObject.SetActive(false);
            linePreviewHalos[i].gameObject.SetActive(false);
        }
    }

    private void HideLinePreview()
    {
        previewLineCells.Clear();
        foreach (Image image in linePreviewImages)
            if (image != null) image.gameObject.SetActive(false);
        foreach (Image image in linePreviewHalos)
            if (image != null) image.gameObject.SetActive(false);
    }

    private Color GetEffectColor(Image source)
    {
        if (source == null || source.sprite == null) return lineGlowColor;
        Sprite sprite = source.sprite;
        if (!spriteEffectColors.TryGetValue(sprite, out Color color))
        {
            // Sprites can be packed and non-readable. Sample once per sprite, never per frame.
            RenderTexture previous = RenderTexture.active;
            RenderTexture sample = RenderTexture.GetTemporary(32, 32, 0);
            Texture2D pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
            try
            {
                Graphics.Blit(sprite.texture, sample);
                RenderTexture.active = sample;
                Vector2 center = sprite.textureRect.center;
                int x = Mathf.Clamp(Mathf.FloorToInt(center.x / sprite.texture.width * 32), 0, 31);
                int y = Mathf.Clamp(Mathf.FloorToInt(center.y / sprite.texture.height * 32), 0, 31);
                pixel.ReadPixels(new Rect(x, y, 1, 1), 0, 0);
                pixel.Apply();
                color = pixel.GetPixel(0, 0);
                spriteEffectColors.Add(sprite, color);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(sample);
                Destroy(pixel);
            }
        }
        color *= source.color;
        color.a = 1f;
        return color;
    }

    private IEnumerator PlayPlacementEffect(RectTransform block, Vector3 targetPosition)
    {
        Vector3 startPosition = block.localPosition;
        Vector3 startScale = block.localScale;
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, placementDuration);
        while (block != null && elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            block.localPosition = Vector3.Lerp(startPosition, targetPosition, ease);
            block.localScale = Vector3.Lerp(startScale, Vector3.one, ease) *
                (1f + Mathf.Sin(t * Mathf.PI) * 0.045f);
            yield return null;
        }
        if (block != null)
        {
            block.localPosition = targetPosition;
            block.localScale = Vector3.one;
        }
        activePlacementAnimations = Mathf.Max(0, activePlacementAnimations - 1);
    }

    private IEnumerator ResolvePlacement()
    {
        while (activePlacementAnimations > 0)
            yield return null;
        ClearCompletedRowsAndColumns();
        while (activeClearAnimations > 0)
            yield return null;
    }

    [ContextMenu("Refresh Grid Cells")]
    public void RefreshGridCells()
    {
        if (gridContainer == null)
            gridContainer = GetComponent<RectTransform>();

        if (placedBlocksRoot == null)
            placedBlocksRoot = gridContainer;

        int cellCount = rows * columns;
        gridCells = new RectTransform[cellCount];

        if (occupiedCells == null ||
            occupiedCells.Length != cellCount)
        {
            occupiedCells = new bool[cellCount];
            placedBlocks = new RectTransform[cellCount];
        }
        else if (placedBlocks == null ||
                 placedBlocks.Length != cellCount)
        {
            placedBlocks = new RectTransform[cellCount];
        }

        for (int i = 0; i < gridContainer.childCount; i++)
        {
            Transform child = gridContainer.GetChild(i);

            if (!child.name.StartsWith(
                    "Square_",
                    StringComparison.Ordinal))
            {
                continue;
            }

            string indexText =
                child.name.Substring("Square_".Length);

            if (!int.TryParse(indexText, out int index))
                continue;

            if (index < 0 || index >= gridCells.Length)
                continue;

            gridCells[index] = child as RectTransform;
        }
    }

    private void SpawnInitialRandomBlocks()
    {
        if (blockPalette == null ||
            !blockPalette.HasValidPrefab)
        {
            Debug.LogWarning(
                "Không tạo ô ban đầu vì Block Palette đang trống."
            );
            return;
        }

        int minimum = Mathf.Clamp(
            minimumInitialBlocks,
            0,
            occupiedCells.Length
        );

        int maximum = Mathf.Clamp(
            maximumInitialBlocks,
            minimum,
            occupiedCells.Length
        );

        int blockCount = UnityEngine.Random.Range(
            minimum,
            maximum + 1
        );

        List<int> freeIndices = new List<int>();

        for (int i = 0; i < occupiedCells.Length; i++)
        {
            if (!occupiedCells[i] && gridCells[i] != null)
                freeIndices.Add(i);
        }

        for (int i = 0;
             i < blockCount && freeIndices.Count > 0;
             i++)
        {
            int listIndex = UnityEngine.Random.Range(
                0,
                freeIndices.Count
            );

            int targetIndex = freeIndices[listIndex];
            freeIndices.RemoveAt(listIndex);

            GameObject prefab = blockPalette.GetRandomPrefab();

            if (prefab == null)
                continue;

            GameObject blockObject = Instantiate(
                prefab,
                placedBlocksRoot,
                false
            );

            RectTransform block =
                blockObject.GetComponent<RectTransform>();

            if (block == null)
            {
                Destroy(blockObject);
                continue;
            }

            occupiedCells[targetIndex] = true;
            placedBlocks[targetIndex] = block;

            PlaceCellVisual(
                block,
                gridCells[targetIndex],
                targetIndex
            );
        }
    }

    private void ClearCompletedRowsAndColumns()
    {
        HideLinePreview();
        List<int> fullRows = new List<int>();
        List<int> fullColumns = new List<int>();
        HashSet<int> indicesToClear = new HashSet<int>();
        CollectCompletedLines(null, fullRows, fullColumns, indicesToClear);

        if (indicesToClear.Count == 0)
            return;

        EnsureScoreDisplay();
        int multiplier = CalculateLineMultiplier(fullRows.Count, fullColumns.Count);
        int gained = CalculateLineScore(fullRows.Count, fullColumns.Count, pointsPerLine);
        Score = (int)Math.Min(int.MaxValue, (long)Score + gained);
        if (Score > BestScore)
        {
            BestScore = Score;
            PlayerPrefs.SetInt(bestScoreKey, BestScore);
            PlayerPrefs.Save();
        }
        Vector3 scorePosition = Vector3.zero;
        foreach (int index in indicesToClear)
            scorePosition += gridCells[index].TransformPoint(gridCells[index].rect.center);
        scorePosition /= indicesToClear.Count;
        scoreDisplay.AnimateAward(Score, BestScore, gained, multiplier, scorePosition);
        StartClearSparks(indicesToClear);
        foreach (int index in indicesToClear)
        {
            occupiedCells[index] = false;
            RectTransform block = placedBlocks[index];
            if (block != null)
            {
                Image image = block.GetComponent<Image>();
                if (image != null && placedLineSprite != null)
                {
                    image.sprite = placedLineSprite;
                    image.color = placedLineTint;
                }
                activeClearAnimations++;
                clearingBlocks.Add(block);
                StartCoroutine(PlayClearEffect(block, GetClearDelay(index, fullRows, fullColumns)));
            }
            placedBlocks[index] = null;
        }
        foreach (int row in fullRows)
            StartLineGlow(row * columns, row * columns + columns - 1, true);
        foreach (int column in fullColumns)
            StartLineGlow(column, (rows - 1) * columns + column, false);
    }

    // A candidate is read as additional occupancy, without changing the board or score.
    // Hover and release deliberately use the same full-line calculation.
    private void CollectCompletedLines(IList<int> candidate, List<int> fullRows,
        List<int> fullColumns, HashSet<int> indicesToClear)
    {
        fullRows.Clear();
        fullColumns.Clear();
        indicesToClear.Clear();
        for (int row = 0; row < rows; row++)
        {
            bool rowIsFull = true;

            for (int column = 0;
                 column < columns;
                 column++)
            {
                int index = row * columns + column;
                if (!occupiedCells[index] && (candidate == null || !candidate.Contains(index)))
                {
                    rowIsFull = false;
                    break;
                }
            }

            if (!rowIsFull)
                continue;

            fullRows.Add(row);

            for (int column = 0;
                 column < columns;
                 column++)
            {
                indicesToClear.Add(row * columns + column);
            }
        }

        for (int column = 0; column < columns; column++)
        {
            bool columnIsFull = true;

            for (int row = 0; row < rows; row++)
            {
                int index = row * columns + column;
                if (!occupiedCells[index] && (candidate == null || !candidate.Contains(index)))
                {
                    columnIsFull = false;
                    break;
                }
            }

            if (!columnIsFull)
                continue;

            fullColumns.Add(column);

            for (int row = 0; row < rows; row++)
            {
                indicesToClear.Add(row * columns + column);
            }
        }

    }

    // Multipliers apply to all completed lines in this single placement.
    public static int CalculateLineMultiplier(int rowCount, int columnCount)
    {
        int total = Mathf.Max(0, rowCount) + Mathf.Max(0, columnCount);
        return total == 0 ? 0 : 1 + total / 2;
    }

    public static int CalculateLineScore(int rowCount, int columnCount, int basePoints = 100)
    {
        int total = Mathf.Max(0, rowCount) + Mathf.Max(0, columnCount);
        return (int)Math.Min(int.MaxValue, (long)total * Mathf.Max(0, basePoints) *
            CalculateLineMultiplier(rowCount, columnCount));
    }

    private void EnsureScoreDisplay()
    {
        if (scoreInitialized || gridContainer == null)
            return;
        BestScore = PlayerPrefs.GetInt(bestScoreKey, 0);
        if (scoreDisplay == null)
        {
            GameObject hud = new GameObject("ScoreDisplay", typeof(RectTransform));
            hud.transform.SetParent(gridContainer.parent, false);
            scoreDisplay = hud.AddComponent<BlockPuzzleScoreDisplay>();
            ownsScoreDisplay = true;
        }
        scoreDisplay.Initialize(gridContainer, GetGlowSprite(), Score, BestScore);
        scoreInitialized = true;
    }

    private Sprite GetGlowSprite()
    {
        if (glowSprite != null)
            return glowSprite;
        const int size = 64;
        glowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        glowTexture.name = "RuntimeLineGlow";
        glowTexture.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float radius = new Vector2((x + 0.5f) / size * 2f - 1f,
                    (y + 0.5f) / size * 2f - 1f).magnitude;
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - radius), 1.6f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        glowTexture.SetPixels(pixels);
        glowTexture.Apply(false, true);
        glowSprite = Sprite.Create(glowTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return glowSprite;
    }

    private Image CreateGlow(string name, Vector3 position, Vector2 size, Color color)
    {
        GameObject visual = new GameObject(name, typeof(RectTransform), typeof(Image));
        visual.transform.SetParent(placedBlocksRoot, false);
        Image image = visual.GetComponent<Image>();
        image.raycastTarget = false;
        image.sprite = GetGlowSprite();
        image.color = color;
        image.rectTransform.sizeDelta = size;
        image.rectTransform.position = position;
        glowVisuals.Add(image.rectTransform);
        return image;
    }

    private void StartLineGlow(int firstIndex, int lastIndex, bool horizontal)
    {
        Vector3 first = gridCells[firstIndex].TransformPoint(gridCells[firstIndex].rect.center);
        Vector3 last = gridCells[lastIndex].TransformPoint(gridCells[lastIndex].rect.center);
        Vector3 localFirst = placedBlocksRoot.InverseTransformPoint(first);
        Vector3 localLast = placedBlocksRoot.InverseTransformPoint(last);
        Vector2 cellSize = gridCells[firstIndex].rect.size;
        Vector2 size = horizontal
            ? new Vector2(Mathf.Abs(localLast.x - localFirst.x) + cellSize.x, cellSize.y * 1.35f)
            : new Vector2(cellSize.x * 1.35f, Mathf.Abs(localLast.y - localFirst.y) + cellSize.y);
        Image halo = CreateGlow("LineAfterglow", (first + last) * 0.5f, size, placedLineEffectColor);
        activeClearAnimations++;
        StartCoroutine(PlayLineGlow(halo));
    }

    private IEnumerator PlayLineGlow(Image halo)
    {
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, lineFlashDuration + clearDuration);
        while (halo != null && elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Color color = placedLineEffectColor;
            color.a = 0.65f * (1f - t) * (1f - t);
            halo.color = color;
            yield return null;
        }
        if (halo != null)
        {
            glowVisuals.Remove(halo.rectTransform);
            Destroy(halo.gameObject);
        }
        activeClearAnimations = Mathf.Max(0, activeClearAnimations - 1);
    }

    private void StartClearSparks(IEnumerable<int> indices)
    {
        GameObject visual = new GameObject("ClearSquareFragments", typeof(RectTransform), typeof(BlockClearSparks));
        RectTransform rect = visual.GetComponent<RectTransform>();
        rect.SetParent(placedBlocksRoot, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = placedBlocksRoot.rect.size;
        List<Vector2> centers = new List<Vector2>();
        foreach (int index in indices)
            centers.Add(rect.InverseTransformPoint(gridCells[index].TransformPoint(gridCells[index].rect.center)));
        BlockClearSparks sparks = visual.GetComponent<BlockClearSparks>();
        sparks.Initialize(centers, gridCells[0].rect.size, placedLineEffectColor);
        glowVisuals.Add(rect);
        activeClearAnimations++;
        StartCoroutine(PlayClearSparks(sparks));
    }

    private IEnumerator PlayClearSparks(BlockClearSparks sparks)
    {
        float elapsed = 0f;
        // Fragments appear just as the solid squares vanish.
        while (sparks != null && elapsed < lineFlashDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        elapsed = 0f;
        float duration = Mathf.Max(0.01f, clearDuration);
        while (sparks != null && elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            sparks.SetProgress(elapsed / duration);
            sparks.transform.SetAsLastSibling();
            yield return null;
        }
        if (sparks != null)
        {
            glowVisuals.Remove(sparks.rectTransform);
            Destroy(sparks.gameObject);
        }
        activeClearAnimations = Mathf.Max(0, activeClearAnimations - 1);
    }

    private float GetClearDelay(
        int index,
        List<int> fullRows,
        List<int> fullColumns)
    {
        int row = index / columns;
        int column = index % columns;
        float delaySteps = float.MaxValue;

        if (fullRows.Contains(row))
        {
            float columnDistance = Mathf.Abs(
                column - (columns - 1) / 2f
            );

            delaySteps = Mathf.Min(
                delaySteps,
                Mathf.Max(0f, columnDistance - 0.5f)
            );
        }

        if (fullColumns.Contains(column))
        {
            float rowDistance = Mathf.Abs(
                row - (rows - 1) / 2f
            );

            delaySteps = Mathf.Min(
                delaySteps,
                Mathf.Max(0f, rowDistance - 0.5f)
            );
        }

        if (delaySteps == float.MaxValue)
            delaySteps = 0f;

        return delaySteps * clearStagger;
    }

    private IEnumerator PlayClearEffect(RectTransform block, float delay)
    {
        if (block == null)
        {
            activeClearAnimations = Mathf.Max(0, activeClearAnimations - 1);
            yield break;
        }
        CanvasGroup group = block.GetComponent<CanvasGroup>();
        if (group == null) group = block.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 1f;

        // A square flash stays inside the cell; there is no oversized white blob.
        GameObject flashObject = new GameObject("CellFlash", typeof(RectTransform), typeof(Image));
        flashObject.transform.SetParent(block, false);
        Image flash = flashObject.GetComponent<Image>();
        flash.raycastTarget = false;
        flash.rectTransform.anchorMin = Vector2.zero;
        flash.rectTransform.anchorMax = Vector2.one;
        flash.rectTransform.offsetMin = block.rect.size * 0.055f;
        flash.rectTransform.offsetMax = -block.rect.size * 0.055f;
        flash.color = new Color(1f, 1f, 1f, clearFlashStrength);

        float elapsed = 0f;
        float hold = Mathf.Max(0.01f, lineFlashDuration) + delay;
        while (block != null && elapsed < hold)
        {
            elapsed += Time.unscaledDeltaTime;
            float pulse = Mathf.Sin(Mathf.Clamp01(elapsed / hold) * Mathf.PI);
            flash.color = new Color(1f, 1f, 1f, clearFlashStrength * (0.35f + 0.65f * pulse));
            yield return null;
        }
        elapsed = 0f;
        float vanishDuration = Mathf.Min(0.085f, clearDuration * 0.24f);
        while (block != null && elapsed < vanishDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, vanishDuration));
            block.localScale = Vector3.one * Mathf.Lerp(1f, 0.82f, t);
            group.alpha = 1f - t;
            yield return null;
        }
        clearingBlocks.Remove(block);
        if (block != null) Destroy(block.gameObject);
        activeClearAnimations = Mathf.Max(0, activeClearAnimations - 1);
    }

    private bool EnsureGridReady()
    {
        if (!boardInitialized)
            InitializeBoard();

        return boardInitialized;
    }

    private int FindNearestGridCell(
        Vector2 pointerScreenPosition,
        Camera eventCamera)
    {
        int nearestIndex = -1;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < gridCells.Length; i++)
        {
            RectTransform cell = gridCells[i];

            if (cell == null)
                continue;

            Vector3 worldCenter =
                cell.TransformPoint(cell.rect.center);

            Vector2 screenCenter =
                RectTransformUtility.WorldToScreenPoint(
                    eventCamera,
                    worldCenter
                );

            float distance =
                (screenCenter - pointerScreenPosition)
                .sqrMagnitude;

            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearestIndex = i;
        }

        return nearestIndex;
    }

    private void PlaceCellVisual(
        RectTransform block,
        RectTransform targetCell,
        int targetIndex)
    {
        Vector3 worldCenter = targetCell.TransformPoint(
            targetCell.rect.center
        );

        block.SetParent(placedBlocksRoot, false);
        block.anchorMin = new Vector2(0.5f, 0.5f);
        block.anchorMax = new Vector2(0.5f, 0.5f);
        block.pivot = new Vector2(0.5f, 0.5f);
        block.localScale = Vector3.one;
        block.localRotation = Quaternion.identity;
        block.sizeDelta = targetCell.rect.size;

        Vector3 localCenter =
            placedBlocksRoot.InverseTransformPoint(worldCenter);

        block.localPosition = new Vector3(
            localCenter.x,
            localCenter.y,
            0f
        );

        block.name = $"PlacedCell_{targetIndex}";
        block.SetAsLastSibling();

        Graphic[] graphics =
            block.GetComponentsInChildren<Graphic>(true);

        foreach (Graphic graphic in graphics)
            graphic.raycastTarget = false;

        GeneratedShapeDragHandle handle =
            block.GetComponent<GeneratedShapeDragHandle>();

        if (handle != null)
            Destroy(handle);
    }

    private void OnEnable()
    {
        if (resumePlacementResolution && boardInitialized)
        {
            resumePlacementResolution = false;
            StartCoroutine(ResolvePlacement());
        }
    }

    private void OnDisable()
    {
        resumePlacementResolution = activePlacementAnimations > 0;
        if (previewOwner != null)
            previewOwner.CancelInteraction();
        HideLinePreview();
        StopAllCoroutines();
        foreach (RectTransform block in clearingBlocks)
            if (block != null)
                Destroy(block.gameObject);
        clearingBlocks.Clear();
        foreach (RectTransform glow in glowVisuals)
            if (glow != null)
                Destroy(glow.gameObject);
        glowVisuals.Clear();
        if (placedBlocks != null && gridCells != null)
            for (int i = 0; i < placedBlocks.Length; i++)
                if (occupiedCells[i] && placedBlocks[i] != null && gridCells[i] != null)
                    PlaceCellVisual(placedBlocks[i], gridCells[i], i);
        activeClearAnimations = 0;
        activePlacementAnimations = 0;
    }

    private void OnDestroy()
    {
        foreach (Image ghost in previewImages)
            if (ghost != null)
                Destroy(ghost.gameObject);
        foreach (Image image in linePreviewImages)
            if (image != null) Destroy(image.gameObject);
        foreach (Image image in linePreviewHalos)
            if (image != null) Destroy(image.gameObject);
        if (ownsScoreDisplay && scoreDisplay != null)
            Destroy(scoreDisplay.gameObject);
        if (glowSprite != null)
            Destroy(glowSprite);
        if (glowTexture != null)
            Destroy(glowTexture);
    }
}
