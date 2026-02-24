using UnityEngine;
using System;

namespace VRWorkspace.UI.RTT
{
    /// <summary>
    /// Manages RTT menu state and navigation.
    /// Extracted from RTTManager to separate concerns.
    /// </summary>
    public class RTTMenuNavigator
    {
        #region Types

        /// <summary>
        /// Menu states
        /// </summary>
        public enum MenuState
        {
            MainMenu,
            RemoteMenu
        }

        #endregion

        #region Fields

        private MenuState _currentState = MenuState.MainMenu;
        private GameObject _mainMenuContent;
        private GameObject _currentMenuContent;
        private bool _mainMenuInitialized;
        private readonly bool _enableLogging;

        #endregion

        #region Events

        /// <summary>
        /// Fired when menu state changes
        /// </summary>
        public event Action<MenuState> OnStateChanged;

        /// <summary>
        /// Fired when main menu needs to be created
        /// </summary>
        public event Action OnMainMenuCreateRequested;

        /// <summary>
        /// Fired when remote menu needs to be created
        /// </summary>
        public event Action OnRemoteMenuCreateRequested;

        /// <summary>
        /// Fired when current menu content should be destroyed
        /// </summary>
        public event Action OnMenuContentDestroyRequested;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new menu navigator.
        /// </summary>
        /// <param name="enableLogging">Enable debug logging</param>
        public RTTMenuNavigator(bool enableLogging = false)
        {
            _enableLogging = enableLogging;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Current menu state
        /// </summary>
        public MenuState CurrentState => _currentState;

        /// <summary>
        /// Whether main menu is active
        /// </summary>
        public bool IsMainMenuActive => _currentState == MenuState.MainMenu;

        /// <summary>
        /// Whether remote menu is active
        /// </summary>
        public bool IsRemoteMenuActive => _currentState == MenuState.RemoteMenu;

        /// <summary>
        /// Whether main menu has been initialized
        /// </summary>
        public bool IsMainMenuInitialized => _mainMenuInitialized;

        /// <summary>
        /// Main menu content GameObject
        /// </summary>
        public GameObject MainMenuContent
        {
            get => _mainMenuContent;
            set => _mainMenuContent = value;
        }

        /// <summary>
        /// Current non-main-menu content
        /// </summary>
        public GameObject CurrentMenuContent
        {
            get => _currentMenuContent;
            set => _currentMenuContent = value;
        }

        #endregion

        #region Navigation

        /// <summary>
        /// Show main menu (persistent, never destroyed).
        /// </summary>
        public void ShowMainMenu()
        {
            if (!_mainMenuInitialized)
            {
                OnMainMenuCreateRequested?.Invoke();
                _mainMenuInitialized = true;
            }

            // Destroy any non-main-menu content
            DestroyCurrentMenuContent();

            // Show persistent main menu
            if (_mainMenuContent != null)
            {
                _mainMenuContent.SetActive(true);
            }

            SetState(MenuState.MainMenu);

            if (_enableLogging)
                Debug.Log("[RTTMenuNavigator] Showing Main Menu (persistent)");
        }

        /// <summary>
        /// Switch to remote menu.
        /// </summary>
        public void SwitchToRemoteMenu()
        {
            if (_currentState == MenuState.RemoteMenu) return;

            // Hide main menu (don't destroy)
            HideMainMenuContent();

            // Destroy any other content
            DestroyCurrentMenuContent();

            // Request remote menu creation
            OnRemoteMenuCreateRequested?.Invoke();

            SetState(MenuState.RemoteMenu);

            if (_enableLogging)
                Debug.Log("[RTTMenuNavigator] Switched to Remote Menu");
        }

        /// <summary>
        /// Return to main menu from any other menu.
        /// </summary>
        public void ReturnToMainMenu()
        {
            if (_currentState == MenuState.MainMenu) return;

            ShowMainMenu();

            if (_enableLogging)
                Debug.Log("[RTTMenuNavigator] Returned to Main Menu");
        }

        #endregion

        #region Content Management

        /// <summary>
        /// Hide main menu content without destroying it.
        /// </summary>
        public void HideMainMenuContent()
        {
            if (_mainMenuContent != null)
            {
                _mainMenuContent.SetActive(false);
            }
        }

        /// <summary>
        /// Show main menu content.
        /// </summary>
        public void ShowMainMenuContent()
        {
            if (_mainMenuContent != null)
            {
                _mainMenuContent.SetActive(true);
            }
        }

        /// <summary>
        /// Destroy current non-main-menu content.
        /// </summary>
        public void DestroyCurrentMenuContent()
        {
            // Never destroy the persistent main menu
            if (_currentMenuContent != null && _currentMenuContent != _mainMenuContent)
            {
                OnMenuContentDestroyRequested?.Invoke();

                GameObject.Destroy(_currentMenuContent);
                _currentMenuContent = null;
            }
        }

        /// <summary>
        /// Mark main menu as initialized.
        /// </summary>
        public void MarkMainMenuInitialized()
        {
            _mainMenuInitialized = true;
        }

        #endregion

        #region State Management

        /// <summary>
        /// Set menu state and fire event.
        /// </summary>
        private void SetState(MenuState newState)
        {
            if (_currentState != newState)
            {
                _currentState = newState;
                OnStateChanged?.Invoke(newState);
            }
        }

        /// <summary>
        /// Force set state without firing events.
        /// Used for initialization.
        /// </summary>
        public void ForceState(MenuState state)
        {
            _currentState = state;
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Reset navigator state.
        /// </summary>
        public void Reset()
        {
            DestroyCurrentMenuContent();

            if (_mainMenuContent != null)
            {
                GameObject.Destroy(_mainMenuContent);
                _mainMenuContent = null;
            }

            _mainMenuInitialized = false;
            _currentState = MenuState.MainMenu;
        }

        #endregion
    }
}
