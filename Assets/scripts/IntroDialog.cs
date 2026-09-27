using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// 开场对话（新游戏走完开场信、进入主场景后自动播放）
/// 逐句显示 lines 中的文本，按 Q / E 推进：上一句消失、下一句出现
/// 最后一句后再按 Q/E 关闭对话框
/// 播放期间冻结玩家移动并屏蔽其它交互，结束后恢复
/// 挂载点：运行时由 OpeningScene 动态挂到 GlobalCanvasRoot（GlobalUIRef.Instance.gameObject）
/// </summary>
public class IntroDialog : MonoBehaviour
{
    /// <summary>跨场景标记：由 OpeningScene 在进入主场景前置 true，本脚本读取后立即清除，保证只弹一次</summary>
    public static bool Pending = false;

    [Header("逐句对话内容（占位，后续替换）")]
    public string[] lines = { "失踪多年的师父怎么突然给我写了一封信？", "还让手艺生疏的我来这个地方修东西", "边修东西边回忆榫卯的结构吧，说不定会有些线索" };

    private GameObject _dialogBox;
    private TMP_Text _dialogText;

    private bool _active;
    private bool _canAdvance;
    private int _index;

    private PlayerMove _player;
    private bool _playerWasEnabled;
    private readonly List<Behaviour> _blocked = new List<Behaviour>();

    private void Start()
    {
        GlobalUIRef ui = GlobalUIRef.Instance;
        if (ui == null)
        {
            Debug.LogError("IntroDialog：GlobalUIRef 未初始化，开场对话不可用");
            enabled = false;
            return;
        }
        _dialogBox = ui.dialogBox;
        _dialogText = ui.dialogTipText;

        if (!Pending)
        {
            enabled = false;
            return;
        }
        Pending = false;

        if (_dialogBox == null || _dialogText == null || lines == null || lines.Length == 0)
        {
            Debug.LogError("IntroDialog：对话框UI或对话内容缺失，跳过开场对话");
            enabled = false;
            return;
        }

        _active = true;
        _index = 0;
        _dialogBox.SetActive(true);
        Canvas.ForceUpdateCanvases();
        _dialogText.text = lines[0];

        StartCoroutine(LockNextFrame());
    }

    private IEnumerator LockNextFrame()
    {
        // 等一帧，确保场景内各组件 Start 执行完毕后再冻结/屏蔽
        yield return null;
        FreezePlayer();
        BlockInteractions();
        _canAdvance = true; // 延迟一帧再接收 Q/E，避免进场按键误跳
    }

    private void Update()
    {
        if (!_active || !_canAdvance) return;

        // Q / E 均视为"继续"
        if (Input.GetKeyDown(GameKeys.DialogConfirm) || Input.GetKeyDown(GameKeys.DialogCancel))
        {
            _index++;
            if (_index >= lines.Length)
                EndDialog();
            else
                _dialogText.text = lines[_index];
        }
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

    private void EndDialog()
    {
        _active = false;
        _canAdvance = false;
        if (_dialogBox != null) _dialogBox.SetActive(false);
        Restore();
        enabled = false;
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
