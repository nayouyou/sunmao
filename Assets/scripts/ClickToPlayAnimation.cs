using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 可交互组装零件点击脚本
/// 挂载：每个场景可点击零件物体
/// 功能：点击弹窗、播放组装视频、组装完成存档并添加物品进背包
/// 依赖：GlobalUIRef、GameGlobalData、BagShowVideoManager、HintManager
/// </summary>
public class ClickToPlayAnimation : MonoBehaviour
{
    [Header("物品绑定")]
    [Tooltip("组装完成后获得的物品ScriptableObject")]
    public ItemData itemData;
    [Tooltip("零件唯一标识，用于存档判断是否已组装")]
    public string partKey;
    [Tooltip("组装动画视频资源")]
    public VideoClip videoClip;
    [Tooltip("视频循环播放次数，默认1次")]
    public int playTimes = 1;

    [Header("额外获得的物品（可为空）")]
    [Tooltip("组装完成后与 itemData 一起放入背包并存档")]
    public ItemData[] additionalItems;

    [Header("无组装视频时显示的静态图（可为空）")]
    [Tooltip("videoClip 为空时，在组装面板显示这张图，按 Esc 关闭即完成组装")]
    public Texture assemblyStillImage;

    [Header("前置条件（需已组装完成的物品）")]
    [Tooltip("缺任意一项时点击只提示，不进入组装流程；留空表示无前置条件")]
    public ItemData[] requiredItems;

    [Header("零件外观素材")]
    public Sprite originalSprite;
    public Sprite assembledSprite;
    public Vector3 originalScale = Vector3.one;
    public Vector3 assembledPos;
    public Vector3 assembledScale = new Vector3(0.8f, 0.8f, 1);

    [Header("弹窗提示文字")]
    public string firstClickTip = "要把这堆木料加工完成吗？(Q确认/E取消)";
    public string secondClickTip = "再次观看组装动画？(Q确认/E取消)";

    [Header("组装完成后的对话文案（{0} 会替换为物品名称）")]
    public string obtainedDialogText = "原来是这样！我已知晓{0}";

    // 全局UI缓存
    private GameObject dialogBox;
    private TMP_Text dialogTipText;
    private GameObject videoPanel;
    private RawImage videoRawImage;

    private SpriteRenderer _spriteRenderer;
    private bool _isAssembled = false;
    private bool _requirementsMet = true;
    private bool _isObtainedTipShowing = false;
    private bool _isShowingStill = false;

    /// <summary>
    /// 当前正在显示静态组装图的实例。场景里多个同类实例共享同一块组装面板，
    /// 关闭时只允许归属者处理，避免其他实例抢先关闭并误完成
    /// </summary>
    private static ClickToPlayAnimation _stillImageOwner;
    private ItemData _pendingUnlockNotify;
    private bool _isDialogShowing = false;
    private int _currentPlayCount = 0;
    private RenderTexture _renderTexture;
    private VideoPlayer _assembleVideoPlayer;

    void Start()
    {
        // 获取精灵渲染组件，无则自动创建
        _spriteRenderer = GetComponent<SpriteRenderer>();
        if (_spriteRenderer == null)
            _spriteRenderer = gameObject.AddComponent<SpriteRenderer>();

        // 读取存档，初始化零件外观
        bool finish = GameGlobalData.Instance.IsPartFinished(partKey);
        if (finish)
        {
            _spriteRenderer.sprite = assembledSprite;
            transform.position = assembledPos;
            transform.localScale = assembledScale;
            _isAssembled = true;

            // 存档恢复：已组装零件的物品重新放入背包（主物品 + 额外物品，AddItemToBag内部按引用去重）
            if (BagShowVideoManager.Instance != null)
            {
                if (itemData != null)
                    BagShowVideoManager.Instance.AddItemToBag(itemData);
                if (additionalItems != null)
                {
                    foreach (ItemData extra in additionalItems)
                    {
                        if (extra != null)
                            BagShowVideoManager.Instance.AddItemToBag(extra);
                    }
                }
            }
        }
        else
        {
            _spriteRenderer.sprite = originalSprite;
            transform.localScale = originalScale;
        }

        // 自动添加2D点击碰撞体
        if (GetComponent<BoxCollider2D>() == null)
            gameObject.AddComponent<BoxCollider2D>().isTrigger = false;

        // 创建内置视频播放器
        _assembleVideoPlayer = gameObject.AddComponent<VideoPlayer>();
        _assembleVideoPlayer.playOnAwake = false;
        _assembleVideoPlayer.renderMode = VideoRenderMode.RenderTexture;

        // 拉取全局UI单例；缺失时禁用自身（弹窗/视频不可用但不崩溃）
        if (GlobalUIRef.Instance == null)
        {
            Debug.LogError($"{gameObject.name}：全局UI单例未初始化！");
            enabled = false;
            return;
        }
        dialogBox = GlobalUIRef.Instance.dialogBox;
        dialogTipText = GlobalUIRef.Instance.dialogTipText;
        videoPanel = GlobalUIRef.Instance.videoPanel;
        videoRawImage = GlobalUIRef.Instance.videoRawImage;

        // videoPanel 在Update中被直接解引用，缺失时提前禁用自身，防止每帧NRE
        if (videoPanel == null)
        {
            Debug.LogError($"{gameObject.name}：GlobalUIRef未绑定videoPanel！");
            enabled = false;
        }
    }

    void Update()
    {
        // 鼠标点击检测，弹窗/视频打开时屏蔽点击
        if (Input.GetMouseButtonDown(0) && !_isDialogShowing && !videoPanel.activeSelf)
        {
            RayCastClick();
        }

        // 弹窗快捷键：Q确认播放 / E取消
        if (_isDialogShowing)
        {
            if (Input.GetKeyDown(GameKeys.DialogConfirm))
            {
                bool canPlay = _requirementsMet && !_isObtainedTipShowing;
                CloseDialog();
                if (canPlay)
                    PlayVideoAnim();
            }
            if (Input.GetKeyDown(GameKeys.DialogCancel))
                CloseDialog();
        }

        // 视频面板关闭快捷键
        if (videoPanel.activeSelf && Input.GetKeyDown(GameKeys.ClosePanel))
        {
            // 无组装视频的物件为静态图模式
            bool isStillMode = (videoClip == null && assemblyStillImage != null);
            if (isStillMode)
            {
                // 只有开启静态图的实例才处理关闭，避免场景里其他同类实例抢先关闭并误完成
                if (_stillImageOwner == this)
                {
                    HideStillImage();
                    CompleteAssembly();
                }
            }
            else
            {
                CloseVideo();
            }
        }
    }

    /// <summary>
    /// 2D点检测是否点击当前零件
    /// </summary>
    void RayCastClick()
    {
        Collider2D hit = Physics2D.OverlapPoint(InputHelper.MouseWorldPos);
        if (hit != null && hit.gameObject == gameObject)
        {
            // 点击时先检查前置条件；缺少物品只提示，不进入确认流程
            List<ItemData> missing = BagChecker.GetMissingItems(requiredItems);
            if (missing.Count > 0)
            {
                ShowMissingTip(missing);
                return;
            }
            OpenDialog();
        }
    }

    /// <summary>
    /// 打开确认弹窗
    /// </summary>
    /// <summary>
    /// 前置条件不满足时，用全局弹窗显示缺少的物品（Q/E 均可关闭，不会播放动画）
    /// </summary>
    void ShowMissingTip(List<ItemData> missing)
    {
        if (dialogBox == null || dialogTipText == null)
        {
            Debug.LogError($"{gameObject.name}：弹窗UI缺失，请检查GlobalUIRef绑定");
            return;
        }

        _requirementsMet = false;
        _isDialogShowing = true;
        dialogBox.SetActive(true);
        Canvas.ForceUpdateCanvases();

        List<string> names = new List<string>();
        foreach (ItemData item in missing)
            names.Add(item != null ? item.itemTitle : "(未命名物品)");
        dialogTipText.text = "还缺少：" + string.Join("、", names) + "（E关闭）";
    }

    void OpenDialog()
    {
        if (dialogBox == null || dialogTipText == null)
        {
            Debug.LogError($"{gameObject.name}：弹窗UI缺失，请检查GlobalUIRef绑定");
            return;
        }
        _requirementsMet = true;
        _isDialogShowing = true;
        dialogBox.SetActive(true);
        Canvas.ForceUpdateCanvases();
        dialogTipText.text = _isAssembled ? secondClickTip : firstClickTip;
    }

    /// <summary>
    /// 关闭确认弹窗
    /// </summary>
    void CloseDialog()
    {
        if (dialogBox == null) return;
        _isDialogShowing = false;
        dialogBox.SetActive(false);

        // "已知晓"提示关闭后，才通知解锁流程
        if (_isObtainedTipShowing)
        {
            _isObtainedTipShowing = false;
            ItemData pending = _pendingUnlockNotify;
            _pendingUnlockNotify = null;
            if (pending != null && ItemUnlockFlow.Instance != null)
                ItemUnlockFlow.Instance.NotifyItemObtained(pending);
        }
    }

    /// <summary>
    /// 组装完成后的"已知晓"提示对话，Q/E 关闭，关闭后才通知解锁流程
    /// </summary>
    void ShowObtainedTip(ItemData item)
    {
        if (dialogBox == null || dialogTipText == null)
        {
            // 弹窗UI缺失时直接通知解锁流程，避免流程中断
            if (ItemUnlockFlow.Instance != null)
                ItemUnlockFlow.Instance.NotifyItemObtained(item);
            return;
        }

        _pendingUnlockNotify = item;
        _isObtainedTipShowing = true;
        _isDialogShowing = true;
        dialogBox.SetActive(true);
        Canvas.ForceUpdateCanvases();

        dialogTipText.text = string.Format(obtainedDialogText, BuildObtainedItemNames(item));
    }

    /// <summary>
    /// 拼装本次获得的物品名称（主物品 + 额外物品），用于"已知晓"对话
    /// 获得两个物品时显示两个名字，例如：霸王枨、粽角榫
    /// </summary>
    string BuildObtainedItemNames(ItemData main)
    {
        List<string> names = new List<string>();
        if (main != null)
            names.Add(main.itemTitle);
        if (additionalItems != null)
        {
            foreach (ItemData extra in additionalItems)
            {
                if (extra != null)
                    names.Add(extra.itemTitle);
            }
        }
        return string.Join("、", names);
    }

    /// <summary>
    /// 播放组装视频
    /// </summary>
    void PlayVideoAnim()
    {
        if (videoPanel == null || videoRawImage == null)
        {
            Debug.LogError($"{gameObject.name}：视频面板UI缺失");
            return;
        }
        // 没有组装视频时，用静态图代替（如椅子）
        if (videoClip == null)
        {
            ShowStillImage();
            return;
        }

        _currentPlayCount = 0;
        videoPanel.SetActive(true);
        videoRawImage.gameObject.SetActive(true);   // 画面区是全局共享对象，确保处于启用状态
        Canvas.ForceUpdateCanvases();

        // 自动重建适配尺寸渲染纹理
        if (_renderTexture == null || _renderTexture.width != videoClip.width || _renderTexture.height != videoClip.height)
        {
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }
            _renderTexture = new RenderTexture((int)videoClip.width, (int)videoClip.height, 0);
            _renderTexture.Create();
        }

        videoRawImage.texture = _renderTexture;
        _assembleVideoPlayer.targetTexture = _renderTexture;
        _assembleVideoPlayer.clip = videoClip;
        _assembleVideoPlayer.isLooping = false;
        // 防止多次绑定回调
        _assembleVideoPlayer.loopPointReached -= OnVideoEnd;
        _assembleVideoPlayer.loopPointReached += OnVideoEnd;
        _assembleVideoPlayer.Play();
    }

    /// <summary>
    /// 关闭视频并释放资源
    /// </summary>
    void CloseVideo()
    {
        if (_assembleVideoPlayer != null)
        {
            _assembleVideoPlayer.Stop();
            _assembleVideoPlayer.loopPointReached -= OnVideoEnd;
        }
        if (videoRawImage != null)
            videoRawImage.texture = null;
        if (videoPanel != null)
            videoPanel.SetActive(false);
    }

    /// <summary>
    /// 显示静态组装图（无组装视频的物件用），按 Esc 关闭后完成组装
    /// </summary>
    void ShowStillImage()
    {
        if (assemblyStillImage == null)
        {
            Debug.LogError($"{gameObject.name}：未赋值组装视频且无静态图");
            return;
        }
        if (videoPanel == null || videoRawImage == null)
        {
            Debug.LogError($"{gameObject.name}：视频面板UI缺失");
            return;
        }

        _stillImageOwner = this;
        _isShowingStill = true;
        videoPanel.SetActive(true);
        Canvas.ForceUpdateCanvases();
        videoRawImage.gameObject.SetActive(true);
        videoRawImage.texture = assemblyStillImage;
    }

    /// <summary>
    /// 关闭静态组装图
    /// </summary>
    void HideStillImage()
    {
        _isShowingStill = false;
        if (_stillImageOwner == this)
            _stillImageOwner = null;
        if (videoRawImage != null)
        {
            // 只清纹理，不要禁用这个全局共享的画面区，否则之后所有视频都看不到
            videoRawImage.texture = null;
        }
        if (videoPanel != null)
            videoPanel.SetActive(false);
    }

    /// <summary>
    /// 发放本次组装获得的物品（主物品 + 额外物品），并写入存档
    /// </summary>
    void GrantItems()
    {
        GrantOne(itemData);
        if (additionalItems != null)
        {
            foreach (ItemData extra in additionalItems)
            {
                GrantOne(extra);
            }
        }
    }

    void GrantOne(ItemData it)
    {
        if (it == null)
        {
            Debug.LogError($"{gameObject.name}：物品配置为空，跳过");
            return;
        }
        if (!GameGlobalData.Instance.IsPartFinished(it.partKey))
        {
            GameGlobalData.Instance.SetPartFinished(it.partKey);
        }
        if (BagShowVideoManager.Instance != null)
        {
            BagShowVideoManager.Instance.AddItemToBag(it);
        }
        else
        {
            Debug.LogError("BagShowVideoManager单例为空，无法存入物品");
        }
    }

    /// <summary>
    /// 视频播放完毕回调：组装完成、存档、新增物品至背包
    /// </summary>
    void OnVideoEnd(VideoPlayer vp)
    {
        _currentPlayCount++;
        // 未达到播放次数则循环播放
        if (_currentPlayCount < playTimes)
        {
            vp.Play();
            return;
        }
        CloseVideo();
        CompleteAssembly();
    }

    /// <summary>
    /// 组装完成：发放物品、弹"已知晓"提示、更新外观、收起感叹号
    /// 视频结束与静态图关闭都走这里
    /// </summary>
    void CompleteAssembly()
    {
        // 仅首次组装执行新增物品逻辑
        if (!_isAssembled)
        {
            if (!GameGlobalData.Instance.IsPartFinished(partKey))
            {
                GameGlobalData.Instance.SetPartFinished(partKey);

                // 传递完整ItemData给背包管理器
                GrantItems();
                if (HintManager.Instance != null)
                {
                    HintManager.Instance.ShowHint("已解锁物品，按Tab打开背包查看");
                }
            }

            // 先弹"已知晓"提示，关闭后再通知解锁流程（避免两个对话叠加）
            ShowObtainedTip(itemData);

            // 更新零件外观为组装完成样式
            _isAssembled = true;
            _spriteRenderer.sprite = assembledSprite;
            transform.position = assembledPos;
            transform.localScale = assembledScale;
        }
        InteractExclamationTip tipComp = GetComponent<InteractExclamationTip>();
        if (tipComp != null)
        {
            tipComp.CompleteInteract();
        }
    }

    void OnDestroy()
    {
        // 释放渲染纹理防止内存泄漏
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
        if (_assembleVideoPlayer != null)
        {
            Destroy(_assembleVideoPlayer);
        }
    }
}
