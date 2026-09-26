using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 新物品解锁流程
/// 玩家通过组装视频获得新物品后，由 ClickToPlayAnimation 通知本脚本：
///   条件满足（requiredItems 全部已拥有；留空视为无条件）→ 弹出对话
///   对话确认（Q）→ 播放解锁动画 → 播放结束发放 unlockItem 并写入存档
///   对话取消（E）→ 关闭对话，不发放
/// unlockItem 已拥有时不再触发
/// 挂载点：与 GlobalUIRef 同一物体（GlobalCanvasRoot），随全局 UI 跨场景保留
/// </summary>
public class ItemUnlockFlow : MonoBehaviour
{
    public static ItemUnlockFlow Instance;

    [Header("触发条件（需已拥有的物品，留空=无条件）")]
    public ItemData[] requiredItems;

    [Header("解锁发放的物品（已拥有则不再触发）")]
    public ItemData unlockItem;

    [Header("解锁动画（可为空，为空时确认后直接发放）")]
    public VideoClip unlockVideo;

    [Header("对话文案（留空=空对话）")]
    [TextArea]
    public string dialogText;

    private GameObject _dialogBox;
    private TMP_Text _dialogTipText;
    private GameObject _videoPanel;
    private RawImage _videoRawImage;
    private VideoPlayer _videoPlayer;
    private RenderTexture _renderTexture;

    private bool _isDialogShowing = false;
    private bool _isPlayingVideo = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        _videoPlayer = gameObject.AddComponent<VideoPlayer>();
        _videoPlayer.playOnAwake = false;
        _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        _videoPlayer.isLooping = false;
    }

    private void Start()
    {
        GlobalUIRef ui = GlobalUIRef.Instance;
        if (ui == null)
        {
            Debug.LogError("ItemUnlockFlow：GlobalUIRef 未初始化，解锁对话与动画不可用");
            enabled = false;
            return;
        }
        _dialogBox = ui.dialogBox;
        _dialogTipText = ui.dialogTipText;
        _videoPanel = ui.videoPanel;
        _videoRawImage = ui.videoRawImage;
    }

    /// <summary>
    /// 由 ClickToPlayAnimation 在获得新物品（组装视频播放完成）后调用
    /// </summary>
    public void NotifyItemObtained(ItemData obtained)
    {
        if (_isDialogShowing || _isPlayingVideo) return;
        if (BagChecker.HasItem(unlockItem)) return;
        if (!BagChecker.AreRequirementsMet(requiredItems)) return;

        ShowDialog();
    }

    private void Update()
    {
        if (!_isDialogShowing) return;

        // 获得斗拱的对话是事件提示（不是可选操作），Q / E 均视为继续
        if (Input.GetKeyDown(GameKeys.DialogConfirm) || Input.GetKeyDown(GameKeys.DialogCancel))
        {
            HideDialog();
            StartUnlock();
        }
    }

    private void ShowDialog()
    {
        if (_dialogBox == null || _dialogTipText == null)
        {
            Debug.LogError("ItemUnlockFlow：全局弹窗UI缺失，无法弹出对话");
            return;
        }
        _isDialogShowing = true;
        _dialogBox.SetActive(true);
        Canvas.ForceUpdateCanvases();
        _dialogTipText.text = dialogText;
    }

    private void HideDialog()
    {
        _isDialogShowing = false;
        if (_dialogBox != null)
            _dialogBox.SetActive(false);
    }

    /// <summary>
    /// 确认后：有解锁动画则播放，否则直接发放
    /// </summary>
    private void StartUnlock()
    {
        if (unlockVideo == null)
        {
            CompleteUnlock();
            return;
        }
        PlayUnlockVideo();
    }

    private void PlayUnlockVideo()
    {
        if (_videoPanel == null || _videoRawImage == null)
        {
            Debug.LogError("ItemUnlockFlow：视频面板UI缺失，跳过动画直接发放");
            CompleteUnlock();
            return;
        }

        if (_renderTexture == null || _renderTexture.width != unlockVideo.width || _renderTexture.height != unlockVideo.height)
        {
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }
            _renderTexture = new RenderTexture((int)unlockVideo.width, (int)unlockVideo.height, 0);
            _renderTexture.Create();
        }

        _isPlayingVideo = true;
        _videoPanel.SetActive(true);
        _videoRawImage.gameObject.SetActive(true);   // 画面区是全局共享对象，确保处于启用状态
        Canvas.ForceUpdateCanvases();
        _videoRawImage.texture = _renderTexture;
        _videoPlayer.targetTexture = _renderTexture;
        _videoPlayer.clip = unlockVideo;
        _videoPlayer.loopPointReached -= OnUnlockVideoEnd;
        _videoPlayer.loopPointReached += OnUnlockVideoEnd;
        _videoPlayer.Play();
    }

    private void OnUnlockVideoEnd(VideoPlayer vp)
    {
        CloseVideo();
        CompleteUnlock();
    }

    private void CloseVideo()
    {
        _isPlayingVideo = false;
        if (_videoPlayer != null)
        {
            _videoPlayer.Stop();
            _videoPlayer.loopPointReached -= OnUnlockVideoEnd;
        }
        if (_videoRawImage != null)
            _videoRawImage.texture = null;
        if (_videoPanel != null)
            _videoPanel.SetActive(false);
    }

    /// <summary>
    /// 发放解锁物品：入包并写入存档（finishedParts）
    /// </summary>
    private void CompleteUnlock()
    {
        if (unlockItem == null)
        {
            Debug.LogError("ItemUnlockFlow：未配置 unlockItem，无法发放解锁物品");
            return;
        }

        if (BagShowVideoManager.Instance != null)
            BagShowVideoManager.Instance.AddItemToBag(unlockItem);
        else
            Debug.LogError("ItemUnlockFlow：BagShowVideoManager 单例为空，无法入包");

        // SetPartFinished 内部会立即写入存档
        GameGlobalData.Instance.SetPartFinished(unlockItem.partKey);

        if (HintManager.Instance != null)
            HintManager.Instance.ShowHint("已解锁新物品，按Tab打开背包查看");
    }

    private void OnDestroy()
    {
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
        if (_videoPlayer != null)
        {
            Destroy(_videoPlayer);
        }
    }
}
