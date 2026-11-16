using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Build executable khusus training RL (hanya scene arena), dipakai mlagents-learn --env.
//   Unity.exe -batchmode -quit -projectPath . -executeMethod TrainingPlayerBuild.Build -buildPath <folder>
public static class TrainingPlayerBuild
{
    [MenuItem("Stealth/FP - Build Executable Training RL")]
    public static void BuildMenu()
    {
        string folder = EditorUtility.SaveFolderPanel("Folder build training", "", "RLTrainingBuild");

        if (!string.IsNullOrEmpty(folder))
            Build(folder);
    }

    public static void Build()
    {
        string folder = "RLTrainingBuild";
        string[] args = System.Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-buildPath") folder = args[i + 1];

        Build(folder);
    }

    private static void Build(string folder)
    {
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Training/RL_GuardSearch.unity" },
            locationPathName = System.IO.Path.Combine(folder, "GuardSearchTraining.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        PlayerSettings.runInBackground = true;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 640;
        PlayerSettings.defaultScreenHeight = 360;

        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[TrainingPlayerBuild] " + report.summary.result + " -> " + options.locationPathName +
                  " (" + report.summary.totalErrors + " error)");

        if (Application.isBatchMode)
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
