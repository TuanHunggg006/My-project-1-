using UnityEngine;
using UnityEngine.EventSystems;

public class GeneratedShapeDragHandle : MonoBehaviour,
    IPointerDownHandler,
    IPointerUpHandler,
    IInitializePotentialDragHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    private DraggableGeneratedShape owner;

    public void Initialize(DraggableGeneratedShape shapeOwner)
    {
        owner = shapeOwner;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (owner != null)
            owner.PrepareForPotentialDrag();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (owner != null)
            owner.CancelPotentialDrag();
    }

    public void OnInitializePotentialDrag(
        PointerEventData eventData)
    {
        // Shape phản hồi ngay, không chờ ngưỡng kéo của ScrollRect.
        eventData.useDragThreshold = false;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (owner != null)
            owner.BeginDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (owner != null)
            owner.Drag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (owner != null)
            owner.EndDrag(eventData);
    }
}
