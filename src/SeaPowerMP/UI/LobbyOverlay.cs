using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using Noesis;
using SeaPowerMP.Core.Session;

namespace SeaPowerMP.UI
{
    /// <summary>
    /// Lobby window rendered with Noesis, the game's own UI system, on a dedicated camera kept above
    /// every game camera. IMGUI does not work here: the game's Noesis views draw over it and take its clicks.
    /// </summary>
    internal sealed class LobbyOverlay : UnityEngine.MonoBehaviour
    {
        private const string XamlResource = "SeaPowerMP.UI.LobbyOverlay.xaml";
        private const float RefreshIntervalSec = 0.25f;
        private const float KeepOnTopIntervalSec = 1f;

        private NetworkService? _net;
        private ModConfig _config = null!;

        private UnityEngine.GameObject? _host;
        private UnityEngine.Camera? _camera;
        private NoesisView? _view;
        private NoesisXaml? _xaml;
        private bool _visible;
        private float _nextRefresh;
        private float _nextKeepOnTop;
        private string _rosterSignature = "";
        private string? _localProblem;

        private FrameworkElement _root = null!;
        private TextBlock _disabledText = null!, _steamHint = null!, _statusText = null!, _problemText = null!;
        private Panel _offlineSection = null!, _sessionSection = null!, _rosterPanel = null!;
        private TextBox _nameBox = null!, _addressBox = null!, _portBox = null!;
        private Button _hostSteamButton = null!, _inviteButton = null!, _leaveButton = null!;

        private FrameworkElement _dragBar = null!;
        private TranslateTransform _panelOffset = null!;
        private bool _dragging;
        private Point _dragStart;
        private float _dragStartX;
        private float _dragStartY;

        public void Init(NetworkService? net, ModConfig config)
        {
            _net = net;
            _config = config;
            try
            {
                BuildView();
                SetVisible(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[UI] Could not create the multiplayer window: " + ex);
                DestroyView();
            }
        }

        private void Update()
        {
            if (_view == null)
                return;
            if (ToggleRequested())
                SetVisible(!_visible);
            if (!_visible)
                return;

            float now = UnityEngine.Time.unscaledTime;
            if (now >= _nextKeepOnTop)
            {
                _nextKeepOnTop = now + KeepOnTopIntervalSec;
                KeepOnTop();
            }
            if (now >= _nextRefresh)
            {
                _nextRefresh = now + RefreshIntervalSec;
                Refresh();
            }
        }

        private void OnDestroy() => DestroyView();

        private void BuildView()
        {
            _host = new UnityEngine.GameObject("SeaPowerMP.Overlay");
            DontDestroyOnLoad(_host);

            _camera = _host.AddComponent<UnityEngine.Camera>();
            _camera.clearFlags = UnityEngine.CameraClearFlags.Depth;
            _camera.cullingMask = 0;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.useOcclusionCulling = false;
            _camera.depth = TopCameraDepth() + 10f;

            _xaml = UnityEngine.ScriptableObject.CreateInstance<NoesisXaml>();
            _xaml.uri = "memory://seapowermp/lobby.xaml";
            _xaml.content = ReadXaml();

            _view = _host.AddComponent<NoesisView>();
            _view.Xaml = _xaml;
            _view.EnableMouse = true;
            _view.EnableKeyboard = false;
            _view.EnableTouch = false;
            _view.EnableActions = false;
            _view.LoadXaml(true);
            _root = _view.Content ?? throw new InvalidOperationException("NoesisView has no content after loading the XAML.");

            Find<TextBlock>("TitleText").Text = "SeaPower MP " + PluginInfo.Version;
            Find<TextBlock>("HotkeyText").Text = FormatShortcut(_config.ToggleWindow.Value) + " to show/hide";
            _disabledText = Find<TextBlock>("DisabledText");
            _steamHint = Find<TextBlock>("SteamHint");
            _statusText = Find<TextBlock>("StatusText");
            _problemText = Find<TextBlock>("ProblemText");
            _offlineSection = Find<Panel>("OfflineSection");
            _sessionSection = Find<Panel>("SessionSection");
            _rosterPanel = Find<Panel>("RosterPanel");
            _nameBox = Find<TextBox>("NameBox");
            _addressBox = Find<TextBox>("AddressBox");
            _portBox = Find<TextBox>("PortBox");
            _hostSteamButton = Find<Button>("HostSteamButton");
            _inviteButton = Find<Button>("InviteButton");
            _leaveButton = Find<Button>("LeaveButton");

            _nameBox.Text = _config.PlayerName.Value;
            _addressBox.Text = _config.LastAddress.Value;
            _portBox.Text = _config.Port.Value.ToString();
            _nameBox.TextChanged += (_, _) => _config.PlayerName.Value = _nameBox.Text ?? "";
            foreach (var box in new[] { _nameBox, _addressBox, _portBox })
            {
                // Keys only go to Noesis while a text field is being edited, so the game keeps its hotkeys.
                box.GotFocus += (_, _) => SetKeyboard(true);
                box.LostFocus += (_, _) => SetKeyboard(false);
            }

            OnClick(_hostSteamButton, "Host session", () => _net?.HostSteam());
            OnClick(Find<Button>("HostIpButton"), "Host on this port", () =>
            {
                if (TryReadPort(out int port))
                    _net?.HostDirect(port);
            });
            OnClick(Find<Button>("JoinIpButton"), "Join", () =>
            {
                string address = (_addressBox.Text ?? "").Trim();
                if (address.Length == 0)
                {
                    _localProblem = "Enter the host's address first.";
                    return;
                }
                if (!TryReadPort(out int port))
                    return;
                _config.LastAddress.Value = address;
                _net?.JoinDirect(address, port);
            });
            OnClick(_inviteButton, "Invite Steam friends", () => _net?.Lobby?.OpenInviteDialog());
            OnClick(_leaveButton, "Leave", () => _net?.Leave());

            var panel = Find<Border>("Panel");
            _panelOffset = panel.RenderTransform as TranslateTransform ?? new TranslateTransform();
            panel.RenderTransform = _panelOffset;
            _dragBar = Find<FrameworkElement>("DragBar");
            _dragBar.MouseLeftButtonDown += OnDragStart;
            _dragBar.MouseMove += OnDragMove;
            _dragBar.MouseLeftButtonUp += OnDragEnd;

            Plugin.Log.LogInfo($"[UI] Multiplayer window ready (overlay camera depth {_camera.depth}).");
        }

        private void DestroyView()
        {
            if (_host != null)
                Destroy(_host);
            if (_xaml != null)
                Destroy(_xaml);
            _host = null;
            _camera = null;
            _view = null;
            _xaml = null;
        }

        private void SetVisible(bool visible)
        {
            if (_camera == null || _view == null)
                return;
            _visible = visible;
            _camera.enabled = visible;
            _view.enabled = visible;
            if (!visible)
            {
                SetKeyboard(false);
                _dragging = false;
            }
            else
            {
                _rosterSignature = "";
                KeepOnTop();
                Refresh();
            }
            Plugin.Log.LogInfo($"[UI] Window {(visible ? "opened" : "closed")}.");
        }

        private void Refresh()
        {
            NetRole role = _net?.Role ?? NetRole.Offline;

            _disabledText.Text = Plugin.DisabledReason ?? "Multiplayer is disabled.";
            _disabledText.Visibility = _net == null ? Visibility.Visible : Visibility.Collapsed;
            _offlineSection.Visibility = _net != null && role == NetRole.Offline ? Visibility.Visible : Visibility.Collapsed;
            _sessionSection.Visibility = role != NetRole.Offline ? Visibility.Visible : Visibility.Collapsed;

            if (_net != null && role == NetRole.Offline)
            {
                _hostSteamButton.IsEnabled = _net.SteamAvailable;
                _steamHint.Text = _net.SteamAvailable
                    ? "No port forwarding needed. Friends join through the Steam invite you send next."
                    : "Steam is not available in this game instance.";
            }

            if (_net?.Host is HostSession host)
            {
                _statusText.Text = $"Hosting - {host.Players.Count}/{_config.MaxPlayers.Value} players";
                bool inLobby = _net.Lobby != null && _net.Lobby.InLobby;
                _inviteButton.Visibility = inLobby ? Visibility.Visible : Visibility.Collapsed;
                if (_net.Lobby != null && _net.Lobby.IsCreating)
                    _statusText.Text += " (creating Steam lobby...)";
                _leaveButton.Content = "Close session";
                RebuildRoster(host.Players, isHost: true, Core.Protocol.HostSlot);
            }
            else if (_net?.Client is ClientSession client)
            {
                _statusText.Text = client.State switch
                {
                    ClientState.Connecting => "Connecting to host...",
                    ClientState.Handshaking => "Joining session...",
                    _ => $"Connected - {client.Players.Count} players",
                };
                _inviteButton.Visibility = Visibility.Collapsed;
                _leaveButton.Content = "Leave";
                RebuildRoster(client.Players, isHost: false, client.LocalSlot);
            }

            string? problem = _localProblem ?? _net?.LastProblem;
            _problemText.Text = problem ?? "";
            _problemText.Visibility = string.IsNullOrEmpty(problem) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Rows hold live buttons, so they are only rebuilt when the roster actually changes.</summary>
        private void RebuildRoster(IReadOnlyList<PlayerInfo> players, bool isHost, byte localSlot)
        {
            string signature = (isHost ? "H" : "C") + localSlot + "|" +
                               string.Join(";", players.Select(p => $"{p.Slot}:{p.Name}:{p.Team}"));
            if (signature == _rosterSignature)
                return;
            _rosterSignature = signature;

            _rosterPanel.Children.Clear();
            foreach (PlayerInfo player in players)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                bool isMe = player.Slot == localSlot;
                string label = $"{player.Slot + 1}. {player.Name}" + (player.IsHost ? " (host)" : "") + (isMe ? " (you)" : "");
                row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });

                Brush teamBrush = (Brush)_root.FindResource(player.Team == Team.Blue ? "BlueBrush" : "RedBrush");
                string teamName = player.Team == Team.Blue ? "Blue" : "Red";
                FrameworkElement teamCell;
                if (isHost || isMe)
                {
                    byte slot = player.Slot;
                    Team next = player.Team == Team.Blue ? Team.Red : Team.Blue;
                    var button = MakeButton(teamName, 64);
                    button.Foreground = teamBrush;
                    OnClick(button, $"Team {next} for slot {slot}", () =>
                    {
                        if (isHost)
                            _net?.Host?.SetTeam(slot, next);
                        else
                            _net?.Client?.RequestTeam(next);
                    });
                    teamCell = button;
                }
                else
                {
                    teamCell = new TextBlock
                    {
                        Text = teamName,
                        Foreground = teamBrush,
                        Width = 64,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                }
                Grid.SetColumn(teamCell, 1);
                row.Children.Add(teamCell);

                if (isHost && !player.IsHost)
                {
                    byte slot = player.Slot;
                    var kick = MakeButton("Kick", 52);
                    OnClick(kick, $"Kick slot {slot}", () => _net?.Host?.Kick(slot, "removed by the host"));
                    Grid.SetColumn(kick, 2);
                    row.Children.Add(kick);
                }
                _rosterPanel.Children.Add(row);
            }
        }

        private Button MakeButton(string content, float width) => new Button
        {
            Content = content,
            Width = width,
            Margin = new Thickness(4, 0, 0, 0),
            Style = (Style)_root.FindResource("Btn"),
        };

        private void OnClick(Button button, string what, Action action)
        {
            button.Click += (_, _) =>
            {
                Plugin.Log.LogInfo($"[UI] Clicked \"{what}\".");
                _localProblem = null;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[UI] \"{what}\" failed: {ex}");
                    _localProblem = "That did not work - see BepInEx/LogOutput.log.";
                }
                _rosterSignature = "";
                Refresh();
            };
        }

        private bool TryReadPort(out int port)
        {
            if (int.TryParse((_portBox.Text ?? "").Trim(), out port) && port >= 1024 && port <= 65535)
            {
                _config.Port.Value = port;
                return true;
            }
            _localProblem = "The port must be a number between 1024 and 65535.";
            return false;
        }

        private void SetKeyboard(bool enabled)
        {
            if (_view != null)
                _view.EnableKeyboard = enabled && _visible;
        }

        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            _dragStart = e.GetPosition(_root);
            _dragStartX = _panelOffset.X;
            _dragStartY = _panelOffset.Y;
            _dragBar.CaptureMouse();
            e.Handled = true;
        }

        private void OnDragMove(object sender, MouseEventArgs e)
        {
            if (!_dragging)
                return;
            Point position = e.GetPosition(_root);
            _panelOffset.X = _dragStartX + position.X - _dragStart.X;
            _panelOffset.Y = _dragStartY + position.Y - _dragStart.Y;
        }

        private void OnDragEnd(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            _dragBar.ReleaseMouseCapture();
        }

        /// <summary>Scenes add their own cameras; stay above all of them.</summary>
        private void KeepOnTop()
        {
            if (_camera == null)
                return;
            float top = TopCameraDepth();
            if (_camera.depth <= top)
                _camera.depth = top + 10f;
        }

        private float TopCameraDepth()
        {
            float top = 0f;
            foreach (var camera in FindObjectsByType<UnityEngine.Camera>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
            {
                if (camera != _camera && camera.depth > top)
                    top = camera.depth;
            }
            return top;
        }

        private bool ToggleRequested()
        {
            try
            {
                return _config.ToggleWindow.Value.IsDown();
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private T Find<T>(string name) where T : class =>
            _root.FindName(name) as T ?? throw new InvalidOperationException($"XAML element '{name}' is missing.");

        private static byte[] ReadXaml()
        {
            using Stream stream = typeof(LobbyOverlay).Assembly.GetManifestResourceStream(XamlResource)
                                  ?? throw new InvalidOperationException("Embedded resource " + XamlResource + " is missing.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private static string FormatShortcut(KeyboardShortcut shortcut)
        {
            var parts = shortcut.Modifiers.Select(key => key switch
            {
                UnityEngine.KeyCode.LeftControl or UnityEngine.KeyCode.RightControl => "Ctrl",
                UnityEngine.KeyCode.LeftShift or UnityEngine.KeyCode.RightShift => "Shift",
                UnityEngine.KeyCode.LeftAlt or UnityEngine.KeyCode.RightAlt => "Alt",
                _ => key.ToString(),
            }).ToList();
            parts.Add(shortcut.MainKey.ToString());
            return string.Join("+", parts);
        }
    }
}
