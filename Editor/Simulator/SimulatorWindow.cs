using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace susaplay.SDK.Editor.Simulator
{
    /// <summary>
    /// SusaPlay → Simulator: the developer's controls. The simulated player, the configuration in
    /// use, how purchases and ads are answered, what is waiting for an answer, the saved state,
    /// failure injection, and the request log.
    /// </summary>
    internal sealed class SimulatorWindow : EditorWindow
    {
        private const string PortalUrl = "https://dev.susaplay.com";
        private const string AnyRoute = "Any route";

        private static string _sdkVersion;

        private Label _project;
        private Label _player;
        private HelpBox _banner;
        private Label _configuration;
        private Button _removeConfig;
        private Toggle _guest;
        private Button _signIn;
        private Label _resetNote;
        private EnumField _purchaseOutcome;
        private EnumField _adOutcome;
        private PopupField<string> _failureRoute;
        private EnumField _failureKind;
        private Label _failureArmed;
        private Button _failureCancel;
        private VisualElement _pending;
        private readonly List<PendingRequest> _shownPending = new List<PendingRequest>();
        private readonly Dictionary<PendingRequest, Label> _countdowns = new Dictionary<PendingRequest, Label>();
        private VisualElement _state;
        private DoubleField _coins;
        private DoubleField _gems;
        private ListView _log;
        private Toggle _errorsOnly;
        private readonly List<RequestLogEntry> _rows = new List<RequestLogEntry>();

        [MenuItem("SusaPlay/Simulator")]
        public static void Open()
        {
            GetWindow<SimulatorWindow>("SusaPlay Simulator");
        }

        public void CreateGUI()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.contentContainer.style.paddingLeft = 6;
            scroll.contentContainer.style.paddingRight = 6;
            scroll.contentContainer.style.paddingTop = 6;
            rootVisualElement.Add(scroll);
            var root = scroll.contentContainer;

            BuildStatus(root);
            BuildConfiguration(root);
            BuildPlayer(root);
            BuildSimulation(root);
            BuildPending(root);
            BuildState(root);
            BuildLog(root);

            // The pending countdowns tick without anything else changing.
            rootVisualElement.schedule.Execute(RefreshPending).Every(500);
            Refresh();
        }

        private void OnEnable()
        {
            SusaPlaySimulator.Changed += Refresh;
            SusaPlaySimulator.Log.Changed += RefreshLog;
        }

        private void OnDisable()
        {
            SusaPlaySimulator.Changed -= Refresh;
            SusaPlaySimulator.Log.Changed -= RefreshLog;
        }

        // ── Sections ────────────────────────────────────────────────────────────────────────

        private void BuildStatus(VisualElement root)
        {
            root.Add(Heading("Status"));
            _project = new Label();
            _player = new Label();
            _banner = new HelpBox("", HelpBoxMessageType.Warning);
            root.Add(_project);
            root.Add(_player);
            root.Add(_banner);
        }

        private void BuildConfiguration(VisualElement root)
        {
            root.Add(Heading("Configuration"));
            _configuration = new Label { style = { whiteSpace = WhiteSpace.Normal } };
            root.Add(_configuration);
            var row = Row();
            row.Add(new Button(ImportConfig) { text = "Import configuration…" });
            _removeConfig = new Button(RemoveConfig) { text = "Remove" };
            row.Add(_removeConfig);
            row.Add(new Button(OpenPortal) { text = "Open in Developer Portal" });
            root.Add(row);
        }

        private void BuildPlayer(VisualElement root)
        {
            root.Add(Heading("Player"));
            _guest = new Toggle("Guest (from the next Play)");
            _guest.RegisterValueChangedCallback(change =>
            {
                SusaPlaySimulator.Settings.guest = change.newValue;
                SusaPlaySimulator.SaveSettings();
            });
            root.Add(_guest);
            var row = Row();
            _signIn = new Button(() => SusaPlaySimulator.Shell.SignIn()) { text = "Sign in", tooltip = "Signs the guest in during Play, as the shell's sign-in prompt does." };
            row.Add(_signIn);
            row.Add(new Button(ConfirmReset) { text = "Reset player" });
            root.Add(row);
            _resetNote = new Label("Reset applies when Play Mode ends.") { style = { unityFontStyleAndWeight = FontStyle.Italic } };
            root.Add(_resetNote);
        }

        private void BuildSimulation(VisualElement root)
        {
            root.Add(Heading("Simulation"));
            _purchaseOutcome = new EnumField("Purchases", SusaPlaySimulator.Settings.purchaseOutcome);
            _purchaseOutcome.RegisterValueChangedCallback(change =>
            {
                SusaPlaySimulator.Settings.purchaseOutcome = (PurchaseOutcome)change.newValue;
                SusaPlaySimulator.SaveSettings();
            });
            _adOutcome = new EnumField("Ads", SusaPlaySimulator.Settings.adOutcome);
            _adOutcome.RegisterValueChangedCallback(change =>
            {
                SusaPlaySimulator.Settings.adOutcome = (AdOutcome)change.newValue;
                SusaPlaySimulator.SaveSettings();
            });
            root.Add(_purchaseOutcome);
            root.Add(_adOutcome);

            var routes = new List<string> { AnyRoute };
            routes.AddRange(LocalBackend.Routes);
            _failureRoute = new PopupField<string>("Next request fails", routes, 0);
            _failureKind = new EnumField("With", InjectedFailure.Internal);
            root.Add(_failureRoute);
            root.Add(_failureKind);
            var row = Row();
            row.Add(new Button(ArmFailure) { text = "Arm" });
            _failureCancel = new Button(() =>
            {
                SusaPlaySimulator.Shell.NextFailure = null;
                Refresh();
            }) { text = "Cancel" };
            row.Add(_failureCancel);
            _failureArmed = new Label { style = { marginLeft = 6, unityTextAlign = TextAnchor.MiddleLeft } };
            row.Add(_failureArmed);
            root.Add(row);
        }

        private void BuildPending(VisualElement root)
        {
            root.Add(Heading("Pending"));
            _pending = new VisualElement();
            root.Add(_pending);
        }

        private void BuildState(VisualElement root)
        {
            root.Add(Heading("State"));
            var wallet = Row();
            _coins = new DoubleField("Coins") { isDelayed = true };
            _gems = new DoubleField("Gems") { isDelayed = true };
            _coins.RegisterValueChangedCallback(change => SusaPlaySimulator.SetPlatformWallet(change.newValue, _gems.value));
            _gems.RegisterValueChangedCallback(change => SusaPlaySimulator.SetPlatformWallet(_coins.value, change.newValue));
            wallet.Add(_coins);
            wallet.Add(_gems);
            root.Add(new Label("Platform wallet") { style = { marginTop = 2 } });
            root.Add(wallet);
            _state = new VisualElement();
            root.Add(_state);
        }

        private void BuildLog(VisualElement root)
        {
            root.Add(Heading("Request log"));
            var toolbar = Row();
            _errorsOnly = new Toggle("Errors only");
            _errorsOnly.RegisterValueChangedCallback(_ => RefreshLog());
            toolbar.Add(_errorsOnly);
            toolbar.Add(new Button(() => SusaPlaySimulator.Log.Clear()) { text = "Clear" });
            toolbar.Add(new Button(CopyLog) { text = "Copy" });
            root.Add(toolbar);

            _log = new ListView(_rows, 18, () => new Label(), (element, index) =>
            {
                var entry = _rows[index];
                var label = (Label)element;
                label.text = entry.Describe();
                label.tooltip = entry.Detail;
                label.style.color = entry.IsError ? new StyleColor(new Color(0.95f, 0.55f, 0.3f)) : new StyleColor(StyleKeyword.Null);
            })
            {
                selectionType = SelectionType.Single,
                style = { height = 320 },
            };
            root.Add(_log);
        }

        // ── Refresh ─────────────────────────────────────────────────────────────────────────

        private void Refresh()
        {
            if (_project == null)
            {
                return;
            }

            var sdkConfig = SDKConfig.Load();
            var projectKey = sdkConfig != null ? sdkConfig.GameKey : null;
            _sdkVersion ??= UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SusaPlaySDK).Assembly)?.version ?? "?";
            _project.text = "Project gameKey: " + (string.IsNullOrEmpty(projectKey) ? "—" : projectKey) + "    SDK " + _sdkVersion;

            var shell = SusaPlaySimulator.Shell;
            var state = SusaPlaySimulator.State;
            var settings = SusaPlaySimulator.Settings;
            var mode = shell.InSession ? (shell.IsGuest ? "guest" : "signed in") : (settings.guest ? "guest at next Play" : "signed in at next Play");
            _player.text = "Player: " + state.displayName + " (" + state.uid + "), " + mode;

            var config = SusaPlaySimulator.Config;
            _configuration.text = config.Loaded
                ? config.GameName + " (" + config.GameId + "), downloaded " + Age(config.ExportedAt) + ". " +
                  config.Achievements.Count + " achievements, " + config.Items.Count + " items, " + config.TopupPacks().Count + " top-up packs."
                : "None imported.";
            _removeConfig.SetEnabled(config.Loaded || config.LoadError != null);

            var banner = Banner(sdkConfig, projectKey, config);
            _banner.text = banner ?? "";
            _banner.style.display = banner == null ? DisplayStyle.None : DisplayStyle.Flex;

            _guest.SetValueWithoutNotify(settings.guest);
            _signIn.SetEnabled(shell.InSession && shell.IsGuest);
            _resetNote.style.display = SusaPlaySimulator.ResetPending ? DisplayStyle.Flex : DisplayStyle.None;
            _purchaseOutcome.SetValueWithoutNotify(settings.purchaseOutcome);
            _adOutcome.SetValueWithoutNotify(settings.adOutcome);

            var failure = shell.NextFailure;
            _failureArmed.text = failure != null ? failure.Describe() : "";
            _failureCancel.SetEnabled(failure != null);

            _coins.SetValueWithoutNotify(state.platformWallet.coins);
            _gems.SetValueWithoutNotify(state.platformWallet.gems);
            RefreshState(state, shell.SessionGameId ?? (config.Loaded ? config.GameId : projectKey));
            RefreshPending();
            RefreshLog();
        }

        private static string Banner(SDKConfig sdkConfig, string projectKey, SimulatorConfig config)
        {
            if (sdkConfig == null)
            {
                return "PlatformConfig.asset is missing, so SusaPlaySDK.Initialize() fails before reaching the simulator. Run SusaPlay → Setup.";
            }
            if (config.LoadError != null)
            {
                return "The configuration file could not be read (" + config.LoadError + "). The simulator behaves as for a game with nothing configured.";
            }
            if (!config.Loaded)
            {
                return "No game configuration imported. The store is empty, every achievement is NOT_FOUND and every purchase is ITEM_NOT_FOUND, " +
                       "as on the platform for a game with nothing configured. Download it from the game's page in the Developer Portal.";
            }
            if (!string.IsNullOrEmpty(config.GameKey) && config.GameKey != projectKey)
            {
                return "The configuration is for " + config.GameName + " (" + config.GameKey + "), not this project's gameKey. One of them is wrong.";
            }
            return null;
        }

        private void RefreshPending()
        {
            if (_pending == null)
            {
                return;
            }
            var shell = SusaPlaySimulator.Shell;
            // Rebuilt only when the list changes; otherwise only the countdowns move, so a
            // button is never replaced under the developer's cursor.
            if (_shownPending.SequenceEqual(shell.Pending) && _pending.childCount > 0)
            {
                foreach (var pair in _countdowns)
                {
                    pair.Value.text = Countdown(pair.Key, shell.Now);
                }
                return;
            }
            _shownPending.Clear();
            _shownPending.AddRange(shell.Pending);
            _countdowns.Clear();
            _pending.Clear();
            if (shell.Pending.Count == 0)
            {
                _pending.Add(new Label(shell.InSession ? "Nothing waiting." : "Nothing waiting. Purchases and ads appear here during Play when set to Ask, or when a guest buys."));
                return;
            }
            foreach (var request in shell.Pending.ToList())
            {
                var row = Row();
                var label = new Label(Countdown(request, shell.Now)) { style = { minWidth = 260, unityTextAlign = TextAnchor.MiddleLeft } };
                _countdowns[request] = label;
                row.Add(label);
                if (request.Kind == "ad")
                {
                    row.Add(new Button(() => shell.AnswerAd(request, AdOutcome.Complete)) { text = "Complete" });
                    row.Add(new Button(() => shell.AnswerAd(request, AdOutcome.Skip)) { text = "Skip" });
                    row.Add(new Button(() => shell.AnswerAd(request, AdOutcome.NoAd)) { text = "No ad" });
                }
                else if (request.AwaitingSignIn)
                {
                    row.Add(new Button(() => shell.SignInAndContinue(request)) { text = "Sign in and continue" });
                    row.Add(new Button(() => shell.DismissSignIn(request)) { text = "Dismiss" });
                }
                else
                {
                    row.Add(new Button(() => shell.AnswerPurchase(request, PurchaseOutcome.Paid)) { text = "Paid" });
                    row.Add(new Button(() => shell.AnswerPurchase(request, PurchaseOutcome.Canceled)) { text = "Canceled" });
                    row.Add(new Button(() => shell.AnswerPurchase(request, PurchaseOutcome.Closed)) { text = "Closed" });
                }
                _pending.Add(row);
            }
        }

        private static string Countdown(PendingRequest request, double now)
        {
            var left = Math.Max(0, PendingRequest.SdkTimeoutSeconds - (now - request.StartedAt));
            return request.Describe() + " — the SDK gives up in " + left.ToString("0", CultureInfo.InvariantCulture) + " s";
        }

        private void RefreshState(SimulatorState state, string gameId)
        {
            _state.Clear();

            var saves = state.saves.Where(s => s.gameId == gameId).OrderBy(s => s.slot, StringComparer.Ordinal).ToList();
            var savesFold = new Foldout { text = "Saves (" + saves.Count + ")", value = false };
            foreach (var save in saves)
            {
                savesFold.Add(new Label(save.slot + " — version " + save.version + ", " + (save.data ?? "").Length + " characters, saved " + Age(save.savedAt))
                {
                    tooltip = save.data,
                });
            }
            _state.Add(savesFold);

            var achievements = state.achievements.Where(a => a.gameId == gameId).OrderBy(a => a.achievementId, StringComparer.Ordinal).ToList();
            var achievementsFold = new Foldout { text = "Achievements (" + achievements.Count(a => a.unlocked) + " unlocked, " + state.xp + " XP)", value = false };
            foreach (var achievement in achievements)
            {
                var text = achievement.achievementId + " — " + (achievement.unlocked ? "unlocked " + Age(achievement.unlockedAt) : "locked");
                if (achievement.hasProgress)
                {
                    text += ", progress " + achievement.progress;
                }
                achievementsFold.Add(new Label(text));
            }
            _state.Add(achievementsFold);

            var inventory = state.FindInventory(gameId, false);
            var entries = inventory == null
                ? new List<string>()
                : inventory.items.Select(e => e.key + " — owned").Concat(inventory.consumables.Select(e => e.key + " × " + e.value)).ToList();
            var inventoryFold = new Foldout { text = "Inventory (" + entries.Count + ")", value = false };
            foreach (var entry in entries)
            {
                inventoryFold.Add(new Label(entry));
            }
            _state.Add(inventoryFold);
        }

        private void RefreshLog()
        {
            if (_log == null)
            {
                return;
            }
            _rows.Clear();
            var errorsOnly = _errorsOnly != null && _errorsOnly.value;
            _rows.AddRange(SusaPlaySimulator.Log.Entries.Where(e => !errorsOnly || e.IsError));
            _log.RefreshItems();
            if (_rows.Count > 0)
            {
                _log.ScrollToItem(_rows.Count - 1);
            }
        }

        // ── Actions ─────────────────────────────────────────────────────────────────────────

        private void ImportConfig()
        {
            var path = EditorUtility.OpenFilePanel("Import SusaPlay configuration", "", "json");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            var refusal = SusaPlaySimulator.ImportConfig(path);
            if (refusal != null)
            {
                EditorUtility.DisplayDialog("Not imported", "This file is not a SusaPlay simulator configuration: " + refusal, "OK");
            }
        }

        private void RemoveConfig()
        {
            if (EditorUtility.DisplayDialog("Remove the configuration?",
                    "The simulator will behave as for a game with nothing configured. The file is removed from ProjectSettings/ for the whole team once committed.",
                    "Remove", "Cancel"))
            {
                SusaPlaySimulator.RemoveConfig();
            }
        }

        private static void OpenPortal()
        {
            var config = SusaPlaySimulator.Config;
            Application.OpenURL(config.Loaded ? PortalUrl + "/game/" + Uri.EscapeDataString(config.GameId) : PortalUrl + "/dashboard");
        }

        private void ArmFailure()
        {
            SusaPlaySimulator.Shell.NextFailure = new FailureInjection
            {
                Route = _failureRoute.value == AnyRoute ? null : _failureRoute.value,
                Failure = (InjectedFailure)_failureKind.value,
            };
            Refresh();
        }

        private void ConfirmReset()
        {
            if (EditorUtility.DisplayDialog("Reset the simulated player?",
                    "Saves, achievements, wallet and inventory are deleted and a new player ID is created.", "Reset", "Cancel"))
            {
                SusaPlaySimulator.Reset();
            }
        }

        private void CopyLog()
        {
            EditorGUIUtility.systemCopyBuffer = SusaPlaySimulator.Log.ToText(_errorsOnly != null && _errorsOnly.value);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────

        private static string Age(string timestamp)
        {
            if (string.IsNullOrEmpty(timestamp) ||
                !DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var moment))
            {
                return "at an unknown time";
            }
            var age = DateTime.UtcNow - moment;
            if (age.TotalMinutes < 1) return "just now";
            if (age.TotalHours < 1) return (int)age.TotalMinutes + " min ago";
            if (age.TotalDays < 1) return (int)age.TotalHours + " h ago";
            return (int)age.TotalDays + " days ago";
        }

        private static VisualElement Row()
        {
            return new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 2 } };
        }

        private static Label Heading(string text)
        {
            return new Label(text) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10, marginBottom = 2 } };
        }
    }
}
