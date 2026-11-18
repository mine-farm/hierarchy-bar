using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MF.Editor.HierarchyObjectPins
{
    [Serializable]
    internal sealed class HierarchyObjectPinEntry
    {
        [SerializeField] private string globalObjectId = string.Empty;
        [SerializeField] private string sceneGuid = string.Empty;
        [SerializeField] private int transientInstanceId;
        [SerializeField] private int transientSceneHandle;
        [SerializeField] private string transientSessionId = string.Empty;
        [SerializeField] private string displayName = string.Empty;

        internal HierarchyObjectPinEntry(GameObject gameObject, string sessionId)
        {
            transientInstanceId = gameObject.GetInstanceID();
            transientSceneHandle = gameObject.scene.handle;
            transientSessionId = sessionId;
            displayName = gameObject.name;
            PromoteIfPossible(gameObject);
        }

        internal string DisplayName => displayName;

        internal bool IsPersistent => !string.IsNullOrEmpty(globalObjectId);

        internal bool IsTransientForSession(string sessionId)
        {
            return string.Equals(
                transientSessionId,
                sessionId,
                StringComparison.Ordinal);
        }

        internal bool IsForScene(Scene scene, string currentSessionId)
        {
            if (!string.IsNullOrEmpty(sceneGuid))
            {
                return !string.IsNullOrEmpty(scene.path) &&
                       string.Equals(
                           sceneGuid,
                           AssetDatabase.AssetPathToGUID(scene.path),
                           StringComparison.Ordinal);
            }

            return string.Equals(
                       transientSessionId,
                       currentSessionId,
                       StringComparison.Ordinal) &&
                   transientSceneHandle == scene.handle;
        }

        internal bool Matches(GameObject gameObject, string currentSessionId)
        {
            if (!string.IsNullOrEmpty(globalObjectId))
            {
                var id = GlobalObjectId.GetGlobalObjectIdSlow(gameObject);
                return id.identifierType != 0 &&
                       string.Equals(globalObjectId, id.ToString(), StringComparison.Ordinal);
            }

            return string.Equals(
                       transientSessionId,
                       currentSessionId,
                       StringComparison.Ordinal) &&
                   transientInstanceId == gameObject.GetInstanceID();
        }

        internal GameObject Resolve(string currentSessionId)
        {
            GameObject gameObject = null;
            if (!string.IsNullOrEmpty(globalObjectId) &&
                GlobalObjectId.TryParse(globalObjectId, out var id))
            {
                gameObject = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
            }

            if (gameObject == null &&
                string.Equals(
                    transientSessionId,
                    currentSessionId,
                    StringComparison.Ordinal))
            {
#pragma warning disable CS0618
                gameObject = EditorUtility.InstanceIDToObject(transientInstanceId) as GameObject;
#pragma warning restore CS0618
            }

            return gameObject;
        }

        internal bool PromoteIfPossible(GameObject gameObject)
        {
            if (gameObject == null || !gameObject.scene.IsValid())
                return false;

            var id = GlobalObjectId.GetGlobalObjectIdSlow(gameObject);
            if (id.identifierType == 0 || string.IsNullOrEmpty(gameObject.scene.path))
                return false;

            globalObjectId = id.ToString();
            sceneGuid = AssetDatabase.AssetPathToGUID(gameObject.scene.path);
            displayName = gameObject.name;
            return true;
        }
    }

    [FilePath(
        "UserSettings/HierarchyObjectPins.asset",
        FilePathAttribute.Location.ProjectFolder)]
    internal sealed class HierarchyObjectPinSettings :
        ScriptableSingleton<HierarchyObjectPinSettings>
    {
        private const string SessionStateKey = "MF.HierarchyObjectPins.SessionId";

        [SerializeField] private List<HierarchyObjectPinEntry> entries = new();

        private static string currentSessionId;

        internal static event Action Changed;

        internal static void Initialize()
        {
            var removed = instance.entries.RemoveAll(entry =>
                entry == null ||
                (!entry.IsPersistent && !entry.IsTransientForSession(CurrentSessionId)));
            if (removed > 0)
                instance.Save(true);
        }

        internal static IReadOnlyList<HierarchyObjectPinEntry> GetPins(Scene scene)
        {
            if (!scene.IsValid())
                return Array.Empty<HierarchyObjectPinEntry>();

            return instance.entries
                .Where(entry => entry != null && entry.IsForScene(scene, CurrentSessionId))
                .ToArray();
        }

        internal static bool Contains(GameObject gameObject)
        {
            return gameObject != null && instance.entries.Any(entry =>
                entry != null && entry.Matches(gameObject, CurrentSessionId));
        }

        internal static bool Toggle(GameObject gameObject)
        {
            if (gameObject == null || !gameObject.scene.IsValid())
                return false;

            var existing = instance.entries.FirstOrDefault(entry =>
                entry != null && entry.Matches(gameObject, CurrentSessionId));
            if (existing != null)
            {
                instance.entries.Remove(existing);
            }
            else
            {
                instance.entries.Add(new HierarchyObjectPinEntry(gameObject, CurrentSessionId));
            }

            SaveAndNotify();
            return existing == null;
        }

        internal static void Remove(HierarchyObjectPinEntry entry)
        {
            if (entry == null || !instance.entries.Remove(entry))
                return;

            SaveAndNotify();
        }

        internal static GameObject Resolve(HierarchyObjectPinEntry entry)
        {
            return entry?.Resolve(CurrentSessionId);
        }

        internal static void PromoteTransientPins(Scene scene)
        {
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
                return;

            var changed = false;
            foreach (var entry in instance.entries)
            {
                if (entry == null || !entry.IsForScene(scene, CurrentSessionId))
                    continue;

                var gameObject = entry.Resolve(CurrentSessionId);
                changed |= entry.PromoteIfPossible(gameObject);
            }

            if (changed)
                SaveAndNotify();
        }

        private static string CurrentSessionId
        {
            get
            {
                if (!string.IsNullOrEmpty(currentSessionId))
                    return currentSessionId;

                currentSessionId = SessionState.GetString(SessionStateKey, string.Empty);
                if (!string.IsNullOrEmpty(currentSessionId))
                    return currentSessionId;

                currentSessionId = Guid.NewGuid().ToString("N");
                SessionState.SetString(SessionStateKey, currentSessionId);
                return currentSessionId;
            }
        }

        private static void SaveAndNotify()
        {
            instance.Save(true);
            Changed?.Invoke();
        }
    }
}
