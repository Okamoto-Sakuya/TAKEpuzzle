using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 盤面上の1個のパズルピース。
/// </summary>
public class PuzzleOrb : MonoBehaviour
{
    public int x;
    public int y;
    public int element { get; private set; }

    private Image image;
    private TMP_Text text;
    private CanvasGroup canvasGroup;
    private Sprite assignedSprite;
    private Coroutine moveCoroutine;

    public void Setup(int newX, int newY, int newElement, Sprite sprite = null)
    {
        x = newX;
        y = newY;
        SetElement(newElement, sprite);

        image = GetComponent<Image>();
        text = GetComponentInChildren<TMP_Text>();
        canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        canvasGroup.alpha = 1f;
        Refresh();
    }

    /// <summary>
    /// 属性変更と画像変更を必ず同時に行います。
    /// elementとSpriteを別々に変更しないことで、属性と画像のズレを防ぎます。
    /// </summary>
    public void SetElement(int newElement, Sprite sprite)
    {
        element = newElement;
        assignedSprite = sprite;
        Refresh();
    }

    /// <summary>
    /// ドラッグ中に、他のOrbを指定マスへ滑らかに移動させます。
    /// </summary>
    public void MoveTo(Vector2 targetPosition, float duration)
    {
        if (moveCoroutine != null)
            StopCoroutine(moveCoroutine);

        moveCoroutine = StartCoroutine(MoveToCoroutine(targetPosition, duration));
    }

    private IEnumerator MoveToCoroutine(Vector2 targetPosition, float duration)
    {
        RectTransform rect = GetComponent<RectTransform>();
        Vector2 startPosition = rect.anchoredPosition;

        if (duration <= 0f)
        {
            rect.anchoredPosition = targetPosition;
            moveCoroutine = null;
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);

            rect.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, t);
            yield return null;
        }

        rect.anchoredPosition = targetPosition;
        moveCoroutine = null;
    }

    public void SetPositionImmediate(Vector2 position)
    {
        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
            moveCoroutine = null;
        }

        GetComponent<RectTransform>().anchoredPosition = position;
    }

    public void ResetAlpha()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        canvasGroup.alpha = 1f;
    }

    public IEnumerator FadeOut(float duration)
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // 徐々に透明になる
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);

            yield return null;
        }

        canvasGroup.alpha = 0f;
    }

    public void Refresh()
    {
        if (image == null)
            image = GetComponent<Image>();

        if (text == null)
            text = GetComponentInChildren<TMP_Text>();

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (image != null)
        {
            if (assignedSprite != null)
            {
                image.sprite = assignedSprite;
                image.color = Color.white;
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
            }
            else
            {
                image.sprite = null;
                image.color = PuzzleBattleManager.GetElementColor(element);
                image.type = Image.Type.Simple;
            }
        }

        if (text != null)
        {
            // パズル画像を設定した場合は文字を消す。
            // 画像がない場合だけ簡易アイコンを表示する。
            text.text = assignedSprite != null
                ? ""
                : PuzzleBattleManager.GetElementSymbol(element);

            text.color = Color.white;
        }

        if (canvasGroup != null)
            canvasGroup.alpha = 1f;
    }
}
