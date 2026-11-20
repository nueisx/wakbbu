using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WaxBall;

/// <summary>
/// 튜닝 설정 파일(Assets/Settings/WaxBallTuning.asset)을 만들고, 열려 있는 씬의 컴포넌트에 연결한다.
/// 여러 번 실행해도 같은 파일을 쓰고 이미 있는 값은 덮어쓰지 않는다. dev 씬과 Quest 씬이 같은 파일을 쓴다.
/// </summary>
public static class WaxBallTuningTools
{
    private const string AssetPath = "Assets/Settings/WaxBallTuning.asset";

    [MenuItem("WaxBall/Apply Tuning To Open Scene")]
    private static void Apply()
    {
        int count = AssignToOpenScene();
        Debug.Log("튜닝 설정 파일을 " + count + "개 컴포넌트에 연결했다. 씬을 저장한다.");
        EditorSceneManager.SaveOpenScenes();
    }

    /// <summary>설정 파일이 없으면 코드 기본값으로 만든다.</summary>
    internal static WaxBallTuning LoadOrCreate()
    {
        var tuning = AssetDatabase.LoadAssetAtPath<WaxBallTuning>(AssetPath);
        if (tuning != null) return tuning;

        Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
        tuning = ScriptableObject.CreateInstance<WaxBallTuning>();
        AssetDatabase.CreateAsset(tuning, AssetPath);
        AssetDatabase.SaveAssets();
        return tuning;
    }

    /// <summary>열려 있는 씬의 상태 머신, 파손 모델, 시각 효과에 설정 파일을 연결한다. 연결한 컴포넌트 수를 돌려준다.</summary>
    internal static int AssignToOpenScene()
    {
        var tuning = LoadOrCreate();
        int count = 0;
        count += Assign(Object.FindObjectsOfType<WaxBallStateMachine>(true), tuning);
        count += Assign(Object.FindObjectsOfType<WaxBallDamage>(true), tuning);
        count += Assign(Object.FindObjectsOfType<WaxBallShellVisual>(true), tuning);
        return count;
    }

    private static int Assign(Object[] components, WaxBallTuning tuning)
    {
        foreach (var component in components)
        {
            var serialized = new SerializedObject(component);
            serialized.FindProperty("tuning").objectReferenceValue = tuning;
            serialized.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(((Component)component).gameObject.scene);
        }
        return components.Length;
    }
}
