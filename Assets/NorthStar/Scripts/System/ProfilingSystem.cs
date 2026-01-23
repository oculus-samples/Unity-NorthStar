// Copyright (c) Meta Platforms, Inc. and affiliates.
using System;
using System.Collections.Generic;
using Meta.Utilities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Meta.Utilities.Environment;


#if UNITY_EDITOR
using Meta.XR.Samples;
using UnityEditor;

namespace NorthStar
{
    /// <summary>
    /// Utility window for sending common commands to the game via the ADB command bridge
    /// </summary>
    [MetaCodeSample("NorthStar")]
    public class CommandSenderWindow : EditorWindow
    {
        private Dictionary<string, object> m_values = new();

        [MenuItem("Tools/NorthStar/Command Sender")]
        public static void ShowWindow()
        {
            _ = GetWindow<CommandSenderWindow>("Command Sender");
        }

        private void OnGUI()
        {
            void CommandGUI<T>(string title, Dictionary<string, Action<T>> commands, Func<string, T, T> field)
            {
                GUILayout.Label(title, EditorStyles.boldLabel);
                foreach (var (key, action) in commands)
                {
                    using var scope = new EditorGUILayout.HorizontalScope();
                    var value = field(key, (m_values.TryGetValue(key, out var val) ? val : null) is T v ? v : default);
                    if (GUILayout.Button("Send"))
                    {
                        action(value);
                    }
                    m_values[key] = value;
                }
            }

            CommandGUI(
                "String Commands",
                ProfilingSystem.StringCommands,
                (key, value) => EditorGUILayout.TextField(key, value));

            GUILayout.Space(10);

            CommandGUI(
                "Float Commands",
                ProfilingSystem.FloatCommands,
                (key, value) => EditorGUILayout.FloatField(key, value));

            GUILayout.Space(10);

            CommandGUI(
                "Integer Commands",
                ProfilingSystem.IntegerCommands,
                (key, value) => EditorGUILayout.IntField(key, value));

            GUILayout.Space(10);

            CommandGUI(
                "Boolean Commands",
                ProfilingSystem.BooleanCommands,
                (key, value) => EditorGUILayout.Toggle(key, value));
        }
    }
}
#endif

namespace NorthStar
{
    /// <summary>
    /// The profiling system is used to handle incomming commands from ADB to enable/disable various features
    ///
    /// Profiling mode allows the game to be benchmarked by utilising profiling cameras that are placed manually in the scene ahead of time
    /// 
    /// </summary>
    public class ProfilingSystem
    {
        /// <summary>
        /// This android proxy class receives events on behalf of the application and then forwards them on the main thread so they can be safely processed
        /// </summary>
        internal class ProfilerCommandHandler : AndroidJavaProxy
        {
            internal ProfilerCommandHandler() : base("com.meta.northstar.ProfileCommandInterface") { }

#pragma warning disable IDE1006 // ReSharper disable InconsistentNaming
            public void setString(string key, string value)
            {
                s_mainThreadContext.Post(_ => SetString(key, value), null);
            }

            public void setFloat(string key, float value)
            {
                s_mainThreadContext.Post(_ => SetFloat(key, value), null);
            }

            public void setInteger(string key, int value)
            {
                s_mainThreadContext.Post(_ => SetInteger(key, value), null);
            }

            public void setBoolean(string key, bool value)
            {
                s_mainThreadContext.Post(_ => SetBoolean(key, value), null);
            }
#pragma warning restore IDE1006 // ReSharper restore InconsistentNaming
        }

        private const string ENABLE_PROFILING_MODE_KEY = "enable_profiling_mode";
        private const string SCENE_KEY = "scene";
        private const string CAMERA_KEY = "camera";

        private static bool s_enabled;
        private static string s_sceneName;
        private static string s_cameraName;
        private static System.Threading.SynchronizationContext s_mainThreadContext;

        internal static Dictionary<string, Action<string>> StringCommands = new();
        internal static Dictionary<string, Action<float>> FloatCommands = new();
        internal static Dictionary<string, Action<int>> IntegerCommands = new();
        internal static Dictionary<string, Action<bool>> BooleanCommands = new();

        public static bool Enabled
        {
            get => s_enabled;
            set
            {
                s_enabled = value;
                UpdateCameras();
            }
        }

        public static string SceneName
        {
            get => s_sceneName;
            set
            {
                s_sceneName = value;
                if (SceneManager.GetActiveScene().name != s_sceneName)
                {
                    SceneManager.LoadScene(s_sceneName);
                }
            }
        }

        public static string CameraName
        {
            get => s_cameraName;
            set
            {
                s_cameraName = value;
                UpdateCameras();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            s_mainThreadContext = System.Threading.SynchronizationContext.Current;

            // Check for launch parameters that enable profiling from the beginning
            if (bool.TryParse(AndroidHelpers.GetStringIntentExtra(ENABLE_PROFILING_MODE_KEY) ?? "false", out var enableProfiling) && enableProfiling)
            {
                Enabled = enableProfiling;
            }

            if (AndroidHelpers.HasIntentExtra(CAMERA_KEY))
            {
                CameraName = AndroidHelpers.GetStringIntentExtra(CAMERA_KEY);
            }

            if (AndroidHelpers.HasIntentExtra(SCENE_KEY))
            {
                SceneName = AndroidHelpers.GetStringIntentExtra(SCENE_KEY);
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            new AndroidJavaClass("com.meta.northstar.ProfileCommandReceiver").SetStatic("profileCommandInterface", new ProfilerCommandHandler());
#endif

            SceneManager.sceneLoaded += SceneLoaded;

            // Here are some sample commands various options that you may want to test on a live build
            AddBooleanCommand("profiling_enabled", (enabled) => Enabled = enabled);

            AddStringCommand("scene", (sceneName) => SceneName = sceneName);

            AddStringCommand("camera", (cameraName) => CameraName = cameraName);

            AddBooleanCommand("bloom_enabled", (enabled) =>
            {
                var volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var volume in volumes)
                {
                    if (volume.profile.TryGet<Bloom>(out var bloom))
                    {
                        bloom.active = enabled;
                    }
                }
            });

            AddIntegerCommand("msaa_level", (level) =>
            {
                UniversalRenderPipeline.asset.msaaSampleCount = level;
            });

            AddFloatCommand("enable_asw_for_duration", (duration) =>
            {
                UnityEngine.Object.FindFirstObjectByType<QualityControls>().EnableSpaceWarpForDuration(duration);
            });

            AddBooleanCommand("enable_asw", (enabled) =>
            {
                var qc = UnityEngine.Object.FindFirstObjectByType<QualityControls>();
                if (enabled)
                {
                    qc.EnableSpaceWarp();
                }
                else
                {
                    qc.CancelSpaceWarp();
                }
            });

            AddFloatCommand("set_fixed_delta_time", (dt) =>
            {
                Time.fixedDeltaTime = dt;
                Time.maximumDeltaTime = dt * 2;
            });
        }

        /// <summary>
        /// This is used by profiling mode to set the current active profiling camera (or re-enable the normal camera)
        /// </summary>
        /// <param name="restoreDefaultVisibilitySet">Whether or not to restore the default visibility set when returning to normal play mode</param>
        private static void UpdateCameras(bool restoreDefaultVisibilitySet = true)
        {
            var cameraRig = UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
            var profilingCameras = UnityEngine.Object.FindObjectsByType<ProfilingCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (s_enabled)
            {
                if (cameraRig)
                {
                    cameraRig.leftEyeCamera.gameObject.SetActive(false);
                    cameraRig.rightEyeCamera.gameObject.SetActive(false);
                }

                if (s_cameraName != null)
                {
                    for (var i = 0; i < profilingCameras.Length; i++)
                    {
                        var pc = profilingCameras[i];
                        pc.gameObject.SetActive(pc.Name == CameraName);
                        pc.Camera.fieldOfView = cameraRig.leftEyeCamera.fieldOfView;
                        pc.Camera.nearClipPlane = cameraRig.leftEyeCamera.nearClipPlane;
                        pc.Camera.farClipPlane = cameraRig.leftEyeCamera.farClipPlane;
                        pc.Camera.GetUniversalAdditionalCameraData().renderPostProcessing = cameraRig.leftEyeCamera.GetUniversalAdditionalCameraData().renderPostProcessing;

                        if (pc.Name == CameraName && VisibilityController.Instance != null)
                        {
                            VisibilityController.Instance.ActiveVisibilitySet = pc.VisibilitySet;
                        }
                    }
                }
                else
                {
                    for (var i = 0; i < profilingCameras.Length; i++)
                    {
                        var pc = profilingCameras[i];
                        pc.gameObject.SetActive(i == 0);
                    }
                }
            }
            else
            {
                if (cameraRig)
                {
                    cameraRig.leftEyeCamera.gameObject.SetActive(true);
                    cameraRig.rightEyeCamera.gameObject.SetActive(true);
                }

                for (var i = 0; i < profilingCameras.Length; i++)
                {
                    var pc = profilingCameras[i];
                    pc.gameObject.SetActive(false);
                }

                if (restoreDefaultVisibilitySet && VisibilityController.Instance != null)
                {
                    VisibilityController.Instance.ActiveVisibilitySet = null;
                }
            }
        }

        private static void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            UpdateCameras(false);
        }

        public static void AddStringCommand(string command, Action<string> callback)
        {
            StringCommands.Add(command, callback);
        }

        public static void AddIntegerCommand(string command, Action<int> callback)
        {
            IntegerCommands.Add(command, callback);
        }

        public static void AddFloatCommand(string command, Action<float> callback)
        {
            FloatCommands.Add(command, callback);
        }

        public static void AddBooleanCommand(string command, Action<bool> callback)
        {
            BooleanCommands.Add(command, callback);
        }

        public static void SetString(string key, string value)
        {
            if (StringCommands.TryGetValue(key, out var cmd))
            {
                cmd.Invoke(value);
            }
        }

        public static void SetFloat(string key, float value)
        {
            if (FloatCommands.TryGetValue(key, out var cmd))
            {
                cmd.Invoke(value);
            }
        }

        public static void SetInteger(string key, int value)
        {
            if (IntegerCommands.TryGetValue(key, out var cmd))
            {
                cmd.Invoke(value);
            }
        }

        public static void SetBoolean(string key, bool value)
        {
            if (BooleanCommands.TryGetValue(key, out var cmd))
            {
                cmd.Invoke(value);
            }
        }
    }
}
