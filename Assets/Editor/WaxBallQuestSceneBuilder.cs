using System.IO;
using Interaction;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WaxBall;

/// <summary>
/// Quest 실기기용 왁뿌볼 테스트 씬(Dev_WaxBallQuest.unity)을 만든다.
/// 패스스루가 켜진 Dev_QuestBuildTest 씬을 바탕으로 큐브 대신 왁뿌볼을 눈앞 35cm에 둔다.
/// grip은 오른손 컨트롤러 트리거, 리셋은 A 버튼이고, 디버그 텍스트는 공 옆 월드 공간에 작게 띄운다.
/// 에디터용 Slider 씬(dev_waxball)은 건드리지 않는다.
/// </summary>
public static class WaxBallQuestSceneBuilder
{
    private const string SourceScenePath = "Assets/Scenes/Dev_QuestBuildTest.unity";
    private const string TargetScenePath = "Assets/Scenes/Dev_WaxBallQuest.unity";

    private const float BallDiameter = 0.07f;
    private const float BallDistance = 0.35f;

    // 디버그 텍스트: 캔버스 한 변 픽셀과 월드 크기(m/픽셀). 500px x 0.0003 = 15cm.
    private const float CanvasScale = 0.0003f;
    private static readonly Vector2 CanvasSize = new Vector2(500f, 300f);
    private const float CanvasOffsetX = 0.12f;

    [MenuItem("WaxBall/Create Quest WaxBall Scene")]
    private static void Create()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // 이미 있으면 같은 파일(GUID 유지)을 다시 열어 오브젝트만 새로 만든다.
        if (!File.Exists(TargetScenePath) && !AssetDatabase.CopyAsset(SourceScenePath, TargetScenePath))
        {
            Debug.LogError(SourceScenePath + " 를 복사하지 못했다. Dev_QuestBuildTest 씬이 있는지 확인한다.");
            return;
        }

        var scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);

        // 큐브와 포스트 프로세싱(Quest 성능)을 지우고, 여러 번 실행해도 중복되지 않게 이전 결과도 지운다.
        foreach (var name in new[] { "Cube", "Global Volume", "WaxBall", "WaxBallDebug", "QuestInput", "FakeFingers" })
            DestroyAllNamed(name);

        var ball = CreateBall(out WaxBallStateMachine machine, out WaxBallContacts contacts, out WaxBallDamage damage);
        WaxBallTuningTools.AssignToOpenScene();
        CreateInput(machine);
        CreateFakeFingersInactive(ball.transform, contacts, machine);
        CreateDebugCanvas(ball.transform, machine, damage);

        AddToBuildSettings();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = ball;
        Debug.Log("Dev_WaxBallQuest 씬을 만들었다. Build Settings에는 이 씬만 켜져 있다.");
    }

    private static GameObject CreateBall(out WaxBallStateMachine machine, out WaxBallContacts contacts, out WaxBallDamage damage)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "WaxBall";
        Object.DestroyImmediate(ball.GetComponent<Collider>());
        ball.transform.position = new Vector3(0f, 0f, BallDistance);
        ball.transform.localScale = Vector3.one * BallDiameter;
        ball.GetComponent<Renderer>().sharedMaterial = WaxBallDevSceneBuilder.LoadOrCreateShellMaterial();

        machine = ball.AddComponent<WaxBallStateMachine>();
        var visual = ball.AddComponent<WaxBallShellVisual>();
        contacts = ball.AddComponent<WaxBallContacts>();
        damage = ball.AddComponent<WaxBallDamage>();

        WaxBallDevSceneBuilder.SetRef(visual, "stateMachine", machine);
        WaxBallDevSceneBuilder.SetRef(visual, "damage", damage);
        WaxBallDevSceneBuilder.SetRef(contacts, "stateMachine", machine);
        WaxBallDevSceneBuilder.SetRef(damage, "stateMachine", machine);
        WaxBallDevSceneBuilder.SetRef(damage, "contacts", contacts);
        return ball;
    }

    private static void CreateInput(WaxBallStateMachine machine)
    {
        var input = new GameObject("QuestInput").AddComponent<QuestControllerInput>();
        WaxBallDevSceneBuilder.SetRef(input, "stateMachine", machine);
    }

    // 가짜 손가락은 꺼 둔다. 켜지 않으면 접촉점 입력이 없어서 공 전체가 깨진다(셰이더가 가장 무거운 경우).
    // 켜면 고정된 세 손가락 자리 주변에서만 깨지는 접촉점 방식을 Quest에서 볼 수 있다.
    private static void CreateFakeFingersInactive(Transform ball, WaxBallContacts contacts, WaxBallStateMachine machine)
    {
        WaxBallDevSceneBuilder.CreateFakeFingers(ball, contacts, machine);
        var fakeFingers = GameObject.Find("FakeFingers");
        if (fakeFingers != null) fakeFingers.SetActive(false);
    }

    private static void CreateDebugCanvas(Transform ball, WaxBallStateMachine machine, WaxBallDamage damage)
    {
        var canvasGo = new GameObject("WaxBallDebug", typeof(Canvas));
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

        var canvasRect = (RectTransform)canvasGo.transform;
        canvasRect.sizeDelta = CanvasSize;
        canvasRect.localScale = Vector3.one * CanvasScale;
        canvasRect.position = ball.position + new Vector3(CanvasOffsetX, 0f, 0f);

        // 밝은 방에서도 읽히도록 반투명한 검은 배경을 깐다.
        var background = new GameObject("Background", typeof(Image));
        background.transform.SetParent(canvasRect, false);
        background.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        Stretch((RectTransform)background.transform, 0f);

        var labelGo = new GameObject("Label", typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(canvasRect, false);
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.fontSize = 30f;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.color = Color.white;
        Stretch((RectTransform)labelGo.transform, 12f);

        canvasGo.AddComponent<FaceHeadset>();

        var debugText = canvasGo.AddComponent<WaxBallStateDebugText>();
        WaxBallDevSceneBuilder.SetRef(debugText, "stateMachine", machine);
        WaxBallDevSceneBuilder.SetRef(debugText, "label", label);
        WaxBallDevSceneBuilder.SetRef(debugText, "damage", damage);
        var showFps = new SerializedObject(debugText);
        showFps.FindProperty("showFps").boolValue = true;
        showFps.ApplyModifiedProperties();
    }

    // Build Settings에서 이 씬만 켠다(Quest에서 바로 Build And Run 하면 이 씬이 실행된다).
    private static void AddToBuildSettings()
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>();
        foreach (var existing in EditorBuildSettings.scenes)
        {
            if (existing.path == TargetScenePath) continue;
            scenes.Add(new EditorBuildSettingsScene(existing.path, false));
        }
        scenes.Insert(0, new EditorBuildSettingsScene(TargetScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void Stretch(RectTransform rect, float margin)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(margin, margin);
        rect.offsetMax = new Vector2(-margin, -margin);
    }

    // 꺼져 있는 오브젝트(FakeFingers)도 찾기 위해 비활성 오브젝트까지 검색한다. 씬의 루트 오브젝트만 지운다.
    private static void DestroyAllNamed(string name)
    {
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t != null && t.parent == null && t.name == name) Object.DestroyImmediate(t.gameObject);
        }
    }
}
