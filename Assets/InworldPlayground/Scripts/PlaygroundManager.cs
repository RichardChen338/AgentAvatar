/*************************************************************************************************
 * Copyright 2024 Theai, Inc. (DBA Inworld)
 *
 * Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
 * that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
 *************************************************************************************************/

using System.Collections;
using Inworld.AEC;
using Inworld.Interactions;
using Inworld.NDK;
using Inworld.Sample;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Inworld.Playground
{
    /// <summary>
    ///     Manages the Playground: changing scenes, updating settings, play/pausing.
    /// </summary>
    public class PlaygroundManager : SingletonBehavior<PlaygroundManager>
    {
        public UnityEvent OnClientChanged;
        
        public InworldGameData GameData => m_GameData;
        public bool Paused => m_Paused;
        
        private const string playgroundSceneName = "Playground";
        private const string setupSceneName = "Setup";
        
        [SerializeField]
        private PlaygroundSettings m_Settings;
        
        [SerializeField] 
        private GameObject m_InworldControllerGameObject;
        
        [SerializeField] 
        private GameObject m_GameMenu;
        
        [Header("Prefabs")] 
        [SerializeField]
        private GameObject m_InworldControllerWebSocket;
        [SerializeField]
        private GameObject m_InworldControllerNDK;
        
        private InworldGameData m_GameData;
        private Coroutine m_SceneChangeCoroutine;
        private Scene m_CurrentScene;
        private bool m_Paused;
        
        /// <summary>
        ///     Loads the current Game Data object into the Inworld Controller for the Playground Workspace.
        /// </summary>
        public void LoadData()
        {
            InworldController.Instance.LoadData(m_GameData);
        }
        
        /// <summary>
        ///     Creates a new Game Data object using the provided key and secret.
        /// </summary>
        /// <param name="key">The API key for the Playground Workspace.</param>
        /// <param name="secret">The API secret for the Playground Workspace.</param>
        public void CreateGameData(string key, string secret)
        {
            m_GameData = Serialization.CreateGameData(key, secret, m_Settings.WorkspaceId);
        }
        
        /// <summary>
        ///     Changes the current scene in Unity.
        /// </summary>
        /// <param name="sceneName">The name of the scene to load.</param>
        public void ChangeScene(string sceneName)
        {
            if(m_SceneChangeCoroutine == null)
                m_SceneChangeCoroutine = StartCoroutine(IChangeScene(sceneName));
        }
        
        /// <summary>
        ///     Initialize and start the Playground demo.
        /// </summary>
        public void Play()
        {
            if (m_CurrentScene.name == setupSceneName)
                SceneManager.LoadScene(playgroundSceneName);
            else
                StartCoroutine(IPlay());
        }
        
        /// <summary>
        ///     Enables all WorldSpaceGraphicRaycasters in the scene.
        /// </summary>
        public void EnableAllWorldSpaceGraphicRaycasters()
        {
#if UNITY_2022_3_OR_NEWER
            var worldSpaceGraphicRaycasters = FindObjectsByType<WorldSpaceGraphicRaycaster>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            var worldSpaceGraphicRaycasters = FindObjectsOfType<WorldSpaceGraphicRaycaster>();
#endif
            foreach (var worldSpaceGraphicRaycaster in worldSpaceGraphicRaycasters)
            {
                worldSpaceGraphicRaycaster.enabled = true;
            }
        }
        
        /// <summary>
        ///     Disables all WorldSpaceGraphicRaycasters in the scene.
        /// </summary>
        public void DisableAllWorldSpaceGraphicRaycasters()
        {
#if UNITY_2022_3_OR_NEWER
            var worldSpaceGraphicRaycasters = FindObjectsByType<WorldSpaceGraphicRaycaster>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            var worldSpaceGraphicRaycasters = FindObjectsOfType<WorldSpaceGraphicRaycaster>();
#endif
            foreach (var worldSpaceGraphicRaycaster in worldSpaceGraphicRaycasters)
            {
                worldSpaceGraphicRaycaster.enabled = false;
            }
        }
        
        #region Settings Getters/Setters

        public string GetPlayerName()
        {
            return m_Settings.PlayerName;
        }
        
        public string GetWorkspaceId()
        {
            return m_Settings.WorkspaceId;
        }
        
        public string GetMicrophoneDevice()
        {
            return m_Settings.MicrophoneDevice;
        }
        
        public MicrophoneMode GetInteractionMode()
        {
            return m_Settings.InteractionMode;
        }
        
        public NetworkClient GetClientType()
        {
            return m_Settings.ClientType;
        }
        
        public bool GetEnableAEC()
        {
            return m_Settings.EnableAEC;
        }

        public void SetPlayerName(string playerName)
        {
            m_Settings.PlayerName = playerName;
            InworldAI.User.Name = m_Settings.PlayerName;
        }

        public void SetWorkspaceId(string workspaceId)
        {
            m_Settings.WorkspaceId = workspaceId;
        }

        public void SetMicrophoneDevice(string deviceName)
        {
            m_Settings.MicrophoneDevice = deviceName;
        }
        
        public void UpdateMicrophoneMode(MicrophoneMode microphoneMode)
        {
            m_Settings.InteractionMode = microphoneMode;
        }
        
        public void UpdateNetworkClient(NetworkClient clientType)
        {
            if (m_Settings.ClientType == clientType) return;
            
            m_Settings.ClientType = clientType;
            StartCoroutine(IUpdateNetworkClient(clientType));
        }

        public void UpdateEnableAEC(bool enableAEC)
        {
            m_Settings.EnableAEC = enableAEC;
        }
        #endregion
        
        #region Unity Event Functions
        private void Awake()
        {
            if (Instance != this)
            {
                Destroy(gameObject);
                InworldAI.LogWarning("Destroying duplicate instance of PlaygroundManager.");
                return;
            }
            
            m_InworldControllerGameObject.SetActive(true);
            
            m_GameData = Serialization.GetGameData();
            if (m_GameData == null && SceneManager.GetActiveScene().name != setupSceneName)
            {
                InworldAI.Log("The Playground GameData could not be found, switching to Setup Scene.");
                SceneManager.LoadScene(setupSceneName);
                return;
            }
                
            if (m_GameData != null)
                m_Settings.WorkspaceId = m_GameData.sceneFullName.Split('/')[1];

            if (string.IsNullOrEmpty(m_Settings.PlayerName))
                SetPlayerName("Player");
            
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            InworldController.Client.OnStatusChanged += OnStatusChanged;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if(InworldController.Client)
                InworldController.Client.OnStatusChanged -= OnStatusChanged;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if(!m_GameMenu.activeSelf)
                    Pause();
            }
        }
        #endregion
        
        #region Event Callback Functions
        private void OnStatusChanged(InworldConnectionStatus status)
        {
            switch (status)
            {
                case InworldConnectionStatus.InitFailed:
                case InworldConnectionStatus.LostConnect:
                case InworldConnectionStatus.Error:
                    // TODO: handle error cases
                    InworldController.Instance.Reconnect();
                    break;
                case InworldConnectionStatus.Initialized:
                    InworldController.Instance.LoadScene();
                    break;
            }
        }
        
        private void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
        {
            m_CurrentScene = scene;
            if (m_CurrentScene.name != setupSceneName && m_GameData != null)
                StartCoroutine(ISetupScene());
        }
        #endregion
        
        #region Enumerators
        private IEnumerator IChangeScene(string sceneName)
        {
            yield return SceneManager.LoadSceneAsync(sceneName);
            m_SceneChangeCoroutine = null;
        }
        
        private IEnumerator ISetupScene()
        {
            SetCharacterBrains();
            if (InworldController.Status != InworldConnectionStatus.Connected)
            {
                if (!CheckNetworkComponent())
                    yield return StartCoroutine(IUpdateNetworkClient(m_Settings.ClientType));
                
                LoadData();
                InworldController.Instance.Init();
        
                yield return new WaitUntil(() => InworldController.Status == InworldConnectionStatus.Connected);
            }
            else
                RegisterCharacters();
           
            yield return StartCoroutine(IPlay());
        }
        
        private IEnumerator IPlay()
        {
            m_GameMenu.SetActive(false);
            
            if (!CheckNetworkComponent())
                throw new MissingComponentException("Missing or incorrect Inworld client");
            
            if (!CheckAudioComponent())
                yield return StartCoroutine(IUpdateAudioComponent(m_Settings.EnableAEC));
            
            CursorHandler.LockCursor();

            Time.timeScale = 1;
            
            InworldController.Audio.ChangeInputDevice(m_Settings.MicrophoneDevice);
            InworldController.Audio.enabled = true;
            
            switch (m_Settings.InteractionMode)
            {
                case MicrophoneMode.Interactive:
                    PlayerController.Instance.SetPushToTalk(false);
                    InworldController.Audio.SampleMode = MicSampleMode.AEC;
                    
                    if(InworldController.Status == InworldConnectionStatus.Connected && 
                       InworldController.CurrentCharacter != null)
                        InworldController.Instance.StartAudio();
                    break;
                case MicrophoneMode.PushToTalk:
                    PlayerController.Instance.SetPushToTalk(true);
                    break;
                case MicrophoneMode.TurnByTurn:
                    PlayerController.Instance.SetPushToTalk(false);
                    InworldController.Audio.SampleMode = MicSampleMode.TURN_BASED;
                    
                    if(InworldController.Status == InworldConnectionStatus.Connected && 
                       InworldController.CurrentCharacter != null)
                        InworldController.Instance.StartAudio();
                    break;
            }
            
            ResumeAllCharacterInteractions();
            
            EnableAllWorldSpaceGraphicRaycasters();
            
            m_Paused = false;
        }
        
        private IEnumerator IUpdateNetworkClient(NetworkClient clientType)
        {
            InworldController.Instance.Disconnect();
#if UNITY_2022_3_OR_NEWER
            var characters = FindObjectsByType<InworldCharacter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            var characters = FindObjectsOfType<InworldCharacter>();
#endif
            if (PlayerController.Instance)
            {
                PlayerController.Instance.GetComponent<ChatPanel>().enabled = false;
                PlayerController.Instance.enabled = false;
            }
            foreach (var character in characters)
                character.gameObject.SetActive(false);
            if(Subtitle.Instance)
                Subtitle.Instance.gameObject.SetActive(false);
            
            yield return new WaitForEndOfFrame();

            yield return new WaitUntil(() => InworldController.Status != InworldConnectionStatus.Connected);
            
            Destroy(InworldController.Instance.gameObject);
            yield return new WaitForEndOfFrame();

            switch (clientType)
            {
                case NetworkClient.WebSocket:
                    Instantiate(m_InworldControllerWebSocket, transform);
                    break;
                case NetworkClient.NDK:
                    Instantiate(m_InworldControllerNDK, transform);
                    break;
            }
            InworldAI.Log("Replacing current Inworld Controller.");
            yield return new WaitForEndOfFrame();
            
            if (PlayerController.Instance)
            {
                PlayerController.Instance.GetComponent<ChatPanel>().enabled = true;
                PlayerController.Instance.enabled = true;
            }
            if(Subtitle.Instance)
                Subtitle.Instance.gameObject.SetActive(true);
            foreach (var character in characters)
                character.gameObject.SetActive(true);
            
            OnClientChanged?.Invoke();
            InworldController.Client.OnStatusChanged += OnStatusChanged;
            OnStatusChanged(InworldController.Status);
        }
        
        private IEnumerator IUpdateAudioComponent(bool enableAEC)
        {
            if(enableAEC)
                InworldController.Instance.AddComponent<InworldAECAudioCapture>();
            else
                InworldController.Instance.AddComponent<AudioCapture>();
                
            Destroy(InworldController.Audio);
            yield return new WaitForEndOfFrame();
        }
        #endregion
        
        private void Pause()
        {
            if(m_Paused)
                return;
            
            DisableAllWorldSpaceGraphicRaycasters();
            
            CursorHandler.UnlockCursor();

            Time.timeScale = 0;
            
            PauseAllCharacterInteractions();
            
            InworldController.Instance.StopAudio();
            
            Subtitle.Instance.Clear();
            
            InworldController.Audio.enabled = false;
            m_GameMenu.SetActive(true);

            m_Paused = true;
        }
        
        private bool CheckAudioComponent()
        {
            var audioCapture = InworldController.Instance.GetComponent<AudioCapture>();
            if (!audioCapture)
                return false;
            
            return m_Settings.EnableAEC
                ? audioCapture is InworldAECAudioCapture
                : audioCapture is not InworldAECAudioCapture;
        }

        private bool CheckNetworkComponent()
        {
            switch (m_Settings.ClientType)
            {
                case NetworkClient.WebSocket:
                    return InworldController.Instance.GetComponent<InworldWebSocketClient>();
                case NetworkClient.NDK:
                    return InworldController.Instance.GetComponent<InworldNDKClient>();
            }
            return false;
        }
        
        private void PauseAllCharacterInteractions()
        {
#if UNITY_2022_3_OR_NEWER
            var interactions = FindObjectsByType<InworldInteraction>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            var interactions = FindObjectsOfType<InworldInteraction>();
#endif
            foreach (var interaction in interactions)
            {
                interaction.CancelResponse();
                interaction.enabled = false;
                if (interaction is InworldAudioInteraction)
                {
                    interaction.GetComponent<AudioSource>().Stop();
                }
            }
        }

        private void ResumeAllCharacterInteractions()
        {
#if UNITY_2022_3_OR_NEWER
            var interactions = FindObjectsByType<InworldInteraction>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            var interactions = FindObjectsOfType<InworldInteraction>();
#endif
            foreach (var interaction in interactions)
            {
                interaction.enabled = true;
            }
        }

        private void SetCharacterBrains()
        {
#if UNITY_2022_3_OR_NEWER
            var characters = FindObjectsByType<InworldCharacter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var characters = FindObjectsOfType<InworldCharacter>(true);
#endif
            foreach (var character in characters)
            {
                string newBrainName = m_GameData.sceneFullName.Substring(0, 
                    m_GameData.sceneFullName.IndexOf('/', 
                        m_GameData.sceneFullName.IndexOf('/', 0) + 1) + 1);
                string oldBrainName = character.Data.brainName;
                newBrainName += oldBrainName.Substring(oldBrainName.IndexOf('/', 
                    oldBrainName.IndexOf('/', 0) + 1) + 1);
                character.Data.brainName = newBrainName;
            }
        }
        
        private void RegisterCharacters()
        {
#if UNITY_2022_3_OR_NEWER
            var characters = FindObjectsByType<InworldCharacter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var characters = FindObjectsOfType<InworldCharacter>(true);
#endif
            foreach (var character in characters)
            {
                character.RegisterLiveSession();
            }
        }
    }
}
