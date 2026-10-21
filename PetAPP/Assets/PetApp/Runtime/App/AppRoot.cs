using System;
using System.IO;
using PetApp.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace PetApp.App
{
    public sealed class AppRoot : MonoBehaviour
    {
        public static readonly string[] Tabs = { "home", "games", "feed" };

        public static AppRoot Instance { get; private set; }

        [SerializeField] UIDocument document;
        [SerializeField] Camera homeCamera;
        [SerializeField] Camera gameCamera;
        [SerializeField] Camera blankCamera;

        public Wallet Wallet { get; private set; }
        public VisualElement Root => document.rootVisualElement;
        public string CurrentTab { get; private set; } = "home";
        public bool InGame { get; private set; }
        public event Action<string> TabChanged;

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            Wallet = new Wallet(new FileWalletStore(Path.Combine(Application.persistentDataPath, "wallet.json")));
        }

        void Start()
        {
            if (Root.panel == null) Debug.LogError("UIDocument has no panel; run Tools > Set scene up again.");
            Root.pickingMode = PickingMode.Ignore;
            foreach (var tab in Tabs)
            {
                var t = tab;
                Root.Q<Button>($"tab-{t}").clicked += () => ShowTab(t);
            }
            Wallet.Changed += UpdateCoins;
            UpdateCoins(Wallet.Coins);
            ShowTab("home");
        }

        void OnDestroy()
        {
            if (Wallet != null) Wallet.Changed -= UpdateCoins;
        }

        void UpdateCoins(int coins)
        {
            Root.Query<Label>(className: "coin-label").ForEach(l => l.text = coins.ToString());
            var pill = Root.Q("coin-pill");
            pill.AddToClassList("coin-pill--bump");
            pill.schedule.Execute(() => pill.RemoveFromClassList("coin-pill--bump")).ExecuteLater(90);
        }

        // Unhide with an "enter" class, then drop it a frame later so the USS transition animates in.
        public static void Animate(VisualElement element, string enterClass)
        {
            element.AddToClassList(enterClass);
            element.RemoveFromClassList("hidden");
            element.schedule.Execute(() => element.RemoveFromClassList(enterClass)).ExecuteLater(30);
        }

        public static void Pop(VisualElement overlay) => Animate(overlay, "overlay--enter");

        public void ShowTab(string tab)
        {
            CurrentTab = tab;
            foreach (var t in Tabs)
            {
                var screen = Root.Q($"screen-{t}");
                if (t == tab && screen.ClassListContains("hidden")) Animate(screen, "screen--enter");
                else if (t != tab) screen.AddToClassList("hidden");
                Root.Q<Button>($"tab-{t}").EnableInClassList("selected", t == tab);
            }
            UseCamera(tab == "home" ? homeCamera : blankCamera);
            TabChanged?.Invoke(tab);
        }

        public void EnterGame()
        {
            InGame = true;
            foreach (var t in Tabs) Root.Q($"screen-{t}").AddToClassList("hidden");
            Root.Q("tab-bar").AddToClassList("hidden");
            Root.Q("screen-game").RemoveFromClassList("hidden");
            UseCamera(gameCamera);
            TabChanged?.Invoke("game");
        }

        public void ExitGame()
        {
            InGame = false;
            Root.Q("screen-game").AddToClassList("hidden");
            Root.Q("tab-bar").RemoveFromClassList("hidden");
            ShowTab(CurrentTab);
        }

        public bool IsPointerOverUI(Vector2 screenPosition)
        {
            var panel = Root.panel;
            if (panel == null) return false;
            var panelPosition = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            return panel.Pick(panelPosition) != null;
        }

        void UseCamera(Camera active)
        {
            homeCamera.enabled = active == homeCamera;
            gameCamera.enabled = active == gameCamera;
            blankCamera.enabled = active == blankCamera;
        }
    }
}
