using UnityEngine;
namespace TCS.SceneManagement {
    public class SceneManagerSingleton<T> : MonoBehaviour where T : Component { 
        public bool m_autoUnparentOnAwake = true;
        protected static T instance;
        public static bool HasInstance => instance; 
        public static T TryGetInstance() => HasInstance ? instance : null;
        public static T Instance {
            get {
                if (instance) return instance;
                instance = FindAnyObjectByType<T>();
                if (instance) return instance;
                var go = new GameObject(typeof(T).Name + " Auto-Generated");
                instance = go.AddComponent<T>();

                return instance;
            }
        }
        
        /// <summary>
        /// Make sure to call base.Awake() in override if you need awake.
        /// </summary>
        protected virtual void Awake() {
            InitializeSingleton();
        }

        void InitializeSingleton() {
            if (!Application.isPlaying) return;
            if (m_autoUnparentOnAwake) {
                transform.SetParent(null);
            }
            if (!instance) {
                instance = this as T;
                DontDestroyOnLoad(gameObject);
            }
            else {
                if (instance != this) {
                    Destroy(gameObject);
                }
            }
        }
    }
}