// Copyright (c) Meta Platforms, Inc. and affiliates.
using System.Collections;
using Meta.Utilities.Narrative;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace NorthStar
{
    /// <summary>
    /// Handles scene managment
    /// </summary>
    [MetaCodeSample("NorthStar")]
    public class GameFlowController : MonoBehaviour
    {
        private static GameFlowController s_instance;

        public static GameFlowController Instance
        {
            get
            {
                if (s_instance == null)
                {
                    // Try to find an existing instance in the scene
                    s_instance = FindFirstObjectByType<GameFlowController>();
                    if (s_instance == null)
                    {
                        // If none exists, create a new GameObject and attach the component
                        var singletonObj = new GameObject("GameFlowController (Singleton)");
                        s_instance = singletonObj.AddComponent<GameFlowController>();
                    }
                }

                return s_instance;
            }
            private set => s_instance = value;
        }

        [SerializeField] private TaskID[] m_sceneTasks;

        public UnityEvent SceneChangeComplete;
        public UnityEvent RestartGameRequested;
        public UnityEvent GameOverRequested;

        public TaskID FirstTask = TaskID.None;
        public bool StartAutomatically = false;

        private AsyncOperation m_loadOperation;
        public bool IsLoading { get; private set; }
        private string m_loadingSceneName;

        [field: SerializeField] public bool GameCompleteOnce { get; private set; }

        private static bool s_firstLaunch = true;

        private void Awake()
        {
            if (s_instance)
            {
                DestroyImmediate(gameObject);
            }
            else
            {
                s_instance = this;
            }
        }

        private void Start()
        {
            if (!Application.isPlaying) return;

            if (s_firstLaunch && ProfilingSystem.SceneName != null)
            {
                LoadScene(ProfilingSystem.SceneName);
            }
            else
            {
                TaskManager.StartNarrativeFromTaskID(FirstTask);
            }

            s_firstLaunch = false;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Init() { }

        public void ShowGameOver() => GameOverRequested?.Invoke();

        public void GoToCredits()
        {
            LoadScreen.Instance.GoToCredits();
        }

        public void SetGameComplete()
        {
            GameCompleteOnce = true;
            TaskManager.StartNarrative();
        }

        public void LoadScene(string sceneName)
        {
            StopAllCoroutines();
            _ = StartCoroutine(ChangeSceneCoroutine(sceneName));
        }

        public void ForceLoadScene(string sceneName)
        {

            StopAllCoroutines();
            _ = StartCoroutine(ChangeSceneCoroutine(sceneName, true));
        }

        private IEnumerator ChangeSceneCoroutine(string sceneName, bool allowReloadScene = false)
        {
            if (OVRScreenFade.instance)
            {
                OVRScreenFade.instance.FadeOut();
                yield return new WaitForSeconds(OVRScreenFade.instance.fadeTime);
            }

            var activeScene = SceneManager.GetActiveScene();

            if (!string.IsNullOrWhiteSpace(sceneName) && (allowReloadScene || !activeScene.IsValid() || activeScene.name != sceneName))
            {
                var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                while (operation != null && !operation.isDone)
                    yield return null;
            }

            SceneChangeComplete?.Invoke();
        }

        public void ResetLoadState()
        {
            IsLoading = false;
            m_loadOperation = null;
            m_loadingSceneName = null;
        }

        public void PreloadScene(string sceneName)
        {
            var buildIndex = SceneUtility.GetBuildIndexByScenePath(sceneName);
            if (buildIndex == -1)
            {
                Debug.LogError($"Error pre-loading scene: {sceneName}, not in build");
                return;
            }
            Debug.Assert(!IsLoading, "Trying to load 2 scenes at once");
            var activeScene = SceneManager.GetActiveScene();
            Debug.Assert(sceneName != activeScene.name, $"Scene: {sceneName} already loaded");

            m_loadOperation = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
            Application.backgroundLoadingPriority = ThreadPriority.Low;
            m_loadOperation.priority = (int)ThreadPriority.Low;
            m_loadOperation.allowSceneActivation = false;
            IsLoading = true;
            m_loadingSceneName = sceneName;
        }

        public void CompleteSceneLoad(string sceneName)
        {
            if (!IsLoading)
            {
                Debug.LogError("Not loading a scene");
                PreloadScene(sceneName);
            }
            Debug.Assert(m_loadingSceneName == sceneName, "Loading the wrong scene");

            if (m_loadOperation.progress < .9f)
            {
                //this is ok but means the scene is loading prematuraly
                Debug.LogWarning("Scene not finished loading perhaps start loading earlier");
            }

            StopAllCoroutines();
            _ = StartCoroutine(CompleteSceneLoadCoroutine());
        }

        public float GetLoadProgress()
        {
            return !IsLoading ? 0f : m_loadOperation == null ? 0f : m_loadOperation.progress;
        }

        private IEnumerator CompleteSceneLoadCoroutine()
        {
            m_loadOperation.allowSceneActivation = true;
            while (!m_loadOperation.isDone)
            {
                yield return null;
            }
            ResetLoadState();
            SceneChangeComplete?.Invoke();
        }

        public void RestartGame() => RestartGameRequested?.Invoke();
    }
}