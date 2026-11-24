using Interaction;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using WaxBall;

/// <summary>
/// 에디터(dev 씬)에서 핸드트래킹 경로를 시험하는 가짜 손을 만든다. 메뉴 `WaxBall > Add Fake Hand Pipeline To Dev Scene`.
/// 가짜 손(FakeFingertipSource)이 진짜 핸드트래킹과 같은 경로(필터 → 공 중심 계산 → ...)로 들어가고,
/// FakeHandCenter(진짜 공 중심)를 Scene 뷰에서 옮겨 Follow Hand를 시험한다.
/// 먼저 `WaxBall > Create Dev Test Objects`로 공과 Slider를 만들어 둬야 한다. 기존 가짜 손가락(FakeFingers)은 같은 입력과 겹치지 않게 꺼 둔다.
/// </summary>
public static class WaxBallHandPipelineBuilder
{
    private const float CenterMarkerDiameter = 0.02f;
    private const float TipMarkerDiameter = 0.012f;
    private const float FilteredMarkerDiameter = 0.007f;

    [MenuItem("WaxBall/Add Fake Hand Pipeline To Dev Scene")]
    private static void Create()
    {
        var machine = Object.FindObjectOfType<WaxBallStateMachine>();
        if (machine == null)
        {
            Debug.LogError("공을 찾지 못했다. 먼저 WaxBall > Create Dev Test Objects를 실행한다.");
            return;
        }

        var ball = machine.transform;
        var tuning = WaxBallTuningTools.LoadOrCreate();
        var material = WaxBallQuestSceneBuilder.LoadOrCreateMarkerMaterial();

        // 여러 번 실행해도 중복되지 않게 이전 결과를 지운다. 기존 가짜 손가락은 끈다(같은 SetContact 입력과 겹치지 않게).
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t != null && t.parent == null && t.name == "FakeHand") Object.DestroyImmediate(t.gameObject);
        }
        var oldFingers = FindRoot("FakeFingers");
        if (oldFingers != null) oldFingers.SetActive(false);

        var root = new GameObject("FakeHand");

        // 진짜 공 중심. 공에서 오른쪽 5cm, 위 3cm에서 시작해서 Follow Hand를 켜면 공이 이쪽으로 따라온다.
        var center = CreateMarker("FakeHandCenter", root.transform, CenterMarkerDiameter, material);
        center.position = ball.position + new Vector3(0.05f, 0.03f, 0f);

        var fake = root.AddComponent<FakeFingertipSource>();
        WaxBallDevSceneBuilder.SetRef(fake, "tuning", tuning);
        WaxBallDevSceneBuilder.SetRef(fake, "stateMachine", machine);
        WaxBallDevSceneBuilder.SetRef(fake, "handCenter", center);
        SetMarkers(fake, root.transform, "Tip", TipMarkerDiameter, material);

        var filtered = root.AddComponent<FilteredFingertips>();
        WaxBallDevSceneBuilder.SetRef(filtered, "tuning", tuning);
        WaxBallDevSceneBuilder.SetRef(filtered, "source", fake);
        SetMarkers(filtered, root.transform, "FilteredTip", FilteredMarkerDiameter, material);

        var estimator = root.AddComponent<BallCenterEstimator>();
        WaxBallDevSceneBuilder.SetRef(estimator, "tuning", tuning);
        WaxBallDevSceneBuilder.SetRef(estimator, "source", filtered);
        WaxBallDevSceneBuilder.SetRef(estimator, "ball", ball);
        WaxBallDevSceneBuilder.SetRef(estimator, "truthCenter", center);

        // 리셋 핀치(에디터에서는 P 키를 1초 누름). Slider 씬은 슬라이더도 0으로 돌린다.
        var gesture = root.AddComponent<HandPinchGesture>();
        WaxBallDevSceneBuilder.SetRef(gesture, "tuning", tuning);
        var ring = new GameObject("ResetRing").AddComponent<PinchProgressRing>();
        ring.transform.SetParent(root.transform, false);
        WaxBallDevSceneBuilder.SetRef(gesture, "resetRing", ring);
        var sliderInput = Object.FindObjectOfType<SliderGripInput>();
        if (sliderInput != null) UnityEventTools.AddPersistentListener(gesture.OnReset, sliderInput.ResetAll);
        else UnityEventTools.AddPersistentListener(gesture.OnReset, machine.ResetBall);

        EnlargeDebugLabel();

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = center.gameObject;
        Debug.Log("가짜 손을 만들었다. FakeHandCenter(진짜 공 중심)를 Scene 뷰에서 옮기면 공이 따라온다. P 키를 1초 누르면 리셋한다.");
    }

    private static Transform CreateMarker(string name, Transform parent, float diameter, Material material)
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = name;
        Object.DestroyImmediate(marker.GetComponent<Collider>());
        marker.transform.SetParent(parent, false);
        marker.transform.localScale = Vector3.one * diameter;
        if (material != null) marker.GetComponent<Renderer>().sharedMaterial = material;
        return marker.transform;
    }

    // 엄지, 검지, 중지 순서의 표시용 구 세 개를 만들어 컴포넌트의 markers 배열에 넣는다.
    private static void SetMarkers(Component component, Transform parent, string prefix, float diameter, Material material)
    {
        var serialized = new SerializedObject(component);
        var markers = serialized.FindProperty("markers");
        markers.arraySize = WaxBallStateMachine.FingerCount;
        for (int i = 0; i < WaxBallStateMachine.FingerCount; i++)
            markers.GetArrayElementAtIndex(i).objectReferenceValue = CreateMarker(prefix + (WaxFinger)i, parent, diameter, material);
        serialized.ApplyModifiedProperties();
    }

    // 디버그 줄이 늘어서 dev 씬의 글자 영역을 키운다.
    private static void EnlargeDebugLabel()
    {
        foreach (var label in Object.FindObjectsOfType<TextMeshProUGUI>(true))
        {
            if (label.name != "StateLabel") continue;
            ((RectTransform)label.transform).sizeDelta = new Vector2(500f, 420f);
        }
    }

    private static GameObject FindRoot(string name)
    {
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
            if (t.parent == null && t.name == name) return t.gameObject;
        return null;
    }
}
