#define RU

using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Plugins;
using Oxide.Core.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using JetBrains.Annotations;
using Oxide.Core.Libraries;
using Oxide.Game.Rust.Cui;
using System.Linq;
using UnityEngine;
using Network;
using UnityEngine.Networking; 
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using ConVar;
using Oxide.Core.Database;
using Facepunch;
using Rust;
using Steamworks;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using UnityEngine;
using Color = System.Drawing.Color;
using Graphics = System.Drawing.Graphics;
using Star = ProtoBuf.PatternFirework.Star;
using ProtoBuf;

namespace Oxide.Plugins
{
  [Info("RustAppv2", "RustApp.io (Anchor-Team)", "2.0.0")]
  public class RustAppv2 : RustPlugin
  {
    #region Variables

    private static MetaInfo _MetaInfo = MetaInfo.Read();

    private static Configuration _Settings;
    private static RustAppv2 _RustApp;
    private static RustAppEngine _RustAppEngine;

    // References to other plugin with API
    [PluginReference] private Plugin NoEscape, RaidZone, RaidBlock, MultiFighting, TirifyGamePluginRust, ExtRaidBlock;

    #endregion

    #region API

    private static class Api {
      public static bool ErrorContains(string error, string text) {
        return error.ToLower().Contains(text.ToLower());
      }
    }

    private static class CourtApi {
      #region Shared

      public class PluginServerDto {
        public string name = ConVar.Server.hostname;
        public string hostname = ConVar.Server.hostname;
        
        public string level = SteamServer.MapName ?? ConVar.Server.level;
        public string description = ConVar.Server.description + " " + ConVar.Server.motd;
        public string branch = ConVar.Server.branch;

        public string avatar_big = ConVar.Server.logoimage;
        public string avatar_url = ConVar.Server.logoimage;
        public string banner_url = ConVar.Server.headerimage;

        public int online = BasePlayer.activePlayerList.Count + ServerMgr.Instance.connectionQueue.queue.Count + ServerMgr.Instance.connectionQueue.joining.Count;
        public int slots = ConVar.Server.maxplayers;

        public string version = _RustApp.Version.ToString();
        public string protocol = Protocol.printable.ToString();
        public string performance = _RustApp.TotalHookTime.ToString();

        public int port = ConVar.Server.port; 

        public bool connected = _RustAppEngine?.AuthWorker.IsAuthed ?? false;
      }

      #endregion

      private static readonly string BaseUrl = "https://court.rustapp.io";

      #region GetServerInfo

      public static StableRequest<object> GetServerInfo() {
        return new StableRequest<object>($"{BaseUrl}/plugin/", RequestMethod.GET, null);
      }

      #endregion

      #region SendPairDetails

      public class PluginPairPayload : PluginServerDto {}

      public class PluginPairResponse
      {
        [CanBeNull] public int ttl;
        [CanBeNull] public string token;
      }

      public static StableRequest<PluginPairResponse> SendPairDetails(string code) {
        return new StableRequest<PluginPairResponse>($"{BaseUrl}/plugin/pair?code={code}", RequestMethod.POST, new PluginPairPayload());
      }

      #endregion
    
      #region StateUpdate

      public class PluginStatePlayerMetaDto {
        public List<string> tags = new List<string>();
        public Dictionary<string, string> fields = new Dictionary<string, string>();
      }
      
      public class PluginStatePlayerDto
      {
        public static PluginStatePlayerDto FromConnection(Network.Connection connection, string status)
        {
          var payload = new PluginStatePlayerDto();

          payload.steam_id = connection.userid.ToString();
          payload.steam_name = connection.username.Replace("<blank>", "blank");
          payload.ip = IPAddressWithoutPort(connection.ipaddress);

          payload.status = status;

          payload.no_license = DetectNoLicense(connection);
          try { payload.meta = CollectPlayerMeta(payload.steam_id, payload.meta); } catch {}

          var team = RelationshipManager.ServerInstance.FindPlayersTeam(connection.userid);
          if (team != null)
          {
            payload.team = team.members
              .Select(v => v.ToString())
              .Where(v => v != connection.userid.ToString())
              .ToList();
          }

          return payload;
        }

        public static PluginStatePlayerDto FromPlayer(BasePlayer player)
        {
          var payload = PluginStatePlayerDto.FromConnection(player.Connection, "active");

          payload.position = player.transform.position.ToString();
          payload.rotation = player.eyes.rotation.ToString();
          payload.coords = GridReference(player.transform.position);

          payload.can_build = DetectBuildingAuth(player);
          payload.is_raiding = DetectIsRaidBlock(player);
          payload.is_alive = player.IsAlive();

          return payload;
        }
        
        public string steam_id;
        public string steam_name;
        public string ip;

        [CanBeNull] public string position;
        [CanBeNull] public string rotation;
        [CanBeNull] public string coords;

        public bool can_build = false;
        public bool is_raiding = false;
        public bool no_license = false;
        public bool is_alive = false;

        public string status;

        public PluginStatePlayerMetaDto meta = new PluginStatePlayerMetaDto();
        public List<string> team = new List<string>();
      }

      public class PluginStateUpdatePayload : PluginServerDto {
        public PluginServerDto server_info = new PluginServerDto();

        public List<PluginStatePlayerDto> players = new List<PluginStatePlayerDto>();
        public Dictionary<string, string> disconnected = new Dictionary<string, string>();
        public Dictionary<string, string> team_changes = new Dictionary<string, string>();
      }

      public static StableRequest<object> SendStateUpdate(PluginStateUpdatePayload data) {
        return new StableRequest<object>($"{BaseUrl}/plugin/state", RequestMethod.PUT, data);
      }

      #endregion
    
      #region SendChatMessages

      public class PluginChatMessageDto
      {
        public string steam_id;
        [CanBeNull]
        public string target_steam_id;

        public bool is_team;

        public string text;
      }

      public class PluginChatMessagePayload {
        public List<PluginChatMessageDto> messages = new List<PluginChatMessageDto>();
      }

      public static StableRequest<object> SendChatMessages(PluginChatMessagePayload payload) {
        return new StableRequest<object>($"{BaseUrl}/plugin/chat", RequestMethod.POST, payload);
      }

      #endregion
    } 
 
    private static class QueueApi {      
      #region Shared

      public class QueueTaskResponse
      {
        public string id;

        public QueueTaskRequestConfigDto request;
      }

      public class QueueTaskRequestConfigDto
      {
        public string name;
        public JObject data;
      }

      #endregion

      private static readonly string BaseUrl = "https://queue.rustapp.io";

      #region GetQueueTasks
      
      public static StableRequest<List<QueueTaskResponse>> GetQueueTasks() {
        return new StableRequest<List<QueueTaskResponse>>($"{BaseUrl}/", RequestMethod.GET, null);
      }

      #endregion

      #region ProcessQueueTasks

      public class QueueTaskResponsePayload {
        public Dictionary<string, object> data = new Dictionary<string, object>();
      }

      public static StableRequest<object> ProcessQueueTasks(QueueTaskResponsePayload payload) {
        return new StableRequest<object>($"{BaseUrl}/", RequestMethod.PUT, payload);
      }

      #endregion
    }

    private static class BanApi {
      private static readonly string BaseUrl = "https://ban.rustapp.io";

      #region BanGetBatch

      public class BanGetBatchEntryPayloadDto {
        public string steam_id;
        public string ip;
      } 

      public class BanGetBatchPayload {
        public List<BanGetBatchEntryPayloadDto> players = new List<BanGetBatchEntryPayloadDto>();
      }

      public class BanGetBatchEntryResponseDto {
        public string steam_id;
        public string ip;
        
        public List<BanDto> bans= new List<BanDto>();
      }

      public class BanDto {
        public int id;
        public string steam_id;
        public string ban_ip;
        public string reason;
        public long expired_at;
        public bool ban_ip_active;
        public bool computed_is_active;

        public int sync_project_id = 0;
        public bool sync_should_kick = false;
      }

      public class BanGetBatchResponse {
        public List<BanGetBatchEntryResponseDto> entries = new List<BanGetBatchEntryResponseDto>();
      }

      public static StableRequest<BanGetBatchResponse> BanGetBatch(BanGetBatchPayload payload) {
        return new StableRequest<BanGetBatchResponse>($"{BaseUrl}/plugin/v2", RequestMethod.POST, payload);
      }
  
      #endregion
    }

    #endregion

    #region Configuration

    public class MetaInfo
    {
      public static MetaInfo Read()
      {
        if (!Interface.Oxide.DataFileSystem.ExistsDatafile("RustApp_Configuration"))
        {
          _MetaInfo = null;
          return null;
        }

        var obj = Interface.Oxide.DataFileSystem.ReadObject<MetaInfo>("RustApp_Configuration");
      
        _MetaInfo = obj;

        return obj;
      }

      public static void write(MetaInfo courtMeta)
      {
        Interface.Oxide.DataFileSystem.WriteObject("RustApp_Configuration", courtMeta);

        MetaInfo.Read();
      }

      [JsonProperty("It is access for RustApp Court, never edit or share it")]
      public string Value;
    }

    private class Configuration
    {

      [JsonProperty("[UI] Chat commands")]
      public List<string> report_ui_commands = new List<string>();

      [JsonProperty("[UI] Report reasons")]
      public List<string> report_ui_reasons = new List<string>();

      [JsonProperty("[UI] Cooldown between reports (seconds)")]
      public int report_ui_cooldown = 300;

      [JsonProperty("[UI] Auto-parse reports from F7 (ingame reports)")]
      public bool report_ui_auto_parse = true;

      [JsonProperty("[UI • Starter Plan] Show 'recently checked' checkbox (amount of days)")]
      public int report_ui_show_check_in = 7;

      [JsonProperty("[Chat] SteamID for message avatar (default account contains RustApp logo)")]
      public string chat_default_avatar_steamid = "76561198134964268";

      [JsonProperty("[Check] Command to send contact")]
      public string check_contact_command = "contact";

      [JsonProperty("[Ban] Enable broadcast server bans")]
      public bool ban_enable_broadcast = true;

      [JsonProperty("[Utils] Use own raidblock hooks")]
      public bool utils_use_own_raidblock = false;

      [JsonProperty("[Custom Actions] Allow custom actions")]
      public bool custom_actions_allow = true;

      public static Configuration Generate()
      {
        return new Configuration
        {
          report_ui_commands = new List<string> { "report", "reports" },
          report_ui_reasons = new List<string> { "Cheat", "Macros", "Abuse" },
          report_ui_cooldown = 300,
          report_ui_auto_parse = true,
          report_ui_show_check_in = 7,

          chat_default_avatar_steamid = "76561198134964268",
          ban_enable_broadcast = true,
          custom_actions_allow = true,
          utils_use_own_raidblock = false
        };
      }
    }

    #endregion

    #region Workers

    private class RustAppEngine : RustAppWorker {
      public GameObject ChildObjectToWorkers;

      public AuthWorker? AuthWorker;
      public BanWorker? BanWorker;
      public StateWorker? StateWorker;
      public CheckWorker? CheckWorker;
      public QueueWorker? QueueWorker;
    
      private void Awake() {
        base.Awake();

        SetupAuthWorker();
      }

      public bool IsPairingNow() {
        return this.gameObject.GetComponent<PairWorker>() != null;
      }

      private void SetupAuthWorker() {
        AuthWorker = this.gameObject.AddComponent<AuthWorker>();

        AuthWorker.OnAuthSuccess += () => {
          Debug("Авторизация успешно, запускаем компоненты");

          CreateSubWorkers();
        };
 
        AuthWorker.OnAuthFailed += () => {
          Debug("Авторизация зафейлилась, убиваем компоненты");
         
          DestroySubWorkers();
        };

        AuthWorker.CycleAuthUpdate();
      }

      private void CreateSubWorkers() {
        ChildObjectToWorkers = this.gameObject.CreateChild();

        StateWorker = ChildObjectToWorkers.AddComponent<StateWorker>();
        CheckWorker = ChildObjectToWorkers.AddComponent<CheckWorker>();
        QueueWorker = ChildObjectToWorkers.AddComponent<QueueWorker>();
        BanWorker = ChildObjectToWorkers.AddComponent<BanWorker>();
      }

      private void DestroySubWorkers() {
        if (ChildObjectToWorkers == null) {
          return;
        }

        UnityEngine.Object.Destroy(ChildObjectToWorkers);
      }
    }

    private class AuthWorker : RustAppWorker {
      public bool IsAuthed = false;

      public Action? OnAuthSuccess;
      public Action? OnAuthFailed;

      public void CycleAuthUpdate() {
        InvokeRepeating(nameof(CheckAuthStatus), 0f, 5f);
      }
 
      public void CheckAuthStatus() {
        if (_RustAppEngine.IsPairingNow()) {
          return;
        }
      
        Action<string> onError = (error) => {
          Error(error);

          if (IsAuthed == false) {
            return;
          }

          IsAuthed = false;
          OnAuthFailed?.Invoke();
        };

        Action onSuccess = () => {
          Debug("Соединение установлено");

          if (IsAuthed == true) {
            return;
          }

          IsAuthed = true;
          OnAuthSuccess?.Invoke();
        };

        CourtApi.GetServerInfo().Execute(
          (_, raw) => {
            onSuccess();
            return;
          }, 
          (err) => {
            // secret = ""
            var codeError1 = Api.ErrorContains(err, "some of required headers are wrong or missing");
            // secret = "123"
            var codeError2 = Api.ErrorContains(err, "authorization secret is corrupted");
            // server.ip != this.ip || server.port != this.port
            var codeError3 = Api.ErrorContains(err, "Check server configuration, required");

            if (codeError1 || codeError2 || codeError3) {
              onError("Welcome to the RustApp.Io!");
              onError("Your server is not paired with our network, follow instructions to pair server:");
              onError("1) If you already start pairing, enter 'ra.pair %code%' which you get from our site");
              onError("2) Open servers page, press 'connect server', and enter command which you get on it");
              return;
            }

            // version < minVersion
            var versionError1 = Api.ErrorContains(err, " is lower than minimal: ");
            // if we block some version
            var versionError2 = Api.ErrorContains(err, "This version contains serious bug, please update plugin");

            if (versionError1 || versionError2) {
              onError("Welcome to the RustApp.Io!");
              onError("Your plugin is outdated, you should download new version!");
              onError("1) Open servers page, press 'update' near server to download new version, then just replace plugin");
              onError("2) If you don't have 'update' button, press settings icon and choose 'download plugin' button");
              return;
            }
            
            // if tariff finished/balance zero
            var paymentError1 = Api.ErrorContains(err, "У вас кончились средства на балансе проекта, пополните на");
            // If some limits broken
            var paymentError2 = Api.ErrorContains(err, "Вы превысили лимиты по");

            if (paymentError1 || paymentError2) {
              onError("Welcome to the RustApp.Io!");
              onError("Seems there are some problems with your tariff, please open site to get more details");
              onError("1) You need to top-up balance to continue working with our service");
              onError("1) You reached players limit, and you neeed to upgrade tariff");
              return;
            }

            Debug($"Неизвестная ошибка: {err.Substring(0, 128)}");
          }
        );
      }
    }

    private class PairWorker : RustAppWorker {
      private string EnteredCode;

      public void StartPairing(string code) {
        EnteredCode = code;

        CourtApi.SendPairDetails(EnteredCode)
          .Execute(
            (data, raw) => {
              InvokeRepeating(nameof(WaitPairFinish), 0f, 1f);
            },
            (err) => {
              if (Api.ErrorContains(err, "code not exists")) {
                Debug("Такого кода не существует");
              } else {
                Debug($"Произошла неизвестная ошибка, попробуйте позже {err}");
              }

              Destroy(this);
            }
          );
      }

      public void WaitPairFinish() {
        CourtApi.SendPairDetails(EnteredCode)
          .Execute(
            (data, raw) => {
              if (data.token == null || data.token.Length == 0) {
                Debug("Завершите подключение на сайте...");
                return;
              }

              Debug("Токен сохранён, переподключение...");

              _RustApp.timer.Once(1f, () => _RustAppEngine.AuthWorker.CheckAuthStatus());
              MetaInfo.write(new MetaInfo { Value = data.token });
              
              Destroy(this);
            },
            (err) => {
              if (Api.ErrorContains(err, "code not exists")) {
                Debug("Судя по всему вы закрыли окно на сайте не нажав сохранить");
              } else {
                Debug($"Произошла неизвестная ошибка, попробуйте позже {err}");
              }

              Destroy(this);
            }
          );
      }
    }

    private class StateWorker : RustAppWorker {
      public Dictionary<string, string> DisconnectReasons = new Dictionary<string, string>();
      public Dictionary<string, string> TeamChanges = new Dictionary<string, string>();

      private void Awake() {
        base.Awake();
      
        InvokeRepeating(nameof(CycleSendUpdate), 0f, 5f);
      }

      public void CycleSendUpdate() {
        SendUpdate(false);
      }

      public void SendUpdate(bool unload = false) {
        var players = unload ? new List<CourtApi.PluginStatePlayerDto>() : this.CollectPlayers();

        var disconnected = unload ? CollectFakeDisconnects() : DisconnectReasons.ToDictionary(v => v.Key, v => v.Value);
        var team_changes = TeamChanges.ToDictionary(v => v.Key, v => v.Value);

        DisconnectReasons = new Dictionary<string, string>();
        TeamChanges = new Dictionary<string, string>();

        CourtApi.SendStateUpdate(new CourtApi.PluginStateUpdatePayload {
          players = players,
          disconnected = disconnected,
          team_changes = team_changes 
        })
          .Execute(
            (data, raw) => Debug("Стейт отправлен успешно"),
            (err) => {
              Debug($"Ошибка отправки стейта {err}");

              ResurrectDictionary(disconnected, DisconnectReasons);  
              ResurrectDictionary(team_changes, TeamChanges);
            }
          );
      }

      private List<CourtApi.PluginStatePlayerDto> CollectPlayers() {
        List<CourtApi.PluginStatePlayerDto> playerStateDtos = new List<CourtApi.PluginStatePlayerDto>();

        foreach (var player in BasePlayer.activePlayerList) {
          try { playerStateDtos.Add(CourtApi.PluginStatePlayerDto.FromPlayer(player)); } catch {}
        }

        foreach (var connection in ServerMgr.Instance.connectionQueue.joining) {
          try { playerStateDtos.Add(CourtApi.PluginStatePlayerDto.FromConnection(connection, "joining")); } catch {}
        }

        for (var i = 0;  i < ServerMgr.Instance.connectionQueue.queue.Count; i++) {
          var connection = ServerMgr.Instance.connectionQueue.queue.ElementAtOrDefault(i);
          if (connection == null) {
            continue;
          }

          try { playerStateDtos.Add(CourtApi.PluginStatePlayerDto.FromConnection(connection, "queued")); } catch {}
        }

        return playerStateDtos;
      }

      private Dictionary<string, string> CollectFakeDisconnects() {
        Dictionary<string, string> disconnect = new Dictionary<string, string>();

        foreach (var player in BasePlayer.activePlayerList) {
          try { disconnect.Add(player.UserIDString, "plugin-unload"); } catch {}
        }

        foreach (var connection in ServerMgr.Instance.connectionQueue.joining) {
          try { disconnect.Add(connection.userid.ToString(), "plugin-unload"); } catch {}
        }

        for (var i = 0;  i < ServerMgr.Instance.connectionQueue.queue.Count; i++) {
          var connection = ServerMgr.Instance.connectionQueue.queue.ElementAtOrDefault(i);
          if (connection == null) {
            continue;
          }

          try { disconnect.Add(connection.userid.ToString(), "plugin-unload"); } catch {}
        }

        return disconnect;
      }
    }

    private class CheckWorker : RustAppWorker {
      private Dictionary<string, bool> ShowedNoticyCache = new Dictionary<string, bool>();

      public bool IsNoticeActive(string steamId) {
        if (!ShowedNoticyCache.ContainsKey(steamId)) {
          return false;
        }

        return ShowedNoticyCache[steamId];
      }

      public void SetNoticeActive(string steamId, bool value) {
        var player = BasePlayer.Find(steamId);

        // TODO: Deprecated
        if (value) {
          Interface.Oxide.CallHook("RustApp_OnCheckNoticeShowed", player);
        } else {
          Interface.Oxide.CallHook("RustApp_OnCheckNoticeHidden", player);
        }
        
        // TODO: New hook
        Interface.Oxide.CallHook("RustApp_CheckNoticeState", steamId, value);

        ShowedNoticyCache[steamId] = value;
      }
    }

    private class QueueWorker : RustAppWorker {

      private List<string> QueueProcessedIds = new List<string>();

      private void Awake() {
        base.Awake();
      
        InvokeRepeating(nameof(GetQueueTasks), 0f, 1f);
      }

      private void GetQueueTasks() {
        QueueApi.GetQueueTasks()
          .Execute(
            (data, raw) => CallQueueTasks(data),
            (error) => {
              Debug($"Ошибка получения очередей {error}");
            }
          );
      }

      private void CallQueueTasks(List<QueueApi.QueueTaskResponse> queuesTasks) {
        if (queuesTasks.Count == 0) {
          Debug("Все задачи обработаны");
          return;
        }

        Dictionary<string, object> queueResponses = new Dictionary<string, object>();

        foreach (var task in queuesTasks) {
          if (QueueProcessedIds.Contains(task.id)) {
            Debug("Эта задача уже обрабатывалась");
            return;
          }

          try {
            // To get our official response
            var response = (object) _RustApp.Call(ConvertToRustAppQueueFormat(task.request.name, true), task.request.data);
           
            queueResponses.Add(task.id, response);

            // Just to broadcast event RustApp_Queue%name%
            Interface.Oxide.CallHook(ConvertToRustAppQueueFormat(task.request.name, false), task.request.data);
          }
          catch (Exception exc) {
            Error($"Не удалось обработать {task.id} {task.request.name}");

            queueResponses.Add(task.id, null);
          }
        }

        ProcessQueueTasks(queueResponses);
      }

      private void ProcessQueueTasks(Dictionary<string, object> queueResponses) {
        if (queueResponses.Keys.Count == 0) {
          return;
        }

        QueueApi.ProcessQueueTasks(new QueueApi.QueueTaskResponsePayload { data = queueResponses })
          .Execute(
            (data, raw) => { 
              QueueProcessedIds = new List<string>();

              Debug("Ответ по очередям успешно доставлен");
            },
            (err) => {
              QueueProcessedIds = new List<string>();

              Error("Не удалось отправить ответ по очередям");
            }
          );
      }

      private string ConvertToRustAppQueueFormat(string input, bool isInternalCall)
      {
        var words = input.Replace("court/", "").Split('-');
        
        for (int i = 0; i < words.Length; i++)
        {
          words[i] = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words[i]);
        }
        
        return $"RustApp_{(isInternalCall ? "Internal" : "")}Queue_" + string.Join("", words);
      }
    }

    private class BanWorker : RustAppWorker {
      private List<BanApi.BanGetBatchEntryPayloadDto> BanUpdateQueue = new List<BanApi.BanGetBatchEntryPayloadDto>();

      public void Awake() {
        base.Awake();

        DefaultScanAllPlayers();
      
        InvokeRepeating(nameof(CycleBanUpdate), 0f, 2f);
      }

      private void DefaultScanAllPlayers() {
        foreach (var player in BasePlayer.activePlayerList)
        {
          try { CheckBans(player); } catch { }
        }

        foreach (var queued in ServerMgr.Instance.connectionQueue.queue)
        {
          try { CheckBans(queued.userid.ToString(), IPAddressWithoutPort(queued.ipaddress)); } catch { }
        }

        foreach (var loading in ServerMgr.Instance.connectionQueue.joining)
        {
          try { CheckBans(loading.userid.ToString(), IPAddressWithoutPort(loading.ipaddress)); } catch { }
        }
      }

      public void CheckBans(BasePlayer player) {
        CheckBans(player.UserIDString, IPAddressWithoutPort(player.Connection.ipaddress));
      }

      public void CheckBans(string steamId, string ip) {
        // Provide an option to bypass ban checks enforced by external plugins
        var over = Interface.Oxide.CallHook("RustApp_CanIgnoreBan", steamId);
        if (over != null)
        {
          return;
        }

        var exists = BanUpdateQueue.Find(v => v.steam_id == steamId);
        if (exists != null) {
          exists.ip = ip;
          return;
        }
 
        BanUpdateQueue.Add(new BanApi.BanGetBatchEntryPayloadDto { steam_id = steamId, ip = ip });
      }

      private void CycleBanUpdate() {
        if (BanUpdateQueue.Count == 0) {
          Debug("Нет банов для проверки");
          return;
        }

        CycleBanUpdateWrapper((steamId, ban) => {
          Debug($"Проверка бана для {steamId}, бан обнаружен: {ban != null}");
          var queuePosition = BanUpdateQueue.Find(v => v.steam_id == steamId);
          if (queuePosition != null) {
            BanUpdateQueue.Remove(queuePosition);
          }

          if (ban == null) {
            return;
          } 

          if (ban.sync_project_id != 0 && !ban.sync_should_kick) {
            return;
          }

          if (ban.steam_id == steamId) {
            // Ban is directly to this steamId
            ReactOnDirectBan(steamId, ban);
          } else {
            // Ban was found by IP
            ReactOnDirectBan(steamId, ban);
          }
        });
      }
      
      private void CycleBanUpdateWrapper(Action<string, BanApi.BanDto?> callback) {
        var payload = new BanApi.BanGetBatchPayload { players = BanUpdateQueue.ToList() };

        BanUpdateQueue = new List<BanApi.BanGetBatchEntryPayloadDto>();

        BanApi.BanGetBatch(payload)
          .Execute(
            (data, raw) => {
              Debug(raw);
              payload.players.ForEach(originalPlayer => {
                var exists = data.entries.Find(banPlayer => banPlayer.steam_id == originalPlayer.steam_id);

                var ban = exists?.bans.FirstOrDefault(v => v.computed_is_active);

                callback.Invoke(originalPlayer.steam_id, ban);
              });
            },
            (err) => {
              ResurrectList(payload.players, BanUpdateQueue);
            
              Error($"Не удалось проверить блокировки: {err}");
            }
          );
      }

      public void ReactOnDirectBan(string steamId, BanApi.BanDto ban) {
        var format = "";

        if (ban.sync_project_id != 0)
        {
          // Get format for sync ban
          format = ban.expired_at == 0 
            ? _RustApp.lang.GetMessage("System.BanSync.Perm.Kick", _RustApp, steamId) 
            : _RustApp.lang.GetMessage("System.BanSync.Temp.Kick", _RustApp, steamId);
        } else {
          // Get format for your own ban
          format = ban.expired_at == 0 
            ? _RustApp.lang.GetMessage("System.Ban.Perm.Kick", _RustApp, steamId) 
            : _RustApp.lang.GetMessage("System.Ban.Temp.Kick", _RustApp, steamId);
        }

        var time = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)
          .AddMilliseconds(ban.expired_at + 3 * 60 * 60 * 1_000)
          .ToString("dd.MM.yyyy HH:mm");

        var finalText = format
          .Replace("%REASON%", ban.reason)
          .Replace("%TIME%", time);

        _RustApp.CloseConnection(steamId, finalText);
      }

      public void ReactOnIpBan(string steamId, BanApi.BanDto ban) {
        _RustApp.CloseConnection(steamId, _RustApp.lang.GetMessage("System.Ban.Ip.Kick", _RustApp, steamId));

        // TODO: CreateAlertForIpBan(ban, steamId);
      }
    }

    private class ChatWorker : RustAppWorker {

    }

    #endregion

    #region Commands

    [ConsoleCommand("ra.pair")]
    private void StartPairing(ConsoleSystem.Arg args) {
      if (args.Player() != null || args.Args.Length == 0) {
        return;
      }

      var code = args.Args[0];

      _RustAppEngine.gameObject.AddComponent<PairWorker>().StartPairing(code);
    }

    #endregion

    #region Hooks

      #region System hooks

      private void OnServerInitialized() {
        if (!CheckRequiredPlugins()) {
          Error("Исправьте проблемы и перезагрузите плагин");
          return;
        }

        _RustApp = this;

        MetaInfo.Read();

        RustAppEngineCreate();
      }

      private void Unload() {
        _RustAppEngine.StateWorker?.SendUpdate(true);

        RustAppEngineDestroy();
      }

      protected override void LoadConfig()
      {
        base.LoadConfig();
        try
        {
          _Settings = Config.ReadObject<Configuration>();
        }
        catch
        {
          PrintWarning($"Error reading config, creating one new config!");
          LoadDefaultConfig();
        }

        SaveConfig();
      }

      protected override void LoadDefaultConfig() => _Settings = Configuration.Generate();
      protected override void SaveConfig() => Config.WriteObject(_Settings);

      protected override void LoadDefaultMessages()
      {
        lang.RegisterMessages(new Dictionary<string, string>
        {
          ["Header.Find"] = "НАЙТИ ИГРОКА",
          ["Header.SubDefault"] = "На кого вы хотите пожаловаться?",
          ["Header.SubFindResults"] = "Вот игроки, которых мы нашли",
          ["Header.SubFindEmpty"] = "Игроки не найдены",
          ["Header.Search"] = "Поиск",
          ["Header.Search.Placeholder"] = "Введите ник/steamid",
          ["Subject.Head"] = "Выберите причину репорта",
          ["Subject.SubHead"] = "На игрока %PLAYER%",
          ["Cooldown"] = "Подожди %TIME% сек.",
          ["Sent"] = "Жалоба успешно отправлена",
          ["Contact.Error"] = "Вы не отправили свой Discord",
          ["Contact.Sent"] = "Вы отправили:",
          ["Contact.SentWait"] = "<size=12>Если вы отправили корректный дискорд - ждите заявку в друзья.</size>",
          ["Check.Text"] = "<color=#c6bdb4><size=32><b>ВЫ ВЫЗВАНЫ НА ПРОВЕРКУ</b></size></color>\n<color=#958D85>У вас есть <color=#c6bdb4><b>3 минуты</b></color> чтобы отправить дискорд и принять заявку в друзья.\nИспользуйте команду <b><color=#c6bdb4>/contact</color></b> чтобы отправить дискорд.\n\nДля связи с модератором - используйте чат, а не команду.</color>",
          ["Chat.Direct.Toast"] = "Получено сообщение от админа, посмотрите в чат!",
          ["UI.CheckMark"] = "Проверен",
          ["Paid.Announce.Clean"] = "Ваша жалоба на \"%SUSPECT_NAME%\" была проверена!\n<size=12><color=#81C5F480>В результате проверки, нарушений не обнаружено</color></size>",
          ["Paid.Announce.Ban"] = "Ваша жалоба на \"%SUSPECT_NAME%\" была проверена!\n<color=#F7D4D080><size=12>Игрок заблокирован, причина: %REASON%</size></color>",

          ["System.Chat.Direct"] = "<size=12><color=#ffffffB3>ЛС от Администратора</color></size>\n<color=#AAFF55>%CLIENT_TAG%</color>: %MSG%",
          ["System.Chat.Global"] = "<size=12><color=#ffffffB3>Сообщение от Администратора</color></size>\n<color=#AAFF55>%CLIENT_TAG%</color>: %MSG%",

          ["System.Ban.Broadcast"] = "Игрок <color=#55AAFF>%TARGET%</color> <color=#bdbdbd></color>был заблокирован.\n<size=12>- причина: <color=#d3d3d3>%REASON%</color></size>",
          ["System.Ban.Temp.Kick"] = "Вы забанены на этом сервере до %TIME% МСК, причина: %REASON%",
          ["System.Ban.Perm.Kick"] = "Вы навсегда забанены на этом сервере, причина: %REASON%",
          ["System.Ban.Ip.Kick"] = "Вам ограничен вход на сервер!",

          ["System.BanSync.Temp.Kick"] = "Обнаружена блокировка на другом проекте до %TIME% МСК, причина: %REASON%",
          ["System.BanSync.Perm.Kick"] = "Обнаружена блокировка на другом проекте, причина: %REASON%",
        }, this, "ru");

        lang.RegisterMessages(new Dictionary<string, string>
        {
          ["Header.Find"] = "FIND PLAYER",
          ["Header.SubDefault"] = "Who do you want to report?",
          ["Header.SubFindResults"] = "Here are players, which we found",
          ["Header.SubFindEmpty"] = "No players was found",
          ["Header.Search"] = "Search",
          ["Header.Search.Placeholder"] = "Enter nickname/steamid",
          ["Subject.Head"] = "Select the reason for the report",
          ["Subject.SubHead"] = "For player %PLAYER%",
          ["Cooldown"] = "Wait %TIME% sec.",
          ["Sent"] = "Report succesful sent",
          ["Contact.Error"] = "You did not sent your Discord",
          ["Contact.Sent"] = "You sent:",
          ["Contact.SentWait"] = "If you sent the correct discord - wait for a friend request.",
          ["Check.Text"] = "<color=#c6bdb4><size=32><b>YOU ARE SUMMONED FOR A CHECK-UP</b></size></color>\n<color=#958D85>You have <color=#c6bdb4><b>3 minutes</b></color> to send discord and accept the friend request.\nUse the <b><color=#c6bdb4>/contact</color></b> command to send discord.\n\nTo contact a moderator - use chat, not a command.</color>",
          ["Chat.Direct.Toast"] = "Received a message from admin, look at the chat!",
          ["UI.CheckMark"] = "Checked",
          ["Paid.Announce.Clean"] = "Your complaint about \"%SUSPECT_NAME%\" has been checked!\n<size=12><color=#81C5F480>As a result of the check, no violations were found</color ></size>",
          ["Paid.Announce.Ban"] = "Your complaint about \"%SUSPECT_NAME%\" has been verified!\n<color=#F7D4D080><size=12>Player banned, reason: %REASON%</ size></color>",

          ["System.Chat.Direct"] = "<size=12><color=#ffffffB3>DM from Administration</color></size>\n<color=#AAFF55>%CLIENT_TAG%</color>: %MSG%",
          ["System.Chat.Global"] = "<size=12><color=#ffffffB3>Message from Administration</color></size>\n<color=#AAFF55>%CLIENT_TAG%</color>: %MSG%",

          ["System.Ban.Broadcast"] = "Player <color=#55AAFF>%TARGET%</color> <color=#bdbdbd></color>was banned.\n<size=12>- reason: <color=#d3d3d3>%REASON%</color></size>",
          ["System.Ban.Temp.Kick"] = "You are banned until %TIME% МСК, reason: %REASON%",
          ["System.Ban.Perm.Kick"] = "You have perm ban, reason: %REASON%",
          ["System.Ban.Ip.Kick"] = "You are restricted from entering the server!",

          ["System.BanSync.Temp.Kick"] = "Detected ban on another project until %TIME% МСК, reason: %REASON%",
          ["System.BanSync.Perm.Kick"] = "Detected ban on another project, reason: %REASON%",
        }, this, "en"); 
      }

      #endregion

      #region Connect hooks

      private void CanUserLogin(string name, string id, string ipAddress)
      {
        OnPlayerConnectedNormalized(id, IPAddressWithoutPort(ipAddress));
      } 

      private void OnPlayerConnected(BasePlayer player)
      { 
        OnPlayerConnectedNormalized(player.UserIDString, IPAddressWithoutPort(player.Connection.ipaddress));
      } 

      #endregion

      #region Disconnect hooks

      private void OnPlayerDisconnected(BasePlayer player, string reason)
      {
        OnPlayerDisconnectedNormalized(player.UserIDString, reason);
      }

      private void OnClientDisconnect(Network.Connection connection, string reason)
      {
        OnPlayerDisconnectedNormalized(connection.userid.ToString(), reason);
      }

      #endregion

      #region Team hooks

      private void OnTeamKick(RelationshipManager.PlayerTeam team, BasePlayer player, ulong target)
      {
        SetTeamChange(player.UserIDString, target.ToString());
      }

      private void OnTeamDisband(RelationshipManager.PlayerTeam team)
      {
        team.members.ForEach(v => SetTeamChange(v.ToString(), v.ToString()));
      }

      private void OnTeamLeave(RelationshipManager.PlayerTeam team, BasePlayer player)
      {
        if (team.members.Count != 1)
        {
          SetTeamChange(player.UserIDString, player.UserIDString);
        }
      }

      #endregion

      #region Queue hooks

        #region Health check

        private object RustApp_InternalQueue_HealthCheck(JObject raw) {
          return true; 
        }

        #endregion

        #region Kick

        private class QueueTaskKickDto
        {
          public string steam_id;
          public string reason;
          public bool announce;
        }

        private object RustApp_InternalQueue_Kick(JObject raw) {
          var data = raw.ToObject<QueueTaskKickDto>();

          var success = _RustApp.CloseConnection(data.steam_id, data.reason);
          if (!success)
          {
            Error($"Не удалось кикнуть игрока {data.steam_id}, игрок не найден или оффлайн");
            return "Player not found or offline";
          }

          Debug($"Игрок {data.steam_id} кикнут по причине {data.reason}");

          // Perhaps one day, we’ll add a switch on the site that will allow users to kick with a message.
          if (data.announce)
          {
          }

          return true;
        }

        #endregion

        #region Ban

        private class QueueTaskBanDto
        {
          public string steam_id;
          public string name;
          public string reason;
        }

        private object RustApp_InternalQueue_Ban(JObject raw) {
          var data = raw.ToObject<QueueTaskBanDto>();

          var ignoreQueueBan = Interface.Oxide.CallHook("RustApp_CanIgnoreBan", data.steam_id);
          if (ignoreQueueBan != null)
          {
            return "An external plugin has overridden queue-ban (RustApp_CanIgnoreBan)";
          }

          // IP address is not relevant in this case
          _RustAppEngine.BanWorker?.CheckBans(data.steam_id, "1.1.1.1");
          
          if (!_Settings.ban_enable_broadcast)
          {
            return true;
          }

          foreach (var player in BasePlayer.activePlayerList)
          {
            var msg = _RustApp.lang.GetMessage("System.Ban.Broadcast", _RustApp, player.UserIDString).Replace("%TARGET%", data.name).Replace("%REASON%", data.reason);

            _RustApp.SendMessage(player, msg);
          }

          return true; 
        }

        #endregion

        #region NoticeStateGet

        private class QueueTaskNoticeStateGetDto
        {
          public string steam_id;
        }

        private object RustApp_InternalQueue_NoticeStateGet(JObject raw) {
          var data = raw.ToObject<QueueTaskNoticeStateGetDto>();
          if (_RustAppEngine.CheckWorker == null) {
            return false;
          }

          return _RustAppEngine.CheckWorker.IsNoticeActive(data.steam_id); 
        }

        #endregion

        #region NoticeStateSet

        private class QueueTaskNoticeStateSetDto
        {
          public string steam_id;
          public bool value;
        }

        private object RustApp_InternalQueue_NoticeStateSet(JObject raw) {
          var data = raw.ToObject<QueueTaskNoticeStateSetDto>();
          if (_RustAppEngine.CheckWorker == null) {
            return false;
          }

          _RustAppEngine.CheckWorker.SetNoticeActive(data.steam_id, data.value);

          return true; 
        }

        #endregion

        #region ChatMessage

        private class QueueTaskChatMessageDto
        {
          public string initiator_name;
          public string initiator_steam_id;

          [CanBeNull] public string target_steam_id;

          public string message;

          public string mode;
        }

        private object RustApp_InternalQueue_ChatMessage(JObject raw) {
          var data = raw.ToObject<QueueTaskChatMessageDto>();

          if (data.target_steam_id is string)
          {
            var player = BasePlayer.Find(data.target_steam_id);
            if (player == null || !player.IsConnected)
            {
              return "Player not found or offline";
            }

            var message = _RustApp.lang.GetMessage("System.Chat.Direct", _RustApp, data.target_steam_id)
              .Replace("%CLIENT_TAG%", data.initiator_name)
              .Replace("%MSG%", data.message);

            _RustApp.SendMessage(player, message);

            _RustApp.SoundToast(player, _RustApp.lang.GetMessage("Chat.Direct.Toast", _RustApp, player.UserIDString), SoundToastType.Error);
          }
          else
          {
            foreach (var player in BasePlayer.activePlayerList)
            {
              var message = _RustApp.lang.GetMessage("System.Chat.Global", _RustApp, player.UserIDString)
                .Replace("%CLIENT_TAG%", data.initiator_name)
                .Replace("%MSG%", data.message);

              _RustApp.SendMessage(player, message);
            }
          }

          return true; 
        }

        #endregion

        #region ExecuteCommand

        private class QueueTaskExecuteCommandDto
        {
          public List<string> commands;
        }

        private object RustApp_InternalQueue_ExecuteCommand(JObject raw) {
          var data = raw.ToObject<QueueTaskExecuteCommandDto>();

          var responses = new List<object>();

          for (var i = 0; i < data.commands.Count; i++) {
            var cmd = data.commands[i];

            var res = ConsoleSystem.Run(ConsoleSystem.Option.Server, cmd);

            try
            {
              responses.Add(new
              {
                success = true,
                command = cmd,
                data = JsonConvert.DeserializeObject(res?.ToString() ?? "Command without response")
              });
            }
            catch
            {
              responses.Add(new
              {
                success = true,
                command = cmd,
                data = res
              });
            }
          }

          return responses; 
        }

        #endregion

        #region DeleteEntity

        private class QueueTaskDeleteEntityDto
        {
          public string net_id;
        }

        private object RustApp_InternalQueue_DeleteEntity(JObject raw) {
          var data = raw.ToObject<QueueTaskDeleteEntityDto>();

          var ent = BaseNetworkable.serverEntities.ToList().Find(v => v.net.ID.Value.ToString() == data.net_id);
          if (ent == null)
          {
            return false;
          }

          ent.Kill();

          return true;
        }

        #endregion

        #region PaidAnnounceBan (deprecated) -> BanEventCreated
        
        private class QueueTaskPaidAnnounceBanDto
        {
          public bool broadcast = false;

          public string suspect_name;
          public string suspect_id;

          public string reason;

          public List<string> targets = new List<string>();
        }

        private object RustApp_InternalQueue_PaidAnnounceBan(JObject raw) {
          return this.RustApp_InternalQueue_BanEventCreated(raw); 
        }

        private object RustApp_InternalQueue_BanEventCreated(JObject raw) {
          var data = raw.ToObject<QueueTaskPaidAnnounceBanDto>();

          // TODO: Deprecated
          Interface.Oxide.CallHook("RustApp_OnPaidAnnounceBan", data.suspect_id, data.targets, data.reason);

          if (!data.broadcast)
          {
            return true;
          }

          foreach (var check in data.targets)
          {
            var player = BasePlayer.Find(check);
            if (player == null || !player.IsConnected)
            {
              continue;
            }

            var msg = _RustApp.lang.GetMessage("Paid.Announce.Ban", _RustApp, player.UserIDString)
              .Replace("%SUSPECT_NAME%", data.suspect_name)
              .Replace("%SUSPECT_ID%", data.suspect_id)
              .Replace("%REASON%", data.reason);

            _RustApp.SoundToast(player, msg, SoundToastType.Error);
          }

          return true;
        }

        #endregion

        #region PaidAnnounceClean (deprecated) -> CheckEventFinishedClean

    private class QueueTaskPaidAnnounceCleanDto
    {
      public bool broadcast = false;

      public string suspect_name;
      public string suspect_id;

      public List<string> targets = new List<string>();
    }

    private object RustApp_InternalQueue_PaidAnnounceClean(JObject raw) {
      return this.RustApp_InternalQueue_CheckEventFinishedClean(raw); 
    }

    private object RustApp_InternalQueue_CheckEventFinishedClean(JObject raw) {
      var data = raw.ToObject<QueueTaskPaidAnnounceCleanDto>();

      // TODO: Add support for last checks
      /**
      if (!_Checks.LastChecks.ContainsKey(payload.suspect_id))
      {
        _Checks.LastChecks.Add(payload.suspect_id, _RustApp.CurrentTime());
      }
      else
      {
        _Checks.LastChecks[payload.suspect_id] = _RustApp.CurrentTime();
      }*/

      // TODO: Deprecated
      Interface.Oxide.CallHook("RustApp_OnPaidAnnounceClean", data.suspect_id, data.targets);

      // TODO: Add support for last checks
      //CheckInfo.write(_Checks);

      if (!data.broadcast)
      {
        return true;
      }

      foreach (var check in data.targets)
      {
        var player = BasePlayer.Find(check);
        if (player == null || !player.IsConnected)
        { 
          continue;
        }

        var msg = _RustApp.lang.GetMessage("Paid.Announce.Clean", _RustApp, player.UserIDString)
          .Replace("%SUSPECT_NAME%", data.suspect_name)
          .Replace("%SUSPECT_ID%", data.suspect_id);

        _RustApp.SoundToast(player, msg, SoundToastType.Info);
      }

      return true;
    }

    #endregion

      #endregion

      #region Chat hooks

      #endregion

    #endregion

    #region Methods

    private void OnPlayerConnectedNormalized(string steamId, string ip) {
      _RustAppEngine.BanWorker?.CheckBans(steamId, ip);
    }

    private void OnPlayerDisconnectedNormalized(string steamId, string reason) {
      if (_RustAppEngine.StateWorker != null) {
        _RustAppEngine.StateWorker.DisconnectReasons[steamId] = reason;
      }

      _RustAppEngine.CheckWorker?.SetNoticeActive(steamId, false);
    }

    private void SetTeamChange(string initiatorSteamId, string targetSteamId) {
      if (_RustAppEngine.StateWorker != null) {
        _RustAppEngine.StateWorker.TeamChanges[initiatorSteamId] = targetSteamId;
      } 
    }

    private void RustAppEngineCreate() {
      var obj = ServerMgr.Instance.gameObject.CreateChild();

      _RustAppEngine = obj.AddComponent<RustAppEngine>();
    }

    private void RustAppEngineDestroy() {
      UnityEngine.Object.Destroy(_RustAppEngine.gameObject);
    }

    #endregion

    #region StableRequest

    public class StableRequest<T>
    {
      private static Dictionary<string, string> DefaultHeaders()
      {
        return new Dictionary<string, string>
        {
          ["x-plugin-auth"] = _MetaInfo?.Value ?? "",
          ["x-plugin-version"] = _RustApp.Version.ToString(),
          ["x-plugin-port"] = ConVar.Server.port.ToString()
        };
      }

      private string url;
      private RequestMethod method;

      private object data;
      public Dictionary<string, string> headers = new Dictionary<string, string>();

      public Action<T, string> onComplete;
      public Action<string> onException;

      public StableRequest(string url, RequestMethod method, object? data)
      {
        this.url = url;
        this.method = method;

        this.data = data;
        this.headers = DefaultHeaders();
      }

      public void Execute(Action<T, string> onComplete, Action<string> onException)
      {
        if (onComplete != null)
        {
          this.onComplete += onComplete;
        }
        if (onException != null)
        {
          this.onException += onException;
        }

        UnityWebRequest webRequest = null;

        var body = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data != null ? data : new { }));

        switch (method)
        {
          case RequestMethod.GET:
            {
              webRequest = UnityWebRequest.Get(url);
              break;
            }
          case RequestMethod.PUT:
            {
              webRequest = UnityWebRequest.Put(url, "{}");

              webRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(body);
              webRequest.uploadHandler.contentType = "application/json";
              break;
            }
          case RequestMethod.POST:
            {
              webRequest = UnityWebRequest.Post(url, "{}");

              webRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(body);
              webRequest.uploadHandler.contentType = "application/json";
              break;
            }
          case RequestMethod.DELETE:
            {
              webRequest = UnityWebRequest.Delete(url);

              webRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(body);
              webRequest.uploadHandler.contentType = "application/json";
              break;
            }
        }

        if (webRequest == null)
        {
          throw new Exception("bad.request.type");
        }

        if (headers != null)
        {
          foreach (var chunk in headers)
          {
            webRequest.SetRequestHeader(chunk.Key, chunk.Value);
          }
        }

        webRequest.SetRequestHeader("Content-Type", "application/json");

        webRequest.timeout = 10;

        Rust.Global.Runner.StartCoroutine(this.WaitForRequest(webRequest));
      }

      private IEnumerator WaitForRequest(UnityWebRequest request)
      {
        yield return request.Send();

        bool isError = request.isHttpError || request.isNetworkError || request.error != null;
        var body = (request.downloadHandler?.text ?? "possible network errors, contact @rustapp_help if you see > 5 minutes").ToLower();

        if (body.Length == 0)
        {
          body = "possible network errors, contact @rustapp_help if you see > 5 minutes";
        }

        if (isError)
        {
          if (body.Contains("502 bad gateway") || body.Contains("cloudflare"))
          {
            onException?.Invoke("rustapp is restarting, wait please");
          }
          else
          {
            onException?.Invoke(body);
          }
        }
        else
        {
          try
          {
            if (typeof(T) == typeof(String))
            {
              onComplete?.Invoke((T)(object)request.downloadHandler?.text, request.downloadHandler?.text);
            }
            else
            {
              var obj = JsonConvert.DeserializeObject<T>(request.downloadHandler?.text);

              onComplete?.Invoke(obj, request.downloadHandler?.text);
            }
          }
          catch (Exception parseException)
          {
            Interface.Oxide.LogError($"Не удалось разобрать ответ от сервера ({request.method.ToUpper()} {request.url}): {parseException} (Response: {request.downloadHandler?.text})");
          }
        }
      }
    }

    #endregion

    #region Utils

    private bool CheckRequiredPlugins() {
      if (plugins.Find("RustAppLite") != null && plugins.Find("RustAppLite").IsLoaded)
      {
        Error(
          "Обнаружена 'Lite' версия плагина, для начала удалите RustAppLite.cs"
        );
        return false;
      }

      if (plugins.Find("ImageLibrary") == null)
      {
        Error(
          "Для работы плагина необходим установленный ImageLibrary"
        );
        return false;
      }

      return true;
    }

    private static void Debug(string text) {
      _RustApp.Puts(text);
    }
    private static void Error(string text) {
      _RustApp.Puts(text);
    }

    private class RustAppWorker : MonoBehaviour {
      public void Awake() {
        Debug($"{this.GetType().Name} worker enabled");
      }

      public void OnDestroy() {
        Debug($"{this.GetType().Name} worker disabled");
      }
    }
    
    private enum SoundToastType {
      Info = 2,
      Error = 1
    }

    private void SoundToast(BasePlayer player, string text, SoundToastType type)
    {
      Effect effect = new Effect("assets/bundled/prefabs/fx/notice/item.select.fx.prefab", player, 0, new Vector3(), new Vector3());
      EffectNetwork.Send(effect, player.Connection);

      player.Command("gametip.showtoast", type, text, type);
    }

    // It is more optimized way to detect building authed instead of default BasePlayer.IsBuildingAuthed()
    private static bool DetectBuildingAuth(BasePlayer player)
    {
      var list = new List<BuildingPrivlidge>();

      Vis.Entities(player.transform.position, 16f, list, Layers.PreventBuilding);

      return list.FirstOrDefault()?.IsAuthed(player) ?? false;
    }

    private static bool DetectIsRaidBlock(BasePlayer player)
        {
          if (_RustApp == null || !_RustApp.IsLoaded)
          {
            return false;
          }

          var plugins = new List<Plugin> {
            _RustApp.NoEscape,
            _RustApp.RaidZone,
            _RustApp.RaidBlock,
            _RustApp.ExtRaidBlock
          };

          var correct = plugins.Find(v => v != null);
          if (correct != null)
          {
            try
            {
              switch (correct.Name)
              {
                case "NoEscape":
                  {
                    return (bool)correct.Call("IsRaidBlocked", player);
                  }
                case "RaidZone":
                  {
                    return (bool)correct.Call("HasBlock", player.userID.Get());
                  }
                case "ExtRaidBlock":
                  {
                    return (bool)correct.Call("IsRaidBlock", player.userID.Get());
                  }
                case "RaidBlock":
                  {
                    try
                    {
                      return (bool)correct.Call("IsInRaid", player);
                    }
                    catch
                    {
                      return (bool)correct.Call("IsRaidBlocked", player);
                    }
                  }
              }
            }
            catch
            {
              Debug("Не удалось вызвать API у RaidBlock'a");
            }
          }

          return false;
        }


        private static bool DetectNoLicense(Network.Connection connection)
        {
          if (_RustApp.MultiFighting != null && _RustApp.MultiFighting.IsLoaded) {
            try
            {
              var isSteam = (bool)_RustApp.MultiFighting.Call("IsSteam", connection);

              return !isSteam;
            }
            catch
            {
              return false;
            }
          }

          if (_RustApp.TirifyGamePluginRust != null && _RustApp.TirifyGamePluginRust.IsLoaded) {
            try
            {
              var isPlayerNoSteam = (bool)_RustApp.TirifyGamePluginRust.Call("IsPlayerNoSteam", connection.userid.ToString());

              return isPlayerNoSteam;
            }
            catch
            {
              return false;
            } 
          }

          return false;
        }

    private static string GridReference(Vector3 position)
    {
      var chars = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "AA", "AB", "AC", "AD", "AE", "AF", "AG", "AH", "AI", "AJ", "AK", "AL", "AM", "AN", "AO", "AP", "AQ", "AR", "AS", "AT", "AU", "AV", "AW", "AX", "AY", "AZ" };

      const float block = 146;

      float size = ConVar.Server.worldsize;
      float offset = size / 2;

      float xpos = position.x + offset;
      float zpos = position.z + offset;

      int maxgrid = (int)(size / block);

      float xcoord = Mathf.Clamp(xpos / block, 0, maxgrid - 1);
      float zcoord = Mathf.Clamp(maxgrid - (zpos / block), 0, maxgrid - 1);

      string pos = string.Concat(chars[(int)xcoord], (int)zcoord);

      return (pos);
    }

    private static void ResurrectDictionary<T, V>(Dictionary<T, V> oldDict, Dictionary<T, V> newDict) {
      foreach (var old in oldDict) {
        if (!newDict.ContainsKey(old.Key)) {
          newDict.Add(old.Key, old.Value);
        }
      }
    }

    private static void ResurrectList<T>(List<T> oldList, List<T> newList) {
      foreach (var old in oldList) {
        newList.Add(old);
      }
    }

    private static CourtApi.PluginStatePlayerMetaDto CollectPlayerMeta(string steamId, CourtApi.PluginStatePlayerMetaDto meta) {
      Interface.Oxide.CallHook("RustApp_CollectPlayerTags", steamId, meta.tags);
      Interface.Oxide.CallHook("RustApp_CollectPlayerFields", steamId, meta.fields);

      return meta;
    }

private bool CloseConnection(string steamId, string reason)
    {
      Debug(
        $"Закрываем соединение с {steamId}: {reason}"
      );

      var player = BasePlayer.Find(steamId);
      if (player != null && player.IsConnected)
      {
        player.Kick(reason);
        return true;
      }

      var connection = ConnectionAuth.m_AuthConnection.Find(v => v.userid.ToString() == steamId);
      if (connection != null)
      {
        Network.Net.sv.Kick(connection, reason);
        return true;
      }

      var loading = ServerMgr.Instance.connectionQueue.joining.Find(v => v.userid.ToString() == steamId);
      if (loading != null)
      {
        Network.Net.sv.Kick(loading, reason);
        return true;
      }

      var queued = ServerMgr.Instance.connectionQueue.queue.Find(v => v.userid.ToString() == steamId);
      if (queued != null)
      {
        Network.Net.sv.Kick(queued, reason);
        return true;
      }

      return false;
    }

    private void SendMessage(BasePlayer player, string message, string initiator_steam_id = "")
    {
      if (initiator_steam_id.Length == 0)
      {
        initiator_steam_id = _Settings.chat_default_avatar_steamid;
      }

      player.SendConsoleCommand("chat.add", 0, initiator_steam_id, message);
    }
    
    private T Reserialize<T>(JObject obj) {
      return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(obj));
    }

    public static string IPAddressWithoutPort(string ipWithPort)
    {
      int num = ipWithPort.LastIndexOf(':');
      if (num != -1)
      {
        return ipWithPort.Substring(0, num);
      }

      return ipWithPort;
    }

    #endregion
  }
}