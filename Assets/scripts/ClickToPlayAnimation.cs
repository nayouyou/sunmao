using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 可交互组装零件点击脚本
/// 挂载：每个场景可点击零件物体
/// 功能：点击弹窗、播放组装视频、组装完成存档并添加物品进背包
/// 文案接口：
///   运行时改某一个物体：GetComponent<ClickToPlayAnimation>().SetMyTexts(句子数组, 首句, 二次句)
///   运行时按 partKey 批量改：ClickToPlayAnimation.SetTexts("attic_chair", 句子数组, 首句, 二次句)
///   场景加载前先登记也行，物体 Start 时会自动套用；传 null 的项表示不改动
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
    public string firstClickTip = "要把这堆木料加工完成吗？";
    public string secondClickTip = "再次观看组装动画？";

    [Header("逐句出现的观察文案（点一次出一句，全部看完才弹出确认窗；留空=直接弹确认）")]
    public string[] introTips;

    [Header("组装完成后的对话文案（{0} 会替换为物品名称）")]
    public string obtainedDialogText = "原来是这样！我获得了{0}";

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
    private bool _isPlayingVideo = false;

    /// <summary>
    /// 当前正在显示静态组装图的实例。场景里多个同类实例共享同一块组装面板，
    /// 关闭时只允许归属者处理，避免其他实例抢先关闭并误完成
    /// </summary>
    private static ClickToPlayAnimation _stillImageOwner;

    // 静态组装图用的独立全屏画面层（不复用视频面板，互不影响）
    private static GameObject _stillFullRoot;

    /// <summary>静态组装图是否正在全屏显示（Esc 菜单等其它系统据此避让）</summary>
    public static bool IsStillImageShowing
    {
        get { return _stillFullRoot != null && _stillFullRoot.activeSelf; }
    }
    private static RawImage _stillFullImage;

    /// <summary>
    /// 把静态组装图铺满整屏：在画布下建一个全屏 RawImage（只建一次，之后复用），
    /// 视频面板完全不参与，所以不会影响视频播放的比例适配
    /// </summary>
    void ShowStillFullScreen()
    {
        Canvas canvas = videoPanel != null ? videoPanel.GetComponentInParent<Canvas>() : null;
        if (canvas == null && videoRawImage != null) canvas = videoRawImage.GetComponentInParent<Canvas>();

        if (canvas == null)
        {
            // 兜底：找不到画布时退回原来的面板显示
            if (videoPanel != null) videoPanel.SetActive(true);
            if (videoRawImage != null)
            {
                videoRawImage.gameObject.SetActive(true);
                videoRawImage.texture = assemblyStillImage;
            }
            return;
        }

        if (_stillFullRoot == null || _stillFullRoot.transform.parent != canvas.transform)
        {
            if (_stillFullRoot != null) Destroy(_stillFullRoot);
            _stillFullRoot = new GameObject("AssembledStillFullScreen", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            RectTransform rt = _stillFullRoot.GetComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            _stillFullImage = _stillFullRoot.GetComponent<RawImage>();
            _stillFullImage.raycastTarget = false;
        }

        _stillFullRoot.SetActive(true);
        _stillFullRoot.transform.SetAsLastSibling();   // 盖在最上层
        _stillFullImage.texture = assemblyStillImage;
    }

    void HideStillFullScreen()
    {
        if (_stillFullRoot != null) _stillFullRoot.SetActive(false);
    }

    private ItemData _pendingUnlockNotify;
    private bool _isDialogShowing = false;
    private bool _isIntroShowing = false;   // 正在逐句显示观察文案
    private bool _introDone = false;        // 本次游玩已看完铺垫，不再重复
    private int _introIndex = -1;
    private int _currentPlayCount = 0;
    private RenderTexture _renderTexture;
    private VideoPlayer _assembleVideoPlayer;

    void Start()
    {
        ApplyTextOverride();      // 先套用外部通过接口设置的文案（没有则用 Inspector 里的）

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
        bool clickUsed = false;
        if (Input.GetMouseButtonDown(0) && !_isDialogShowing && !videoPanel.activeSelf && !_isShowingStill)
        {
            RayCastClick();
            clickUsed = true;      // 这一下点击已经用来开窗，同一帧不能再翻句
        }

        // 逐句铺垫：点击或 Q/E 都翻到下一句
        if (_isIntroShowing)
        {
            if ((!clickUsed && Input.GetMouseButtonDown(0))
                || Input.GetKeyDown(GameKeys.DialogConfirm)
                || Input.GetKeyDown(GameKeys.DialogCancel))
            {
                AdvanceIntro();
            }
        }
        // 弹窗快捷键：Q确认播放 / E取消（只在确认窗里生效）
        else if (_isDialogShowing)
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
        if ((videoPanel.activeSelf || _isShowingStill) && Input.GetKeyDown(GameKeys.ClosePanel))
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
            else if (_isPlayingVideo)
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
        bool clickedSelf = false;
        Collider2D[] hits = Physics2D.OverlapPointAll(InputHelper.MouseWorldPos);
        foreach (Collider2D h in hits)
        {
            if (h != null && h.gameObject == gameObject) { clickedSelf = true; break; }
        }
        if (clickedSelf)
        {
            // 点击时先检查前置条件；缺少物品只提示，不进入确认流程
            List<ItemData> missing = BagChecker.GetMissingItems(requiredItems);
            if (missing.Count > 0)
            {
                ShowMissingTip(missing);
                return;
            }
            StartIntroOrDialog();
        }
    }

    /// <summary>
    /// 这个物体是不是"只是看看"：没有组装动画、没有静态图、也没有物品奖励
    /// 这类物体的文案顺序是"原句先说，再逐句补细节"，说完自动关闭，不弹确认窗
    /// </summary>
    bool IsLookOnly()
    {
        return videoClip == null
            && assemblyStillImage == null
            && itemData == null
            && (additionalItems == null || additionalItems.Length == 0);
    }

    /// <summary>
    /// 点击零件：有铺垫文案就逐句显示，看完（或已组装过）再弹确认窗
    /// </summary>，看完（或已组装过）再弹确认窗
    /// </summary>
    /// <summary>本次要逐句显示的完整序列（观看类 = 原句 + 铺垫；组装类 = 铺垫）</summary>
    string[] BuildSequence()
    {
        if (introTips == null || introTips.Length == 0) return null;
        if (!IsLookOnly()) return introTips;

        string first = _isAssembled ? secondClickTip : firstClickTip;
        if (string.IsNullOrEmpty(first)) return introTips;

        string[] seq = new string[introTips.Length + 1];
        seq[0] = first;
        for (int i = 0; i < introTips.Length; i++) seq[i + 1] = introTips[i];
        return seq;
    }

    void StartIntroOrDialog()
    {
        if (!_isAssembled && !_introDone && introTips != null && introTips.Length > 0)
        {
            _introDone = true;
            _introIndex = 0;
            ShowIntroLine();
            return;
        }
        OpenDialog();
    }

    /// <summary>显示当前这一句铺垫</summary>
    void ShowIntroLine()
    {
        if (dialogBox == null || dialogTipText == null)
        {
            OpenDialog();
            return;
        }
        _requirementsMet = true;
        _isDialogShowing = true;
        _isIntroShowing = true;
        dialogBox.SetActive(true);
        Canvas.ForceUpdateCanvases();
        string[] seq = BuildSequence();
        if (seq == null || seq.Length == 0) { OpenDialog(); return; }
        _introIndex = Mathf.Clamp(_introIndex, 0, seq.Length - 1);
        dialogTipText.text = seq[_introIndex];
    }

    /// <summary>翻到下一句；说完最后一句就接原来的确认窗</summary>
    void AdvanceIntro()
    {
        _introIndex++;
        string[] seq = BuildSequence();
        if (seq != null && _introIndex < seq.Length)
        {
            ShowIntroLine();
            return;
        }

        _isIntroShowing = false;
        // 只是看看的物体：说完就关，不弹确认窗；
        // 同时把感叹号永久去掉——观察类物品只提示一次
        if (IsLookOnly())
        {
            InteractExclamationTip tip = GetComponent<InteractExclamationTip>();
            if (tip != null) tip.CompleteInteract();
            CloseDialog();
            return;
        }
        OpenDialog();
    }

    // ================= 文案接口 =================
    // 覆盖表：按键（partKey）登记文案，场景加载前登记也能在物体 Start 时生效
    private static readonly Dictionary<string, string[]> _overrideIntro = new Dictionary<string, string[]>();
    private static readonly Dictionary<string, string> _overrideFirst = new Dictionary<string, string>();
    private static readonly Dictionary<string, string> _overrideSecond = new Dictionary<string, string>();

    /// <summary>这个物体的文案键：优先用 partKey，为空时用物体名</summary>
    public string TextKey { get { return string.IsNullOrEmpty(partKey) ? gameObject.name : partKey; } }

    /// <summary>
    /// 文案接口：按键设置某个交互物的文案。任何脚本、任何时刻都能调用；
    /// 物体还没加载时会先记下来，等它 Start 时自动套用。
    /// introLines / firstTip / secondTip 传 null 表示该项不改。
    /// </summary>
    public static void SetTexts(string key, string[] introLines, string firstTip = null, string secondTip = null)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (introLines != null) _overrideIntro[key] = introLines;
        if (firstTip != null) _overrideFirst[key] = firstTip;
        if (secondTip != null) _overrideSecond[key] = secondTip;

        // 场景里已经存在的同类物体，立即同步
        ClickToPlayAnimation[] all = FindObjectsOfType<ClickToPlayAnimation>();
        foreach (ClickToPlayAnimation c in all)
        {
            if (c != null && c.TextKey == key) c.ApplyTextOverride();
        }
    }

    /// <summary>清除某个键的文案覆盖，恢复用 Inspector 里的内容</summary>
    public static void ClearTexts(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        _overrideIntro.Remove(key);
        _overrideFirst.Remove(key);
        _overrideSecond.Remove(key);
    }

    /// <summary>取某个键当前的铺垫文案（没有覆盖时返回 null）</summary>
    public static string[] GetIntroTips(string key)
    {
        string[] tips;
        if (!string.IsNullOrEmpty(key) && _overrideIntro.TryGetValue(key, out tips)) return tips;
        return null;
    }

    /// <summary>实例接口：直接改这一个交互物的文案（同时登记到覆盖表，重进场景仍生效）</summary>
    public void SetMyTexts(string[] introLines, string firstTip = null, string secondTip = null)
    {
        if (introLines != null) introTips = introLines;
        if (firstTip != null) firstClickTip = firstTip;
        if (secondTip != null) secondClickTip = secondTip;
        SetTexts(TextKey, introLines, firstTip, secondTip);
    }

    /// <summary>把覆盖表里的文案套到这个实例上（Start 时自动调用）</summary>
    public void ApplyTextOverride()
    {
        string k = TextKey;
        string[] tips;
        if (_overrideIntro.TryGetValue(k, out tips) && tips != null) introTips = tips;
        string t;
        if (_overrideFirst.TryGetValue(k, out t) && t != null) firstClickTip = t;
        if (_overrideSecond.TryGetValue(k, out t) && t != null) secondClickTip = t;
    }

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
        _isIntroShowing = false;
        dialogBox.SetActive(false);

        // "已知晓"提示关闭后，才通知解锁流程
        if (_isObtainedTipShowing)
        {
            _isObtainedTipShowing = false;
            ItemData pending = _pendingUnlockNotify;
            _pendingUnlockNotify = null;
            if (pending != null && ItemUnlockFlow.Instance != null)
                ItemUnlockFlow.Instance.NotifyItemObtained(pending);
            if (pending != null && EndingFlow.Instance != null)
                EndingFlow.Instance.NotifyItemObtained(pending);
            if (pending != null && PictureHintFlow.Instance != null)
                PictureHintFlow.Instance.NotifyItemObtained(pending);
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
        _isPlayingVideo = true;
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
        _isPlayingVideo = false;
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
        Canvas.ForceUpdateCanvases();
        ShowStillFullScreen();      // 用独立的全屏层显示，不碰视频面板
    }

    /// <summary>
    /// 关闭静态组装图
    /// </summary>
    void HideStillImage()
    {
        _isShowingStill = false;
        HideStillFullScreen();      // 只关掉独立全屏层
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
