using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// 游戏内 ESC 菜单（跨场景常驻，挂在 GlobalCanvasRoot 上，所有游戏场景均生效）
/// 打开时显示当前进度，提供三个按键：继续游戏 / 返回主菜单 / 退出游戏
/// 菜单样式图未导入，留 Image 字段，导入后在 Inspector 拖入
/// "返回主菜单"不清档：之后可在主菜单点"继续游戏"接着玩
/// </summary>
public class SaveGameMenu : MonoBehaviour
{
    [Header("面板与部件")]
    public GameObject menuPanel;
    public Image menuStyleImage;
    public TMP_Text progressText;

    [Header("按键（自上而下）")]
    public Button continueButton;
    public Button backToMainMenuButton;
    public Button quitButton;

    [Header("返回的主菜单场景")]
    public string mainMenuSceneName = "mainMenu";

    private bool _menuOpen = false;

    void Start()
    {
        if (menuPanel != null) menuPanel.SetActive(false);

        if (continueButton != null) continueButton.onClick.AddListener(CloseMenu);
        if (backToMainMenuButton != null) backToMainMenuButton.onClick.AddListener(BackToMainMenu);
        if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
    }

    void Update()
    {
        if (!Input.GetKeyDown(GameKeys.ClosePanel)) return;

        // 视频面板/背包打开时，Esc 优先归它们处理
        GlobalUIRef ui = GlobalUIRef.Instance;
        bool otherPanelOpen = ui != null
            && ((ui.videoPanel != null && ui.videoPanel.activeSelf)
                || (ui.bagPanel != null && ui.bagPanel.activeSelf)
                || ClickToPlayAnimation.IsStillImageShowing);   // 组装静态图全屏时也归它处理

        if (!_menuOpen && !otherPanelOpen)
            OpenMenu();
        else if (_menuOpen)
            CloseMenu();
    }

    void OpenMenu()
    {
        _menuOpen = true;
        RefreshProgress();
        if (menuPanel != null) menuPanel.SetActive(true);
    }

    void CloseMenu()
    {
        _menuOpen = false;
        if (menuPanel != null) menuPanel.SetActive(false);
    }

    void RefreshProgress()
    {
        if (progressText == null) return;

        GameGlobalData data = GameGlobalData.Instance;
        GlobalInteractRecord record = GlobalInteractRecord.Instance;
        progressText.text = $"已组装零件：{data.finishedPartDict.Count}\n已交互物品：{record.interactedIdList.Count}";
    }

    /// <summary>
    /// 返回主菜单（不清档）
    /// </summary>
    public void BackToMainMenu()
    {
        CloseMenu();

        AsyncOperation op = SceneManager.LoadSceneAsync(mainMenuSceneName, LoadSceneMode.Single);
        if (op == null)
            Debug.LogError($"SaveGameMenu：主菜单场景 {mainMenuSceneName} 不在Build Settings中！");
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
