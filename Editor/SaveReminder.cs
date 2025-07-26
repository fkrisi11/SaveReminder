#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public class SaveReminder : EditorWindow
{
    // Instance fields for the EditorWindow — loaded in OnEnable.
    private bool windowEnabled;
    private int windowWarningTimeSeconds;
    private bool windowFlashText;
    private float windowFlashSpeed;
    private Color windowTextColor;
    private int windowFontSize;
    private float windowLastFlashSpeed;

    // Static fields for SceneView/GameView drawing.
    private static bool enabled;
    private static int warningTimeSeconds;
    private static bool flashText;
    private static float flashSpeed;
    private static Color textColor;
    private static int fontSize;

    private static double lastUnsavedChangeTime = -1;

    private const string PrefPrefix = "SaveReminder_";

    static SaveReminder()
    {
        LoadPrefsToStatic();

        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.update += UpdateGameView;

        // Reset timer when saving or compiling.
        EditorSceneManager.sceneSaved += (scene) => ResetTimerIfNoDirty();
        AssemblyReloadEvents.afterAssemblyReload += ResetTimerIfNoDirty;

        EditorSceneManager.sceneDirtied += (scene) =>
        {
            if (lastUnsavedChangeTime < 0 && AnySceneDirty())
                lastUnsavedChangeTime = EditorApplication.timeSinceStartup;
        };
    }

    [MenuItem("TohruTheDragon/Save Reminder")]
    public static void ShowWindow()
    {
        var window = GetWindow<SaveReminder>("Save Reminder");
        window.minSize = new Vector2(300, 280);
    }

    private void OnEnable()
    {
        LoadWindowPrefs();
    }

    private void OnDisable()
    {
        // Auto-save when window is closed
        SaveAndApplySettings();
    }

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField("Save Reminder Settings", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Enable/Disable toggle with prominent styling
        bool previousEnabled = windowEnabled;

        GUIStyle toggleStyle = new GUIStyle(GUI.skin.toggle);
        toggleStyle.fontSize = 14;
        toggleStyle.fontStyle = FontStyle.Bold;

        EditorGUILayout.BeginHorizontal();
        windowEnabled = EditorGUILayout.Toggle(windowEnabled, toggleStyle, GUILayout.Width(20));

        GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel);
        labelStyle.fontSize = 14;
        labelStyle.normal.textColor = windowEnabled ? Color.green : Color.red;

        EditorGUILayout.LabelField(windowEnabled ? "Save Reminder ENABLED" : "Save Reminder DISABLED", labelStyle);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // Settings section - disable GUI if not enabled
        EditorGUI.BeginDisabledGroup(!windowEnabled);

        windowWarningTimeSeconds = EditorGUILayout.IntField("Warning After (sec)", windowWarningTimeSeconds);
        windowWarningTimeSeconds = Mathf.Max(0, windowWarningTimeSeconds);

        bool previousFlash = windowFlashText;
        windowFlashText = EditorGUILayout.Toggle("Flash Text", windowFlashText);

        if (windowFlashText)
        {
            EditorGUILayout.HelpBox("The flashing animation only works correctly, if the scene is set to Always Refresh", MessageType.Warning);
        }

        if (windowFlashText)
        {
            if (!previousFlash)
            {
                // Restore last used speed if re-enabling
                if (windowFlashSpeed <= 0f)
                    windowFlashSpeed = windowLastFlashSpeed > 0f ? windowLastFlashSpeed : 2.0f;
            }

            windowFlashSpeed = EditorGUILayout.Slider("Flash Speed", windowFlashSpeed, 0.1f, 5f);
        }
        else
        {
            if (windowFlashSpeed > 0f)
            {
                // Save current speed for next time
                windowLastFlashSpeed = windowFlashSpeed;
            }

            windowFlashSpeed = 0f;
        }

        windowTextColor = EditorGUILayout.ColorField("Text Color", windowTextColor);
        windowFontSize = EditorGUILayout.IntSlider("Font Size", windowFontSize, 10, 50);

        EditorGUI.EndDisabledGroup();

        // Auto-save when any setting changes
        if (EditorGUI.EndChangeCheck())
        {
            SaveAndApplySettings();
        }
    }

    private void SaveAndApplySettings()
    {
        SavePrefsFromWindow();
        ApplyPrefsToStatic();

        string status = windowEnabled ? "enabled" : "disabled";
    }

    private void LoadWindowPrefs()
    {
        windowEnabled = EditorPrefs.GetBool(PrefPrefix + "Enabled", true);
        windowWarningTimeSeconds = EditorPrefs.GetInt(PrefPrefix + "WarningTime", 600);
        windowFlashText = EditorPrefs.GetBool(PrefPrefix + "Flash", false);
        windowFlashSpeed = EditorPrefs.GetFloat(PrefPrefix + "FlashSpeed", 2.0f);
        windowLastFlashSpeed = EditorPrefs.GetFloat(PrefPrefix + "LastFlashSpeed", 2.0f);

        string colorStr = EditorPrefs.GetString(PrefPrefix + "TextColor", ColorUtility.ToHtmlStringRGBA(Color.red));
        if (ColorUtility.TryParseHtmlString("#" + colorStr, out Color col))
            windowTextColor = col;
        else
            windowTextColor = Color.red;

        windowFontSize = EditorPrefs.GetInt(PrefPrefix + "FontSize", 20);
    }

    private static string FormatTime(double elapsed)
    {
        int days = (int)(elapsed / 86400);
        elapsed %= 86400;
        int hours = (int)(elapsed / 3600);
        elapsed %= 3600;
        int minutes = (int)(elapsed / 60);
        int secs = (int)(elapsed % 60);

        return days > 0 ? $"{days}d {hours}h {minutes}m {secs}s" :
               hours > 0 ? $"{hours}h {minutes}m {secs}s" :
               minutes > 0 ? $"{minutes}m {secs}s" :
               $"{secs}s";
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        // Early exit if disabled
        if (!enabled)
            return;

        if (EditorApplication.isPlaying)
            return;

        if (!ShouldDisplay())
            return;

        double elapsed = EditorApplication.timeSinceStartup - lastUnsavedChangeTime;
        string message = GetMessage(elapsed);

        GUIStyle style = CreateStyle(elapsed);

        Handles.BeginGUI();
        EditorGUI.DropShadowLabel(new Rect(0, 0, sceneView.position.width, 50), message, style);
        Handles.EndGUI();
    }

    private static void UpdateGameView()
    {
        // Early exit if disabled
        if (!enabled)
            return;

        if (EditorApplication.isPlaying)
            return;

        if (!ShouldDisplay())
            return;

        var gameViews = Resources.FindObjectsOfTypeAll(typeof(EditorWindow));
        foreach (EditorWindow win in gameViews)
        {
            if (win.GetType().ToString() == "UnityEditor.GameView")
                win.Repaint();
        }
    }

    private static bool ShouldDisplay()
    {
        // Early exit if disabled
        if (!enabled)
        {
            lastUnsavedChangeTime = -1;
            return false;
        }

        if (!AnySceneDirty())
        {
            lastUnsavedChangeTime = -1;
            return false;
        }

        if (lastUnsavedChangeTime < 0)
        {
            lastUnsavedChangeTime = EditorApplication.timeSinceStartup;
            return false;
        }

        double elapsed = EditorApplication.timeSinceStartup - lastUnsavedChangeTime;
        return elapsed >= warningTimeSeconds;
    }

    private static string GetMessage(double elapsed)
    {
        string timeString = FormatTime(elapsed);
        return $"⚠ You haven't saved in {timeString}! ⚠";
    }

    private static GUIStyle CreateStyle(double elapsed)
    {
        var s = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.UpperCenter,
            fontSize = fontSize,
            normal = { textColor = GetFlashingColor(elapsed) }
        };
        return s;
    }

    private static Color GetFlashingColor(double elapsed)
    {
        if (!flashText || flashSpeed <= 0f)
            return textColor;

        float t = Mathf.Abs(Mathf.Sin((float)(elapsed * flashSpeed)));
        Color transparent = new Color(textColor.r, textColor.g, textColor.b, 0f);
        return Color.Lerp(transparent, textColor, t);
    }

    private static void ResetTimerIfNoDirty()
    {
        if (!AnySceneDirty())
            lastUnsavedChangeTime = -1;
    }

    private static bool AnySceneDirty()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isDirty)
                return true;
        }
        return false;
    }

    private void SavePrefsFromWindow()
    {
        EditorPrefs.SetBool(PrefPrefix + "Enabled", windowEnabled);
        EditorPrefs.SetInt(PrefPrefix + "WarningTime", windowWarningTimeSeconds);
        EditorPrefs.SetBool(PrefPrefix + "Flash", windowFlashText);
        EditorPrefs.SetFloat(PrefPrefix + "FlashSpeed", windowFlashSpeed);
        EditorPrefs.SetFloat(PrefPrefix + "LastFlashSpeed", windowLastFlashSpeed);
        EditorPrefs.SetString(PrefPrefix + "TextColor", ColorUtility.ToHtmlStringRGBA(windowTextColor));
        EditorPrefs.SetInt(PrefPrefix + "FontSize", windowFontSize);
    }

    private static void LoadPrefsToStatic()
    {
        enabled = EditorPrefs.GetBool(PrefPrefix + "Enabled", true);
        warningTimeSeconds = EditorPrefs.GetInt(PrefPrefix + "WarningTime", 600);
        flashText = EditorPrefs.GetBool(PrefPrefix + "Flash", false);
        flashSpeed = EditorPrefs.GetFloat(PrefPrefix + "FlashSpeed", 2.0f);

        string colorStr = EditorPrefs.GetString(PrefPrefix + "TextColor", ColorUtility.ToHtmlStringRGBA(Color.red));
        if (ColorUtility.TryParseHtmlString("#" + colorStr, out Color col))
            textColor = col;
        else
            textColor = Color.red;

        fontSize = EditorPrefs.GetInt(PrefPrefix + "FontSize", 20);
    }

    private void ApplyPrefsToStatic()
    {
        enabled = windowEnabled;
        warningTimeSeconds = windowWarningTimeSeconds;
        flashText = windowFlashText;
        flashSpeed = windowFlashSpeed;
        textColor = windowTextColor;
        fontSize = windowFontSize;
    }
}
#endif