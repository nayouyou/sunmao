using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

/// <summary>
/// 开场场景（开始新游戏后进入）
/// 流程：
///   点击信封 → 播放信封开启动画 + 信封向下缓动（信纸留在原处）
///   点击信纸 → 播放信纸展开动画并显示信上文字
///   再点击 → 进入游戏主场景
/// 信封/信纸样式未导入（留 Image），两段动画未导入（留 Animator，与角色行走动画同一套工具）
/// 右下角提示文字按阶段切换
/// </summary>
public class OpeningScene : MonoBehaviour
{
    [Header("结束后进入的场景")]
    public string gameSceneName = "playScenes";

    [Header("界面部件")]
    public GameObject envelope;
    public RectTransform envelopeRect;
    public GameObject letter;
    public GameObject letterText;
    public GameObject fullScreenClick;
    public TMP_Text hintText;

    [Header("分阶段提示文案（右下角）")]
    public string envelopeHint = "点击继续";
    public string letterHint = "点击继续";
    public string finishHint = "点击继续";

    [Header("信封向下缓动")]
    public Vector2 envelopeTargetOffset = new Vector2(0f, -320f);
    public float moveDuration = 0.6f;

    [Header("动画（未导入，Animator 留空即跳过）")]
    public Animator envelopeAnimator;
    public string envelopeOpenTrigger = "open";
    public Animator letterAnimator;
    public string letterOpenTrigger = "open";

    private enum Stage { Envelope, Letter, Finish }
    private Stage _stage = Stage.Envelope;
    private Coroutine _moveRoutine;

    void Start()
    {
        _stage = Stage.Envelope;
        if (letter != null) letter.SetActive(false);
        if (letterText != null) letterText.SetActive(false);
        if (fullScreenClick != null) fullScreenClick.SetActive(false);
        SetHint(envelopeHint);

        // 按钮点击在代码里绑定，无需在 Inspector 配置
        if (envelope != null)
        {
            Button b = envelope.GetComponent<Button>();
            if (b != null) b.onClick.AddListener(OnEnvelopeClicked);
        }
        if (letter != null)
        {
            Button b = letter.GetComponent<Button>();
            if (b != null) b.onClick.AddListener(OnLetterClicked);
        }
        if (fullScreenClick != null)
        {
            Button b = fullScreenClick.GetComponent<Button>();
            if (b != null) b.onClick.AddListener(OnFinishClicked);
        }
    }

    /// <summary>点击信封：播放开启动画并向下移出，信纸留在原处</summary>
    public void OnEnvelopeClicked()
    {
        if (_stage != Stage.Envelope) return;
        _stage = Stage.Letter;

        if (envelope != null)
        {
            Button btn = envelope.GetComponent<Button>();
            if (btn != null) btn.interactable = false;
        }

        // 打开信封：信封收起，信纸展开
        if (envelope != null) envelope.SetActive(false);
        if (letter != null) letter.SetActive(true);

        SetHint(letterHint);
    }

    /// <summary>点击信纸：播放展开动画并显示信上文字</summary>
    public void OnLetterClicked()
    {
        if (_stage != Stage.Letter) return;
        _stage = Stage.Finish;

        if (letter != null)
        {
            Button btn = letter.GetComponent<Button>();
            if (btn != null) btn.interactable = false;
        }

        PlayTrigger(letterAnimator, letterOpenTrigger);

        if (letterText != null) letterText.SetActive(true);
        if (fullScreenClick != null) fullScreenClick.SetActive(true);
        SetHint(finishHint);
    }

    /// <summary>展开后再点击：进入游戏主场景</summary>
    public void OnFinishClicked()
    {
        if (_stage != Stage.Finish) return;

        // 标记开场对话：进入主场景后由 IntroDialog 自动弹出
        IntroDialog.Pending = true;
        SceneManager.sceneLoaded -= OnGameSceneLoaded;
        SceneManager.sceneLoaded += OnGameSceneLoaded;

        AsyncOperation op = SceneManager.LoadSceneAsync(gameSceneName, LoadSceneMode.Single);
        if (op == null)
            Debug.LogError($"OpeningScene：场景 {gameSceneName} 不在Build Settings中！");
    }

    /// <summary>主场景加载完成后，把开场对话脚本挂到跨场景的 GlobalCanvasRoot 上（无需在场景里手动配置）</summary>
    void OnGameSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != gameSceneName) return;
        SceneManager.sceneLoaded -= OnGameSceneLoaded;

        GlobalUIRef ui = GlobalUIRef.Instance;
        if (ui == null)
        {
            Debug.LogError("OpeningScene：GlobalUIRef 缺失，无法启动开场对话");
            return;
        }
        if (ui.GetComponent<IntroDialog>() == null)
            ui.gameObject.AddComponent<IntroDialog>();
    }

    void PlayTrigger(Animator animator, string trigger)
    {
        if (animator == null) return;
        if (animator.runtimeAnimatorController == null) return;
        if (string.IsNullOrEmpty(trigger)) return;
        animator.SetTrigger(trigger);
    }

    IEnumerator MoveEnvelope()
    {
        if (envelopeRect == null) yield break;

        Vector2 start = envelopeRect.anchoredPosition;
        Vector2 target = start + envelopeTargetOffset;
        float t = 0f;

        while (t < moveDuration)
        {
            t += Time.deltaTime;
            float k = moveDuration > 0f ? Mathf.Clamp01(t / moveDuration) : 1f;
            float eased = k * k * (3f - 2f * k);
            envelopeRect.anchoredPosition = Vector2.Lerp(start, target, eased);
            yield return null;
        }

        envelopeRect.anchoredPosition = target;
        _moveRoutine = null;
    }

    void SetHint(string text)
    {
        if (hintText != null) hintText.text = text;
    }
}
