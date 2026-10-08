using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DraggableGeneratedShape : MonoBehaviour
{
    public class CellData
    {
        public RectTransform RectTransform { get; }
        public Vector2Int Coordinate { get; }

        public CellData(
            RectTransform rectTransform,
            Vector2Int coordinate)
        {
            RectTransform = rectTransform;
            Coordinate = coordinate;
        }
    }

    public IReadOnlyList<CellData> Cells => cells;

    private readonly List<CellData> cells =
        new List<CellData>();

    private GridDropBoard dropBoard;
    private PolyominoShapeGenerator suggestionGenerator;
    private RectTransform shapeRect;
    private Canvas rootCanvas;
    private CanvasGroup canvasGroup;
    private ScrollRect parentScrollRect;

    private Transform originalParent;
    private int originalSiblingIndex;
    private Vector2 originalAnchoredPosition;
    private Vector3 originalLocalScale;
    private Quaternion originalLocalRotation;
    private Vector2Int grabbedCell;
    private bool isDragging;
    private bool scrollLocked;
    private bool scrollWasEnabled;

    public void Initialize(
        GridDropBoard board,
        PolyominoShapeGenerator generator)
    {
        dropBoard = board;
        suggestionGenerator = generator;
        shapeRect = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void RegisterCell(
        RectTransform cellRect,
        Vector2Int coordinate)
    {
        cells.Add(new CellData(cellRect, coordinate));
    }

    public void PrepareForPotentialDrag()
    {
        if (scrollLocked)
            return;

        if (parentScrollRect == null)
        {
            parentScrollRect =
                GetComponentInParent<ScrollRect>();
        }

        if (parentScrollRect == null)
            return;

        scrollWasEnabled = parentScrollRect.enabled;
        parentScrollRect.enabled = false;
        scrollLocked = true;
    }

    public void CancelPotentialDrag()
    {
        if (!isDragging)
            RestoreParentScroll();
    }

    public void BeginDrag(PointerEventData eventData)
    {
        if (isDragging || dropBoard == null || cells.Count == 0)
            return;

        if (shapeRect == null)
            shapeRect = GetComponent<RectTransform>();

        rootCanvas = GetComponentInParent<Canvas>();

        if (rootCanvas == null)
            return;

        rootCanvas = rootCanvas.rootCanvas;
        originalParent = shapeRect.parent;
        originalSiblingIndex = shapeRect.GetSiblingIndex();
        originalAnchoredPosition = shapeRect.anchoredPosition;
        originalLocalScale = shapeRect.localScale;
        originalLocalRotation = shapeRect.localRotation;

        grabbedCell = FindClosestCell(
            eventData.position,
            eventData.pressEventCamera
        );

        PrepareForPotentialDrag();

        shapeRect.SetParent(rootCanvas.transform, true);
        shapeRect.SetAsLastSibling();

        // Ở khay shape nhỏ; khi kéo thì phóng đúng cỡ ô Grid.
        shapeRect.localScale = Vector3.one;
        MoveGrabbedCellToPointer(eventData);

        canvasGroup.blocksRaycasts = false;
        isDragging = true;
    }

    public void Drag(PointerEventData eventData)
    {
        if (!isDragging || rootCanvas == null)
            return;

        shapeRect.anchoredPosition +=
            eventData.delta / rootCanvas.scaleFactor;
    }

    public void EndDrag(PointerEventData eventData)
    {
        if (!isDragging)
            return;

        isDragging = false;
        canvasGroup.blocksRaycasts = true;

        RestoreParentScroll();

        bool placed = dropBoard.TryPlaceShape(
            this,
            grabbedCell,
            eventData.position,
            eventData.pressEventCamera
        );

        if (!placed)
        {
            ReturnToPreview();
            return;
        }

        if (suggestionGenerator != null)
            suggestionGenerator.NotifySuggestionPlaced();
    }

    private void MoveGrabbedCellToPointer(
        PointerEventData eventData)
    {
        RectTransform grabbedRect = null;

        foreach (CellData cell in cells)
        {
            if (cell.Coordinate == grabbedCell)
            {
                grabbedRect = cell.RectTransform;
                break;
            }
        }

        if (grabbedRect == null)
            return;

        Vector3 worldCenter = grabbedRect.TransformPoint(
            grabbedRect.rect.center
        );

        Vector2 cellScreenCenter =
            RectTransformUtility.WorldToScreenPoint(
                eventData.pressEventCamera,
                worldCenter
            );

        Vector2 screenDelta =
            eventData.position - cellScreenCenter;

        shapeRect.anchoredPosition +=
            screenDelta / rootCanvas.scaleFactor;
    }

    private Vector2Int FindClosestCell(
        Vector2 screenPosition,
        Camera eventCamera)
    {
        float closestDistance = float.MaxValue;
        Vector2Int closestCoordinate = Vector2Int.zero;

        foreach (CellData cell in cells)
        {
            Vector3 worldCenter =
                cell.RectTransform.TransformPoint(
                    cell.RectTransform.rect.center
                );

            Vector2 screenCenter =
                RectTransformUtility.WorldToScreenPoint(
                    eventCamera,
                    worldCenter
                );

            float distance =
                (screenCenter - screenPosition).sqrMagnitude;

            if (distance >= closestDistance)
                continue;

            closestDistance = distance;
            closestCoordinate = cell.Coordinate;
        }

        return closestCoordinate;
    }

    private void ReturnToPreview()
    {
        shapeRect.SetParent(originalParent, false);
        shapeRect.SetSiblingIndex(originalSiblingIndex);
        shapeRect.anchoredPosition = originalAnchoredPosition;
        shapeRect.localScale = originalLocalScale;
        shapeRect.localRotation = originalLocalRotation;
    }

    private void RestoreParentScroll()
    {
        if (parentScrollRect != null && scrollLocked)
            parentScrollRect.enabled = scrollWasEnabled;

        scrollLocked = false;
    }

    private void OnDisable()
    {
        RestoreParentScroll();
    }
}
