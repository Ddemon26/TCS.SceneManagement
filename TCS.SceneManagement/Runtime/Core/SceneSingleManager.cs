using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Eflatun.SceneReference;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace TCS.SceneManagement {
    public class SceneSingleManager {
        public event Action<string> OnSceneLoaded = delegate { };
        public event Action<string> OnSceneUnloaded = delegate { };

        // Use this group to track addressable scene handles (in case you need to unload).
        readonly AsyncOperationHandleGroup m_handleGroup = new(1);

        string m_activeSceneName;

        /// <summary>
        /// Loads a single scene (Regular or Addressable).
        /// If it's already loaded and <paramref name="reloadIfLoaded"/> is false, it skips reloading.
        /// Otherwise, it unloads the current scene before loading again.
        /// </summary>
        /// <param name="sceneRef">Scene reference to load.</param>
        /// <param name="progress">Progress object for reporting loading progress (0..1).</param>
        /// <param name="token"></param>
        /// <param name="reloadIfLoaded">Whether to unload/reload if this scene is already loaded.</param>
        public async Task LoadSceneAsync(SceneReference sceneRef, [CanBeNull] IProgress<float> progress, CancellationToken token, bool reloadIfLoaded = false) {
            if (IsSceneLoaded(sceneRef)) {
                if (!reloadIfLoaded) {
                    return;
                }
            }

            m_handleGroup.Handles.Clear();
            var operationGroup = new AsyncOperationGroup(1);

            if (sceneRef.State == SceneReferenceState.Regular) {
                var loadOp = SceneManager.LoadSceneAsync(sceneRef.Name, LoadSceneMode.Additive);
                await Task.Delay(TimeSpan.FromSeconds(1f), token);

                if (loadOp != null) {
                    operationGroup.Operations.Add(loadOp);
                }
            }
            else if (sceneRef.State == SceneReferenceState.Addressable) {
                AsyncOperationHandle<SceneInstance> handle = Addressables.LoadSceneAsync(sceneRef.Path, LoadSceneMode.Additive);
                m_handleGroup.Handles.Add(handle);
            }

            while (!operationGroup.IsDone || !m_handleGroup.IsDone) {
                token.ThrowIfCancellationRequested();
                float combined = (operationGroup.Progress + m_handleGroup.Progress) / 2f;
                progress?.Report(combined);
                await Task.Delay(100, token);
            }

            var loadedScene = SceneManager.GetSceneByName(sceneRef.Name);
            if (loadedScene.IsValid()) {
                SceneManager.SetActiveScene(loadedScene);
            }

            m_activeSceneName = sceneRef.Name;
            OnSceneLoaded.Invoke(m_activeSceneName);
        }

        public async Task UnloadSceneAsync(SceneReference sceneRef, CancellationToken token) {
            var scene = SceneManager.GetSceneByName(sceneRef.Name);
            if (!scene.IsValid() || !scene.isLoaded) {
                Debug.LogWarning($"Scene '{sceneRef.Name}' is not loaded.");
                return;
            }

            if (sceneRef.State == SceneReferenceState.Regular) {
                var unloadOp = SceneManager.UnloadSceneAsync(sceneRef.Name);
                if (unloadOp != null) {
                    var opGroup = new AsyncOperationGroup(1);
                    opGroup.Operations.Add(unloadOp);

                    while (!opGroup.IsDone) {
                        token.ThrowIfCancellationRequested();
                        await Task.Delay(100, token);
                    }
                }
            }
            else if (sceneRef.State == SceneReferenceState.Addressable) {
                foreach (AsyncOperationHandle<SceneInstance> handle in m_handleGroup.Handles.Where(h => h.IsValid())) {
                    if (handle.Result.Scene.name.Equals(sceneRef.Name, StringComparison.Ordinal)) {
                        await Addressables.UnloadSceneAsync(handle).Task;
                        break;
                    }
                }
            }

            OnSceneUnloaded.Invoke(sceneRef.Name);
            m_handleGroup.Handles.Clear();

            if (m_activeSceneName == sceneRef.Name) {
                m_activeSceneName = null;
            }
        }

        static bool IsSceneLoaded(SceneReference sceneRef) {
            var scene = SceneManager.GetSceneByName(sceneRef.Name);
            return scene.IsValid() && scene.isLoaded;
        }
    }
}