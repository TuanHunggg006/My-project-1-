using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// One UI mesh for the small square fragments, regardless of how many lines clear.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BlockClearSparks : MaskableGraphic
{
    private struct Spark
    {
        public Vector2 origin, travel;
        public float size, lifetime;
    }

    private readonly List<Spark> sparks = new List<Spark>();
    private float progress;

    public void Initialize(IEnumerable<Vector2> centers, Vector2 cellSize, Color tint)
    {
        raycastTarget = false;
        color = tint;
        sparks.Clear();
        int index = 0;
        foreach (Vector2 center in centers)
            for (int fragment = 0; fragment < 3; fragment++, index++)
            {
                // Stable variation does not consume the gameplay random sequence.
                float variation = Mathf.Repeat(index * 0.618034f, 1f);
                float angle = index * 2.39996f;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                sparks.Add(new Spark
                {
                    origin = center + Vector2.Scale(direction, cellSize) * 0.2f,
                    travel = Vector2.Scale(direction, cellSize) * (0.16f + variation * 0.2f)
                        + Vector2.up * cellSize.y * 0.22f,
                    size = Mathf.Min(cellSize.x, cellSize.y) * (0.07f + variation * 0.06f),
                    lifetime = 0.65f + variation * 0.35f
                });
            }
        SetProgress(0f);
    }

    public void SetProgress(float value)
    {
        progress = Mathf.Clamp01(value);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        foreach (Spark spark in sparks)
        {
            float t = Mathf.Clamp01(progress / spark.lifetime);
            if (t >= 1f) continue;
            Vector2 center = spark.origin + spark.travel * (1f - Mathf.Pow(1f - t, 2f));
            float half = spark.size * (1f - t * 0.65f) * 0.5f;
            Color tint = Color.Lerp(color, Color.white, (1f - t) * 0.25f);
            tint.a *= (1f - t) * Mathf.Clamp01(progress / 0.06f);
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector3(center.x - half, center.y - half), tint, Vector2.zero);
            mesh.AddVert(new Vector3(center.x - half, center.y + half), tint, Vector2.zero);
            mesh.AddVert(new Vector3(center.x + half, center.y + half), tint, Vector2.zero);
            mesh.AddVert(new Vector3(center.x + half, center.y - half), tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
