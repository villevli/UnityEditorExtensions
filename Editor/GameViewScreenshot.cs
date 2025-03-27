using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace VLEditorExtensions
{
    /// <summary>
    /// Quickly take screenshots with F12 from the Game View or the Simulator View.
    /// You can rebind it in the Shortcuts window.
    /// Saves the image as png to a Screenshots/ folder next to your project folder.
    /// Works instantly in edit mode and play mode. Just make sure the Game or Simulator window is open.
    /// Set the game view to the desired resolution first.
    /// </summary>
    public static class GameViewScreenshot
    {
        private static readonly System.Type GameViewType =
            typeof(SceneView).Assembly
#if UNITY_2020_1_OR_NEWER
            .GetType("UnityEditor.PlayModeView");
#else
            .GetType("UnityEditor.GameView");
#endif
        private static readonly PropertyInfo HasFocusProperty =
            typeof(EditorWindow).GetProperty("hasFocus", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo RepaintImmediatelyMethod =
            typeof(EditorWindow).GetMethod("RepaintImmediately", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>
        /// Takes a screenshot of the game view if it's open and saves to a file.
        /// May take a few frames to complete.
        /// </summary>
        [MenuItem("Tools/Take Screenshot (Game View) _F12")]
        public static async void SnapGameView()
        {
            EditorWindow focusedGameView = FindFocusedWindow(GameViewType);
            if (focusedGameView == null)
            {
                Debug.LogWarning("Cannot take screenshot: Game View not open!");
                return;
            }

            string path = GetScreenshotFilePathWithTimestamp(Application.productName);

            ScreenCapture.CaptureScreenshot(path);

            RepaintImmediatelyMethod.Invoke(focusedGameView, null);

            // Need to wait for Unity to save the file so that RevealInFinder works
            var time = EditorApplication.timeSinceStartup;
            while (!File.Exists(path) && EditorApplication.timeSinceStartup < time + 5)
                await Task.Yield();

            if (File.Exists(path))
            {
                Debug.Log($"Saved screenshot in {path}");
                EditorUtility.RevealInFinder(path);
            }
            else
            {
                Debug.LogWarning($"Failed to save screenshot in {path}");
            }
        }

        // Not the same as EditorWindow.focusedWindow. This returns any window that is visible
        static EditorWindow FindFocusedWindow(System.Type type)
        {
            var windows = (EditorWindow[])Resources.FindObjectsOfTypeAll(type);
            for (int i = 0; i < windows.Length; i++)
            {
                if ((bool)HasFocusProperty.GetValue(windows[i]))
                    return windows[i];
            }
            return null;
        }

        static string GetScreenshotFilePathWithTimestamp(string name)
        {
            // Save into a Screenshots folder next to the project folder
            // TODO: Configurable location
            var dir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Application.dataPath)), "Screenshots");
            Directory.CreateDirectory(dir);
            var time = System.DateTime.UtcNow;
            var path = Path.Combine(dir, $"{Util.MakeValidFileName(name)}_{time:yyyyMMdd_HHmmss}.png");
            return path;
        }

        private static class Util
        {
            static readonly char[] InvalidFileNameChars = (new string(Path.GetInvalidFileNameChars()) + ' ').ToCharArray();

            public static string MakeValidFileName(string name) => string.Concat(name.Split(InvalidFileNameChars));
        }
    }
}
