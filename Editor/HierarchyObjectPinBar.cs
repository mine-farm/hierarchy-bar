using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace MF.Editor.HierarchyObjectPins
{
    [InitializeOnLoad]
    internal static class HierarchyObjectPinBar
    {
        private const string StyleSheetPath =
            "Packages/com.mf.hierarchy-object-pins/Editor/HierarchyObjectPinBar.uss";

        private const string ViewportName = "mf-hierarchy-object-pin-viewport";
        private const string BarName = "mf-hierarchy-object-pin-bar";
        private const float RowHeight = 22f;
        private const float BarHeight = RowHeight * 2f;
        private const double DiscoveryInterval = 0.5d;

        private static readonly Type HierarchyWindowType =
            typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");

        private static readonly FieldInfo SceneHierarchyField =
            HierarchyWindowType?.GetField(
                "m_SceneHierarchy",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly Dictionary<int, WindowState> WindowStates = new();

        private static double nextDiscoveryTime;
        private static bool barsDirty = true;

        static HierarchyObjectPinBar()
        {
            HierarchyObjectPinSettings.Initialize();

            EditorApplication.update += Maintain;
            EditorApplication.hierarchyChanged += MarkDirty;
            Selection.selectionChanged += MarkDirty;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            HierarchyObjectPinSettings.Changed += MarkDirty;
            AssemblyReloadEvents.beforeAssemblyReload += Detach;

            EditorApplication.delayCall += DiscoverHierarchyWindows;
        }

        private static void Maintain()
        {
            if (EditorApplication.timeSinceStartup >= nextDiscoveryTime)
            {
                nextDiscoveryTime = EditorApplication.timeSinceStartup + DiscoveryInterval;
                DiscoverHierarchyWindows();
            }

            var staleIds = ListPool<int>.Get();
            try
            {
                foreach (var pair in WindowStates)
                {
                    var state = pair.Value;
                    if (state.Window == null)
                    {
                        staleIds.Add(pair.Key);
                        continue;
                    }

                    MaintainWindow(state);
                    if (barsDirty)
                        RebuildBar(state);
                }

                foreach (var staleId in staleIds)
                    WindowStates.Remove(staleId);
            }
            finally
            {
                ListPool<int>.Release(staleIds);
            }

            barsDirty = false;
        }

        private static void DiscoverHierarchyWindows()
        {
            if (HierarchyWindowType == null || SceneHierarchyField == null)
                return;

            foreach (var candidate in Resources.FindObjectsOfTypeAll(HierarchyWindowType))
            {
                if (candidate is not EditorWindow window)
                    continue;

                var id = window.GetInstanceID();
                if (WindowStates.TryGetValue(id, out var state))
                {
                    if (state.Viewport.parent == null)
                        AttachVisuals(state);

                    continue;
                }

                if (!TryCreateState(window, out state))
                    continue;

                WindowStates.Add(id, state);
                AttachVisuals(state);
                RebuildBar(state);
            }
        }

        private static bool TryCreateState(EditorWindow window, out WindowState state)
        {
            state = null;
            if (!TryGetTreeView(window, out var treeView, out var members))
                return false;

            var viewport = new VisualElement
            {
                name = ViewportName,
                pickingMode = PickingMode.Ignore,
                usageHints = UsageHints.DynamicTransform,
            };
            viewport.AddToClassList("mf-hierarchy-object-pin-viewport");

            var bar = new VisualElement
            {
                name = BarName,
                usageHints = UsageHints.DynamicTransform,
            };
            bar.AddToClassList("mf-hierarchy-object-pin-bar");
            viewport.Add(bar);

            state = new WindowState(window, viewport, bar, treeView, members);
            return true;
        }

        private static void AttachVisuals(WindowState state)
        {
            var root = state.Window.rootVisualElement;
            if (root == null)
                return;

            root.Q<VisualElement>(ViewportName)?.RemoveFromHierarchy();

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet == null)
                return;

            if (!root.styleSheets.Contains(styleSheet))
                root.styleSheets.Add(styleSheet);

            root.Add(state.Viewport);
        }

        private static void MaintainWindow(WindowState state)
        {
            if (!TryGetTreeView(state.Window, out var treeView, out var members))
                return;

            if (!ReferenceEquals(state.TreeView, treeView))
            {
                RestoreTopMargin(state);
                state.SetTreeView(treeView, members);
                barsDirty = true;
            }

            var currentMargin = Convert.ToSingle(state.Members.TopRowMarginField.GetValue(state.Members.Gui));
            if (!Mathf.Approximately(currentMargin, BarHeight))
            {
                state.Members.TopRowMarginField.SetValue(state.Members.Gui, BarHeight);
                state.Window.Repaint();
            }

            var scrollPosition = ReadScrollPosition(state.Members);
            state.Bar.style.translate = new Translate(
                0f,
                -Mathf.Max(0f, scrollPosition.y),
                0f);
        }

        private static void RebuildBar(WindowState state)
        {
            var bar = state.Bar;
            bar.Clear();

            var scene = SceneManager.GetActiveScene();

            var tagRow = new VisualElement();
            tagRow.AddToClassList("mf-hierarchy-object-pin-row");
            tagRow.AddToClassList("mf-hierarchy-object-pin-tag-row");
            tagRow.Add(CreateTagButton("World", scene));
            tagRow.Add(CreateTagButton("Player", scene));
            bar.Add(tagRow);

            var pinRow = new VisualElement();
            pinRow.AddToClassList("mf-hierarchy-object-pin-row");
            pinRow.AddToClassList("mf-hierarchy-object-pin-favorites-row");
            bar.Add(pinRow);

            var selected = Selection.activeGameObject;
            var selectedInScene = selected != null && selected.scene.handle == scene.handle;
            var isPinned = selectedInScene && HierarchyObjectPinSettings.Contains(selected);
            var canToggle = isPinned ||
                            (selectedInScene && !EditorApplication.isPlayingOrWillChangePlaymode);

            var pinToggle = new Button
            {
                text = isPinned ? "★" : "☆",
                tooltip = isPinned
                    ? "선택한 오브젝트의 고정을 해제합니다."
                    : selectedInScene
                        ? "선택한 오브젝트를 빠른 선택 바에 고정합니다."
                        : "활성 씬의 오브젝트를 선택하면 고정할 수 있습니다.",
            };
            pinToggle.AddToClassList("mf-hierarchy-object-pin-toggle");
            pinToggle.SetEnabled(canToggle);
            pinToggle.clicked += ToggleCurrentSelection;
            pinRow.Add(pinToggle);

            var scrollView = new ScrollView(ScrollViewMode.Horizontal)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
            };
            scrollView.AddToClassList("mf-hierarchy-object-pin-scroll");
            scrollView.contentContainer.AddToClassList("mf-hierarchy-object-pin-list");
            pinRow.Add(scrollView);

            var pins = HierarchyObjectPinSettings.GetPins(scene);
            if (pins.Count == 0)
            {
                var placeholder = new Label("오브젝트를 선택한 뒤 ☆를 눌러 고정")
                {
                    pickingMode = PickingMode.Ignore,
                };
                placeholder.AddToClassList("mf-hierarchy-object-pin-placeholder");
                scrollView.Add(placeholder);
                return;
            }

            foreach (var entry in pins)
                scrollView.Add(CreatePinButton(entry, scene));
        }

        private static Button CreateTagButton(string tag, Scene scene)
        {
            var taggedObject = FindTaggedObject(scene, tag);
            var button = new Button
            {
                text = tag,
                tooltip = taggedObject != null
                    ? $"tag: {tag} 오브젝트 선택 ({GetHierarchyPath(taggedObject.transform)})"
                    : $"활성 씬에 tag: {tag} 오브젝트가 없습니다.",
            };
            button.AddToClassList("mf-hierarchy-object-pin-tag-button");
            button.SetEnabled(taggedObject != null);

            if (taggedObject != null)
            {
                button.clicked += () => Select(taggedObject, false);
                if (Selection.activeGameObject == taggedObject)
                    button.AddToClassList("mf-hierarchy-object-pin-button--selected");
            }

            return button;
        }

        private static GameObject FindTaggedObject(Scene scene, string tag)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (transform.CompareTag(tag))
                            return transform.gameObject;
                    }
                }
            }
            catch (UnityException)
            {
                return null;
            }

            return null;
        }

        private static Button CreatePinButton(HierarchyObjectPinEntry entry, Scene scene)
        {
            var gameObject = HierarchyObjectPinSettings.Resolve(entry);
            if (gameObject != null && gameObject.scene.handle != scene.handle)
                gameObject = null;

            var capturedObject = gameObject;
            var displayName = capturedObject != null ? capturedObject.name : entry.DisplayName;
            var button = new Button
            {
                text = displayName,
                tooltip = capturedObject != null
                    ? $"{GetHierarchyPath(capturedObject.transform)} 선택\n더블 클릭: Scene 뷰에서 프레임"
                    : "오브젝트를 찾을 수 없습니다. 우클릭하여 고정을 제거할 수 있습니다.",
            };
            button.AddToClassList("mf-hierarchy-object-pin-button");

            if (capturedObject != null)
            {
                button.clicked += () => Select(capturedObject, false);
                button.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 0 && evt.clickCount == 2)
                        Select(capturedObject, true);
                });

                if (Selection.activeGameObject == capturedObject)
                    button.AddToClassList("mf-hierarchy-object-pin-button--selected");
            }
            else
            {
                button.AddToClassList("mf-hierarchy-object-pin-button--missing");
            }

            button.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                if (capturedObject != null)
                {
                    evt.menu.AppendAction("선택", _ => Select(capturedObject, false));
                    evt.menu.AppendAction("Scene 뷰에서 프레임", _ => Select(capturedObject, true));
                    evt.menu.AppendSeparator();
                }

                evt.menu.AppendAction(
                    "고정 제거",
                    _ => HierarchyObjectPinSettings.Remove(entry));
            }));

            return button;
        }

        private static void ToggleCurrentSelection()
        {
            var gameObject = Selection.activeGameObject;
            var activeScene = SceneManager.GetActiveScene();
            if (gameObject == null || gameObject.scene.handle != activeScene.handle)
                return;

            HierarchyObjectPinSettings.Toggle(gameObject);
        }

        private static void Select(GameObject gameObject, bool frame)
        {
            if (gameObject == null)
                return;

            Selection.activeGameObject = gameObject;
            EditorGUIUtility.PingObject(gameObject);
            if (frame)
                SceneView.FrameLastActiveSceneView();
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = $"{transform.name}/{path}";
            }

            return path;
        }

        private static bool TryGetTreeView(
            EditorWindow window,
            out object treeView,
            out TreeViewMembers members)
        {
            treeView = null;
            members = default;

            var sceneHierarchy = SceneHierarchyField?.GetValue(window);
            if (sceneHierarchy == null)
                return false;

            var treeViewField = FindField(sceneHierarchy.GetType(), "m_TreeView");
            treeView = treeViewField?.GetValue(sceneHierarchy);
            if (treeView == null)
                return false;

            var guiProperty = FindProperty(treeView.GetType(), "gui");
            var stateProperty = FindProperty(treeView.GetType(), "state");
            var gui = guiProperty?.GetValue(treeView);
            var treeViewState = stateProperty?.GetValue(treeView);
            if (gui == null || treeViewState == null)
                return false;

            var topRowMarginField = FindField(gui.GetType(), "k_TopRowMargin");
            var scrollPositionProperty = FindProperty(treeViewState.GetType(), "scrollPos");
            var scrollPositionField = FindField(treeViewState.GetType(), "scrollPos");
            if (topRowMarginField == null ||
                (scrollPositionProperty == null && scrollPositionField == null))
            {
                return false;
            }

            members = new TreeViewMembers(
                gui,
                treeViewState,
                topRowMarginField,
                scrollPositionProperty,
                scrollPositionField);
            return true;
        }

        private static Vector2 ReadScrollPosition(TreeViewMembers members)
        {
            if (members.ScrollPositionProperty?.GetValue(members.TreeViewState) is Vector2 propertyValue)
                return propertyValue;

            if (members.ScrollPositionField?.GetValue(members.TreeViewState) is Vector2 fieldValue)
                return fieldValue;

            return Vector2.zero;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            while (type != null)
            {
                var field = type.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                    return field;

                type = type.BaseType;
            }

            return null;
        }

        private static PropertyInfo FindProperty(Type type, string name)
        {
            while (type != null)
            {
                var property = type.GetProperty(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                    return property;

                type = type.BaseType;
            }

            return null;
        }

        private static void MarkDirty()
        {
            barsDirty = true;
            EditorApplication.RepaintHierarchyWindow();
        }

        private static void OnSceneSaved(Scene scene)
        {
            HierarchyObjectPinSettings.PromoteTransientPins(scene);
            MarkDirty();
        }

        private static void OnActiveSceneChanged(Scene previousScene, Scene newScene)
        {
            MarkDirty();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            MarkDirty();
        }

        private static void Detach()
        {
            EditorApplication.update -= Maintain;

            foreach (var state in WindowStates.Values)
            {
                RestoreTopMargin(state);
                state.Viewport.RemoveFromHierarchy();
                state.Window?.Repaint();
            }

            WindowStates.Clear();
        }

        private static void RestoreTopMargin(WindowState state)
        {
            if (state?.Members.TopRowMarginField == null || state.Members.Gui == null)
                return;

            state.Members.TopRowMarginField.SetValue(state.Members.Gui, state.OriginalTopRowMargin);
        }

        private sealed class WindowState
        {
            public WindowState(
                EditorWindow window,
                VisualElement viewport,
                VisualElement bar,
                object treeView,
                TreeViewMembers members)
            {
                Window = window;
                Viewport = viewport;
                Bar = bar;
                SetTreeView(treeView, members);
            }

            public EditorWindow Window { get; }

            public VisualElement Viewport { get; }

            public VisualElement Bar { get; }

            public object TreeView { get; private set; }

            public TreeViewMembers Members { get; private set; }

            public float OriginalTopRowMargin { get; private set; }

            public void SetTreeView(object treeView, TreeViewMembers members)
            {
                TreeView = treeView;
                Members = members;
                OriginalTopRowMargin = Convert.ToSingle(members.TopRowMarginField.GetValue(members.Gui));
            }
        }

        private readonly struct TreeViewMembers
        {
            public TreeViewMembers(
                object gui,
                object treeViewState,
                FieldInfo topRowMarginField,
                PropertyInfo scrollPositionProperty,
                FieldInfo scrollPositionField)
            {
                Gui = gui;
                TreeViewState = treeViewState;
                TopRowMarginField = topRowMarginField;
                ScrollPositionProperty = scrollPositionProperty;
                ScrollPositionField = scrollPositionField;
            }

            public object Gui { get; }

            public object TreeViewState { get; }

            public FieldInfo TopRowMarginField { get; }

            public PropertyInfo ScrollPositionProperty { get; }

            public FieldInfo ScrollPositionField { get; }
        }

        private static class ListPool<T>
        {
            private static readonly Stack<List<T>> Pool = new();

            public static List<T> Get()
            {
                return Pool.Count > 0 ? Pool.Pop() : new List<T>();
            }

            public static void Release(List<T> list)
            {
                list.Clear();
                Pool.Push(list);
            }
        }
    }
}
