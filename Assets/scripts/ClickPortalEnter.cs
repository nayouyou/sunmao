using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// 传送门场景切换脚本
/// 触发方式可选：进入触发器自动触发（Auto）/ 鼠标点击触发（Click，要求玩家站在触发器范围内）
/// 前置条件：requiredItems 需已组装完成（调用 BagChecker 对比存档 finishedParts），留空视为无条件
/// 条件不满足时用 GlobalUIRef 弹窗提示缺少的物品，E/Q 关闭
/// 条件满足时可直接传送，或先弹确认对话（requireConfirm），Q 确认后再传送
/// 挂载物体需要 Collider2D 并勾选 Is Trigger
/// </summary>
public class ClickPortalEnter : MonoBehaviour
{
    public enum TriggerMode { Auto, Click }
    private enum DialogMode { None, Missing, Confirm }

    [Header("目标场景名称")]
    public string targetSceneName = "workroom";
    [Header("玩家专用出生点坐标")]
    public Vector2 playerSpawnPos = new Vector2(2.9f, -1.5f);
    [Header("触发方式")]
    [Tooltip("Auto=进入触发器自动触发；Click=鼠标点击触发（需玩家站在触发器范围内）")]
    public TriggerMode triggerMode = TriggerMode.Auto;
    [Header("条件满足后是否需要确认")]
    public bool requireConfirm = false;
    [Header("确认对话文案（留空=空对话）")]
    [TextArea]
    public string confirmText;
    [Header("前置条件（需已组装完成的物品，留空=无条件）")]
    [Tooltip("缺任意一项时不传送，改为提示缺少的物品")]
    public ItemData[] requiredItems;
    [Header("条件不满足时的提示文案（留空=默认列出缺少的物品）")]
    [TextArea]
    public string missingText;

    private Collider2D portalCol;
    private bool isLoadingScene = false;
    private DialogMode _dialogMode = DialogMode.None;

    void Start()
    {
        portalCol = GetComponent<Collider2D>();
        if (portalCol == null)
        {
            Debug.LogError($"传送门 {gameObject.name} 缺少Collider2D组件！");
            enabled = false;
        }
        else if (!portalCol.isTrigger)
        {
            Debug.LogWarning($"传送门 {gameObject.name} 的 Collider2D 未勾选 Is Trigger，自动/点击范围判定会不准确");
        }
    }

    void Update()
    {
        if (_dialogMode == DialogMode.None) return;

        if (Input.GetKeyDown(GameKeys.DialogConfirm))
        {
            DialogMode mode = _dialogMode;
            HideDialog();
            if (mode == DialogMode.Confirm)
                EnterScene(CurrentPlayer());
            return;
        }
        if (Input.GetKeyDown(GameKeys.DialogCancel))
            HideDialog();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (triggerMode != TriggerMode.Auto) return;
        if (isLoadingScene || _dialogMode != DialogMode.None) return;

        GameObject player = CurrentPlayer();
        if (player == null) return;
        if (!IsSameObjectOrChild(other.transform, player.transform)) return;

        TryTrigger(player);
    }

    void OnMouseDown()
    {
        if (triggerMode != TriggerMode.Click) return;
        if (isLoadingScene || _dialogMode != DialogMode.None) return;

        GameObject player = CurrentPlayer();
        if (player == null) return;
        // 点击触发要求玩家先站在触发器范围内
        if (!IsPlayerInside(player)) return;

        TryTrigger(player);
    }

    /// <summary>
    /// 条件判定：缺物品则提示，需要确认则弹对话，否则直接传送
    /// </summary>
    void TryTrigger(GameObject player)
    {
        List<ItemData> missing = BagChecker.GetMissingItems(requiredItems);
        if (missing.Count > 0)
        {
            // 优先使用自定义提示文案；留空时默认列出缺少的物品
            string msg = string.IsNullOrEmpty(missingText)
                ? "无法前往，还缺少：" + JoinItemTitles(missing) + "（E关闭）"
                : missingText;
            ShowDialog(DialogMode.Missing, msg);
            return;
        }

        if (requireConfirm)
        {
            ShowDialog(DialogMode.Confirm, confirmText);
            return;
        }

        EnterScene(player);
    }

    /// <summary>
    /// 切换场景，并把跨场景保留的玩家放到目标出生点
    /// </summary>
    void EnterScene(GameObject player)
    {
        if (player == null || isLoadingScene) return;

        isLoadingScene = true;

        AsyncOperation loadOp = SceneManager.LoadSceneAsync(targetSceneName, LoadSceneMode.Single);
        if (loadOp == null)
        {
            Debug.LogError($"传送门 {gameObject.name}：目标场景 {targetSceneName} 不在Build Settings中！");
            isLoadingScene = false;
            return;
        }

        // 加载期间把玩家移出屏幕，避免切换瞬间残留画面
        player.transform.position = new Vector2(-9999, -9999);

        loadOp.completed += (op) =>
        {
            // 目标场景自带同名玩家分身时销毁，避免出现两个玩家
            GameObject[] allPlayers = Object.FindObjectsOfType<GameObject>(true);
            foreach (GameObject obj in allPlayers)
            {
                if (obj.name == player.name && obj != player)
                {
                    Destroy(obj);
                }
            }
            player.transform.position = playerSpawnPos;
            isLoadingScene = false;
        };
    }

    void ShowDialog(DialogMode mode, string text)
    {
        GlobalUIRef ui = GlobalUIRef.Instance;
        if (ui == null || ui.dialogBox == null || ui.dialogTipText == null)
        {
            Debug.LogError($"传送门 {gameObject.name}：全局弹窗UI缺失，无法提示");
            return;
        }

        _dialogMode = mode;
        ui.dialogBox.SetActive(true);
        Canvas.ForceUpdateCanvases();
        ui.dialogTipText.text = text;
    }

    void HideDialog()
    {
        _dialogMode = DialogMode.None;
        GlobalUIRef ui = GlobalUIRef.Instance;
        if (ui != null && ui.dialogBox != null)
        {
            ui.dialogBox.SetActive(false);
        }
    }

    GameObject CurrentPlayer()
    {
        PlayerManager pm = PlayerManager.Instance;
        return pm != null ? PlayerManager.OnlyPlayer : null;
    }

    static bool IsSameObjectOrChild(Transform t, Transform root)
    {
        return t == root || t.IsChildOf(root);
    }

    /// <summary>
    /// 玩家碰撞体是否与传送门范围重叠（点击触发用）
    /// </summary>
    bool IsPlayerInside(GameObject player)
    {
        if (portalCol == null) return false;

        Collider2D[] hits = new Collider2D[20];
        int count = Physics2D.OverlapCollider(portalCol, new ContactFilter2D(), hits);
        for (int i = 0; i < count; i++)
        {
            if (hits[i] != null && IsSameObjectOrChild(hits[i].transform, player.transform))
                return true;
        }
        return false;
    }

    static string JoinItemTitles(List<ItemData> items)
    {
        List<string> names = new List<string>();
        foreach (ItemData item in items)
        {
            names.Add(item != null ? item.itemTitle : "(未命名物品)");
        }
        return string.Join("、", names);
    }
}
