using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VLEditorExtensions
{
    /// <summary>
    /// Quickly open a tab for assets or objects that are dragged and dropped into the dock area.
    /// </summary>
    [InitializeOnLoad]
    public static class DockAreaDragAndDrop
    {
        private static readonly float DockAreaTabHeight = 20;

        static DockAreaDragAndDrop()
        {
            EditorApplication.update += Update;

            DockAreaTabHeight = (float)typeof(EditorWindow).Assembly
                                    .GetType("UnityEditor.DockArea")
                                    .GetField("kTabHeight", BindingFlags.Static | BindingFlags.NonPublic)
                                    .GetValue(null) + 1;
        }

        private static void Update()
        {
            if (!EditorWindow.mouseOverWindow)
            {
                return;
            }

            var curEvent = (Event)typeof(Event).GetField("s_Current", BindingFlags.Static | BindingFlags.NonPublic)
                                               .GetValue(null);

            var dockAreaPos = EditorWindow.mouseOverWindow.rootVisualElement.contentRect;
            dockAreaPos.height = DockAreaTabHeight;
            if (!dockAreaPos.Contains(curEvent.mousePosition))
            {
                return;
            }

            if (curEvent.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
            else if (curEvent.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();

                AddTab(OpenPropertyEditor(DragAndDrop.objectReferences.First()), nextTo: EditorWindow.mouseOverWindow);
            }
        }

        private static EditorWindow OpenPropertyEditor(UnityEngine.Object target)
        {
            return (EditorWindow)typeof(EditorWindow).Assembly.GetType("UnityEditor.PropertyEditor")
                .GetMethod("OpenPropertyEditor", BindingFlags.Static | BindingFlags.NonPublic, null,
                    new[] { typeof(UnityEngine.Object), typeof(bool) }, null)
                .Invoke(null, new object[] { target, false });
        }

        private static void AddTab(EditorWindow window, EditorWindow nextTo)
        {
            var dockArea = typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic)
                                               .GetValue(nextTo);

            typeof(EditorWindow).Assembly.GetType("UnityEditor.DockArea")
                .GetMethod("AddTab", BindingFlags.Instance | BindingFlags.Public, null,
                    new[] { typeof(EditorWindow), typeof(bool) }, null)
                .Invoke(dockArea, new object[] { window, true });
        }
    }
}
