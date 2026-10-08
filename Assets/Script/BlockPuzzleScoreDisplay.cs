using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BlockPuzzleScoreDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text bestScoreText;
    [SerializeField, Min(0.01f)] private float countDuration = 0.38f;
    [SerializeField, Min(0.01f)] private float popupDuration = 0.85f;

    private RectTransform hudRect;
    private RectTransform gridRect;
    private Image scoreGlow;
    private Coroutine scoreAnimation;
    private readonly List<GameObject> popups = new List<GameObject>();
    private int displayedScore;
    private int targetScore;
    private int targetBest;
    private Vector3 scoreBaseScale;
    private Color scoreBaseColor;

    public void Initialize(RectTransform grid, Sprite glowSprite, int score, int best)
    {
        gridRect = grid;
        hudRect = GetComponent<RectTransform>();
        hudRect.anchorMin = new Vector2(0f, 0f);
        hudRect.anchorMax = new Vector2(1f, 1f);
        hudRect.offsetMin = Vector2.zero;
        hudRect.offsetMax = Vector2.zero;
        hudRect.SetAsLastSibling();

        if (scoreText == null)
        {
            scoreText = CreateText("CurrentScore", 76f, Color.white);
            SetTopPosition(scoreText.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0f, 94f), new Vector2(600f, 120f));
        }
        if (bestScoreText == null)
        {
            bestScoreText = CreateText("BestScore", 30f, new Color(1f, 0.8f, 0.25f));
            bestScoreText.alignment = TextAlignmentOptions.Left;
            SetTopPosition(bestScoreText.rectTransform, new Vector2(0f, 1f),
                new Vector2(180f, 170f), new Vector2(300f, 60f));
        }

        GameObject glow = new GameObject("ScoreGlow", typeof(RectTransform), typeof(Image));
        glow.transform.SetParent(hudRect, false);
        scoreGlow = glow.GetComponent<Image>();
        scoreGlow.sprite = glowSprite;
        scoreGlow.raycastTarget = false;
        scoreGlow.color = new Color(0.9f, 0.2f, 1f, 0f);
        scoreGlow.rectTransform.sizeDelta = new Vector2(260f, 160f);
        scoreGlow.rectTransform.position = scoreText.rectTransform.position;
        scoreGlow.transform.SetSiblingIndex(scoreText.transform.GetSiblingIndex());
        scoreBaseScale = scoreText.rectTransform.localScale;
        scoreBaseColor = scoreText.color;
        targetScore = score;
        targetBest = best;
        SetImmediateScore();
    }

    private TMP_Text CreateText(string name, float size, Color color)
    {
        GameObject label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(hudRect, false);
        TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = size * 0.55f;
        text.fontSizeMax = size;
        text.raycastTarget = false;
        text.outlineColor = new Color32(19, 35, 88, 255);
        text.outlineWidth = 0.12f;
        return text;
    }

    private static void SetTopPosition(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    public void AnimateAward(int score, int best, int gained, int multiplier, Vector3 worldPosition)
    {
        targetScore = score;
        targetBest = best;
        if (!isActiveAndEnabled)
        {
            SetImmediateScore();
            return;
        }
        if (scoreAnimation != null)
            StopCoroutine(scoreAnimation);
        scoreAnimation = StartCoroutine(CountScore());
        StartCoroutine(ShowAward(gained, multiplier, worldPosition));
    }

    private IEnumerator CountScore()
    {
        int start = displayedScore;
        float elapsed = 0f;
        while (elapsed < countDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, countDuration));
            displayedScore = Mathf.RoundToInt(Mathf.Lerp(start, targetScore, 1f - Mathf.Pow(1f - t, 3f)));
            scoreText.SetText(displayedScore.ToString());
            float pulse = Mathf.Sin(t * Mathf.PI);
            scoreText.rectTransform.localScale = scoreBaseScale * (1f + pulse * 0.15f);
            scoreText.color = Color.Lerp(scoreBaseColor, new Color(1f, 0.8f, 1f), pulse * 0.7f);
            scoreGlow.color = new Color(0.9f, 0.2f, 1f, pulse * 0.85f);
            yield return null;
        }
        SetImmediateScore();
        scoreAnimation = null;
    }

    private IEnumerator ShowAward(int gained, int multiplier, Vector3 worldPosition)
    {
        TMP_Text popup = CreateText("LineScorePopup", multiplier > 1 ? 58f : 48f,
            new Color(1f, 0.9f, 0.5f));
        popup.text = multiplier > 1
            ? $"<color=#D5EBFF>Combo</color> <color=#FFD55A>x{multiplier}</color>\n+{gained}"
            : $"+{gained}";
        popup.rectTransform.sizeDelta = new Vector2(600f, multiplier > 1 ? 160f : 90f);
        // Keep awards away from the total and the suggestion tray.
        Vector3 localPosition = hudRect.InverseTransformPoint(worldPosition);
        Vector3 gridTop = hudRect.InverseTransformPoint(gridRect.TransformPoint(
            new Vector3(gridRect.rect.center.x, gridRect.rect.yMax, 0f)));
        localPosition.y = Mathf.Min(localPosition.y, gridTop.y - 125f);
        popup.rectTransform.localPosition = localPosition;
        Vector3 start = localPosition;
        Color color = popup.color;
        popups.Add(popup.gameObject);
        float elapsed = 0f;
        while (popup != null && elapsed < popupDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, popupDuration));
            popup.rectTransform.localPosition = start + Vector3.up * (110f * t);
            float pop = Mathf.Sin(Mathf.Clamp01(t / 0.3f) * Mathf.PI);
            popup.rectTransform.localScale = Vector3.one * (1f + pop * 0.14f);
            color.a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, t));
            popup.color = color;
            yield return null;
        }
        if (popup != null)
        {
            popups.Remove(popup.gameObject);
            Destroy(popup.gameObject);
        }
    }

    private void SetImmediateScore()
    {
        displayedScore = targetScore;
        if (scoreText != null)
        {
            scoreText.SetText(targetScore.ToString());
            scoreText.rectTransform.localScale = scoreBaseScale;
            scoreText.color = scoreBaseColor;
        }
        if (bestScoreText != null)
            bestScoreText.SetText($"BEST {targetBest}");
        if (scoreGlow != null)
            scoreGlow.color = new Color(0.9f, 0.2f, 1f, 0f);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        scoreAnimation = null;
        SetImmediateScore();
        foreach (GameObject popup in popups)
            if (popup != null)
                Destroy(popup);
        popups.Clear();
    }
}
