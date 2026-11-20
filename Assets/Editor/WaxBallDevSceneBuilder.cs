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

    [MenuItem("WaxBall/Create Dev Test Objects")]
    private static void Create()
    {
        // 여러 번 실행해도 중복되지 않도록 기존 테스트 오브젝트를 먼저 지운다.
        foreach (var name in new[] { "WaxBall", "DevCanvas", "SliderGripInput" })
        {
            var existing = GameObject.Find(name);
            while (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
                existing = GameObject.Find(name);
            }
        }

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "WaxBall";
        ball.transform.position = new Vector3(0f, 0f, 3f);
        ball.transform.localScale = Vector3.one * BallDiameter;
        var machine = ball.AddComponent<WaxBallStateMachine>();
        ball.GetComponent<Renderer>().sharedMaterial = LoadOrCreateShellMaterial();
        var visual = ball.AddComponent<WaxBallShellVisual>();
        SetRef(visual, "stateMachine", machine);

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
        labelRect.sizeDelta = new Vector2(400f, 190f);
        var debugText = labelGo.AddComponent<WaxBallStateDebugText>();
        SetRef(debugText, "stateMachine", machine);
        SetRef(debugText, "label", label);

        PlaceCamera(ball.transform.position);

        Undo.RegisterCreatedObjectUndo(ball, "Create WaxBall Dev Objects");
        Selection.activeGameObject = ball;
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

    private static Material LoadOrCreateShellMaterial()
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

    private static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }
}
