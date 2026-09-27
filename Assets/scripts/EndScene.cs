using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 结束场景（通关后进入）
/// 显示“游戏结束”，提供返回主菜单 / 退出游戏两个按键
/// 返回主菜单不清档：之后可在主菜单点“继续游戏”接着玩
/// 进入时清理跨场景常驻对象（玩家、游戏 UI 等），避免残留到结束场景
/// </summary>
public class EndScene : MonoBehaviour
{
    [Header("按键")]
    public Button backToMainMenuButton;
    public Button quitButton;

    [Header("返回的主菜单场景")]
    public string mainMenuSceneName = "mainMenu";

    void Start()
    {
        // 清掉从游戏场景带回的常驻对象（玩家、GlobalCanvasRoot 等）
        CleanupPersistentObjects();

        if (backToMainMenuButton != null) backToMainMenuButton.onClick.AddListener(BackToMainMenu);
        if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
    }

    public void BackToMainMenu()
    {
        AsyncOperation op = SceneManager.LoadSceneAsync(mainMenuSceneName, LoadSceneMode.Single);
        if (op == null)
            Debug.LogError($"EndScene：主菜单场景 {mainMenuSceneName} 不在Build Settings中！");
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// 清掉跨场景常驻对象（DontDestroyOnLoad 根对象），
    /// 避免玩家与游戏 UI 残留在结束场景上
    /// </summary>
    void CleanupPersistentObjects()
    {
        GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject go in all)
        {
            if (go == null) continue;
            if (go.transform.parent != null) continue;                // 只处理根对象
            if (!go.scene.IsValid()) continue;                        // 跳过工程资产
            if (go.scene.name != "DontDestroyOnLoad") continue;       // 只处理跨场景常驻对象
            Destroy(go);
        }
    }
}
