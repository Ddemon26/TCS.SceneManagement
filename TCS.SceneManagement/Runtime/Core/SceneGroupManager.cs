using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Eflatun.SceneReference;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
namespace TCS.SceneManagement {
    public class SceneGroupManager {
        public event Action<string> OnSceneLoaded = delegate { };
        public event Action<string> OnSceneUnloaded = delegate { };
        public event Action OnSceneGroupLoaded = delegate { };

        readonly AsyncOperationHandleGroup m_handleGroup = new(10);

        SceneGroup m_activeSceneGroup;
        
        public async Task LoadScenes(SceneGroup group, IProgress<float> progress, bool reloadDupScenes = false) { 
            m_activeSceneGroup = group;
            List<string> loadedScenes = new();

            await UnloadScenes();

            int sceneCount = SceneManager.sceneCount;
            
            for (var i = 0; i < sceneCount; i++) {
                loadedScenes.Add(SceneManager.GetSceneAt(i).name);
            }

            int totalScenesToLoad = m_activeSceneGroup.m_scenes.Count;

            var operationGroup = new AsyncOperationGroup(totalScenesToLoad);

            for (var i = 0; i < totalScenesToLoad; i++) {
                var sceneData = group.m_scenes[i];
                if (reloadDupScenes == false && loadedScenes.Contains(sceneData.Name)) continue;
                
                if (sceneData.m_reference.State == SceneReferenceState.Regular)
                {
                    var operation = SceneManager.LoadSceneAsync(sceneData.m_reference.Path, LoadSceneMode.Additive);
                    
                    //SceneEvents.OnLoadInfo.Invoke("Loading " + sceneData.Name);
                    
                    await Task.Delay(TimeSpan.FromSeconds(10f)); // TODO: Remove this delay (This is a too simulate a long loading time)
                    
                    operationGroup.Operations.Add(operation);
                }
                else if (sceneData.m_reference.State == SceneReferenceState.Addressable)
                {
                    AsyncOperationHandle<SceneInstance> sceneHandle = Addressables.LoadSceneAsync(sceneData.m_reference.Path, LoadSceneMode.Additive);
                    m_handleGroup.Handles.Add(sceneHandle);
                }
                
                OnSceneLoaded.Invoke(sceneData.Name);
            }
            
            // Wait until all AsyncOperations in the group are done
            while (!operationGroup.IsDone || !m_handleGroup.IsDone) {
                progress?.Report((operationGroup.Progress + m_handleGroup.Progress) / 2);
                await Task.Delay(100);
            }

            var activeScene = SceneManager.GetSceneByName(m_activeSceneGroup.FindSceneNameByType(SceneType.ActiveScene));

            if (activeScene.IsValid()) {
                SceneManager.SetActiveScene(activeScene);
            }

            OnSceneGroupLoaded.Invoke();
        }

        public async Task UnloadScenes() { 
            List<string> scenes = new();
            string activeScene = SceneManager.GetActiveScene().name;
            
            int sceneCount = SceneManager.sceneCount;

            for (int i = sceneCount - 1; i > 0; i--) {
                var sceneAt = SceneManager.GetSceneAt(i);
                if (!sceneAt.isLoaded) continue;
                
                string sceneName = sceneAt.name;
                if (sceneName.Equals(activeScene)) continue;
                if (m_handleGroup.Handles.Any(h => h.IsValid() && h.Result.Scene.name == sceneName)) continue;
                
                scenes.Add(sceneName);
            }
            
            // Create an AsyncOperationGroup
            var operationGroup = new AsyncOperationGroup(scenes.Count);
            
            foreach (string scene in scenes) { 
                var operation = SceneManager.UnloadSceneAsync(scene);
                if (operation == null) continue;
                
                operationGroup.Operations.Add(operation);

                OnSceneUnloaded.Invoke(scene);
            }
            
            foreach (AsyncOperationHandle<SceneInstance> handle in m_handleGroup.Handles.Where(handle => handle.IsValid())) {
                Addressables.UnloadSceneAsync(handle);
            }
            m_handleGroup.Handles.Clear();

            // Wait until all AsyncOperations in the group are done
            while (!operationGroup.IsDone) {
                await Task.Delay(100); // delay to avoid tight loop
            }
            
            // Optional: UnloadUnusedAssets - unloads all unused assets from memory
            //await Resources.UnloadUnusedAssets();
        }
    }
}