using System.Collections.Generic;
using UnityEngine;

public class GridGenerator : MonoBehaviour
{
    [SerializeField] private GameObject squarePrefab;

    [Header("Grid 8 x 8")]
    [SerializeField] private int rows = 8;
    [SerializeField] private int columns = 8;

    // 1 = sử dụng toàn bộ vùng có thể
    [Range(0.1f, 1f)]
    [SerializeField] private float squareScale = 1f;

    [SerializeField] private float squareGap = 0f;

    [Header("Khoảng cách với viền khung")]
    [SerializeField] private float gridPadding = 10f;

    private readonly List<GameObject> gridSquares =
        new List<GameObject>();

    private void Start()
    {
        SpawnGridSquares();

        Canvas.ForceUpdateCanvases();

        RectTransform container =
            GetComponent<RectTransform>();

        container.ForceUpdateRectTransforms();

        SetGridSquaresPosition();
    }

    private void SpawnGridSquares()
    {
        if (squarePrefab == null)
        {
            Debug.LogError("Chưa gán Square Prefab!");
            return;
        }

        if (rows <= 0 || columns <= 0)
        {
            Debug.LogError(
                "Rows và Columns phải lớn hơn 0!"
            );

            return;
        }

        int totalSquares = rows * columns;

        for (int i = 0; i < totalSquares; i++)
        {
            // GridContainer chính là transform hiện tại
            GameObject newSquare =
                Instantiate(
                    squarePrefab,
                    transform,
                    false
                );

            newSquare.name = $"Square_{i}";

            // UI giữ Scale = 1
            newSquare.transform.localScale =
                Vector3.one;

            gridSquares.Add(newSquare);

            // Nếu GridSquare của bạn có hàm SetIndex
            if (newSquare.TryGetComponent(
                    out GridSquare gridSquare))
            {
                gridSquare.SetIndex(i);
            }
        }
    }

    private void SetGridSquaresPosition()
    {
        if (gridSquares.Count == 0)
            return;

        RectTransform container =
            GetComponent<RectTransform>();

        // Kích thước còn lại sau khi trừ
        // Padding và khoảng cách giữa các ô
        float availableWidth =
            container.rect.width -
            gridPadding * 2f -
            (columns - 1) * squareGap;

        float availableHeight =
            container.rect.height -
            gridPadding * 2f -
            (rows - 1) * squareGap;

        // Kích thước riêng theo chiều ngang
        // và chiều dọc để lưới phủ đều khung
        float squareWidth =
            availableWidth / columns;

        float squareHeight =
            availableHeight / rows;

        squareWidth *= squareScale;
        squareHeight *= squareScale;

        float stepX =
            squareWidth + squareGap;

        float stepY =
            squareHeight + squareGap;

        float gridWidth =
            columns * squareWidth +
            (columns - 1) * squareGap;

        float gridHeight =
            rows * squareHeight +
            (rows - 1) * squareGap;

        // Căn toàn bộ lưới vào giữa GridContainer
        float offsetX =
            (container.rect.width - gridWidth) / 2f;

        float offsetY =
            (container.rect.height - gridHeight) / 2f;

        for (int i = 0; i < gridSquares.Count; i++)
        {
            int column = i % columns;
            int row = i / columns;

            RectTransform squareRect =
                gridSquares[i]
                    .GetComponent<RectTransform>();

            if (squareRect == null)
                continue;

            // Neo từng ô từ góc trên bên trái
            squareRect.anchorMin =
                new Vector2(0f, 1f);

            squareRect.anchorMax =
                new Vector2(0f, 1f);

            squareRect.pivot =
                new Vector2(0f, 1f);

            squareRect.localScale =
                Vector3.one;

            // Đặt chiều rộng của ô
            squareRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                squareWidth
            );

            // Đặt chiều cao của ô
            squareRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                squareHeight
            );

            // Đặt vị trí ô theo hàng và cột
            squareRect.anchoredPosition =
                new Vector2(
                    offsetX + column * stepX,
                    -offsetY - row * stepY
                );
        }
    }
}