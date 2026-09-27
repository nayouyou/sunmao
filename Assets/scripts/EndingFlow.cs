using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// 结局流程（获得“榫卯匠人之证”后）
/// 由 ClickToPlayAnimation 在“获得物品”提示关闭后通知（与 ItemUnlockFlow 同一时机）：
///   物品与 endingItem 一致且未触发过时，逐句显示 lines，按 Q / E 推进，
///   最后一句后再按 Q/E 进入结束场景
/// 播放期间冻结玩家移动并屏蔽其它交互，结束后恢复
/// 挂载点：GlobalCanvasRoot（与 GlobalUIRef 同一物体，随全局跨场景保留）
/// </summary>
public class EndingFlow : MonoBehaviour
{
    public static EndingFlow Instance;

    [Header("触发物品（榫卯匠人之证）")]
    public ItemData endingItem;

    [Header("结局对话内容")]
    public string[] lines = {
        "这便是师父让我来这个小院修复家具与亭子的原因吧",
        "我一定会将榫卯技艺发扬光大，不辜负师父的栽培",
        "该出发继续寻找师父了"
    };

    [Header("结束后进入的场景")]
    public string endingSceneName = "ending";

    private GameObject _dialogBox;
    private TMP_Text _dialogText;

    private bool _active;
    private bool _canAdvance;
    private bool _done;
    private int _index;

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
            Debug.LogError("EndingFlow：GlobalUIRef 未初始化，结局流程不可用");
            enabled = false;
            return;
        }
        _dialogBox = ui.dialogBox;
        _dialogText = ui.dialogTipText;
    }

    /// <summary>由 ClickToPlayAnimation 在“获得物品”提示关闭后调用</summary>
    public void NotifyItemObtained(ItemData obtained)
    {
        if (_done || _active) return;
        if (obtained == null || endingItem == null || obtained != endingItem) return;

        _done = true;

        if (_dialogBox == null || _dialogText == null || lines == null || lines.Length == 0)
        {
            Debug.LogError("EndingFlow：对话框UI或对话内容缺失，直接进入结束场景");
            LoadEndingScene();
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
        _canAdvance = true; // 延迟一帧再接收 Q/E，避免误跳
    }

    private void Update()
    {
        if (!_active || !_canAdvance) return;

        // Q / E 均视为“继续”
        if (Input.GetKeyDown(GameKeys.DialogConfirm) || Input.GetKeyDown(GameKeys.DialogCancel))
        {
            _index++;
            if (_index >= lines.Length)
                LoadEndingScene();
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

    private void LoadEndingScene()
    {
        _active = false;
        _canAdvance = false;
        if (_dialogBox != null) _dialogBox.SetActive(false);
        Restore();

        AsyncOperation op = SceneManager.LoadSceneAsync(endingSceneName, LoadSceneMode.Single);
        if (op == null)
            Debug.LogError($"EndingFlow：场景 {endingSceneName} 不在Build Settings中！");
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
