using System.Collections.Generic;
using UnityEngine;

public class BlockPrefabPalette : MonoBehaviour
{
    [Header("Colored block prefabs")]
    [Tooltip("Mỗi màu là một UI prefab có RectTransform và Image.")]
    [SerializeField]
    private List<GameObject> blockPrefabs =
        new List<GameObject>();

    public bool HasValidPrefab
    {
        get
        {
            foreach (GameObject prefab in blockPrefabs)
            {
                if (prefab != null)
                    return true;
            }

            return false;
        }
    }

    public GameObject GetRandomPrefab()
    {
        List<GameObject> validPrefabs =
            new List<GameObject>();

        foreach (GameObject prefab in blockPrefabs)
        {
            if (prefab != null)
                validPrefabs.Add(prefab);
        }

        if (validPrefabs.Count == 0)
        {
            Debug.LogError(
                "BlockPrefabPalette chưa có prefab màu nào."
            );
            return null;
        }

        return validPrefabs[
            Random.Range(0, validPrefabs.Count)
        ];
    }
}
