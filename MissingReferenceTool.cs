using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MissingReferenceTool
{
    /// <summary> Windows and popups for missing reference searching with menu item methods </summary>
    public class ToolWindow : EditorWindow
    {
        private const string Title = "Find Missing References";

        private static SearchEngine finder;
        private static Row[] Result;

        private Vector2 scrollPosition;

        private static bool CanDrawTable() => Result != null && Result.Length != 0;

        /// <summary> Return false if there are unsaved scene or prefab </summary>
        [MenuItem("Tools/" + Title, true)]
        private static bool CanBeStarted()
        {
            if (SceneManager.GetActiveScene().isDirty)
                return false;

            var stage = PrefabStageUtility.GetCurrentPrefabStage();

            if (stage != null)
                if (stage.scene.isDirty)
                    return false;

            return true;
        }

        [MenuItem("Tools/" + Title, false)]
        private static void SelectInMenu()
        {
            if (!EditorUtility.DisplayDialog(Title, "This operation may take time. Are you sure?", "Start", "Cancel"))
                return;

            try
            {
                finder = new SearchEngine();
                // start search and wait for results
                Result = finder.Find(DrawProgressBar);
            }
            catch (System.Exception exc)
            {
                Debug.LogError("Error raised during the missing references search at " + finder.Current.Path + " -> " + finder.Current.SubPath);
                Debug.LogWarning(exc);
            }

            ClearProgressBar();

            if (CanDrawTable())
                GetWindow<ToolWindow>("Missing References");
            else
                EditorUtility.DisplayDialog(Title, "No missing references found", "OK");
        }

        private void OnGUI()
        {
            if (!CanDrawTable())
                return;

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            for (int i = 0; i < Result.Length; i++)
            {
                // draw rows like in console
                if (i % 2 == 0)
                    GUILayout.BeginVertical("CN EntryBackEven");
                else
                    GUILayout.BeginVertical("CN EntryBackodd");

                GUILayout.Label(Result[i].Description);
                GUILayout.TextField(Result[i].Path);
                GUILayout.EndVertical();
            }

            GUILayout.EndScrollView();
        }
        private static void DrawProgressBar(string path, float progress) => EditorUtility.DisplayProgressBar(Title, path, progress);
        private static void ClearProgressBar() => EditorUtility.ClearProgressBar();
    }

    /// <summary> Searching result row </summary>
    public class Row
    {
        public string Path { get; private set; } = string.Empty;
        public string SubPath { get; private set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description => "Missing " + Name + (SubPath == string.Empty ? string.Empty : " in ") + SubPath;

        public Row(string path) => Path = path;
        public Row(Row old)
        {
            Path = old.Path;
            SubPath = old.SubPath;
        }

        public void StepInto(string path) => SubPath += (SubPath == string.Empty) ? path : '/' + path;
        public void StepBack() => SubPath = SubPath.Contains("/") ? SubPath.Remove(SubPath.LastIndexOf('/')) : string.Empty;
    }

    /// <summary> Searching for missing references in all assets </summary>
    public class SearchEngine
    {
        public delegate void SearchHandler(string path, float progress);
        private List<Row> rows;

        public Row Current { get; private set; }

        public Row[] Find(SearchHandler observer = null)
        {
            rows = new List<Row>();
            string[] paths = AssetDatabase.GetAllAssetPaths();

            for (int i = 0; i < paths.Length; i++)
            {
                // only Assets/.. files
                if (!paths[i].StartsWith("Assets"))
                    continue;

                Current = new Row(paths[i]);
                observer?.Invoke(Current.Path, (float)i / paths.Length);

                if (Current.Path.EndsWith(".prefab"))
                    SearchMissingReferencesInObject(AssetDatabase.LoadAssetAtPath<GameObject>(Current.Path));
                else
                if (Current.Path.EndsWith(".unity"))
                    SearchMissingReferencesInObject(EditorSceneManager.OpenScene(Current.Path));
                else
                    SearchMissingReferencesInObject(AssetDatabase.LoadAssetAtPath<Object>(Current.Path));
            }

            return rows.ToArray();
        }

        // search in all game objects in the scene
        private void SearchMissingReferencesInObject(Scene scene)
        {
            if (scene == null)
            {
                DeclareMissing();
                return;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
                SearchMissingReferencesInObject(root);
        }

        // search in root game object and all his childs
        private void SearchMissingReferencesInObject(GameObject root)
        {
            if (root == null)
            {
                DeclareMissing();
                return;
            }

            Current.StepInto(root.name);

            // search in root game object components
            foreach (Component component in root.GetComponents<Component>())
                SearchMissingReferencesInObject(component);

            // search in child game objects components
            foreach (Transform child in root.transform)
                SearchMissingReferencesInObject(child.gameObject);

            Current.StepBack();
        }

        // search for missing properties in serialized object
        private void SearchMissingReferencesInObject(Object obj)
        {
            if (obj == null)
            {
                DeclareMissing();
                return;
            }

            Current.StepInto(GetObjectName(obj));
            var container = new SerializedObject(obj);
            var property = container.GetIterator();

            while (property.NextVisible(true))
                if (IsPropertyMissing(property))
                    DeclareMissing(property.displayName);

            Current.StepBack();
        }

        // add to results
        private void DeclareMissing(string property = "Object")
        {
            Current.Name = property;
            rows.Add(Current);
            Current = new Row(Current);
        }

        private static bool IsPropertyMissing(SerializedProperty property)
        {
            if (property == null)
                return false;

            if (property.propertyType != SerializedPropertyType.ObjectReference)
                return false;

            // if property is null but has instance id - it's missing
            if (property.objectReferenceValue == null &&
                property.objectReferenceInstanceIDValue != 0)
                return true;

            return false;
        }

        private static string GetObjectName(Object obj)
        {
            string objName = obj.GetType().ToString();

            if (objName.Contains("."))
                objName = objName.Substring(objName.LastIndexOf('.') + 1);

            return objName;
        }
    }
}
