using PCL.Core.App;
using PCL.Core.Link.Natayark;
using PCL.Core.Link.Scaffolding;
using PCL.Core.Link.Scaffolding.Client.Models;
using PCL.Core.Link.Scaffolding.EasyTier;
using PCL.Core.Logging;
using PCL.Core.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Text;
using System.Text.Json;

namespace PCL.Core.Link.Lobby;

/// <summary>
/// Lobby server. For auto-management
/// </summary>
[LifecycleService(LifecycleState.Loaded)]
public class LobbyService() : GeneralService("lobby", "LobbyService")
{
    private static readonly LobbyController _LobbyController = new();
    private static CancellationTokenSource _lobbyCts = new();

    private static Task? _discoveringTask;
    private static CancellationTokenSource _discoveringCts = new();

    private static readonly Timer _ServerGameWatcher =
        new(_CheckGameState, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));

    private static bool _isGameWatcherRunnable = false;

    /// <summary>
    /// Current lobby state.
    /// </summary>
    public static LobbyState CurrentState { get; private set; } = LobbyState.Idle;


    /// <summary>
    /// Founded local Minecraft worlds.
    /// </summary>
    public static ObservableCollection<FoundWorld> DiscoveredWorlds { get; } = [];

    /// <summary>
    /// Current players in current lobby.
    /// </summary>
    public static ObservableCollection<PlayerProfile> Players { get; private set; } = [];

    /// <summary>
    /// Demonstrate whether the current user is the host of the lobby.
    /// </summary>
    public static bool IsHost => _LobbyController.IsHost;

    /// <summary>
    /// Current lobby full code.
    /// </summary>
    public static string? CurrentLobbyCode { get; private set; }

    public static string? CurrentLobbyId { get; private set; }

    /// <summary>
    /// Current lobby username.
    /// </summary>
    public static string? CurrentUserName { get; private set; }

    #region UI Events

    /// <summary>
    /// Invoked when lobby state changed. (first arg is the old state; second arg is the new state.)
    /// </summary>
    public static event Action<LobbyState, LobbyState>? StateChanged;

    /// <summary>
    /// Used for UI layer to send Hint.
    /// </summary>
    public static event Action<string, CoreHintType>? OnHint;

    /// <summary>
    /// Invoked when need to download EasyTier core files.
    /// </summary>
    public static event Action? OnNeedDownloadEasyTier;

    /// <summary>
    /// Invoked when user stop the game in server mode.
    /// </summary>
    public static event Action? OnUserStopGame;

    /// <summary>
    /// Invoked when client ping happened.
    /// </summary>
    public static event Action<long>? OnClientPing;

    /// <summary>
    /// Invoked when server shut down.
    /// </summary>
    public static event Action? OnServerShutDown;


    /// <summary>
    /// Invoked when server started successfully.
    /// </summary>
    public static event Action? OnServerStarted;

    public static event Action<Exception>? OnServerException;

    #endregion

    /// <inheritdoc />
    public override void Stop()
    {
        _ = LeaveLobbyAsync();
        _ServerGameWatcher.Dispose();
        _lobbyCts.Dispose();

        _discoveringCts.Cancel();
        if (_discoveringTask is not null)
        {
            Task.WhenAll(_discoveringTask);
            _discoveringTask.Dispose();
        }

        _discoveringCts.Dispose();
    }

    private static bool _IsEasyTierCoreFileNotExist() =>
        !File.Exists(Path.Combine(EasyTierMetadata.EasyTierFilePath, "easytier-core.exe")) &&
        !File.Exists(Path.Combine(EasyTierMetadata.EasyTierFilePath, "Packet.dll")) &&
        !File.Exists(Path.Combine(EasyTierMetadata.EasyTierFilePath, "easytier-cli.exe"));


    public static async Task InitializeAsync()
    {
        if (CurrentState is not LobbyState.Idle && CurrentState is not LobbyState.Error)
        {
            return;
        }

        _SetState(LobbyState.Initializing);
        try
        {
            LogWrapper.Info("LobbyService", "Lobby service prerequisites check completed.");

            if (LobbyInfoProvider.RequiresLogin)
            {
                var naidRefreshToken = Config.Link.NaidRefreshToken;
                if (!string.IsNullOrWhiteSpace(naidRefreshToken))
                {
                    var expTime = Config.Link.NaidRefreshExpireTime;
                    if (!string.IsNullOrWhiteSpace(expTime) &&
                        Convert.ToDateTime(expTime).CompareTo(DateTime.Now) < 0)
                    {
                        Config.Link.NaidRefreshToken = string.Empty;
                        OnHint?.Invoke("Natayark ID 令牌已过期，请重新登录", CoreHintType.Critical);
                    }
                    else
                    {
                        await NatayarkProfileManager.GetNaidDataAsync(naidRefreshToken, true).ConfigureAwait(false);
                    }
                }
            }

            _SetState(LobbyState.Initialized);
            LogWrapper.Info("LobbyService", "Lobby service initialized successfully.");

            _ = DiscoverWorldAsync();
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "LobbyService", "Lobby service initialization failed.");
            OnHint?.Invoke("大厅服务初始化失败，请检查网络连接。", CoreHintType.Critical);
            _SetState(LobbyState.Error);
        }
    }

    /// <summary>
    /// Discover minecraft shared world.
    /// </summary>
    public static async Task DiscoverWorldAsync()
    {
        if (_discoveringCts.IsCancellationRequested)
        {
            return;
        }

        if (CurrentState is not LobbyState.Initialized && CurrentState is not LobbyState.Idle)
        {
            return;
        }

        _SetState(LobbyState.Discovering);
        await _RunInUiAsync(() => DiscoveredWorlds.Clear()).ConfigureAwait(false);

        _discoveringTask = Task.Run(async () =>
        {
            var recordedPorts = new ConcurrentSet<int>();
            using var listener = new BroadcastListener();

            var handler = new Action<BroadcastRecord, IPEndPoint>(async (info, _) =>
            {
                if (!recordedPorts.TryAdd(info.Address.Port)) return;

                using var pinger = new McPing(new IPEndPoint(IPAddress.Loopback, info.Address.Port));
                using var cts = new CancellationTokenSource(2000);

                try
                {
                    var pingRes = await pinger.PingAsync(cts.Token).ConfigureAwait(false);

                    if (pingRes is null)
                    {
                        throw new ArgumentNullException(nameof(pingRes), "Failed to ping minecraft entity.");
                    }

                    var worldName = $"{pingRes.Description} / {pingRes.Version.Name} ({info.Address.Port})";
                    await _RunInUiAsync(() => DiscoveredWorlds.Add(new FoundWorld(worldName, info.Address.Port)))
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogWrapper.Error(ex, "LobbyService", $"Pinging port {info.Address.Port} failed.");
                }
            });

            listener.OnReceive += handler;
            listener.Start();
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            listener.OnReceive -= handler;
        }, _discoveringCts.Token);

        _SetState(LobbyState.Initialized);
    }

    private static bool _NotHaveNaid() =>
        LobbyInfoProvider.RequiresLogin &&
        string.IsNullOrWhiteSpace(NatayarkProfileManager.NaidProfile.AccessToken);

    /// <summary>
    /// Create a new lobby.
    /// </summary>
    /// <param name="port">Minecraft share port.</param>
    /// <param name="username">Player name.</param>
    /// <param name="publicAddress">Public address for lobby join, e.g., "host:port".</param>
    public static async Task<bool> CreateLobbyAsync(int port, string username, string? publicAddress = null)
    {
        await _discoveringCts.CancelAsync().ConfigureAwait(false);

        _SetState(LobbyState.Creating);
        try
        {
            CurrentUserName = username;

            using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
            var payload = JsonSerializer.Serialize(new
            {
                name = $"{username}的大厅",
                address = string.IsNullOrWhiteSpace(publicAddress) ? $"127.0.0.1:{port}" : publicAddress,
                max_players = 10
            });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await http.PostAsync("/lobbies", content).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                OnHint?.Invoke("创建大厅失败，服务器返回错误。", CoreHintType.Critical);
                return false;
            }

            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            string lobbyId = string.Empty;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("id", out var idProp))
                    {
                        lobbyId = idProp.ValueKind == JsonValueKind.String ? (idProp.GetString() ?? string.Empty) : idProp.GetRawText();
                    }
                    else if (root.TryGetProperty("lobby_id", out var lidProp))
                    {
                        lobbyId = lidProp.ValueKind == JsonValueKind.String ? (lidProp.GetString() ?? string.Empty) : lidProp.GetRawText();
                    }
                    else
                    {
                        foreach (var prop in root.EnumerateObject())
                        {
                            var val = prop.Value;
                            if (val.ValueKind == JsonValueKind.Object && val.TryGetProperty("id", out var innerId))
                            {
                                lobbyId = innerId.ValueKind == JsonValueKind.String ? (innerId.GetString() ?? prop.Name) : prop.Name;
                                break;
                            }
                            lobbyId = prop.Name;
                            break;
                        }
                    }
                }
            }
            catch
            {
                lobbyId = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(lobbyId))
            {
                OnHint?.Invoke("创建大厅成功但未返回编号。", CoreHintType.Info);
            }

            CurrentLobbyId = lobbyId;
            CurrentLobbyCode = lobbyId;

            _LobbyController.IsHost = true;

            var host = new PCL.Core.Link.Scaffolding.Client.Models.PlayerProfile
            {
                Name = username,
                MachineId = Environment.MachineName,
                Vendor = "PCL2-CE",
                Kind = PCL.Core.Link.Scaffolding.Client.Models.PlayerKind.HOST
            };
            await _RunInUiAsync(() =>
            {
                Players.Clear();
                Players.Add(host);
            }).ConfigureAwait(false);

            _SetState(LobbyState.Connected);
            OnServerStarted?.Invoke();
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "LobbyService", "Failed to create lobby.");
            OnHint?.Invoke("创建大厅失败，请检查日志或向开发者反馈。", CoreHintType.Critical);
            await LeaveLobbyAsync().ConfigureAwait(false);

            return false;
        }

        return true;
    }

    private static void _ServerOnServerException(Exception? ex)
    {
        if (ex is null)
        {
            return;
        }

        OnServerException?.Invoke(ex);
    }

    private static void _ServerOnServerStarted(IReadOnlyList<PlayerProfile> profiles)
    {
        LogWrapper.Debug("LobbyService", "Send server started event.");
        OnServerStarted?.Invoke();
        _ServerOnPlayerPing(profiles);
    }

    private static void _ServerOnPlayerPing(IReadOnlyList<PlayerProfile> players)
    {
        _ = _RunInUiAsync(() =>
        {
            var currentMachineIds = new HashSet<string>(Players.Select(p => p.MachineId));
            var newMachineIds = new HashSet<string>(players.Select(p => p.MachineId));
            
            if (currentMachineIds.SetEquals(newMachineIds))
            {
                LogWrapper.Debug("Player list has not changed");
                return; // nothing was changed
            }

            LogWrapper.Debug("LobbyService", "Player list membership has changed, updating UI.");

            var sortedNewPlayers = PlayerListHandler.Sort(players);

            var idsToRemove = currentMachineIds.Except(newMachineIds).ToList();
            if (idsToRemove.Any())
            {
                var playersToRemove = Players.Where(p => idsToRemove.Contains(p.MachineId)).ToList();
                foreach (var player in playersToRemove)
                {
                    Players.Remove(player);
                }
            }

            var idsToAdd = newMachineIds.Except(currentMachineIds).ToList();
            if (idsToAdd.Any())
            {
                var playersToAdd = sortedNewPlayers.Where(p => idsToAdd.Contains(p.MachineId)).ToList();
                foreach (var player in playersToAdd)
                {
                    Players.Add(player);
                }
            }
        });
    }

    /// <summary>
    /// Join an exist lobby.
    /// </summary>
    /// <param name="lobbyCode">Lobby share code.</param>
    /// <param name="username">Current use name.</param>
    public static async Task<bool> JoinLobbyAsync(string lobbyCode, string username)
    {
        await _discoveringCts.CancelAsync().ConfigureAwait(false);

        _SetState(LobbyState.Joining);

        LogWrapper.Info("LobbyService", $"Try to join lobby {lobbyCode}");

        try
        {
            CurrentUserName = username;
            using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
            var payload = JsonSerializer.Serialize(new { player_name = username });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await http.PostAsync($"/lobbies/{lobbyCode}/join", content).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                OnHint?.Invoke("加入大厅失败，服务器返回错误。", CoreHintType.Critical);
                await LeaveLobbyAsync().ConfigureAwait(false);
                return false;
            }

            CurrentLobbyCode = lobbyCode;
            CurrentLobbyId = lobbyCode;

            _LobbyController.IsHost = false;

            var self = new PCL.Core.Link.Scaffolding.Client.Models.PlayerProfile
            {
                Name = username,
                MachineId = Environment.MachineName,
                Vendor = "PCL2-CE",
                Kind = PCL.Core.Link.Scaffolding.Client.Models.PlayerKind.GUEST
            };
            await _RunInUiAsync(() =>
            {
                Players.Clear();
                Players.Add(self);
            }).ConfigureAwait(false);

            _SetState(LobbyState.Connected);
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "LobbyService", $"Failed to join lobby {lobbyCode}.");
            OnHint?.Invoke("加入大厅失败，请检查网络或服务器状态。", CoreHintType.Critical);
            await LeaveLobbyAsync().ConfigureAwait(false);

            return false;
        }

        return true;
    }

    private static void _ClientOnServerShutDown()
    {
        OnServerShutDown?.Invoke();

        _ = LeaveLobbyAsync();
    }

    private static void _ClientOnHeartbeat(IReadOnlyList<PlayerProfile> players, long latency)
    {
        _ = _RunInUiAsync(() =>
        {
            var currentMachineIds = new HashSet<string>(Players.Select(p => p.MachineId));
            var newMachineIds = new HashSet<string>(players.Select(p => p.MachineId));

            if (currentMachineIds.SetEquals(newMachineIds))
            {
                return; // nothing was changed
            }

            LogWrapper.Debug("LobbyService", "Player list membership has changed, updating UI.");

            var sortedNewPlayers = PlayerListHandler.Sort(players);

            var idsToRemove = currentMachineIds.Except(newMachineIds).ToList();
            if (idsToRemove.Any())
            {
                var playersToRemove = Players.Where(p => idsToRemove.Contains(p.MachineId)).ToList();
                foreach (var player in playersToRemove)
                {
                    Players.Remove(player);
                }
            }

            var idsToAdd = newMachineIds.Except(currentMachineIds).ToList();
            if (idsToAdd.Any())
            {
                var playersToAdd = sortedNewPlayers.Where(p => idsToAdd.Contains(p.MachineId)).ToList();
                foreach (var player in playersToAdd)
                {
                    Players.Add(player);
                }
            }

            OnClientPing?.Invoke(latency);
        });
    }


    /// <summary>
    /// Leave from lobby.
    /// </summary>
    public static async Task LeaveLobbyAsync()
    {
        _SetState(LobbyState.Leaving);

        try
        {
            await _lobbyCts.CancelAsync().ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(CurrentLobbyId) && !string.IsNullOrWhiteSpace(CurrentUserName))
            {
                try
                {
                    using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
                    var payload = JsonSerializer.Serialize(new { player_name = CurrentUserName });
                    using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                    await http.PostAsync($"/lobbies/{CurrentLobbyId}/leave", content).ConfigureAwait(false);
                    if (_LobbyController.IsHost)
                    {
                        await http.PostAsync($"/lobbies/{CurrentLobbyId}/delete", new StringContent("", Encoding.UTF8, "application/json")).ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    LogWrapper.Warn(e, "LobbyService", "Leave lobby API failed");
                }
            }

            Players.Clear();
            CurrentLobbyCode = null;
            CurrentUserName = null;
            CurrentLobbyId = null;

            if (_LobbyController.ScfClientEntity?.Client != null)
            {
                _LobbyController.ScfClientEntity.Client.Heartbeat -= _ClientOnHeartbeat;
            }

            if (_LobbyController.ScfServerEntity?.Server != null)
            {
                _LobbyController.ScfServerEntity.Server.PlayerProfilePing -= _ServerOnPlayerPing;
                _LobbyController.ScfServerEntity.Server.ServerStarted -= _ServerOnServerStarted;
            }

            await _LobbyController.CloseAsync().ConfigureAwait(false);


            _lobbyCts = new CancellationTokenSource();
            _SetState(LobbyState.Initialized);

            LogWrapper.Info("LobbyService", "Left lobby and cleaned up resources.");

            _isGameWatcherRunnable = false;

            _discoveringCts = new CancellationTokenSource();
            _ = DiscoverWorldAsync();
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "LobbyService", "Failed when leave lobby.");
        }
    }

    private static void _SetState(LobbyState newState)
    {
        var oldState = CurrentState;
        if (oldState == newState)
        {
            return;
        }

        CurrentState = newState;

        LogWrapper.Info("LobbyService", $"Lobby state changed from {oldState} to {newState}");

        StateChanged?.Invoke(oldState, newState);
    }

    private static void _CheckGameState(object? state)
    {
        if (!_isGameWatcherRunnable)
        {
            return;
        }

        if (_LobbyController.ScfServerEntity is null)
        {
            return;
        }

        LobbyController.IsHostInstanceAvailableAsync(_LobbyController.ScfServerEntity.EasyTier.MinecraftPort)
            .ContinueWith(async (task) =>
            {
                var isExist = await task.ConfigureAwait(false);
                if (!isExist)
                {
                    _isGameWatcherRunnable = false;
                    OnUserStopGame?.Invoke();
                }
            });
    }

    private static async Task _RunInUiAsync(Action action)
    {
        await Application.Current.Dispatcher.InvokeAsync(action);
    }
}

/// <summary>
/// Founded minecraft world information.
/// </summary>
/// <param name="Name">World name.</param>
/// <param name="Port">World share port.</param>
public record FoundWorld(string Name, int Port);

/// <summary>
/// Hint type in PCL.Core (for UI display).
/// </summary>
public enum CoreHintType
{
    /// <summary>
    /// 信息，通常是蓝色的“i”。
    /// </summary>
    /// <remarks></remarks>
    Info,

    /// <summary>
    /// 已完成，通常是绿色的“√”。
    /// </summary>
    /// <remarks></remarks>
    Finish,

    /// <summary>
    /// 错误，通常是红色的“×”。
    /// </summary>
    /// <remarks></remarks>
    Critical
}