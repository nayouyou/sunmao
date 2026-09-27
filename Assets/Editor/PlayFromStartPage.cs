using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 让编辑器按 Play 时永远先加载开始页面（主菜单），而不是当前打开的场景。
/// 打开工程时自动设一次；也可用 Tools 菜单手动设置或清除。
/// </summary>
public static class PlayFromStartPage
{
    const string StartScenePath = "Assets/Scenes/mainMenu.unity";

    [InitializeOnLoadMethod]
    static void AutoSetOnLoad()
    {
        if (EditorSceneManager.playModeStartScene == null) SetStartScene();
    }

    [MenuItem("Tools/从开始页面开始播放")]
    public static void SetStartScene()
    {
        SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(StartScenePath);
        if (scene == null)
        {
            Debug.LogError("PlayFromStartPage：找不到场景 " + StartScenePath);
            return;
        }
        EditorSceneManager.playModeStartScene = scene;
        Debug.Log("PlayFromStartPage：按 Play 时将先加载 " + StartScenePath);
    }

    [MenuItem("Tools/清除播放起点（改为运行当前场景）")]
    public static void ClearStartScene()
    {
        EditorSceneManager.playModeStartScene = null;
        Debug.Log("PlayFromStartPage：已清除播放起点，按 Play 将运行当前打开的场景");
    }
}
