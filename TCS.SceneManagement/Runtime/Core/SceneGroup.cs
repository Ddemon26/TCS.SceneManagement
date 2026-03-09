using System;
using System.Collections.Generic;
using System.Linq;
using Eflatun.SceneReference;
namespace TCS.SceneManagement {
    [Serializable]
    public class SceneGroup {
        public string m_groupName = "New Scene Group";
        public List<SceneData> m_scenes;
        
        public string FindSceneNameByType(SceneType sceneType) {
            return m_scenes.FirstOrDefault(scene => scene.m_sceneType == sceneType)?.m_reference.Name;
        }
    }
    
    [Serializable]
    public class SceneData {
        public SceneReference m_reference;
        public string Name => m_reference.Name;
        public SceneType m_sceneType;
    }
    
    public enum SceneType { ActiveScene, MainMenu, UserInterface, HUD, Cinematic, Environment, Tooling }
}