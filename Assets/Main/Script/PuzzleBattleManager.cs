using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

/// <summary>
/// パズドラ風パズルバトルのメイン管理。
/// 6×6盤面、ドラッグ交換、3個消し、連鎖、敵HP、味方攻撃、スキルを管理します。
/// DOTween不要。
/// </summary>
public class PuzzleBattleManager : MonoBehaviour
{
    [Header("盤面")]
    [SerializeField] private int boardWidth = 6;
    [SerializeField] private int boardHeight = 6;
    [SerializeField] private float orbSize = 82f;
    [SerializeField] private float orbSpacing = 4f;
    [SerializeField] private float moveTime = 0.08f;

    [Header("パズルドラッグ時間")]
    [SerializeField] private float dragTimeLimit = 5f;

    [Header("消去演出")]
    [SerializeField] private float orbFadeOutTime = 0.25f;
    [SerializeField] private float orbFallTime = 0.22f;
    [SerializeField] private float orbSpawnHeight = 180f;

    [Header("UI画像")]
    [Tooltip("背景画像。未設定なら単色背景。")]
    [SerializeField] private Sprite backgroundSprite;

    [Tooltip("火・水・木・光・闇・回復の順。未設定なら簡易色アイコン。")]
    [SerializeField] private Sprite[] orbSprites = new Sprite[6];

    [Tooltip("味方3人の画像。未設定なら属性色のボタン。")]
    [SerializeField] private Sprite[] partySprites = new Sprite[3];

    [Tooltip("敵3体の画像。未設定なら簡易表示。")]
    [SerializeField] private Sprite[] enemySprites = new Sprite[3];

    [Tooltip("6×6盤面の背景画像。未設定でも動作します。")]
    [SerializeField] private Sprite boardBackgroundSprite;

    [Header("戦闘")]
    [SerializeField] private int enemyCount = 3;
    [SerializeField] private int enemyMaxHP = 5000;
    [SerializeField] private float enemyAttackInterval = 4f;

    [Header("味方")]
    [SerializeField] private int partyCount = 3;
    [SerializeField] private int partyMaxHP = 15620;
    [SerializeField] private int skillMaxGauge = 10;

    private int[,] board;
    private PuzzleOrb[,] orbObjects;

    private Canvas canvas;
    private RectTransform root;
    private RectTransform boardRoot;

    private TMP_Text waveText;
    private TMP_Text comboText;
    private TMP_Text damageText;
    private TMP_Text enemyText;
    private TMP_Text playerHpText;
    private TMP_Text statusText;

    private Image playerHpFill;
    private Image enemyHpFill;

    private readonly int[] enemyHP = new int[3];
    private readonly int[] enemyMax = new int[3];
    private readonly int[] enemyTurns = new int[3];

    private readonly int[] skillGauge = new int[3];
    private readonly int[] partyDamage = { 250, 220, 200 };
    private readonly int[] partyElement = { 0, 1, 2 };

    private int playerHP;
    private int comboCount;
    private int totalDamage;
    private float enemyTimer;

    private bool isDragging;
    private int dragStartX;
    private int dragStartY;
    private PuzzleOrb draggedOrb;

    private bool inputLocked;

    // パズドラ風ドラッグ用
    private int currentDragX;
    private int currentDragY;
    private int heldElement;
    private float dragTimer;
    private GameObject dragVisual;
    private readonly List<Vector2Int> dragPath = new List<Vector2Int>();

    // =====================================================
    // Unity
    // =====================================================

    private void Start()
    {
        CreateCanvas();
        CreateBattleUI();
        CreateBoard();
        GenerateBoard();
        RefreshAllUI();
        StartCoroutine(EnemyAttackLoop());
    }

    private void Update()
    {
        if (inputLocked || !isDragging || draggedOrb == null)
            return;

        UpdateDragInput();
    }

    // =====================================================
    // Canvas
    // =====================================================

    private void CreateCanvas()
    {
        GameObject canvasObject = new GameObject("Canvas");
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
        }

        GameObject rootObject = new GameObject("BattleRoot");
        rootObject.transform.SetParent(canvas.transform, false);

        root = rootObject.AddComponent<RectTransform>();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
    }

    // =====================================================
    // Battle UI
    // =====================================================

    private void CreateBattleUI()
    {
        GameObject background = CreatePanel(
            "Background",
            root,
            Vector2.zero,
            new Vector2(1, 1),
            new Vector2(0, 0),
            new Vector2(0, 0),
            new Color(0.035f, 0.045f, 0.07f, 1f)
        );

        if (backgroundSprite != null)
        {
            Image backgroundImage = background.GetComponent<Image>();
            backgroundImage.sprite = backgroundSprite;
            backgroundImage.color = Color.white;
            backgroundImage.type = Image.Type.Simple;
            backgroundImage.preserveAspect = false;
        }

        waveText = CreateText(
            "WAVE",
            root,
            new Vector2(0.5f, 0.96f),
            new Vector2(0.5f, 0.96f),
            new Vector2(-220, -30),
            new Vector2(220, 30),
            30,
            TextAlignmentOptions.Center
        );
        waveText.text = "WAVE 1 / 3";

        CreateEnemyArea();
        CreatePartyArea();
        CreateBoardArea();
        CreateBottomArea();
    }

    private void CreateEnemyArea()
    {
        enemyText = CreateText(
            "Enemies",
            root,
            new Vector2(0.5f, 0.78f),
            new Vector2(0.5f, 0.78f),
            new Vector2(-450, -40),
            new Vector2(450, 80),
            28,
            TextAlignmentOptions.Center
        );

        enemyText.text = "敵  × 3";

        // 敵画像3枠
        for (int i = 0; i < enemyCount; i++)
        {
            float x = 0.22f + i * 0.28f;

            GameObject enemyImageObject = new GameObject("EnemyImage_" + i);
            enemyImageObject.transform.SetParent(root, false);

            RectTransform rect = enemyImageObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(x, 0.79f);
            rect.anchorMax = new Vector2(x, 0.79f);
            rect.offsetMin = new Vector2(-85, -65);
            rect.offsetMax = new Vector2(85, 65);

            Image image = enemyImageObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;

            if (i < enemySprites.Length && enemySprites[i] != null)
            {
                image.sprite = enemySprites[i];
                image.color = Color.white;
            }
            else
            {
                image.color = new Color(0.22f, 0.22f, 0.26f, 1f);
            }
        }

        enemyHpFill = CreateBar(
            "EnemyHP",
            root,
            new Vector2(0.5f, 0.70f),
            new Vector2(0.5f, 0.70f),
            new Vector2(-400, -16),
            new Vector2(400, 16),
            Color.red
        );
    }

    private void CreatePartyArea()
    {
        for (int i = 0; i < partyCount; i++)
        {
            float x = 0.22f + i * 0.28f;

            Button button = CreateButton(
                "Party_" + i,
                root,
                new Vector2(x, 0.60f),
                new Vector2(x, 0.60f),
                new Vector2(-105, -75),
                new Vector2(105, 75)
            );

            int index = i;
            button.onClick.AddListener(() => UseSkill(index));

            TMP_Text text = button.GetComponentInChildren<TMP_Text>();
            text.text = "SKILL";
            text.fontSize = 24;

            Image image = button.GetComponent<Image>();
            image.color = GetElementColor(partyElement[i]);

            // キャラクター画像をボタン内に表示
            if (i < partySprites.Length && partySprites[i] != null)
            {
                GameObject portraitObject = new GameObject("Portrait");
                portraitObject.transform.SetParent(button.transform, false);
                portraitObject.transform.SetAsFirstSibling();

                Image portrait = portraitObject.AddComponent<Image>();
                portrait.sprite = partySprites[i];
                portrait.color = Color.white;
                portrait.preserveAspect = true;
                portrait.raycastTarget = false;

                RectTransform portraitRect = portraitObject.GetComponent<RectTransform>();
                portraitRect.anchorMin = Vector2.zero;
                portraitRect.anchorMax = Vector2.one;
                portraitRect.offsetMin = new Vector2(8, 8);
                portraitRect.offsetMax = new Vector2(-8, -8);
            }

            CreateText(
                "SkillGauge_" + i,
                button.transform as RectTransform,
                new Vector2(0.5f, 0.02f),
                new Vector2(0.5f, 0.02f),
                new Vector2(-90, 0),
                new Vector2(90, 38),
                18,
                TextAlignmentOptions.Center
            ).text = "0 / " + skillMaxGauge;
        }

        playerHpFill = CreateBar(
            "PlayerHP",
            root,
            new Vector2(0.5f, 0.49f),
            new Vector2(0.5f, 0.49f),
            new Vector2(-400, -18),
            new Vector2(400, 18),
            new Color(1f, 0.25f, 0.55f)
        );

        playerHpText = CreateText(
            "PlayerHPText",
            root,
            new Vector2(0.5f, 0.49f),
            new Vector2(0.5f, 0.49f),
            new Vector2(-200, -25),
            new Vector2(200, 25),
            22,
            TextAlignmentOptions.Center
        );
    }

    private void CreateBoardArea()
    {
        GameObject boardObject = new GameObject("PuzzleBoard");
        boardObject.transform.SetParent(root, false);

        boardRoot = boardObject.AddComponent<RectTransform>();
        boardRoot.anchorMin = new Vector2(0.5f, 0.25f);
        boardRoot.anchorMax = new Vector2(0.5f, 0.25f);
        boardRoot.pivot = new Vector2(0.5f, 0.5f);

        float width = boardWidth * orbSize + (boardWidth - 1) * orbSpacing;
        float height = boardHeight * orbSize + (boardHeight - 1) * orbSpacing;

        boardRoot.sizeDelta = new Vector2(width, height);
        boardRoot.anchoredPosition = Vector2.zero;

        // 盤面の背面に画像を置けるようにする
        if (boardBackgroundSprite != null)
        {
            GameObject boardBackground = new GameObject("BoardBackground");
            boardBackground.transform.SetParent(boardRoot, false);
            boardBackground.transform.SetAsFirstSibling();

            RectTransform backgroundRect = boardBackground.AddComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = new Vector2(-8f, -8f);
            backgroundRect.offsetMax = new Vector2(8f, 8f);

            Image backgroundImage = boardBackground.AddComponent<Image>();
            backgroundImage.sprite = boardBackgroundSprite;
            backgroundImage.color = Color.white;
            backgroundImage.preserveAspect = false;
            backgroundImage.raycastTarget = false;
        }
    }

    private void CreateBottomArea()
    {
        comboText = CreateText(
            "Combo",
            root,
            new Vector2(0.28f, 0.08f),
            new Vector2(0.28f, 0.08f),
            new Vector2(-220, -50),
            new Vector2(220, 50),
            42,
            TextAlignmentOptions.Center
        );

        damageText = CreateText(
            "Damage",
            root,
            new Vector2(0.72f, 0.08f),
            new Vector2(0.72f, 0.08f),
            new Vector2(-250, -50),
            new Vector2(250, 50),
            34,
            TextAlignmentOptions.Center
        );

        statusText = CreateText(
            "Status",
            root,
            new Vector2(0.5f, 0.015f),
            new Vector2(0.5f, 0.015f),
            new Vector2(-450, -20),
            new Vector2(450, 20),
            20,
            TextAlignmentOptions.Center
        );
    }

    // =====================================================
    // Board
    // =====================================================

    private void CreateBoard()
    {
        board = new int[boardWidth, boardHeight];
        orbObjects = new PuzzleOrb[boardWidth, boardHeight];

        for (int y = 0; y < boardHeight; y++)
        {
            for (int x = 0; x < boardWidth; x++)
            {
                GameObject orbObject = new GameObject($"Orb_{x}_{y}");
                orbObject.transform.SetParent(boardRoot, false);

                Image image = orbObject.AddComponent<Image>();
                image.raycastTarget = true;

                PuzzleOrb orb = orbObject.AddComponent<PuzzleOrb>();

                RectTransform rect = orbObject.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(orbSize, orbSize);
                rect.anchoredPosition = GetBoardPosition(x, y);

                CreateOrbLabel(orbObject);

                EventTrigger trigger = orbObject.AddComponent<EventTrigger>();

                AddPointerEvent(trigger, EventTriggerType.PointerDown, _ =>
                {
                    if (inputLocked)
                        return;

                    BeginDrag(orb);
                });

                AddPointerEvent(trigger, EventTriggerType.PointerUp, _ =>
                {
                    if (inputLocked)
                        return;

                    EndDrag();
                });

                orbObjects[x, y] = orb;
            }
        }
    }

    private void GenerateBoard()
    {
        for (int y = 0; y < boardHeight; y++)
        {
            for (int x = 0; x < boardWidth; x++)
            {
                int element;

                do
                {
                    element = Random.Range(0, 6);
                    board[x, y] = element;
                }
                while (CreatesMatchAt(x, y));

                orbObjects[x, y].Setup(x, y, element, GetOrbSprite(element));
            }
        }
    }

    private bool CreatesMatchAt(int x, int y)
    {
        int element = board[x, y];

        if (x >= 2 &&
            board[x - 1, y] == element &&
            board[x - 2, y] == element)
            return true;

        if (y >= 2 &&
            board[x, y - 1] == element &&
            board[x, y - 2] == element)
            return true;

        return false;
    }

    private Vector2 GetBoardPosition(int x, int y)
    {
        float width = boardWidth * orbSize + (boardWidth - 1) * orbSpacing;
        float height = boardHeight * orbSize + (boardHeight - 1) * orbSpacing;

        float px = -width / 2f + orbSize / 2f + x * (orbSize + orbSpacing);
        float py = -height / 2f + orbSize / 2f + y * (orbSize + orbSpacing);

        return new Vector2(px, py);
    }

    private void CreateOrbLabel(GameObject parent)
    {
        GameObject labelObject = new GameObject("Symbol");
        labelObject.transform.SetParent(parent.transform, false);

        TMP_Text text = labelObject.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 42;
        text.fontStyle = FontStyles.Bold;

        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // =====================================================
    // Drag
    // =====================================================

    private void BeginDrag(PuzzleOrb orb)
    {
        if (inputLocked || isDragging)
            return;

        isDragging = true;
        draggedOrb = orb;

        dragStartX = orb.x;
        dragStartY = orb.y;
        currentDragX = orb.x;
        currentDragY = orb.y;

        // クリックしたピースを手に持つ
        heldElement = board[currentDragX, currentDragY];
        dragTimer = dragTimeLimit;

        dragPath.Clear();
        dragPath.Add(new Vector2Int(currentDragX, currentDragY));

        // 実際のOrbは一旦盤面から外し、マウス位置に専用のOrbを表示します。
        // これにより、他のOrbが元の場所へ滑らかにずれるようになります。
        orbObjects[currentDragX, currentDragY] = null;

        CanvasGroup draggedGroup = draggedOrb.GetComponent<CanvasGroup>();
        if (draggedGroup == null)
            draggedGroup = draggedOrb.gameObject.AddComponent<CanvasGroup>();

        draggedGroup.alpha = 0f;

        CreateDragVisual(heldElement);
        UpdateDragVisualPosition();

        statusText.text = $"残り {dragTimer:0.0} 秒";
    }

    private void UpdateDragInput()
    {
        if (Mouse.current == null)
            return;

        // クリックしている間だけ時間を減らす
        dragTimer -= Time.unscaledDeltaTime;

        if (dragTimer <= 0f)
        {
            dragTimer = 0f;
            EndDrag();
            return;
        }

        UpdateDragVisualPosition();
        statusText.text = $"残り {dragTimer:0.0} 秒";

        // マウスを離した瞬間に確定
        if (!Mouse.current.leftButton.isPressed)
        {
            EndDrag();
            return;
        }

        Vector2 mousePosition = Mouse.current.position.ReadValue();

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            boardRoot,
            mousePosition,
            null,
            out Vector2 local
        );

        int x = Mathf.RoundToInt(
            (local.x + boardRoot.rect.width / 2f - orbSize / 2f)
            / (orbSize + orbSpacing)
        );

        int y = Mathf.RoundToInt(
            (local.y + boardRoot.rect.height / 2f - orbSize / 2f)
            / (orbSize + orbSpacing)
        );

        if (x < 0 || x >= boardWidth || y < 0 || y >= boardHeight)
            return;

        int dx = x - currentDragX;
        int dy = y - currentDragY;

        // 隣のマスに入った瞬間、持っているピースと交換する。
        // これをクリック中ずっと繰り返すことで、自由に盤面を動かせる。
        if (Mathf.Abs(dx) + Mathf.Abs(dy) == 1)
        {
            MoveHeldOrbTo(x, y);
        }
        else if (Mathf.Abs(dx) > 1 || Mathf.Abs(dy) > 1)
        {
            // 1フレームで複数マス飛んだ場合も1マスだけ進める
            int stepX = 0;
            int stepY = 0;

            if (Mathf.Abs(dx) >= Mathf.Abs(dy))
                stepX = Mathf.Clamp(dx, -1, 1);
            else
                stepY = Mathf.Clamp(dy, -1, 1);

            MoveHeldOrbTo(currentDragX + stepX, currentDragY + stepY);
        }
    }

    private void MoveHeldOrbTo(int targetX, int targetY)
    {
        if (targetX < 0 || targetX >= boardWidth ||
            targetY < 0 || targetY >= boardHeight)
            return;

        // 移動先にいるOrbを取得します。
        // 持っているOrbが移動した分、そのOrbは元いたマスへずれます。
        PuzzleOrb targetOrb = orbObjects[targetX, targetY];

        if (targetOrb == null)
            return;

        int targetElement = board[targetX, targetY];

        // データ上も入れ替える。
        board[currentDragX, currentDragY] = targetElement;
        board[targetX, targetY] = heldElement;

        // 見た目も「移動先のOrbが1マスずれる」ようにする。
        orbObjects[currentDragX, currentDragY] = targetOrb;
        orbObjects[targetX, targetY] = null;

        targetOrb.x = currentDragX;
        targetOrb.y = currentDragY;
        targetOrb.SetElement(targetElement, GetOrbSprite(targetElement));
        targetOrb.SetPositionImmediate(targetOrb.GetComponent<RectTransform>().anchoredPosition);
        targetOrb.MoveTo(GetBoardPosition(currentDragX, currentDragY), moveTime);

        // 持っているOrbの属性は変わりません。
        // ここでtargetElementを代入すると、画像と属性がズレます。

        currentDragX = targetX;
        currentDragY = targetY;

        dragPath.Add(new Vector2Int(currentDragX, currentDragY));
    }

    private void UpdateDragVisualPosition()
    {
        if (dragVisual == null || Mouse.current == null)
            return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            boardRoot,
            Mouse.current.position.ReadValue(),
            null,
            out Vector2 local
        );

        RectTransform rect = dragVisual.GetComponent<RectTransform>();
        rect.anchoredPosition = local;
    }

    private void CreateDragVisual(int element)
    {
        DestroyDragVisual();

        dragVisual = new GameObject("DraggedOrb");
        dragVisual.transform.SetParent(boardRoot, false);

        RectTransform rect = dragVisual.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(orbSize, orbSize);

        Image image = dragVisual.AddComponent<Image>();
        Sprite dragSprite = GetOrbSprite(element);

        if (dragSprite != null)
        {
            image.sprite = dragSprite;
            image.color = Color.white;
            image.preserveAspect = true;
        }
        else
        {
            image.color = GetElementColor(element);
        }

        image.raycastTarget = false;

        GameObject labelObject = new GameObject("Symbol");
        labelObject.transform.SetParent(dragVisual.transform, false);

        TMP_Text label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = GetOrbSprite(element) != null ? "" : GetElementSymbol(element);
        label.fontSize = 42;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        dragVisual.transform.SetAsLastSibling();
    }

    private void DestroyDragVisual()
    {
        if (dragVisual != null)
        {
            Destroy(dragVisual);
            dragVisual = null;
        }
    }

    private void EndDrag()
    {
        if (!isDragging)
            return;

        isDragging = false;

        // 手に持っていた元のOrbを最後にいたマスへ戻します。
        board[currentDragX, currentDragY] = heldElement;

        draggedOrb.x = currentDragX;
        draggedOrb.y = currentDragY;
        draggedOrb.SetElement(heldElement, GetOrbSprite(heldElement));

        CanvasGroup draggedGroup = draggedOrb.GetComponent<CanvasGroup>();
        if (draggedGroup == null)
            draggedGroup = draggedOrb.gameObject.AddComponent<CanvasGroup>();

        draggedGroup.alpha = 1f;
        draggedOrb.SetPositionImmediate(GetBoardPosition(currentDragX, currentDragY));
        orbObjects[currentDragX, currentDragY] = draggedOrb;

        DestroyDragVisual();
        RefreshBoardVisuals();

        int endX = currentDragX;
        int endY = currentDragY;

        draggedOrb = null;

        StartCoroutine(ResolveAfterDrag(endX, endY));
    }

    private void RestoreBoardAfterInvalidDrag()
    {
        // dragPath は「開始位置 → 通過した位置」の順。
        // 最後の位置から開始位置まで逆に交換すると、ドラッグ前に戻る。
        for (int i = dragPath.Count - 1; i > 0; i--)
        {
            Vector2Int from = dragPath[i];
            Vector2Int to = dragPath[i - 1];

            int temp = board[from.x, from.y];
            board[from.x, from.y] = board[to.x, to.y];
            board[to.x, to.y] = temp;

            // 見た目のOrbも同じ順番で戻します。
            PuzzleOrb tempOrb = orbObjects[from.x, from.y];
            orbObjects[from.x, from.y] = orbObjects[to.x, to.y];
            orbObjects[to.x, to.y] = tempOrb;
        }

        // 最初に持っていたOrbを開始地点へ戻す。
        if (draggedOrb != null)
        {
            orbObjects[dragStartX, dragStartY] = draggedOrb;
            draggedOrb.x = dragStartX;
            draggedOrb.y = dragStartY;
            draggedOrb.SetElement(
                board[dragStartX, dragStartY],
                GetOrbSprite(board[dragStartX, dragStartY])
            );

            CanvasGroup group = draggedOrb.GetComponent<CanvasGroup>();
            if (group == null)
                group = draggedOrb.gameObject.AddComponent<CanvasGroup>();

            group.alpha = 1f;
            draggedOrb.SetPositionImmediate(GetBoardPosition(dragStartX, dragStartY));
        }

        dragPath.Clear();
    }

    private IEnumerator ResolveAfterDrag(int endX, int endY)
    {
        inputLocked = true;

        comboCount = 0;
        totalDamage = 0;

        HashSet<Vector2Int> matches = FindMatches();

        // 3個揃っていない場合は、ドラッグ前の盤面に戻す。
        // ドラッグ中に通過したマスを逆順に戻す代わりに、履歴を使う方式に変更する場合はここを拡張できます。
        if (matches.Count == 0)
        {
            // ドラッグした経路を逆順にたどって元の盤面へ戻す
            RestoreBoardAfterInvalidDrag();
            RefreshBoardVisuals();

            statusText.text = "3個以上そろえてください";
            inputLocked = false;
            yield break;
        }

        while (matches.Count > 0)
        {
            comboCount++;

            Dictionary<int, int> elementCounts = new Dictionary<int, int>();

            foreach (Vector2Int pos in matches)
            {
                int element = board[pos.x, pos.y];

                if (!elementCounts.ContainsKey(element))
                    elementCounts[element] = 0;

                elementCounts[element]++;
            }

            foreach (KeyValuePair<int, int> pair in elementCounts)
            {
                int element = pair.Key;
                int count = pair.Value;

                if (element >= 0 && element <= 2)
                {
                    int partyIndex = element;

                    int damage = Mathf.RoundToInt(
                        partyDamage[partyIndex] *
                        count *
                        (1f + (comboCount - 1) * 0.5f)
                    );

                    DealDamageToEnemies(damage);
                    skillGauge[partyIndex] =
                        Mathf.Min(skillMaxGauge, skillGauge[partyIndex] + count);

                    totalDamage += damage;
                }
            }

            // 揃ったOrbが透明になって消える
            yield return StartCoroutine(FadeAndClearMatches(matches));

            // 消えた場所を詰めて、空いた場所へ上からOrbを落とす
            yield return StartCoroutine(FallAndRefillBoardAnimated());

            matches = FindMatches();
        }

        statusText.text = IsAllEnemiesDead()
            ? "WIN！"
            : $"{comboCount} COMBO！";

        RefreshAllUI();
        dragPath.Clear();
        inputLocked = false;
    }

    // =====================================================
    // Match / Combo
    // =====================================================

    private IEnumerator SwapAndResolve(int x1, int y1, int x2, int y2)
    {
        inputLocked = true;

        SwapBoard(x1, y1, x2, y2);
        RefreshBoardVisuals();

        yield return new WaitForSeconds(moveTime);

        comboCount = 0;
        totalDamage = 0;

        bool resolvedAny = false;

        while (true)
        {
            HashSet<Vector2Int> matches = FindMatches();

            if (matches.Count == 0)
                break;

            resolvedAny = true;
            comboCount++;

            Dictionary<int, int> elementCounts = new Dictionary<int, int>();

            foreach (Vector2Int pos in matches)
            {
                int element = board[pos.x, pos.y];

                if (!elementCounts.ContainsKey(element))
                    elementCounts[element] = 0;

                elementCounts[element]++;
            }

            foreach (KeyValuePair<int, int> pair in elementCounts)
            {
                int element = pair.Key;
                int count = pair.Value;

                if (element >= 0 && element <= 2)
                {
                    int partyIndex = element;

                    int damage = Mathf.RoundToInt(
                        partyDamage[partyIndex] *
                        count *
                        (1f + (comboCount - 1) * 0.5f)
                    );

                    DealDamageToEnemies(damage);
                    skillGauge[partyIndex] =
                        Mathf.Min(skillMaxGauge, skillGauge[partyIndex] + count);

                    totalDamage += damage;
                }
            }

            yield return StartCoroutine(FadeAndClearMatches(matches));
            yield return new WaitForSeconds(0.15f);

            FallBoard();
            RefreshBoardVisuals();

            yield return new WaitForSeconds(0.15f);

            RefillBoard();
            RefreshBoardVisuals();

            yield return new WaitForSeconds(0.15f);
        }

        if (!resolvedAny)
        {
            // 3個揃っていない場合は元に戻す
            SwapBoard(x1, y1, x2, y2);
            RefreshBoardVisuals();

            statusText.text = "3個以上そろえてください";
        }
        else
        {
            statusText.text = $"{comboCount} COMBO！";

            if (IsAllEnemiesDead())
            {
                statusText.text = "WIN！";
            }
        }

        RefreshAllUI();

        inputLocked = false;
    }

    private HashSet<Vector2Int> FindMatches()
    {
        HashSet<Vector2Int> result = new HashSet<Vector2Int>();

        // 横
        for (int y = 0; y < boardHeight; y++)
        {
            int runStart = 0;

            while (runStart < boardWidth)
            {
                int element = board[runStart, y];
                int runEnd = runStart + 1;

                while (runEnd < boardWidth &&
                       board[runEnd, y] == element)
                {
                    runEnd++;
                }

                if (element >= 0 && runEnd - runStart >= 3)
                {
                    for (int x = runStart; x < runEnd; x++)
                        result.Add(new Vector2Int(x, y));
                }

                runStart = runEnd;
            }
        }

        // 縦
        for (int x = 0; x < boardWidth; x++)
        {
            int runStart = 0;

            while (runStart < boardHeight)
            {
                int element = board[x, runStart];
                int runEnd = runStart + 1;

                while (runEnd < boardHeight &&
                       board[x, runEnd] == element)
                {
                    runEnd++;
                }

                if (element >= 0 && runEnd - runStart >= 3)
                {
                    for (int y = runStart; y < runEnd; y++)
                        result.Add(new Vector2Int(x, y));
                }

                runStart = runEnd;
            }
        }

        return result;
    }

    private IEnumerator FadeAndClearMatches(HashSet<Vector2Int> matches)
    {
        // 揃ったOrbを先に取得しておく。
        // 消去後にorbObjectsへ古いOrbが残ると、落下・再補充時に
        // 古いOrbを参照してしまい、見た目だけ残ったり再び表示されたりするため。
        List<PuzzleOrb> matchedOrbs = new List<PuzzleOrb>();

        foreach (Vector2Int pos in matches)
        {
            PuzzleOrb orb = orbObjects[pos.x, pos.y];

            if (orb != null)
            {
                matchedOrbs.Add(orb);
                StartCoroutine(orb.FadeOut(orbFadeOutTime));
            }
        }

        // すべてのパズルが透明になるまで待つ。
        if (orbFadeOutTime > 0f)
            yield return new WaitForSeconds(orbFadeOutTime);

        // 盤面データとOrb参照を同時に完全クリアする。
        foreach (Vector2Int pos in matches)
        {
            board[pos.x, pos.y] = -1;
            orbObjects[pos.x, pos.y] = null;
        }

        // 消えたOrb本体も確実に削除する。
        // これをしないと透明なImageがRaycastを邪魔する場合がある。
        for (int i = 0; i < matchedOrbs.Count; i++)
        {
            if (matchedOrbs[i] != null)
                Destroy(matchedOrbs[i].gameObject);
        }

        // Destroyはフレーム末尾で実行されるため、1フレーム待って
        // 古いOrbが完全に盤面から抜けた状態にする。
        yield return null;
    }


    // =====================================================
    // パズドラ風：消去後の落下アニメーション
    // =====================================================

    private IEnumerator FallAndRefillBoardAnimated()
    {
        List<PuzzleOrb> movingOrbs = new List<PuzzleOrb>();
        List<Vector2> startPositions = new List<Vector2>();
        List<Vector2> targetPositions = new List<Vector2>();

        for (int x = 0; x < boardWidth; x++)
        {
            // =====================================================
            // この列を一度完全に整理する。
            // 古いorbObjects参照を残さないことが重要。
            // =====================================================
            List<PuzzleOrb> survivingOrbs = new List<PuzzleOrb>();
            List<int> survivingElements = new List<int>();

            for (int y = 0; y < boardHeight; y++)
            {
                if (board[x, y] < 0)
                    continue;

                PuzzleOrb orb = orbObjects[x, y];

                // 盤面データが残っているのにOrb本体がない場合も、
                // 新しいOrbとして補充できるようにする。
                if (orb != null)
                {
                    survivingOrbs.Add(orb);
                    survivingElements.Add(board[x, y]);
                }
            }

            // まず列全体を空にする。
            for (int y = 0; y < boardHeight; y++)
            {
                board[x, y] = -1;
                orbObjects[x, y] = null;
            }

            // =====================================================
            // 既存Orbを下へ詰める
            // =====================================================
            int writeY = 0;

            for (int i = 0; i < survivingOrbs.Count; i++)
            {
                PuzzleOrb orb = survivingOrbs[i];
                int element = survivingElements[i];

                int oldY = orb.y;
                int targetY = writeY;

                board[x, targetY] = element;
                orbObjects[x, targetY] = orb;

                orb.x = x;
                orb.y = targetY;
                orb.SetElement(element, GetOrbSprite(element));

                if (oldY != targetY)
                {
                    Vector2 start = orb.GetComponent<RectTransform>().anchoredPosition;
                    Vector2 target = GetBoardPosition(x, targetY);

                    movingOrbs.Add(orb);
                    startPositions.Add(start);
                    targetPositions.Add(target);
                }
                else
                {
                    // 移動しないOrbも座標を確実に正規化。
                    orb.SetPositionImmediate(GetBoardPosition(x, targetY));
                }

                writeY++;
            }

            // =====================================================
            // 上側の空白へ新しいOrbを生成
            // =====================================================
            for (int y = writeY; y < boardHeight; y++)
            {
                int element = Random.Range(0, 6);
                board[x, y] = element;

                PuzzleOrb newOrb = CreateOrbForBoard(x, y, element);
                orbObjects[x, y] = newOrb;

                Vector2 target = GetBoardPosition(x, y);
                Vector2 start = target + Vector2.up * orbSpawnHeight;

                RectTransform rect = newOrb.GetComponent<RectTransform>();
                rect.anchoredPosition = start;

                movingOrbs.Add(newOrb);
                startPositions.Add(start);
                targetPositions.Add(target);
            }
        }

        // =====================================================
        // すべてのOrbを同時に落とす
        // =====================================================
        float elapsed = 0f;

        if (orbFallTime > 0f)
        {
            while (elapsed < orbFallTime)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / orbFallTime);

                // 上からスッと加速して落ちる
                float eased = 1f - Mathf.Pow(1f - t, 2f);

                for (int i = 0; i < movingOrbs.Count; i++)
                {
                    if (movingOrbs[i] == null)
                        continue;

                    RectTransform rect =
                        movingOrbs[i].GetComponent<RectTransform>();

                    rect.anchoredPosition =
                        Vector2.Lerp(
                            startPositions[i],
                            targetPositions[i],
                            eased
                        );
                }

                yield return null;
            }
        }

        // 最終位置を確定
        for (int i = 0; i < movingOrbs.Count; i++)
        {
            if (movingOrbs[i] == null)
                continue;

            movingOrbs[i].GetComponent<RectTransform>().anchoredPosition =
                targetPositions[i];
        }

        // ここではRefreshBoardVisualsを呼ばない。
        // RefreshはOrbの透明度を1に戻すため、消去演出中の古いOrbを
        // 誤って復活させないようにする。

        yield return null;
    }

    private PuzzleOrb CreateOrbForBoard(int x, int y, int element)
    {
        GameObject orbObject = new GameObject($"Orb_{x}_{y}");
        orbObject.transform.SetParent(boardRoot, false);

        Image image = orbObject.AddComponent<Image>();
        image.raycastTarget = true;

        PuzzleOrb orb = orbObject.AddComponent<PuzzleOrb>();

        RectTransform rect = orbObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(orbSize, orbSize);

        CreateOrbLabel(orbObject);

        Sprite sprite = null;

        if (orbSprites != null &&
            element >= 0 &&
            element < orbSprites.Length)
        {
            sprite = orbSprites[element];
        }

        orb.Setup(x, y, element, sprite);

        EventTrigger trigger = orbObject.AddComponent<EventTrigger>();

        AddPointerEvent(
            trigger,
            EventTriggerType.PointerDown,
            _ =>
            {
                if (!inputLocked)
                    BeginDrag(orb);
            }
        );

        AddPointerEvent(
            trigger,
            EventTriggerType.PointerUp,
            _ =>
            {
                if (!inputLocked)
                    EndDrag();
            }
        );

        return orb;
    }

    private void FallBoard()
    {
        for (int x = 0; x < boardWidth; x++)
        {
            int writeY = 0;

            for (int y = 0; y < boardHeight; y++)
            {
                if (board[x, y] < 0)
                    continue;

                board[x, writeY] = board[x, y];
                writeY++;
            }

            while (writeY < boardHeight)
            {
                board[x, writeY] = -1;
                writeY++;
            }
        }
    }

    private void RefillBoard()
    {
        for (int x = 0; x < boardWidth; x++)
        {
            for (int y = 0; y < boardHeight; y++)
            {
                if (board[x, y] < 0)
                    board[x, y] = Random.Range(0, 6);
            }
        }
    }

    private void SwapBoard(int x1, int y1, int x2, int y2)
    {
        int temp = board[x1, y1];
        board[x1, y1] = board[x2, y2];
        board[x2, y2] = temp;
    }

    private void RefreshBoardVisuals()
    {
        for (int y = 0; y < boardHeight; y++)
        {
            for (int x = 0; x < boardWidth; x++)
            {
                PuzzleOrb orb = orbObjects[x, y];

                if (orb == null)
                    continue;

                orb.x = x;
                orb.y = y;
                orb.SetElement(board[x, y], GetOrbSprite(board[x, y]));
                orb.SetPositionImmediate(GetBoardPosition(x, y));
            }
        }
    }

    // =====================================================
    // Battle
    // =====================================================

    private void DealDamageToEnemies(int damage)
    {
        int aliveIndex = GetFirstAliveEnemy();

        if (aliveIndex < 0)
            return;

        enemyHP[aliveIndex] =
            Mathf.Max(0, enemyHP[aliveIndex] - damage);
    }

    private int GetFirstAliveEnemy()
    {
        for (int i = 0; i < enemyCount; i++)
        {
            if (enemyHP[i] > 0)
                return i;
        }

        return -1;
    }

    private bool IsAllEnemiesDead()
    {
        return GetFirstAliveEnemy() < 0;
    }

    private IEnumerator EnemyAttackLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(enemyAttackInterval);

            if (IsAllEnemiesDead())
                continue;

            int aliveCount = 0;

            for (int i = 0; i < enemyCount; i++)
            {
                if (enemyHP[i] > 0)
                    aliveCount++;
            }

            int damage = aliveCount * 120;

            playerHP = Mathf.Max(0, playerHP - damage);

            if (playerHP <= 0)
            {
                statusText.text = "LOSE...";
                inputLocked = true;
            }

            RefreshAllUI();
        }
    }

    // =====================================================
    // Skill
    // =====================================================

    private void UseSkill(int index)
    {
        if (inputLocked)
            return;

        if (skillGauge[index] < skillMaxGauge)
        {
            statusText.text = "スキルゲージが足りません";
            return;
        }

        skillGauge[index] = 0;

        if (index == 0)
        {
            DealDamageToEnemies(1200);
            statusText.text = "火属性スキル！ 大ダメージ！";
        }
        else if (index == 1)
        {
            playerHP = Mathf.Min(partyMaxHP, playerHP + 2500);
            statusText.text = "水属性スキル！ HP回復！";
        }
        else
        {
            DealDamageToEnemies(800);
            statusText.text = "木属性スキル！ 攻撃！";
        }

        RefreshAllUI();
    }

    // =====================================================
    // UI Refresh
    // =====================================================

    private void RefreshAllUI()
    {
        if (enemyMax[0] <= 0)
        {
            for (int i = 0; i < enemyCount; i++)
            {
                enemyMax[i] = enemyMaxHP;
                enemyHP[i] = enemyMaxHP;
            }

            playerHP = partyMaxHP;
        }

        int totalEnemyHP = 0;
        int totalEnemyMax = 0;

        for (int i = 0; i < enemyCount; i++)
        {
            totalEnemyHP += enemyHP[i];
            totalEnemyMax += enemyMax[i];
        }

        if (enemyHpFill != null)
            enemyHpFill.fillAmount =
                totalEnemyMax <= 0 ? 0 : (float)totalEnemyHP / totalEnemyMax;

        if (playerHpFill != null)
            playerHpFill.fillAmount =
                (float)playerHP / partyMaxHP;

        if (playerHpText != null)
            playerHpText.text =
                $"{playerHP} / {partyMaxHP}";

        if (comboText != null)
            comboText.text =
                comboCount > 0
                ? $"{comboCount} COMBO!\nx{1f + (comboCount - 1) * 0.5f:0.0}"
                : "0 COMBO";

        if (damageText != null)
            damageText.text =
                $"TOTAL DAMAGE\n{totalDamage:N0}";

        for (int i = 0; i < partyCount; i++)
        {
            Transform party = root.Find($"Party_{i}");

            if (party == null)
                continue;

            TMP_Text gaugeText =
                party.GetComponentInChildren<TMP_Text>();

            if (gaugeText != null)
                gaugeText.text =
                    $"{skillGauge[i]} / {skillMaxGauge}";
        }
    }

    // =====================================================
    // UI Helpers
    // =====================================================

    private GameObject CreatePanel(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax,
        Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Image image = obj.AddComponent<Image>();
        image.color = color;

        return obj;
    }

    private TMP_Text CreateText(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        TMP_Text text = obj.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;

        return text;
    }

    private Image CreateBar(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax,
        Color color)
    {
        GameObject background = CreatePanel(
            name + "_Background",
            parent,
            anchorMin,
            anchorMax,
            offsetMin,
            offsetMax,
            new Color(0.08f, 0.08f, 0.08f, 1f)
        );

        GameObject fillObject = new GameObject(name + "_Fill");
        fillObject.transform.SetParent(background.transform, false);

        RectTransform rect = fillObject.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image fill = fillObject.AddComponent<Image>();
        fill.color = color;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;

        return fill;
    }

    private Button CreateButton(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Image image = obj.AddComponent<Image>();
        image.color = new Color(0.15f, 0.15f, 0.18f, 1f);

        Button button = obj.AddComponent<Button>();
        button.targetGraphic = image;

        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(obj.transform, false);

        TMP_Text text = textObject.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 22;
        text.color = Color.white;

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return button;
    }

    private void AddPointerEvent(
        EventTrigger trigger,
        EventTriggerType eventType,
        System.Action<BaseEventData> action)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry();
        entry.eventID = eventType;
        entry.callback.AddListener(data => action(data));
        trigger.triggers.Add(entry);
    }

    // =====================================================
    // Element
    // =====================================================

    private Sprite GetOrbSprite(int element)
    {
        if (orbSprites == null || element < 0 || element >= orbSprites.Length)
            return null;

        return orbSprites[element];
    }

    public static Color GetElementColor(int element)
    {
        switch (element)
        {
            case 0: return new Color(1f, 0.20f, 0.12f); // 火
            case 1: return new Color(0.10f, 0.50f, 1f); // 水
            case 2: return new Color(0.20f, 0.85f, 0.25f); // 木
            case 3: return new Color(1f, 0.75f, 0.15f); // 光
            case 4: return new Color(0.55f, 0.20f, 0.90f); // 闇
            case 5: return new Color(1f, 0.35f, 0.65f); // 回復
            default: return Color.gray;
        }
    }

    public static string GetElementSymbol(int element)
    {
        switch (element)
        {
            case 0: return "??";
            case 1: return "??";
            case 2: return "??";
            case 3: return "?";
            case 4: return "?";
            case 5: return "?";
            default: return "?";
        }
    }
}
