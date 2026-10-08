using TMPro;
using UnityEngine;

public class GridSquare : MonoBehaviour
{
    [SerializeField] private TMP_Text textIndex;

    public int SquareIndex { get; private set; }

    public void SetIndex(int index)
    {
        SquareIndex = index;

        if (textIndex != null)
        {
            textIndex.SetText(index.ToString());
        }
    }
}