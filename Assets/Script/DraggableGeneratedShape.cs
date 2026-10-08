using System.Collections;
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

        public CellData(RectTransform rectTransform, Vector2Int coordinate)
        {
            RectTransform = rectTransform;
            Coordinate = coordinate;
        }
    }

    [Header("Drag feel")]
    [SerializeField, Min(1f)] private float followSpeed = 32f;
    [SerializeField, Range(0f, 1f)] private float magnetStrength = 0.8f;
    [SerializeField, Min(0.01f)] private float pickupDuration = 0.1f;
    [SerializeField, Min(0.01f)] private float returnDuration = 0.18f;

    public IReadOnlyList<CellData> Cells => cells;
    public bool IsInteracting => isDragging || isReturning || scrollLocked;
    private readonly List<CellData> cells = new List<CellData>();
    private readonly List<int> previewTargets = new List<int>();
    private GridDropBoard dropBoard;
    private PolyominoShapeGenerator suggestionGenerator;
    private RectTransform shapeRect;
    private RectTransform canvasRect;
    private CanvasGroup canvasGroup;
    private ScrollRect parentScrollRect;
    private Transform originalParent;
    private int originalSiblingIndex;
    private Vector2 originalAnchoredPosition;
    private Vector3 originalLocalScale;
    private Quaternion originalLocalRotation;
    private Vector3 pickupStartScale;
    private Vector3 dragScale;
    private Vector2Int grabbedCell;
    private RectTransform grabbedRect;
    private Vector2 pointerPosition;
    private Camera eventCamera;
    private float pickupElapsed;
    private int pointerId;
    private bool isDragging;
    private bool isReturning;
    private bool scrollLocked;
    private bool scrollWasEnabled;
    private Coroutine returnCoroutine;

    public void Initialize(GridDropBoard board, PolyominoShapeGenerator generator)
    {
        dropBoard = board;
        suggestionGenerator = generator;
        shapeRect = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void RegisterCell(RectTransform cellRect, Vector2Int coordinate)
    {
        cells.Add(new CellData(cellRect, coordinate));
    }

    public void PrepareForPotentialDrag()
    {
        if (scrollLocked || isReturning || dropBoard == null || dropBoard.IsClearingLines)
            return;
        if (parentScrollRect == null)
            parentScrollRect = GetComponentInParent<ScrollRect>();
        if (parentScrollRect == null)
            return;
        scrollWasEnabled = parentScrollRect.enabled;
        parentScrollRect.StopMovement();
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
        if (isDragging || isReturning)
            return;
        if (dropBoard == null ||
            dropBoard.IsClearingLines || cells.Count == 0 || !dropBoard.TryBeginDrag(this))
        {
            RestoreParentScroll();
            return;
        }

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            dropBoard.EndDragPreview(this);
            RestoreParentScroll();
            return;
        }
        canvasRect = canvas.rootCanvas.transform as RectTransform;
        eventCamera = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : (eventData.pressEventCamera != null ? eventData.pressEventCamera : canvas.rootCanvas.worldCamera);
        pointerId = eventData.pointerId;
        pointerPosition = eventData.position;
        originalParent = shapeRect.parent;
        originalSiblingIndex = shapeRect.GetSiblingIndex();
        originalAnchoredPosition = shapeRect.anchoredPosition;
        originalLocalScale = shapeRect.localScale;
        originalLocalRotation = shapeRect.localRotation;
        grabbedRect = FindClosestCell(pointerPosition, eventCamera, out grabbedCell);
        PrepareForPotentialDrag();
        shapeRect.SetParent(canvasRect, true);
        shapeRect.SetAsLastSibling();
        pickupStartScale = shapeRect.localScale;
        dragScale = dropBoard.GetDragScale(canvasRect);
        pickupElapsed = 0f;
        canvasGroup.blocksRaycasts = false;
        isDragging = true;
    }

    public void Drag(PointerEventData eventData)
    {
        if (isDragging && eventData.pointerId == pointerId)
            pointerPosition = eventData.position;
    }

    private void LateUpdate()
    {
        if (!isDragging || canvasRect == null || grabbedRect == null)
            return;
        pickupElapsed += Time.unscaledDeltaTime;
        float pickup = Mathf.Clamp01(pickupElapsed / Mathf.Max(0.01f, pickupDuration));
        shapeRect.localScale = Vector3.Lerp(pickupStartScale, dragScale, 1f - Mathf.Pow(1f - pickup, 3f));
        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                canvasRect, pointerPosition, eventCamera, out Vector3 pointerWorld))
            return;

        Vector3 desiredCenter = pointerWorld;
        if (dropBoard.TryGetPlacement(this, grabbedCell, pointerPosition, eventCamera,
                previewTargets, out Vector3 snapCenter))
        {
            desiredCenter = Vector3.Lerp(pointerWorld, snapCenter, magnetStrength);
            dropBoard.ShowPlacementPreview(this, previewTargets);
        }
        else
            dropBoard.HidePlacementPreview(this);

        Vector3 grabbedWorld = grabbedRect.TransformPoint(grabbedRect.rect.center);
        Vector3 targetPosition = shapeRect.position + desiredCenter - grabbedWorld;
        float follow = 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
        shapeRect.position = Vector3.Lerp(shapeRect.position, targetPosition, follow);
    }

    public void EndDrag(PointerEventData eventData)
    {
        if (!isDragging || eventData.pointerId != pointerId)
            return;
        isDragging = false;
        dropBoard.EndDragPreview(this);
        RestoreParentScroll();
        bool placed = dropBoard.TryPlaceShape(this, grabbedCell, eventData.position, eventCamera);
        if (!placed)
        {
            returnCoroutine = StartCoroutine(ReturnToPreview());
            return;
        }
        if (suggestionGenerator != null)
            suggestionGenerator.NotifySuggestionPlaced();
    }

    private RectTransform FindClosestCell(Vector2 screenPosition, Camera camera, out Vector2Int coordinate)
    {
        float closestDistance = float.MaxValue;
        RectTransform closest = null;
        coordinate = Vector2Int.zero;
        foreach (CellData cell in cells)
        {
            Vector2 center = RectTransformUtility.WorldToScreenPoint(camera,
                cell.RectTransform.TransformPoint(cell.RectTransform.rect.center));
            float distance = (center - screenPosition).sqrMagnitude;
            if (distance >= closestDistance)
                continue;
            closestDistance = distance;
            coordinate = cell.Coordinate;
            closest = cell.RectTransform;
        }
        return closest;
    }

    private IEnumerator ReturnToPreview()
    {
        isReturning = true;
        canvasGroup.blocksRaycasts = false;
        Vector3 startPosition = shapeRect.position;
        Vector3 startScale = shapeRect.localScale;
        shapeRect.SetParent(originalParent, false);
        shapeRect.anchoredPosition = originalAnchoredPosition;
        shapeRect.localScale = originalLocalScale;
        Vector3 targetPosition = shapeRect.position;
        shapeRect.SetParent(canvasRect, true);
        Vector3 targetScale = shapeRect.localScale;
        shapeRect.position = startPosition;
        shapeRect.localScale = startScale;
        float elapsed = 0f;
        while (elapsed < returnDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, returnDuration));
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            shapeRect.position = Vector3.Lerp(startPosition, targetPosition, ease);
            shapeRect.localScale = Vector3.Lerp(startScale, targetScale, ease);
            yield return null;
        }
        RestorePreviewTransform();
        canvasGroup.blocksRaycasts = true;
        isReturning = false;
        returnCoroutine = null;
    }

    private void RestorePreviewTransform()
    {
        if (originalParent == null)
            return;
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

    public void CancelInteraction()
    {
        if (dropBoard != null)
            dropBoard.EndDragPreview(this);
        if (isDragging || isReturning)
        {
            if (returnCoroutine != null)
                StopCoroutine(returnCoroutine);
            RestorePreviewTransform();
            if (canvasGroup != null)
                canvasGroup.blocksRaycasts = true;
        }
        isDragging = false;
        isReturning = false;
        returnCoroutine = null;
        RestoreParentScroll();
    }

    private void OnDisable()
    {
        CancelInteraction();
    }
}
