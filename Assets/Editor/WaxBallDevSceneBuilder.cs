using Interaction;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WaxBall;

public static class WaxBallDevSceneBuilder
{
    private const float BallDiameter = 0.07f;
    private const float CameraDistance = 0.35f;

    // 가짜 손가락 3개의 기본 위치(공 중심에서 본 방향). 세 손가락으로 공을 집듯이 쥔 모양이다:
    // 엄지(Thumb)는 왼쪽, 검지(Index)와 중지(Middle)는 반대편(오른쪽)에서 위아래로 나란히 누른다.
    private static readonly Vector3[] FingerDirections =
    {
        new Vector3(-0.95f, -0.1f, -0.3f),
        new Vector3(0.8f, 0.4f, -0.45f),
        new Vector3(0.8f, -0.2f, -0.55f),
    };

    [MenuItem("WaxBall/Create Dev Test Objects")]
    private static void Create()
    {
        // 여러 번 실행해도 중복되지 않도록 기존 테스트 오브젝트를 먼저 지운다.
        foreach (var name in new[] { "WaxBall", "DevCanvas", "SliderGripInput", "FakeFingers" })
        {
            var existing = GameObject.Find(name);
            while (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
                existing = GameObject.Find(name);
            }
        }
        RemoveStaleFakeFingers();

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "WaxBall";
        ball.transform.position = new Vector3(0f, 0f, 3f);
        ball.transform.localScale = Vector3.one * BallDiameter;
        var machine = ball.AddComponent<WaxBallStateMachine>();
        ball.GetComponent<Renderer>().sharedMaterial = LoadOrCreateShellMaterial();
        var visual = ball.AddComponent<WaxBallShellVisual>();
        SetRef(visual, "stateMachine", machine);
        var contacts = ball.AddComponent<WaxBallContacts>();
        SetRef(contacts, "stateMachine", machine);
        var damage = ball.AddComponent<WaxBallDamage>();
        SetRef(damage, "stateMachine", machine);
        SetRef(damage, "contacts", contacts);
        SetRef(visual, "damage", damage);
        CreateFakeFingers(ball.transform, contacts, machine);

        var canvasGo = new GameObject("DevCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        if (Object.FindObjectOfType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        var sliderGo = DefaultControls.CreateSlider(new DefaultControls.Resources());
        sliderGo.name = "GripSlider";
        sliderGo.transform.SetParent(canvasGo.transform, false);
        var sliderRect = (RectTransform)sliderGo.transform;
        sliderRect.anchorMin = sliderRect.anchorMax = new Vector2(0.5f, 0f);
        sliderRect.anchoredPosition = new Vector2(0f, 60f);
        sliderRect.sizeDelta = new Vector2(500f, 30f);
        var slider = sliderGo.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;

        var inputGo = new GameObject("SliderGripInput");
        var input = inputGo.AddComponent<SliderGripInput>();
        SetRef(input, "stateMachine", machine);
        SetRef(input, "gripSlider", slider);

        var buttonGo = DefaultControls.CreateButton(new DefaultControls.Resources());
        buttonGo.name = "ResetButton";
        buttonGo.transform.SetParent(canvasGo.transform, false);
        var buttonRect = (RectTransform)buttonGo.transform;
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.anchoredPosition = new Vector2(0f, 120f);
        buttonRect.sizeDelta = new Vector2(160f, 40f);
        buttonGo.GetComponentInChildren<Text>().text = "Reset (R)";
        UnityEditor.Events.UnityEventTools.AddPersistentListener(buttonGo.GetComponent<Button>().onClick, input.ResetAll);

        var labelGo = new GameObject("StateLabel", typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(canvasGo.transform, false);
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.fontSize = 28f;
        label.alignment = TextAlignmentOptions.TopLeft;
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(0f, 1f);
        labelRect.pivot = new Vector2(0f, 1f);
        labelRect.anchoredPosition = new Vector2(20f, -20f);
        labelRect.sizeDelta = new Vector2(500f, 260f);
        var debugText = labelGo.AddComponent<WaxBallStateDebugText>();
        SetRef(debugText, "stateMachine", machine);
        SetRef(debugText, "label", label);
        SetRef(debugText, "damage", damage);

        PlaceCamera(ball.transform.position);

        Undo.RegisterCreatedObjectUndo(ball, "Create WaxBall Dev Objects");
        Selection.activeGameObject = ball;
    }

    // 예전 구조(손가락 5개, FakeFinger0~4)나 Undo 때문에 FakeFingers 밖에 남은 가짜 손가락, 그리고
    // FakeFingers 아래에 있지만 Thumb/Index/Middle이 아닌 오브젝트를 지운다.
    private static void RemoveStaleFakeFingers()
    {
        var valid = new System.Collections.Generic.HashSet<string>();
        foreach (WaxFinger finger in System.Enum.GetValues(typeof(WaxFinger))) valid.Add("Fake" + finger);

        var stale = new System.Collections.Generic.List<GameObject>();
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            bool underRoot = t.parent != null && t.parent.name == "FakeFingers";
            bool legacyName = t.name.StartsWith("FakeFinger") && t.name != "FakeFingers";
            if ((underRoot && !valid.Contains(t.name)) || legacyName) stale.Add(t.gameObject);
        }

        foreach (var go in stale)
            if (go != null) Undo.DestroyObjectImmediate(go);
    }

    // 공 주변에 가짜 손가락 3개를 만든다. Scene 뷰에서 Move 도구로 드래그해 접촉 위치를 옮긴다.
    // 공의 자식으로 두지 않는다(공은 grip에 따라 찌그러지므로 자식도 같이 변형된다).
    internal static void CreateFakeFingers(Transform ball, WaxBallContacts contacts, WaxBallStateMachine machine)
    {
        var root = new GameObject("FakeFingers");
        var fake = root.AddComponent<FakeFingerContacts>();
        SetRef(fake, "contacts", contacts);
        SetRef(fake, "stateMachine", machine);

        float radius = ball.localScale.x * 0.5f;
        var serialized = new SerializedObject(fake);
        var fingersProperty = serialized.FindProperty("fingers");
        fingersProperty.arraySize = FingerDirections.Length;

        for (int i = 0; i < FingerDirections.Length; i++)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Fake" + (WaxFinger)i;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(root.transform, false);
            marker.transform.localScale = Vector3.one * radius * 0.3f;
            marker.transform.position = ball.position + FingerDirections[i].normalized * radius * 1.15f;
            fingersProperty.GetArrayElementAtIndex(i).objectReferenceValue = marker.transform;
        }
        serialized.ApplyModifiedProperties();

        Undo.RegisterCreatedObjectUndo(root, "Create Fake Fingers");
    }

    // 실제 Quest 시점처럼 공 바로 앞(30~40cm)에서 바라보게 카메라를 맞춘다.
    private static void PlaceCamera(Vector3 ballPosition)
    {
        var cam = Camera.main;
        if (cam == null) return;

        Undo.RecordObject(cam.transform, "Place Dev Camera");
        Undo.RecordObject(cam, "Place Dev Camera");
        cam.transform.SetPositionAndRotation(ballPosition + Vector3.back * CameraDistance, Quaternion.identity);
        cam.nearClipPlane = 0.01f;
    }

    internal static Material LoadOrCreateShellMaterial()
    {
        const string dir = "Assets/Materials";
        const string path = dir + "/WaxShell.mat";

        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        var shader = Shader.Find("WaxBall/WaxShell");
        if (shader == null)
        {
            Debug.LogError("WaxBall/WaxShell 셰이더를 찾지 못했다. 셰이더 컴파일 에러가 없는지 확인한다.");
            return null;
        }

        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Materials");
        material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    internal static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }
}
