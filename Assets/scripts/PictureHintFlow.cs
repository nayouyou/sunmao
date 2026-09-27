using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// 画像提示流程（直榫 / 肩榫 / 全透燕尾榫 三件齐后触发）
/// 由 ClickToPlayAnimation 在“获得物品”提示关闭后通知（与 ItemUnlockFlow 同一时机）：
///   requiredItems 全部拥有且从未弹过时，弹出 dialogText 提示
///   按 Q / E 关闭对话，关闭瞬间写入存档标记（interactedIds），读档进入游戏不再弹出
/// 弹出期间冻结玩家移动并屏蔽其它交互，关闭后恢复
/// 挂载点：GlobalCanvasRoot（与 GlobalUIRef 同一物体，随全局跨场景保留）
/// </summary>
public class PictureHintFlow : MonoBehaviour
{
    public static PictureHintFlow Instance;

    [Header("触发条件（需全部已拥有）")]
    public ItemData[] requiredItems;

    [Header("提示文本")]
    [TextArea]
    public string dialogText = "画像后面好像有什么动静";

    [Header("存档标记ID（用于只弹一次）")]
    public string hintId = "picture_hint";

    private GameObject _dialogBox;
    private TMP_Text _dialogText;

    private bool _isShowing;
    private bool _canClose;
    private bool _waiting;

    private PlayerMove _player;
    private bool _playerWasEnabled;
    private readonly List<Behaviour> _blocked = new List<Behaviour>();

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(this);
    }

    private void Start()
    {
        GlobalUIRef ui = GlobalUIRef.Instance;
        if (ui == null)
        {
            Debug.LogError("PictureHintFlow：GlobalUIRef 未初始化，画像提示不可用");
            enabled = false;
            return;
        }
        _dialogBox = ui.dialogBox;
        _dialogText = ui.dialogTipText;
    }

    /// <summary>由 ClickToPlayAnimation 在“获得物品”提示关闭后调用</summary>
    public void NotifyItemObtained(ItemData obtained)
    {
        if (_isShowing || _waiting) return;
        if (GlobalInteractRecord.Instance.IsInteracted(hintId)) return;
        if (!BagChecker.AreRequirementsMet(requiredItems)) return;
        if (_dialogBox == null || _dialogText == null)
        {
            Debug.LogError("PictureHintFlow：对话框UI缺失，无法显示画像提示");
            return;
        }

        // 全局对话框正被其它对话占用时（流程上一般不发生），等它关闭后再弹
        if (_dialogBox.activeSelf)
        {
            _waiting = true;
            StartCoroutine(WaitThenShow());
            return;
        }

        ShowHint();
    }

    private IEnumerator WaitThenShow()
    {
        while (_dialogBox != null && _dialogBox.activeSelf)
            yield return null;

        _waiting = false;
        if (!_isShowing && !GlobalInteractRecord.Instance.IsInteracted(hintId))
            ShowHint();
    }

    private void ShowHint()
    {
        _isShowing = true;
        _canClose = false;
        _dialogBox.SetActive(true);
        Canvas.ForceUpdateCanvases();
        _dialogText.text = dialogText;

        StartCoroutine(LockNextFrame());
    }

    private IEnumerator LockNextFrame()
    {
        // 等一帧，确保场景内各组件 Start 执行完毕后再冻结/屏蔽
        yield return null;
        FreezePlayer();
        BlockInteractions();
        _canClose = true; // 延迟一帧再接收 Q/E，避免误关
    }

    private void Update()
    {
        if (!_isShowing || !_canClose) return;

        // Q / E 均视为关闭
        if (Input.GetKeyDown(GameKeys.DialogConfirm) || Input.GetKeyDown(GameKeys.DialogCancel))
            CloseHint();
    }

    private void CloseHint()
    {
        _isShowing = false;
        _canClose = false;
        if (_dialogBox != null) _dialogBox.SetActive(false);
        GlobalInteractRecord.Instance.MarkInteracted(hintId);
        Restore();
    }

    private void FreezePlayer()
    {
        _player = FindFirstObjectByType<PlayerMove>();
        if (_player == null) return;
        _playerWasEnabled = _player.enabled;
        _player.enabled = false;
        Rigidbody2D rb = _player.GetComponent<Rigidbody2D>();
        if (rb != null) rb.velocity = Vector2.zero;
    }

    private void BlockInteractions()
    {
        Block<Sign>();
        Block<SaveGameMenu>();
        Block<BagShowVideoManager>();
        Block<ClickToPlayAnimation>();
        Block<ClickPortalEnter>();
        Block<ItemUnlockFlow>();
        Block<EndingFlow>();
    }

    private void Block<T>() where T : Behaviour
    {
        T[] found = FindObjectsByType<T>(FindObjectsSortMode.None);
        foreach (T c in found)
        {
            if (c == null || !c.enabled) continue;
            c.enabled = false;
            _blocked.Add(c);
        }
    }

    private void Restore()
    {
        foreach (Behaviour c in _blocked)
            if (c != null) c.enabled = true;
        _blocked.Clear();

        if (_player != null && _playerWasEnabled)
            _player.enabled = true;
        _player = null;
    }

    private void OnDestroy()
    {
        Restore();
    }
}
