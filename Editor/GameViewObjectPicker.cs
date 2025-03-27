using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
#if USE_UGUI
using UnityEngine.EventSystems;
using UnityEngine.UI;
#endif

namespace VLEditorExtensions
{
    /// <summary>
    /// Left click to select the object that is visible under the cursor in game view.
    /// If in play mode it needs to be paused.
    /// Repeated clicks will cycle between all objects that are under.
    /// </summary>
    /// <remarks>
    /// In Unity 6 and later you can remap the button in the Shortcuts window.
    /// Otherwise you should edit this script to change it.
    /// 
    /// The Update method can also be modified to not require pausing in play mode.
    /// This approach would not work with the Shortcut methods.
    /// </remarks>
    [InitializeOnLoad]
    public static class GameViewObjectPicker
    {
        static List<RaycastResult> _lastObjectsAtScreenPos;
        static bool _selectionChanging = false;

        static GameViewObjectPicker()
        {
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.update += Update;

#if UNITY_2023_3_OR_NEWER
            ShortcutManager.RegisterContext(_shortcutContext);
#endif
        }

        static void OnSelectionChanged()
        {
            if (!_selectionChanging)
            {
                _lastObjectsAtScreenPos = null;
            }
            _selectionChanging = false;
        }

        // Use mouse shortcuts from Unity 2023.3 onwards
#if UNITY_2023_3_OR_NEWER
        static GameViewObjectPickerShortcutContext _shortcutContext = new();
        static bool _debounceShortcut = false;

        private class GameViewObjectPickerShortcutContext : IShortcutContext
        {
            public bool active => IsGameViewWindow(EditorWindow.focusedWindow);
        }

        [Shortcut("Game View/Select Renderer", typeof(GameViewObjectPickerShortcutContext), KeyCode.Mouse0)]
        private static void ShortcutSelectRenderer()
        {
            ShortcutSelect(RaycastMode.Renderers);
        }

        // FIXME: EventSystem mode only works in play mode when not paused but this shortcut is not called then
        [Shortcut("Game View/Select Raycast Target", typeof(GameViewObjectPickerShortcutContext), KeyCode.Mouse0, ShortcutModifiers.Shift)]
        private static void ShortcutSelectRaycastTarget()
        {
            ShortcutSelect(RaycastMode.EventSystem);
        }

        private static void ShortcutSelect(RaycastMode mode)
        {
            // _debounceShortcut is a bugfix. When play mode is paused this triggers twice and the second trigger has incorrect mouse position
            if (_debounceShortcut)
                return;
            _debounceShortcut = true;
            EditorApplication.delayCall += () => _debounceShortcut = false;

            var window = EditorWindow.mouseOverWindow;
            if (IsGameViewWindow(window))
            {
                var evt = Event.current;
                DelayTrySelectObjectAtScreenPos(
                    GetGameMousePosition(window, evt.mousePosition),
                    GetTargetDisplay(window),
                    mode,
                    true
                );
            }
        }
#endif

        static void Update()
        {
#if !UNITY_2023_3_OR_NEWER
            // Use visual element APIs for a solution that works when paused and in edit mode
            // TODO: Could we instead use the 'internal static CallbackFunction globalEventHandler' from EditorApplication?

            var window = EditorWindow.mouseOverWindow;
            if (IsGameViewWindow(window))
            {
                var imgui = window.rootVisualElement.parent.Q<IMGUIContainer>();
                if (imgui != null)
                {
                    // Callback methods must be static so they do not get registered multiple times
                    imgui.RegisterCallback<MouseUpEvent, EditorWindow>(OnGameViewMouseUp, window, TrickleDown.TrickleDown);

                    window.rootVisualElement.RegisterCallback<DetachFromPanelEvent, IMGUIContainer>(static (detached, imgui) =>
                    {
                        imgui.UnregisterCallback<MouseUpEvent, EditorWindow>(OnGameViewMouseUp, TrickleDown.TrickleDown);
                    }, imgui);
                }

                static void OnGameViewMouseUp(MouseUpEvent evt, EditorWindow window)
                {
                    // Only when paused or not playing so we don't interfere with game inputs
                    // FIXME: EventSystem mode only works in play mode when not paused but this is not called then
                    if (EditorApplication.isPlaying && !EditorApplication.isPaused)
                        return;
                    if (evt.button != (int)MouseButton.LeftMouse)
                        return;

                    var editorMousePosition = evt.mousePosition;
                    editorMousePosition.y -= 19; // subtract dock area

                    DelayTrySelectObjectAtScreenPos(
                        GetGameMousePosition(window, editorMousePosition),
                        GetTargetDisplay(window),
                        evt.shiftKey ? RaycastMode.EventSystem : RaycastMode.Renderers,
                        true
                    );
                }
            }
#endif
        }

        static readonly System.Type GameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");

        static bool IsGameViewWindow(EditorWindow window)
        {
            if (window == null)
                return false;
            return window.GetType() == GameViewType;
        }

        static Vector2 GetGameMousePosition(EditorWindow gameView, Vector2 editorMousePosition)
        {
            Rect targetInContent = (Rect)GameViewType.GetProperty("targetInContent",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(gameView);
            Vector2 gameMouseOffset = (Vector2)GameViewType.GetProperty("gameMouseOffset",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(gameView);
            float gameMouseScale = (float)GameViewType.GetProperty("gameMouseScale",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(gameView);

            var gameMousePosition = (editorMousePosition + gameMouseOffset) * gameMouseScale;
            gameMousePosition.y = targetInContent.height - gameMousePosition.y;
            return gameMousePosition;
        }

        static int GetTargetDisplay(EditorWindow gameView)
        {
            int targetDisplay = (int)GameViewType.GetProperty("targetDisplay",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
                .GetValue(gameView);
            return targetDisplay;
        }

        // Using delay call as running directly sometimes seems to cause Camera.ScreenPointToRay to behave incorrectly
        static void DelayTrySelectObjectAtScreenPos(Vector3 screenPos, int targetDisplay, RaycastMode mode, bool includeTransparent)
        {
            EditorApplication.delayCall += () => TrySelectObjectAtScreenPos(screenPos, targetDisplay, mode, includeTransparent);
        }

        static void TrySelectObjectAtScreenPos(Vector3 screenPos, int targetDisplay, RaycastMode mode, bool includeTransparent)
        {
            // Debug.Log($"TrySelectObjectAtScreenPos {screenPos} display:{targetDisplay} input:{Input.mousePosition}");

            List<RaycastResult> results = new();
            List<RaycastResult> lastResults = _lastObjectsAtScreenPos;
            _lastObjectsAtScreenPos = null;

            RaycastAtScreenPos(screenPos, targetDisplay, mode, includeTransparent, results);
            if (results.Count == 0)
                return;

            GameObject toSelect = results[0].gameObject;

            // Cycle through all hit objects at pos if clicking in same spot
            var selectedGo = Selection.activeGameObject;
            if (selectedGo != null && lastResults != null)
            {
                int idx = results.FindIndex(x => x.gameObject == selectedGo);

                // Cycle only if the objects hit before the selected one stay the same
                if (idx != -1 && lastResults.Count > idx
                              && lastResults.Take(idx + 1).Select(x => x.gameObject).SequenceEqual(
                                     results.Take(idx + 1).Select(x => x.gameObject)))
                {
                    toSelect = results[(idx + 1) % results.Count].gameObject;
                }
            }

            _lastObjectsAtScreenPos = results;

            if (Selection.count != 1 || Selection.activeGameObject != toSelect)
            {
                _selectionChanging = true;
                Selection.activeGameObject = toSelect;
            }
        }

        public struct RaycastResult
        {
            public GameObject gameObject;
            public float distance;
            public Vector3 normal;
            public Camera eventCamera;
            public int sortingLayer;
            public int sortingOrder;
            public int depth;
            public Canvas rootCanvas; // If this result was from a Graphic
        }

        public enum RaycastMode
        {
            None = 0,
            Renderers = 1,
            Physics = 2,
            EventSystem = 4,
        }

        public static void RaycastAtScreenPos(Vector3 screenPos, int targetDisplay, RaycastMode mode, bool includeTransparent, List<RaycastResult> results)
        {
            // TODO: Running this with multiple flags is not good as the combined results are not sorted or deduplicated
            if ((mode & RaycastMode.Renderers) != 0)
                RaycastRenderersAtScreenPos(screenPos, targetDisplay, includeTransparent, results);
            if ((mode & RaycastMode.EventSystem) != 0)
                RaycastEventSystemAtScreenPos(screenPos, targetDisplay, results);
            if ((mode & RaycastMode.Physics) != 0)
                RaycastPhysicsAtScreenPos(screenPos, targetDisplay, results);
        }

        public static void RaycastEventSystemAtScreenPos(Vector3 screenPos, int targetDisplay, List<RaycastResult> results)
        {
#if USE_UGUI
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                Debug.LogWarning("No EventSystem in the scene. Cannot raycast!");
                return;
            }

            var pte = new PointerEventData(eventSystem) { position = screenPos };
            var eventSystemResults = new List<UnityEngine.EventSystems.RaycastResult>();
            eventSystem.RaycastAll(pte, eventSystemResults);
            foreach (var res in eventSystemResults)
            {
                if (res.displayIndex != targetDisplay)
                    continue;
                results.Add(new()
                {
                    gameObject = res.gameObject,
                    distance = res.distance,
                    eventCamera = res.module.eventCamera,
                    rootCanvas = res.module.rootRaycaster?.GetComponent<Canvas>(),
                    sortingLayer = res.sortingLayer,
                    sortingOrder = res.sortingOrder,
                    depth = res.depth,
                    normal = res.worldNormal
                });
            }
#endif
        }

        public static void RaycastPhysicsAtScreenPos(Vector3 screenPos, int targetDisplay, List<RaycastResult> results)
        {
            foreach (var camera in Camera.allCameras)
            {
                if (camera.targetTexture != null)
                    continue;
                if (camera.targetDisplay != targetDisplay)
                    continue;

                var physicsResults = Physics.RaycastAll(camera.ScreenPointToRay(screenPos), Mathf.Infinity, camera.cullingMask);
                foreach (var hit in physicsResults)
                {
                    results.Add(new()
                    {
                        gameObject = hit.transform.gameObject,
                        distance = hit.distance,
                        normal = hit.normal,
                        eventCamera = camera
                    });
                }
            }
        }

        /// <summary>
        /// Raycasts all visible Meshes and Canvas Graphic components and sorts by distance and sorting order
        /// so the one visible at the screen position should be first in the results list.
        /// </summary>
        /// <param name="screenPos"></param>
        /// <param name="includeTransparent"></param>
        /// <param name="results"></param>
        public static void RaycastRenderersAtScreenPos(Vector3 screenPos, int targetDisplay, bool includeTransparent, List<RaycastResult> results)
        {
            // Debug.Log("Renderers: " + string.Join("\n", Object.FindObjectsOfType<Renderer>(false)
            //     .GroupBy(x => x.GetType())
            //     .Select(group => $"{group.Key,-20} {group.Count()}")));

#if USE_UGUI
            RaycastCanvasGraphics(screenPos, targetDisplay, includeTransparent, results);
#endif

            foreach (var camera in Camera.allCameras)
            {
                if (camera.targetTexture != null)
                    continue;
                if (camera.targetDisplay != targetDisplay)
                    continue;

                var ray = camera.ScreenPointToRay(screenPos);

                // Debug.DrawRay(ray.origin, ray.direction * 100, Color.red, 5);

                // Custom raycasting to pick renderers/meshes without collider
                RaycastMeshes(ray, camera, includeTransparent, results);
            }

            results.Sort(RaycastComparer);

            // Debug.Log(string.Join("\n", results.Select(
            //     h => $"{h.gameObject.name} distance:{h.distance}, depth:{h.depth}, sortingLayer:{h.sortingLayer}, sortingOrder:{h.sortingOrder}, cam:{h.eventCamera?.name}, renderOrder:{h.rootCanvas?.renderOrder}"
            // )));
        }

        // Adapted from EventSystem.cs in com.unity.ugui
        private static int RaycastComparer(RaycastResult lhs, RaycastResult rhs)
        {
            if (lhs.eventCamera != rhs.eventCamera)
            {
                var lhsEventCamera = lhs.eventCamera;
                var rhsEventCamera = rhs.eventCamera;
                if (lhsEventCamera != null && rhsEventCamera != null && lhsEventCamera.depth != rhsEventCamera.depth)
                {
                    // need to reverse the standard compareTo
                    if (lhsEventCamera.depth < rhsEventCamera.depth)
                        return 1;
                    else
                        return -1;
                }
            }

            // Renderer sorting
            if (lhs.sortingLayer != rhs.sortingLayer)
            {
                // Uses the layer value to properly compare the relative order of the layers.
                var rid = SortingLayer.GetLayerValueFromID(rhs.sortingLayer);
                var lid = SortingLayer.GetLayerValueFromID(lhs.sortingLayer);
                return rid.CompareTo(lid);
            }

            if (lhs.sortingOrder != rhs.sortingOrder)
                return rhs.sortingOrder.CompareTo(lhs.sortingOrder);

            // comparing depth only makes sense if the two raycast results have the same root canvas (case 912396)
            if (lhs.depth != rhs.depth && lhs.rootCanvas == rhs.rootCanvas)
                return rhs.depth.CompareTo(lhs.depth);

            if (lhs.distance != rhs.distance)
                return lhs.distance.CompareTo(rhs.distance);

            if (lhs.rootCanvas != null && rhs.rootCanvas != null && lhs.rootCanvas.renderOrder != rhs.rootCanvas.renderOrder)
                return rhs.rootCanvas.renderOrder.CompareTo(lhs.rootCanvas.renderOrder);

            // #if PACKAGE_PHYSICS2D
            //             // Sorting group
            //             if (lhs.sortingGroupID != SortingGroup.invalidSortingGroupID && rhs.sortingGroupID != SortingGroup.invalidSortingGroupID)
            //             {
            //                 if (lhs.sortingGroupID != rhs.sortingGroupID)
            //                     return lhs.sortingGroupID.CompareTo(rhs.sortingGroupID);
            //                 if (lhs.sortingGroupOrder != rhs.sortingGroupOrder)
            //                     return rhs.sortingGroupOrder.CompareTo(lhs.sortingGroupOrder);
            //             }
            // #endif

            return 0;//lhs.index.CompareTo(rhs.index);
        }

#if USE_UGUI
        public static void RaycastCanvasGraphics(Vector2 screenPos, int targetDisplay, bool includeNonVisible, List<RaycastResult> results)
        {
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!canvas.enabled)
                    continue;
                if (!canvas.isRootCanvas)
                    continue;

                var renderMode = canvas.renderMode;
                if (renderMode == RenderMode.ScreenSpaceOverlay
                    || (renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null))
                {
                    if (canvas.targetDisplay != targetDisplay)
                        continue;
                    RaycastCanvasGraphics(canvas.gameObject, screenPos, null, includeNonVisible, true, results);
                }
                else // World space canvas
                {
                    foreach (var camera in Camera.allCameras)
                    {
                        if (camera.targetTexture != null)
                            continue;
                        if (camera.targetDisplay != targetDisplay)
                            continue;

                        RaycastCanvasGraphics(canvas.gameObject, screenPos, camera, includeNonVisible, true, results);
                    }
                }
            }
        }

        private static void RaycastCanvasGraphics(GameObject gameObject, Vector2 screenPos, Camera eventCamera, bool includeNonVisible, bool canvasGroupVisible, List<RaycastResult> results)
        {
            if (!gameObject.activeInHierarchy)
                return;

            if (gameObject.TryGetComponent<Canvas>(out var canvas)
                && !canvas.isActiveAndEnabled)
            {
                return;
            }

            if (gameObject.TryGetComponent<CanvasGroup>(out var canvasGroup)
                && canvasGroup.isActiveAndEnabled && (canvasGroupVisible || canvasGroup.ignoreParentGroups))
            {
                canvasGroupVisible = canvasGroup.alpha > 0;
            }

            if (canvasGroupVisible && gameObject.TryGetComponent<Graphic>(out var graphic)
                && RaycastGraphic(graphic, screenPos, eventCamera, includeNonVisible, out var hit))
            {
                results.Add(hit);
            }

            if (gameObject.TryGetComponent<UnityEngine.UI.Mask>(out var mask)
                && !mask.IsRaycastLocationValid(screenPos, eventCamera))
            {
                return;
            }

            if (gameObject.TryGetComponent<UnityEngine.UI.RectMask2D>(out var rectMask)
                && !rectMask.IsRaycastLocationValid(screenPos, eventCamera))
            {
                return;
            }

            var t = gameObject.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                RaycastCanvasGraphics(t.GetChild(i).gameObject, screenPos, eventCamera, includeNonVisible, canvasGroupVisible, results);
            }
        }

        /// <summary>
        /// Raycast method that only cares about the visibility, not if it's marked as a raycast target.
        /// Effect of parent CanvasGroups or Masks need to be checked separately.
        /// </summary>
        /// <param name="graphic"></param>
        /// <param name="screenPos"></param>
        /// <param name="eventCamera"></param>
        /// <param name="includeNonVisible"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        public static bool RaycastGraphic(Graphic graphic, Vector2 screenPos, Camera eventCamera, bool includeNonVisible, out RaycastResult result)
        {
            result = default;

            if (!graphic.isActiveAndEnabled)
                return false;
            if (graphic.depth == -1)
                return false;

            Canvas canvas = graphic.canvas;
            if (canvas == null || !canvas.isActiveAndEnabled)
                return false;
            if (eventCamera != null && (eventCamera.cullingMask & 1 << canvas.gameObject.layer) == 0)
                return false;
            if (graphic.canvasRenderer.cull)
                return false;
            if (graphic.canvasRenderer.cullTransparentMesh && graphic.color.a == 0) // does not account for customized vertex colors
                return false;
            // TODO: ui-extensions support
            // if (graphic is NonDrawingGraphic)
            //     return false;
            if (graphic.TryGetComponent<UnityEngine.UI.Mask>(out var mask) && mask.enabled && !mask.showMaskGraphic)
                return false;
            if (!includeNonVisible && IsGraphicInvisible(graphic))
                return false;
            if (!RectTransformUtility.RectangleContainsScreenPoint(graphic.rectTransform, screenPos, eventCamera, graphic.raycastPadding))
                return false;
            if (graphic is ICanvasRaycastFilter rf && !rf.IsRaycastLocationValid(screenPos, eventCamera))
                return false;

            Ray ray = new();
            if (eventCamera != null)
                ray = eventCamera.ScreenPointToRay(screenPos);

            float distance;
            Transform trans = graphic.transform;
            Vector3 transForward = trans.forward;

            if (eventCamera == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                distance = 0;
            else
            {
                // http://geomalgorithms.com/a06-_intersect-2.html
                distance = (Vector3.Dot(transForward, trans.position - ray.origin) / Vector3.Dot(transForward, ray.direction));

                // Check to see if the go is behind the camera.
                if (distance < 0)
                    return false;
            }

            result = new()
            {
                gameObject = graphic.gameObject,
                distance = distance,
                eventCamera = eventCamera,
                rootCanvas = canvas.rootCanvas,
                depth = graphic.depth,
                sortingLayer = canvas.sortingLayerID,
                sortingOrder = canvas.sortingOrder,
                normal = -transForward
            };
            return true;
        }

        static bool IsGraphicInvisible(Graphic graphic)
        {
            // Might not be correct with custom materials
            if (graphic.color.a == 0)
            {
                return true;
            }
            // Not really logical here but makes the object selection better
            if (!graphic.raycastTarget && (graphic.color.a < 0.9f
            // TODO: ui-extensions support
            // || graphic is UIParticleSystem
            ))
            {
                return true;
            }
            return false;
        }
#endif // USE_UGUI

        private static Mesh _bakedMesh;
        private static Mesh _spriteMesh;

        public static bool RaycastMeshes(Ray ray, Camera eventCamera, bool includeTransparent, List<RaycastResult> results)
        {
            bool isHit = false;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!r.enabled)
                    continue;
                if ((eventCamera.cullingMask & 1 << r.gameObject.layer) == 0)
                    continue;
                if (r.sharedMaterials.Length == 0) // this seems to be a special case where the renderer will not render anything. null would render magenta
                    continue;
                if (!r.isVisible) // Will account for LOD groups. Although should be checked per camera instead
                    continue;

                var mat = r.sharedMaterial;
                if (!includeTransparent && mat != null && mat.shader != null)
                {
                    // skip transparent renderers
                    if (mat.renderQueue > (int)RenderQueue.GeometryLast)
                        continue;
                }

                if (r is MeshRenderer mr)
                {
                    if (!mr.TryGetComponent<MeshFilter>(out var filter))
                        continue;
                    var mesh = filter.sharedMesh;
                    if (mesh == null)
                        continue;
                    // These checks prevent an assertion error from IntersectRayMesh
                    if (mesh.subMeshCount == 0)
                        continue;
                    if (mesh.vertexCount == 0)
                        continue;
                    if (!MeshCanAccess(mesh))
                        continue;

                    if (IntersectRayMesh(ray, mesh, filter.transform.localToWorldMatrix, out var rayHit))
                    {
                        isHit = true;
                        results.Add(new()
                        {
                            gameObject = mr.gameObject,
                            distance = rayHit.distance,
                            normal = rayHit.normal,
                            eventCamera = eventCamera
                        });
                    }
                }
                else if (r is SkinnedMeshRenderer smr)
                {
                    if (smr.sharedMesh == null)
                        continue;

                    if (_bakedMesh == null)
                        _bakedMesh = new();

                    smr.BakeMesh(_bakedMesh, true);

                    if (IntersectRayMesh(ray, _bakedMesh, smr.transform.localToWorldMatrix, out var rayHit))
                    {
                        isHit = true;
                        results.Add(new()
                        {
                            gameObject = smr.gameObject,
                            distance = rayHit.distance,
                            normal = rayHit.normal,
                            eventCamera = eventCamera
                        });
                    }
                }
                else if (r is SpriteRenderer sr)
                {
                    var sprite = sr.sprite;
                    if (sprite == null)
                        continue;
                    if (mat == null)
                        continue;

                    if (_spriteMesh == null)
                        _spriteMesh = new();

                    _spriteMesh.Clear();
                    _spriteMesh.vertices = sprite.vertices.Select(v => new Vector3(v.x * (sr.flipX ? -1 : 1), v.y * (sr.flipY ? -1 : 1), 0)).ToArray();
                    _spriteMesh.SetTriangles(sprite.triangles, 0);

                    if (IntersectRayMesh(ray, _spriteMesh, sr.transform.localToWorldMatrix, out var rayHit))
                    {
                        isHit = true;
                        results.Add(new()
                        {
                            gameObject = sr.gameObject,
                            distance = rayHit.distance,
                            normal = rayHit.normal,
                            eventCamera = eventCamera,
                            sortingLayer = sr.sortingLayerID,
                            sortingOrder = sr.sortingOrder
                        });
                    }
                }
                // Disabled this for now as it causes issues. Even crashed once maybe due to threaded nature of particle rendering
                else if (false && r is ParticleSystemRenderer psr)
                {
                    if (_bakedMesh == null)
                        _bakedMesh = new();

                    float dist = float.PositiveInfinity;

                    if (psr.renderMode != ParticleSystemRenderMode.None)
                    {
                        psr.BakeMesh(_bakedMesh, eventCamera, false);
                        if (_bakedMesh.subMeshCount > 0 && _bakedMesh.vertexCount > 0)
                        {
                            if (IntersectRayMesh(ray, _bakedMesh, psr.transform.localToWorldMatrix, out var rayHit))
                            {
                                isHit = true;
                                dist = rayHit.distance;
                                results.Add(new()
                                {
                                    gameObject = psr.gameObject,
                                    distance = rayHit.distance,
                                    normal = rayHit.normal,
                                    eventCamera = eventCamera
                                });
                            }
                        }
                    }

                    psr.BakeTrailsMesh(_bakedMesh, eventCamera, false);
                    if (_bakedMesh.subMeshCount > 0 && _bakedMesh.vertexCount > 0)
                    {
                        if (IntersectRayMesh(ray, _bakedMesh, psr.transform.localToWorldMatrix, out var trailHit) && trailHit.distance < dist)
                        {
                            isHit = true;
                            results.Add(new()
                            {
                                gameObject = psr.gameObject,
                                distance = trailHit.distance,
                                normal = trailHit.normal,
                                eventCamera = eventCamera
                            });
                        }
                    }
                }
            }
            return isHit;
        }

        public static bool IntersectRayMesh(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit hit)
            => intersectRayMeshFunc(ray, mesh, matrix, out hit);
        public static bool MeshCanAccess(Mesh mesh)
            => meshCanAccessFunc(mesh);

        delegate bool IntersectRayMeshDelegate(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit hit);
        delegate bool MeshCanAccessDelegate(Mesh mesh);

        static readonly IntersectRayMeshDelegate intersectRayMeshFunc = (IntersectRayMeshDelegate)
            typeof(HandleUtility)
            .GetMethod("IntersectRayMesh", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(IntersectRayMeshDelegate));

        static readonly MeshCanAccessDelegate meshCanAccessFunc = (MeshCanAccessDelegate)
            typeof(Mesh)
            .GetProperty("canAccess", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetMethod
            .CreateDelegate(typeof(MeshCanAccessDelegate));
    }
}
