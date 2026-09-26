using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 唯一背包管理器
/// 挂载：场景根物体 GlobalCanvasRoot
/// Tab键开关背包，点击格子预览物品标题/描述/视频
/// </summary>
public class BagShowVideoManager : MonoBehaviour
{
    [Header("背包UI拖拽绑定")]
    [Tooltip("所有背包格子Image数组，按顺序拖拽")]
    public Image[] bagItemSlots;
    [Header("调试：空白格子的临时占位物品（留空=关闭）")]
    [Tooltip("点击尚未获得物品的格子时显示该物品，仅用于测试，正式发布前清空")]
    public ItemData debugPlaceholderItem;
    [Tooltip("调试占位时描述文本的前缀，实际显示为 前缀+格子序号，例如 测试0")]
    public string debugPlaceholderTextPrefix = "测试";
    [Tooltip("物品预览总父物体 bag/Show")]
    public GameObject itemPreviewPanel;
    [Tooltip("物品标题TMP文本")]
    public TMP_Text itemTitleText;
    [Tooltip("物品描述TMP文本")]
    public TMP_Text itemDescText;
    [Tooltip("物品预览视频RawImage")]
    public RawImage bagVideoRawImage;

    // 视频渲染纹理缓存
    private RenderTexture _renderTexture;
    // 背包内置视频播放器
    private VideoPlayer _localVideoPlayer;
    // 当前已占用格子数量
    private int currentItemCount = 0;
    // 已获取物品全局缓存列表（存储完整ItemData）
    private List<ItemData> ownedItemCache = new List<ItemData>();

    // 全局单例
    public static BagShowVideoManager Instance;

    private void Awake()
    {
        // 单例去重
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // 初始化背包视频播放器
        _localVideoPlayer = gameObject.AddComponent<VideoPlayer>();
        _localVideoPlayer.playOnAwake = false;
        _localVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
        _localVideoPlayer.isLooping = true;

        // 初始化格子状态
        InitBagSlots();
    }

    private void Start()
    {
        RestoreBagFromSave();
    }

    /// <summary>
    /// 按存档 finishedParts 全局恢复背包（不依赖当前场景里有哪些构件）
    /// 物品资产位于 Resources/Items，运行时可全部加载后按 partKey 匹配
    /// AddItemToBag 内部按引用去重，与构件自身的恢复逻辑不会重复
    /// </summary>
    void RestoreBagFromSave()
    {
        GameSaveData data = SaveSystem.Load();
        if (data == null || data.finishedParts == null) return;

        ItemData[] allItems = Resources.LoadAll<ItemData>("Items");
        if (allItems == null || allItems.Length == 0)
        {
            Debug.LogWarning("RestoreBagFromSave：Resources/Items 下没有找到物品资产");
            return;
        }

        foreach (ItemData item in allItems)
        {
            if (item == null || string.IsNullOrEmpty(item.partKey)) continue;
            if (data.finishedParts.Contains(item.partKey))
                AddItemToBag(item);
        }
    }

    private void Update()
    {
        // Tab键切换背包显隐
        if (Input.GetKeyDown(GameKeys.Bag))
        {
            // 校验全局UI单例
            if (GlobalUIRef.Instance == null)
            {
                Debug.LogError("Tab失败：GlobalUIRef未初始化");
                return;
            }
            GameObject bagPanel = GlobalUIRef.Instance.bagPanel;
            if (bagPanel == null)
            {
                Debug.LogError("Tab失败：GlobalUIRef未绑定bagPanel");
                return;
            }

            bool newState = !bagPanel.activeSelf;
            bagPanel.SetActive(newState);
            Canvas.ForceUpdateCanvases();

            // 打开背包刷新格子，关闭清空预览
            if (newState)
                RefreshBagUIFromCache();
            else
                CloseItemPreview();
        }
    }

    /// <summary>
    /// 初始化背包格子：清空贴图、透明可点击按钮
    /// </summary>
    private void InitBagSlots()
    {
        if (bagItemSlots == null) return;
        foreach (Image slot in bagItemSlots)
        {
            if (slot != null)
            {
                slot.enabled = false;
                slot.sprite = null;
                Button btn = slot.GetComponent<Button>();
                if (btn != null)
                {
                    Image btnImg = btn.GetComponent<Image>();
                    if (btnImg != null)
                    {
                        btnImg.color = new Color(1, 1, 1, 0);
                        btnImg.raycastTarget = true;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 接收外部传递的完整ItemData，存入缓存并渲染格子图标
    /// </summary>
    public void AddItemToBag(ItemData item)
    {
        if (item == null)
        {
            Debug.LogError("AddItemToBag：传入ItemData为空");
            return;
        }
        if (bagItemSlots == null || bagItemSlots.Length == 0)
        {
            Debug.LogError("AddItemToBag：背包格子数组未拖拽");
            return;
        }
        if (currentItemCount >= bagItemSlots.Length)
        {
            Debug.LogWarning("AddItemToBag：背包格子已满");
            return;
        }
        if (bagItemSlots[currentItemCount] == null)
        {
            Debug.LogError("AddItemToBag：格子数组存在空元素，请检查Inspector绑定");
            return;
        }
        if (ownedItemCache.Exists(x => x == item))
        {
            Debug.LogWarning($"物品【{item.itemTitle}】已存在，跳过");
            return;
        }

        ownedItemCache.Add(item);
        bagItemSlots[currentItemCount].sprite = item.itemSprite;
        bagItemSlots[currentItemCount].enabled = true;
        currentItemCount++;
    }

    /// <summary>
    /// 打开背包时刷新格子，自动关闭预览面板
    /// </summary>
    public void RefreshBagUIFromCache()
    {
        // 打开背包先隐藏预览
        if (itemPreviewPanel != null)
            itemPreviewPanel.SetActive(false);

        if (bagItemSlots == null) return;
        // 清空全部格子
        for (int i = 0; i < bagItemSlots.Length; i++)
        {
            bagItemSlots[i].sprite = null;
            bagItemSlots[i].enabled = false;
        }
        currentItemCount = 0;
        // 填充物品图标
        foreach (ItemData item in ownedItemCache)
        {
            if (currentItemCount >= bagItemSlots.Length) break;
            bagItemSlots[currentItemCount].sprite = item.itemSprite;
            bagItemSlots[currentItemCount].enabled = true;
            currentItemCount++;
        }
    }

    /// <summary>
    /// 清空背包缓存与全部格子显示，用于游戏重置
    /// </summary>
    public void ClearBag()
    {
        ownedItemCache.Clear();
        currentItemCount = 0;
        if (bagItemSlots != null)
        {
            for (int i = 0; i < bagItemSlots.Length; i++)
            {
                if (bagItemSlots[i] != null)
                {
                    bagItemSlots[i].sprite = null;
                    bagItemSlots[i].enabled = false;
                }
            }
        }
        CloseItemPreview();
    }

    /// <summary>
    /// 点击格子，加载物品标题/描述/预览视频
    /// </summary>
    public void OnClickBagSlot(int index)
    {
        ItemData target;
        int debugSlotIndex = -1;
        if (index < 0 || index >= ownedItemCache.Count)
        {
            // 调试占位：配置了临时物品时，尚未获得物品的格子点击也显示它
            if (debugPlaceholderItem != null && index >= 0 && bagItemSlots != null && index < bagItemSlots.Length)
            {
                target = debugPlaceholderItem;
                debugSlotIndex = index;
            }
            else
            {
                Debug.LogWarning("该下标无物品数据");
                CloseItemPreview();
                return;
            }
        }
        else
        {
            target = ownedItemCache[index];
        }
        if (target == null)
        {
            Debug.LogError("缓存内该物品为空");
            CloseItemPreview();
            return;
        }

        // 弹出预览总面板
        if (itemPreviewPanel != null)
            itemPreviewPanel.SetActive(true);
        else
            Debug.LogError("itemPreviewPanel（Show）未拖拽！");

        // 赋值标题文本
        if (itemTitleText != null)
        {
            itemTitleText.text = target.itemTitle;
            itemTitleText.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogError("itemTitleText 标题文本未拖拽赋值！");
        }

        // 赋值描述文本（调试占位时显示 前缀+格子序号）
        if (itemDescText != null)
        {
            itemDescText.text = debugSlotIndex >= 0
                ? debugPlaceholderTextPrefix + debugSlotIndex
                : target.itemDescription;
            itemDescText.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogError("itemDescText 描述文本未拖拽赋值！");
        }

        // 播放预览视频
        if (target.autoPlay)
        {
            if (target.itemVideo == null)
                Debug.LogWarning("该物品无预览视频Clip");
            PlayBagItemVideo(target.itemVideo);
        }
    }

    /// <summary>
    /// 播放物品预览视频
    /// </summary>
    private void PlayBagItemVideo(VideoClip clip)
    {
        if (bagVideoRawImage == null)
        {
            Debug.LogError("bagVideoRawImage 视频框未拖拽！");
            return;
        }
        _localVideoPlayer.Stop();
        if (clip == null)
        {
            bagVideoRawImage.gameObject.SetActive(false);
            bagVideoRawImage.texture = null;
            return;
        }
        if (_renderTexture == null || _renderTexture.width != clip.width || _renderTexture.height != clip.height)
        {
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }
            _renderTexture = new RenderTexture((int)clip.width, (int)clip.height, 0);
            _renderTexture.Create();
        }
        bagVideoRawImage.gameObject.SetActive(true);
        bagVideoRawImage.texture = _renderTexture;
        _localVideoPlayer.targetTexture = _renderTexture;
        _localVideoPlayer.clip = clip;
        _localVideoPlayer.Play();
    }

    /// <summary>
    /// 关闭预览：隐藏Show、停止视频、清空纹理
    /// </summary>
    public void CloseItemPreview()
    {
        _localVideoPlayer.Stop();
        if (bagVideoRawImage != null)
        {
            bagVideoRawImage.texture = null;
            bagVideoRawImage.gameObject.SetActive(false);
        }
        if (itemPreviewPanel != null)
            itemPreviewPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
        if (_localVideoPlayer != null)
        {
            Destroy(_localVideoPlayer);
        }
    }
}
