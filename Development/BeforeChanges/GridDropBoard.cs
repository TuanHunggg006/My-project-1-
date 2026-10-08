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
    [SerializeField] private float clearDuration = 0.45f;
    [Range(0f, 0.15f)]
    [SerializeField] private float clearStagger = 0.035f;
    [Range(1f, 2f)]
    [SerializeField] private float clearPopScale = 1.2f;
    [SerializeField] private float clearRotation = 90f;
    [Range(0f, 1f)]
    [SerializeField] private float clearFlashStrength = 0.75f;

    private RectTransform[] gridCells;
    private bool[] occupiedCells;
    private RectTransform[] placedBlocks;
    private bool boardInitialized;
    private bool initialBlocksCreated;
    private int activeClearAnimations;

    public bool IsClearingLines => activeClearAnimations > 0;

    private IEnumerator Start()
    {
        yield return null;
        InitializeBoard();
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
            gridCells[0] != null;

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

                    if (occupiedCells[targetIndex])
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
        if (shape == null ||
            IsClearingLines ||
            !EnsureGridReady())
        {
            return false;
        }

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
        List<int> targetIndices = new List<int>();

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

            if (gridCells[targetIndex] == null ||
                occupiedCells[targetIndex])
            {
                return false;
            }

            targetIndices.Add(targetIndex);
        }

        for (int i = 0; i < shape.Cells.Count; i++)
        {
            int targetIndex = targetIndices[i];
            occupiedCells[targetIndex] = true;

            RectTransform block =
                shape.Cells[i].RectTransform;

            PlaceCellVisual(
                block,
                gridCells[targetIndex],
                targetIndex
            );

            placedBlocks[targetIndex] = block;
        }

        ClearCompletedRowsAndColumns();
        Destroy(shape.gameObject);
        return true;
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
        List<int> fullRows = new List<int>();
        List<int> fullColumns = new List<int>();
        HashSet<int> indicesToClear = new HashSet<int>();

        for (int row = 0; row < rows; row++)
        {
            bool rowIsFull = true;

            for (int column = 0;
                 column < columns;
                 column++)
            {
                if (!occupiedCells[row * columns + column])
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
                if (!occupiedCells[row * columns + column])
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

        foreach (int index in indicesToClear)
        {
            occupiedCells[index] = false;

            RectTransform block = placedBlocks[index];

            if (block != null)
            {
                float delay = GetClearDelay(
                    index,
                    fullRows,
                    fullColumns
                );

                activeClearAnimations++;
                StartCoroutine(
                    PlayClearEffect(block, delay)
                );
            }

            placedBlocks[index] = null;
        }
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

    private IEnumerator PlayClearEffect(
        RectTransform block,
        float delay)
    {
        if (block == null)
        {
            activeClearAnimations = Mathf.Max(
                0,
                activeClearAnimations - 1
            );
            yield break;
        }

        block.SetAsLastSibling();

        CanvasGroup canvasGroup =
            block.GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = block.gameObject.AddComponent<CanvasGroup>();

        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        canvasGroup.alpha = 1f;

        Graphic[] graphics =
            block.GetComponentsInChildren<Graphic>(true);

        Color[] originalColors = new Color[graphics.Length];

        for (int i = 0; i < graphics.Length; i++)
            originalColors[i] = graphics[i].color;

        float delayElapsed = 0f;

        while (delayElapsed < delay)
        {
            if (block == null)
                break;

            delayElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        float duration = Mathf.Max(0.01f, clearDuration);
        float elapsed = 0f;

        while (block != null && elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float time = Mathf.Clamp01(elapsed / duration);

            if (time < 0.32f)
            {
                float popTime = time / 0.32f;
                float easedPop =
                    1f - Mathf.Pow(1f - popTime, 3f);

                block.localScale = Vector3.one * Mathf.Lerp(
                    1f,
                    clearPopScale,
                    easedPop
                );
            }
            else
            {
                float shrinkTime =
                    (time - 0.32f) / 0.68f;

                float easedShrink =
                    shrinkTime * shrinkTime *
                    (3f - 2f * shrinkTime);

                block.localScale = Vector3.one * Mathf.Lerp(
                    clearPopScale,
                    0f,
                    easedShrink
                );

                block.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    Mathf.Lerp(
                        0f,
                        clearRotation,
                        easedShrink
                    )
                );

                canvasGroup.alpha = 1f - easedShrink;
            }

            float flash = Mathf.Sin(time * Mathf.PI) *
                          clearFlashStrength;

            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] != null)
                {
                    graphics[i].color = Color.Lerp(
                        originalColors[i],
                        Color.white,
                        flash
                    );
                }
            }

            yield return null;
        }

        if (block != null)
            Destroy(block.gameObject);

        activeClearAnimations = Mathf.Max(
            0,
            activeClearAnimations - 1
        );
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
}
