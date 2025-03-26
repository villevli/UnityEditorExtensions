using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Text;
using System.Buffers;
using System.Reflection;

namespace VLEditorExtensions
{
    /// <summary>
    /// Custom editor that makes all unsupported files have a preview in the inspector.
    /// </summary>
    [CustomEditor(typeof(DefaultAsset), editorForChildClasses: true, isFallback = false)]
    [CanEditMultipleObjects]
    public class DefaultAssetInspector : Editor
    {
        // Mimick Unity's TextAssetInspector
        private const int kMaxChars = 7000;
        [NonSerialized]
        private GUIStyle m_TextStyle;
        private GUIContent m_CachedPreview;

        public virtual void OnEnable()
        {
            CachePreview();
        }

        public override void OnInspectorGUI()
        {
            m_TextStyle ??= "ScriptText";

            string path = AssetDatabase.GetAssetPath(target);
            bool enabledTemp = GUI.enabled;
            GUI.enabled = true;
            if (File.Exists(path))
            {
                Rect rect = GUILayoutUtility.GetRect(m_CachedPreview, m_TextStyle);
                rect.x = 0;
                rect.y -= 3;
                GUI.Box(rect, "");
                EditorGUI.SelectableLabel(rect, m_CachedPreview.text, m_TextStyle);
            }
            GUI.enabled = enabledTemp;
        }

        private void CachePreview()
        {
            string path = AssetDatabase.GetAssetPath(target);
            string text = string.Empty;

            var fileInfo = new FileInfo(path);

            if (fileInfo.Exists)
            {
                if (targets.Length > 1)
                {
                    text = GetTargetTitle();
                }
                else
                {
                    text = GetFileTextPreview(fileInfo, kMaxChars);
                    if (text.Length >= kMaxChars)
                        text = text[..kMaxChars] + "...\n\n<...etc...>";
                }
            }

            m_CachedPreview = new GUIContent(text);
        }

        private string GetTargetTitle()
        {
            // TODO: Use cached delegate to reduce overhead
            return (string)typeof(Editor).GetProperty("targetTitle", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(this);
        }

        private static string GetFileTextPreview(FileInfo file, int maxChars)
        {
            using var sr = new StreamReader(file.OpenRead());
            var sb = new StringBuilder((int)Math.Min(maxChars, file.Length));
            var buffer = ArrayPool<char>.Shared.Rent(1024);

            while (maxChars > 0 && sr.Peek() >= 0)
            {
                int read = sr.Read(buffer, 0, Math.Min(maxChars, 1024));
                maxChars -= read;

                // Change unsupported characters to spaces
                for (int i = 0; i < read; i++)
                {
                    if (i < read - 1 && buffer[i] == '\r' && buffer[i + 1] != '\n')
                    {
                        buffer[i] = ' ';
                    }
                    else if (buffer[i] == 0 || (!char.IsWhiteSpace(buffer[i]) && char.IsControl(buffer[i])))
                    {
                        buffer[i] = ' ';
                    }
                }

                sb.Append(buffer, 0, read);
            }

            ArrayPool<char>.Shared.Return(buffer);
            return sb.ToString();
        }
    }
}
