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
  [Info("RustApp", "RustApp.io (Anchor-Team)", "2.0.0")]
  public class RustApp : RustPlugin
  {
    #region Variables

    // References to other plugin with API
    [PluginReference] private Plugin NoEscape, RaidZone, RaidBlock, MultiFighting, TirifyGamePluginRust, ExtRaidBlock;

    private static MetaInfo _MetaInfo = MetaInfo.Read();
    private static CheckInfo _CheckInfo = CheckInfo.Read();

    private static RustApp _RustApp;
    private static Configuration _Settings;

    private static RustAppEngine _RustAppEngine;

    #endregion

    #region Web API

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
    
      #region SendReports

      public class PluginReportDto
      {
        public string initiator_steam_id;
        [CanBeNull]
        public string target_steam_id;

        public List<string> sub_targets_steam_ids;

        public string reason;

        [CanBeNull]
        public string message;
      }

      public class PluginReportBatchPayload {
        public List<CourtApi.PluginReportDto> reports = new List<CourtApi.PluginReportDto>();
      }

      public static StableRequest<object> SendReports(PluginReportBatchPayload payload) {
        return new StableRequest<object>($"{BaseUrl}/plugin/reports", RequestMethod.POST, payload);
      }

      #endregion
    
      #region SendContact

      public static StableRequest<object> SendContact(string steamId, string contact) {
        return new StableRequest<object>($"{BaseUrl}/plugin/contact", RequestMethod.POST ,new { steam_id = steamId, message = contact });
      }

      #endregion
    
      #region SendWipe

      public static StableRequest<object> SendWipe() {
        return new StableRequest<object>($"{BaseUrl}/plugin/wipe", RequestMethod.POST, null);
      }

      #endregion
    
      #region BanCreate

      public class PluginBanCreatePayload {
        public string target_steam_id;
        public string reason;
        public bool global;
        public bool ban_ip;
        public string duration;
        public string comment;
      }

      public static StableRequest<object> BanCreate(PluginBanCreatePayload payload) {
        return new StableRequest<object>($"{BaseUrl}/plugin/ban", RequestMethod.POST, payload);
      }

      #endregion

      #region BanDelete

      public static StableRequest<object> BanDelete(string steamId) {
        return new StableRequest<object>($"{BaseUrl}/plugin/unban", RequestMethod.POST, new { target_steam_id = steamId });
      }

      #endregion
    
      #region PlayerAlert

      public static class PluginPlayerAlertType
      {
        public static readonly string join_with_ip_ban = "join_with_ip_ban";
        public static readonly string dug_up_stash = "dug_up_stash";
        public static readonly string custom_api = "custom_api";
      }

      public class PluginPlayerAlertDto
      {
        public string type;
        public object meta;
      }

      public class PluginPlayerAlertJoinWithIpBanMeta
      {
        public string steam_id;
        public string ip;
        public int ban_id;
      }

      public class PluginPlayerAlertDugUpStashMeta
      {
        public string steam_id;
        public string owner_steam_id;
        public string position;
        public string square;
      }

      public static StableRequest<object> CreatePlayerAlerts(List<PluginPlayerAlertDto> alerts) {
        return new StableRequest<object>($"{BaseUrl}/plugin/custom-alert", RequestMethod.POST, new { alerts });
      }

      #endregion

      #region PlayerAlertCustom

      public class PluginPlayerAlertCustomDto {
        public string msg;
        public object data;

        public string custom_icon;
        public bool hide_in_table = false;
        public string category;
        public List<string> custom_links = new List<string>();
      }
      
      public class PluginPlayerAlertCustomAlertMeta
      {
        public string name = "";
        public string custom_icon = null;
        public List<string> custom_links = null;
      }

      public static StableRequest<object> CreatePlayerAlertsCustom(PluginPlayerAlertCustomDto payload) {
        return new StableRequest<object>($"{BaseUrl}/plugin/custom-alert", RequestMethod.POST, payload);
      }

      #endregion
    
      #region SendSignage

      public class PluginSignageCreateDto {
        public string steam_id;
        public ulong net_id;

        public string base64_image;

        public string type;
        public string position;
        public string square;
      }

      public static StableRequest<object> SendSignage(PluginSignageCreateDto payload) {
        return new StableRequest<object>($"{BaseUrl}/plugin/signage", RequestMethod.POST, payload);
      }

      #endregion
    
      #region SendSignageDestroyed

      public class SignageDestroyedDto {
        public List<string> net_ids = new List<string>();
      }

      public static StableRequest<object> SendSignageDestroyed(SignageDestroyedDto payload) {
        return new StableRequest<object>($"{BaseUrl}/plugin/signage", RequestMethod.DELETE, payload);
      }

      #endregion
    
      #region SendKills

      public class PluginKillsDto {
        public List<PluginKillEntryDto> kills = new List<PluginKillEntryDto>();
      }

      public class PluginKillEntryDto
      {
        public string initiator_steam_id;
        public string target_steam_id;
        public string game_time;
        public float distance;
        public string weapon;

        public bool is_headshot;

        public List<CombatLogEventDto> hit_history = new List<CombatLogEventDto>();
      }

      public class CombatLogEventDto {
        public float time;

        public string attacker_steam_id;

        public string target_steam_id;

        public string attacker;

        public string target;

        public string weapon;

        public string ammo;

        public string bone;

        public float distance;

        public float hp_old;

        public float hp_new;

        public string info;

        public int proj_hits;

        public float pi;

        public float proj_travel;

        public float pm;

        public int desync;

        public bool ad;

        public CombatLogEventDto(CombatLog.Event ev) {
          if (ev.attacker == "player") {
            var attacker = BasePlayer.activePlayerList.FirstOrDefault(v => v.net.ID.Value == ev.attacker_id) ?? BasePlayer.sleepingPlayerList.FirstOrDefault(v => v.net.ID.Value == ev.attacker_id);

            this.attacker_steam_id = attacker?.UserIDString ?? "";
          }

          if (ev.target == "player") {
            var target = BasePlayer.activePlayerList.FirstOrDefault(v => v.net.ID.Value == ev.target_id) ?? BasePlayer.sleepingPlayerList.FirstOrDefault(v => v.net.ID.Value == ev.target_id);

            this.target_steam_id = target?.UserIDString ?? "";
          }

          this.time = UnityEngine.Time.realtimeSinceStartup - ev.time - ConVar.Server.combatlogdelay;
          this.attacker = ev.attacker;
          this.target = ev.target;
          this.weapon = ev.weapon;
          this.ammo = ev.ammo;
          this.bone = ev.bone;
          this.distance = (float) Math.Round(ev.distance, 2);
          this.hp_old = (float) Math.Round(ev.health_old, 2);
          this.hp_new = (float) Math.Round(ev.health_new, 2);
          this.info = ev.info;
          this.proj_hits = ev.proj_hits;
          this.proj_travel = ev.proj_travel;

          this.desync = ev.desync;

          this.pi = ev.proj_integrity;
          this.pm = ev.proj_mismatch;
          this.ad = ev.attacker_dead;
        }

        public string getInitiator() {
          if (this.attacker != "player") {
            return this.attacker;
          }

          return this.attacker_steam_id;
        }

        public string getTarget() {
          if (this.target != "player") {
            return this.target;
          }

          return this.target_steam_id;
        }
      }

      public static StableRequest<object> SendKills(PluginKillsDto payload) {
        return new StableRequest<object>($"{BaseUrl}/plugin/kills", RequestMethod.POST, payload);
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

    public class CheckInfo
    {
      public static CheckInfo Read()
      {
        if (!Interface.Oxide.DataFileSystem.ExistsDatafile("RustApp_CheckMeta"))
        {
          return new CheckInfo();
        }

        return Interface.Oxide.DataFileSystem.ReadObject<CheckInfo>("RustApp_CheckMeta");
      }

      public static void write(CheckInfo courtMeta)
      {
        // Clear checks > 30 days ago
        courtMeta.LastChecks = courtMeta.LastChecks.Where(v => _RustApp.CurrentTime() - v.Value < 30 * 24 * 60 * 60).ToDictionary(v => v.Key, v => v.Value);

        Interface.Oxide.DataFileSystem.WriteObject("RustApp_CheckMeta", courtMeta);
      }

      [JsonProperty("List of recent checks to show green-check on player")]
      public Dictionary<string, double> LastChecks = new Dictionary<string, double>();
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
      public ChatWorker? ChatWorker;
      public ReportWorker? ReportWorker;
      public PlayerAlertsWorker? PlayerAlertsWorker;
      public SignageWorker? SignageWorker;
      public KillsWorker? KillsWorker;
    
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
          Trace("Authed success, components enabled");

          CreateSubWorkers();
        };
 
        AuthWorker.OnAuthFailed += () => {
          Trace("Auth failed, components disabled");
         
          DestroySubWorkers();
        };

        AuthWorker.CycleAuthUpdate();
      }

      private void CreateSubWorkers() {
        ChildObjectToWorkers = this.gameObject.CreateChild();

        StateWorker = ChildObjectToWorkers.AddComponent<StateWorker>();
        CheckWorker = ChildObjectToWorkers.AddComponent<CheckWorker>();
        QueueWorker = ChildObjectToWorkers.AddComponent<QueueWorker>();
        ChatWorker = ChildObjectToWorkers.AddComponent<ChatWorker>();
        BanWorker = ChildObjectToWorkers.AddComponent<BanWorker>();
        ReportWorker = ChildObjectToWorkers.AddComponent<ReportWorker>();
        PlayerAlertsWorker = ChildObjectToWorkers.AddComponent<PlayerAlertsWorker>();
        SignageWorker = ChildObjectToWorkers.AddComponent<SignageWorker>();
        KillsWorker = ChildObjectToWorkers.AddComponent<KillsWorker>();
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
          if (IsAuthed == false) {
            return;
          }

          Error(error);

          IsAuthed = false;
          OnAuthFailed?.Invoke();
        };

        Action onSuccess = () => {
          if (IsAuthed == true) {
            return;
          }

          Log("Connection to the service established");

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

            Debug($"Unknown exception in auth: {err.Substring(0, 128)}");
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
                Error("Pairing failed: requested code not exists");
              } else {
                Debug($"Pairing failed: unknown exception {err}");
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
                Log("Complete pairing on site (press save button)...");
                return;
              }

              _RustApp.timer.Once(1f, () => _RustAppEngine.AuthWorker.CheckAuthStatus());
              MetaInfo.write(new MetaInfo { Value = data.token });

              Log("Pairing completed, reloading...");
              
              Destroy(this);
            },
            (err) => {
              if (Api.ErrorContains(err, "code not exists")) {
                Error("Pairing failed: seems you closed modal on site");
              } else {
                Error($"Pairing failed: unknown exception {err}");
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
            (data, raw) => { 
              Trace("State was sent successfull");
            },
            (err) => {
              Debug($"State sent error: {err}");

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

        if (player != null) {
          if (value) {
            _RustApp.DrawNoticeInterface(player);
          } else {
            CuiHelper.DestroyUi(player, CheckLayer);
          }
        }
      }

      public void OnDestroy() {
        base.OnDestroy();

        foreach (var check in ShowedNoticyCache) {
          if (check.Value == false) {
            continue;
          }

          SetNoticeActive(check.Key, false);
        }
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
              Debug($"Queue retreive failed {error}");
            }
          );
      }

      private void CallQueueTasks(List<QueueApi.QueueTaskResponse> queuesTasks) {
        if (queuesTasks.Count == 0) {
          return;
        }

        Dictionary<string, object> queueResponses = new Dictionary<string, object>();

        foreach (var task in queuesTasks) {
          if (QueueProcessedIds.Contains(task.id)) {
            Debug($"This task was already processed: {task.id}");
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
            Error($"Failed to process task {task.id} {task.request.name}: {exc.ToString()}");

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

              Trace("Ответ по очередям успешно доставлен");
            },
            (err) => {
              QueueProcessedIds = new List<string>();

              Debug($"Failed to process queue: {err}");
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
          return;
        }

        CycleBanUpdateWrapper((steamId, ban) => {
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
        if (BanUpdateQueue.Count == 0) {
          return;
        }

        var payload = new BanApi.BanGetBatchPayload { players = BanUpdateQueue.ToList() };

        BanUpdateQueue = new List<BanApi.BanGetBatchEntryPayloadDto>();

        BanApi.BanGetBatch(payload)
          .Execute(
            (data, raw) => {
              payload.players.ForEach(originalPlayer => {
                var exists = data.entries.Find(banPlayer => banPlayer.steam_id == originalPlayer.steam_id);

                var ban = exists?.bans.FirstOrDefault(v => v.computed_is_active);

                callback.Invoke(originalPlayer.steam_id, ban);
              });
            },
            (err) => {
              ResurrectList(payload.players, BanUpdateQueue);
            
              Error($"Failed to process ban checks ({payload.players.Count}), retrying...");
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

        _RustAppEngine.PlayerAlertsWorker?.SavePlayerAlert(new CourtApi.PluginPlayerAlertDto
        {
          type = CourtApi.PluginPlayerAlertType.join_with_ip_ban,
          meta = new CourtApi.PluginPlayerAlertJoinWithIpBanMeta
          {
            steam_id = steamId,
            ip = ban.ban_ip,
            ban_id = ban.id
          }
        });
      }
    }

    private class ChatWorker : RustAppWorker {
      private List<CourtApi.PluginChatMessageDto> QueueMessages = new List<CourtApi.PluginChatMessageDto>();
    
      private void Awake() {
        base.Awake();

        InvokeRepeating(nameof(SendChatMessages), 0f, 1f);
      }

      public void SaveChatMessage(CourtApi.PluginChatMessageDto message) {
        QueueMessages.Add(message);
      } 

      private void SendChatMessages() {
        if (QueueMessages.Count == 0) {
          return;
        }

        var payload = new CourtApi.PluginChatMessagePayload { messages = QueueMessages.ToList() };

        QueueMessages = new List<CourtApi.PluginChatMessageDto>();

        CourtApi.SendChatMessages(payload)
          .Execute(
            (data, raw) => {},
            (error) => {
              ResurrectList(payload.messages, QueueMessages);
            }
          );
      }
    }

    private class ReportWorker : RustAppWorker {
      public Dictionary<ulong, double> ReportCooldowns = new Dictionary<ulong, double>();
      private List<CourtApi.PluginReportDto> QueueReportSend = new List<CourtApi.PluginReportDto>();

      public void Awake() {
        base.Awake();

        InvokeRepeating(nameof(CycleReportSend), 0f, 1f);
      }

      public void SendReport(CourtApi.PluginReportDto report) {
        QueueReportSend.Add(report);
      }

      private void CycleReportSend() {
        if (QueueReportSend.Count == 0) {
          return;
        }

        var payload = new CourtApi.PluginReportBatchPayload { reports = QueueReportSend.ToList() };

        QueueReportSend = new List<CourtApi.PluginReportDto>();

        CourtApi.SendReports(payload)
          .Execute(
            (data, raw) => {},
            (error) => {
              ResurrectList(payload.reports, QueueReportSend);
            }
          );
      }
    }

    private class PlayerAlertsWorker : RustAppWorker {
      public List<CourtApi.PluginPlayerAlertDto> PlayerAlertQueue = new List<CourtApi.PluginPlayerAlertDto>();

      private void Awake() {
        base.Awake();

        InvokeRepeating(nameof(CycleSendPlayerAlerts), 0f, 5f);
      }

      public void SavePlayerAlert(CourtApi.PluginPlayerAlertDto alert) {
        PlayerAlertQueue.Add(alert);
      }

      private void CycleSendPlayerAlerts() {
        if (PlayerAlertQueue.Count == 0) {
          return;
        }

        var alerts = PlayerAlertQueue.ToList();

        PlayerAlertQueue = new List<CourtApi.PluginPlayerAlertDto>();

        CourtApi.CreatePlayerAlerts(alerts)
          .Execute(
            (data, raw) => {},
            (error) => {
              ResurrectList(alerts, PlayerAlertQueue);
            } 
          );
      }
    }

    private class SignageWorker : RustAppWorker {
      private List<string> DestroyedSignagesQueue = new List<string>();

      private void Awake() {
        base.Awake();
      
        InvokeRepeating(nameof(CycleSendUpdate), 5f, 5f);
      }

      public void AddSignageDestroy(string netId) {
        DestroyedSignagesQueue.Add(netId);
      }

      public void SignageCreate(BaseImageUpdate update) {
        var obj = new CourtApi.PluginSignageCreateDto
        {
          steam_id = update.PlayerId.ToString(),
          net_id = update.Entity.net.ID.Value,

          base64_image = Convert.ToBase64String(update.GetImage()),

          type = update.Entity.ShortPrefabName,
          position = update.Entity.transform.position.ToString(),
          square = GridReference(update.Entity.transform.position)
        };

        CourtApi.SendSignage(obj)
          .Execute(
            (data, raw) => {},
            (error) => {}
          );
      }

      private void CycleSendUpdate() {
        if (DestroyedSignagesQueue.Count == 0) {
          return;
        }

        var payload = new CourtApi.SignageDestroyedDto { net_ids = DestroyedSignagesQueue.ToList() };

        DestroyedSignagesQueue = new List<string>();

        CourtApi.SendSignageDestroyed(payload)
          .Execute(
            (data, raw) => {},
            (error) => {
              ResurrectList(payload.net_ids, DestroyedSignagesQueue);
            } 
          );
      }
    }

    private class KillsWorker : RustAppWorker {
      public Dictionary<string, HitInfo> WoundedHits = new Dictionary<string, HitInfo>();
      public List<CourtApi.PluginKillEntryDto> KillsQueue = new List<CourtApi.PluginKillEntryDto>();
      
      private void Awake() {
        base.Awake();
      
        InvokeRepeating(nameof(CycleSendKills), 5f, 5f);
      }

      public void AddKill(CourtApi.PluginKillEntryDto data) {
        KillsQueue.Add(data);
      }

      private void CycleSendKills() {
        if (KillsQueue.Count == 0) {
          return;
        }

        var payload = new CourtApi.PluginKillsDto { kills = KillsQueue.ToList() };

        KillsQueue = new List<CourtApi.PluginKillEntryDto>();

        CourtApi.SendKills(payload)
          .Execute(
            (data, raw) => {},
            (error) => {
              ResurrectList(payload.kills, KillsQueue);
            }
          );
      }
    }

    #endregion

    #region Commands

    private void CmdSendContact(BasePlayer player, string contact, string[] args) {
      if (args.Length == 0)
      {
        SendMessage(player, lang.GetMessage("Contact.Error", this, player.UserIDString));
        return;
      }

      CourtApi.SendContact(player.UserIDString, String.Join(" ", args))
        .Execute(
          (data, raw) => {
            SendMessage(player, lang.GetMessage("Contact.Sent", this, player.UserIDString) + $"<color=#8393cd> {String.Join(" ", args)}</color>");
            SendMessage(player, lang.GetMessage("Contact.SentWait", this, player.UserIDString));
          },
          (error) => {}
        );
    }

    private void CmdChatReportInterface(BasePlayer player) {
      if (plugins.Find("ImageLibrary") == null)
      {
        Error("To use plugin report-UI you need to install ImageLibrary");
        Error("https://umod.org/plugins/image-library");
        return;
      }

      DrawReportInterface(player);
    }

    [ConsoleCommand("ra.pair")]
    private void StartPairing(ConsoleSystem.Arg args) {
      if (args.Player() != null || args.Args.Length == 0) {
        return;
      }

      var code = args.Args[0];

      _RustAppEngine.gameObject.AddComponent<PairWorker>().StartPairing(code);
    }

    [ConsoleCommand("ra.ban")]
    private void CmdConsoleBan(ConsoleSystem.Arg args)
    {
      if (args.Player() != null && !args.Player().IsAdmin)
      {
        return;
      }

      var clearArgs = (args.Args ?? new string[0]).Where(v => v != "--ban-ip" && v != "--global").ToList();

      if (clearArgs.Count() < 2)
      {
        Error("Incorrect command format!\nCorrect format: ra.ban <steam-id> <reason> <time (optional)>\n\nAdditional options are available:\n'--ban-ip' - bans IP\n'--global' - bans globally\n\nExample of banning with IP, globally: ra.ban 7656119812110397 \"cheat\" 7d --ban-ip --global");
        return;
      }

      var steam_id = clearArgs[0];
      var reason = clearArgs[1];
      var duration = clearArgs.Count() == 3 ? clearArgs[2] : "";

      var global_bool = args.FullString.Contains("--global");
      var ip_bool = args.FullString.Contains("--ban-ip");

      BanCreate(steam_id, new CourtApi.PluginBanCreatePayload {
          target_steam_id = steam_id,
          reason = reason,
          global = global_bool,
          ban_ip = ip_bool,
          duration = duration.Length > 0 ? duration : null,
          comment = "Ban via console"
      });
    }

    [ConsoleCommand("ra.unban")]
    private void CmdConsoleBanDelete(ConsoleSystem.Arg args)
    {
      if (args.Player() != null && !args.Player().IsAdmin)
      {
        return;
      }

      var clearArgs = (args.Args ?? new string[0]).ToList();

      if (clearArgs.Count() != 1)
      {
        Error("Incorrect command format!\nCorrect format: ra.unban <steam-id>");
        return;
      }

      var steam_id = clearArgs[0];

      BanDelete(steam_id);
    }

    #endregion

    #region Hooks

      #region System hooks

      private void OnServerInitialized() { 
        _RustApp = this;

        if (!CheckRequiredPlugins()) {
          Error("Fix pending errors, and use 'o.reload RustApp'");
          return;
        }

        timer.Once(1f, () => {
          MetaInfo.Read(); 

          RustAppEngineCreate();
          RegisterCommands(); 
        });
      }

      private void Unload() {
        _RustAppEngine.StateWorker?.SendUpdate(true);

        RustAppEngineDestroy();
        DestroyAllUi();
      }

      
    private void OnNewSave(string saveName)
    {
      // Remove in 5 minutes
      timer.Once(300, () =>
      {
        CourtApi.SendWipe().Execute(
          (data, raw) => {},
          (error) => {}
        );
      });
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

      #region Chat hooks

      private void OnPlayerChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel)
      {
        if (channel != ConVar.Chat.ChatChannel.Team && channel != ConVar.Chat.ChatChannel.Global && channel != ConVar.Chat.ChatChannel.Local)
        {
          return;
        }

        _RustAppEngine.ChatWorker?.SaveChatMessage(new CourtApi.PluginChatMessageDto
        {
          steam_id = player.UserIDString,

          is_team = channel == ConVar.Chat.ChatChannel.Team,

          text = message
        });
      }

      #endregion

      #region Report hooks
    
      private void OnPlayerReported(BasePlayer reporter, string targetName, string targetId, string subject, string message, string type)
      {
        if (!_Settings.report_ui_auto_parse)
        {
          return;
        }

        var target = BasePlayer.Find(targetId) ?? BasePlayer.FindSleeping(targetId);
        if (target == null)
        {
          return;
        }

        RA_ReportSend(reporter.UserIDString, targetId, type, message);
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
            return "Player not found or offline";
          }

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

      if (!_CheckInfo.LastChecks.ContainsKey(data.suspect_id))
      {
        _CheckInfo.LastChecks.Add(data.suspect_id, _RustApp.CurrentTime());
      }
      else
      {
        _CheckInfo.LastChecks[data.suspect_id] = _RustApp.CurrentTime();
      }

      Interface.Oxide.CallHook("RustApp_OnPaidAnnounceClean", data.suspect_id, data.targets);

      CheckInfo.write(_CheckInfo);

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

      #region Alert hooks

      private void OnStashExposed(StashContainer stash, BasePlayer player)
      {
        if (stash == null)
        {
          return;
        }

        var team = player.Team;
        if (team != null)
        {
          if (team.members.Contains(stash.OwnerID))
          {
            return;
          }
        }

        var owner = stash.OwnerID;

        if (player.userID == stash.OwnerID || owner == 0)
        {
          return;
        }

        _RustAppEngine.PlayerAlertsWorker?.SavePlayerAlert(new CourtApi.PluginPlayerAlertDto
        {
          type = CourtApi.PluginPlayerAlertType.dug_up_stash,
          meta = new CourtApi.PluginPlayerAlertDugUpStashMeta
          {
            owner_steam_id = owner.ToString(),
            position = player.transform.position.ToString(),
            square = GridReference(player.transform.position),
            steam_id = player.UserIDString
          }
        });
      }

      #endregion

      #region SignageHooks

      private void OnEntityKill(BaseNetworkable entity)
      {
        if (entity?.net?.ID == null || entity?.net?.ID.Value == null || entity?.ShortPrefabName == null)
        {
          return;
        }

        var whiteList = new List<string> { "photoframe", "spinner.wheel" };

        if (!entity.ShortPrefabName.StartsWith("sign.") || whiteList.Any(v => entity.ShortPrefabName.Contains(v)))
        {
          return;
        }

        _RustAppEngine.SignageWorker?.AddSignageDestroy(entity.net.ID.Value.ToString());
      }

      #endregion

      #region Kills

      private void OnPlayerWound( BasePlayer instance, HitInfo info )
      {
        if (_RustAppEngine.KillsWorker == null) {
          return;
        }

        _RustAppEngine.KillsWorker.WoundedHits[instance.UserIDString] = info;
      }
      
      private void OnPlayerRespawn( BasePlayer instance )
      {
        if (_RustAppEngine.KillsWorker == null) {
          return;
        }

        if (_RustAppEngine.KillsWorker.WoundedHits.ContainsKey(instance.UserIDString)) {
          _RustAppEngine.KillsWorker.WoundedHits.Remove(instance.UserIDString);
        }
      }

      void OnPlayerRecovered(BasePlayer player)
      {
        if (_RustAppEngine.KillsWorker == null) {
          return;
        }

        if (_RustAppEngine.KillsWorker.WoundedHits.ContainsKey(player.UserIDString)) {
          _RustAppEngine.KillsWorker.WoundedHits.Remove(player.UserIDString);
        }
      }

      private void OnPlayerDeath( BasePlayer player, HitInfo infos )
      {
        if (infos?.InitiatorPlayer == null || player == null) {
          return;
        }
    
        var trueInfo = GetRealInfo(player, infos);

        var targetId = player.UserIDString;
        var initiatorId = trueInfo.InitiatorPlayer?.userID;
        if (trueInfo == null || initiatorId == player.userID || trueInfo.InitiatorPlayer == null || initiatorId == null) { 
          return;
        }

        var initiatorIdString = initiatorId.ToString();
        var distance = trueInfo?.ProjectileDistance ?? 0;
        var time = UnityEngine.Time.realtimeSinceStartup + 1;

        var weapon = "unknown";

        try {
          weapon = trueInfo?.Weapon?.name ?? trueInfo?.WeaponPrefab?.name ?? "unknown";
        }
        catch {
        }

        timer.Once(ConVar.Server.combatlogdelay + 1, () =>
        {
          try {
            var log = GetCorrectCombatlog(player.userID, time);
          
            _RustAppEngine.KillsWorker?.AddKill(new CourtApi.PluginKillEntryDto {
              initiator_steam_id = initiatorIdString,
              target_steam_id = targetId,
              distance = distance,
              game_time = Env.time.ToTimeSpan().ToShortString(),
              hit_history = log,  
              is_headshot = trueInfo?.isHeadshot ?? false,
              weapon = weapon
            });
          } 
          catch (Exception exc) {
            //Error("Обнаружена ошибка в бета-алгоритме, сообщите разработчикам", "Detect error in beta-mechanism");
            //PrintError(exc.ToString());
          }
        });
      }

      #endregion

    #endregion

    #region Interface

    private static string ReportLayer = "UI_RP_ReportPanelUI";
    
    private void DrawReportInterface(BasePlayer player, int page = 0, string search = "", bool redraw = false)
    {
      var lineAmount = 6;
      var lineMargin = 8;

      var size = (float)(700 - lineMargin * lineAmount) / lineAmount;

      var list = BasePlayer.activePlayerList
          .ToList(); 
 
      var finalList = list
          .FindAll(v => v.displayName.ToLower().Contains(search) || v.UserIDString.ToLower().Contains(search) || search == null);

      finalList = finalList
          .Skip(page * 18)
          .Take(18)
          .ToList();

      if (finalList.Count() == 0)
      {
        if (search == null)
        {
          DrawReportInterface(player, page - 1);
          return;
        }
      }

      CuiElementContainer container = new CuiElementContainer();

      if (!redraw)
      {
        container.Add(new CuiPanel
        {
          CursorEnabled = true,
          RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
          Image = { Color = "0 0 0 0.8", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" }
        }, "Overlay", ReportLayer, ReportLayer);

        container.Add(new CuiButton()
        {
          RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
          Button = { Color = HexToRustFormat("#343434"), Sprite = "assets/content/ui/ui.background.transparent.radial.psd", Close = ReportLayer },
          Text = { Text = "" }
        }, ReportLayer);
      }

      container.Add(new CuiPanel
      {
        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-368 -200", OffsetMax = "368 142" },
        Image = { Color = "1 0 0 0" }
      }, ReportLayer, ReportLayer + ".C", ReportLayer + ".C");

      container.Add(new CuiPanel
      {
        RectTransform = { AnchorMin = "1 0", AnchorMax = "1 1", OffsetMin = "-36 0", OffsetMax = "0 0" },
        Image = { Color = "0 0 1 0" }
      }, ReportLayer + ".C", ReportLayer + ".R");
      
      container.Add(new CuiButton()
      {
        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.5", OffsetMin = "0 0", OffsetMax = "0 -4" },
        Button = { 
          Color = HexToRustFormat($"#{(list.Count > 18 && finalList.Count() == 18 ? "D0C6BD4D" : "D0C6BD33")}"), 
          Command = UICommand((player, args, input) => {
            DrawReportInterface(player, args.page, args.search, true);
          }, new { search = search, page = list.Count > 18 && finalList.Count() == 18 ? page + 1 : page }, "nextPageGo")
        },
        Text = { Text = "↓", Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf", FontSize = 24, Color = HexToRustFormat($"{(list.Count > 18 && finalList.Count() == 18 ? "D0C6BD" : "D0C6BD4D")}") }
      }, ReportLayer + ".R", ReportLayer + ".RD");

      container.Add(new CuiButton()
      {
        RectTransform = { AnchorMin = "0 0.5", AnchorMax = "1 1", OffsetMin = "0 4", OffsetMax = "0 0" },
        Button = { 
          Color = HexToRustFormat($"#{(page == 0 ? "D0C6BD33" : "D0C6BD4D")}"), 
          Command = UICommand((player, args, input) => {
            DrawReportInterface(player, args.page, args.search, true);
          }, new { search = search, page = page == 0 ? 0 : page - 1 }, "prevPageGo")
        },
        Text = { Text = "↑", Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf", FontSize = 24, Color = HexToRustFormat($"{(page == 0 ? "D0C6BD4D" : "D0C6BD")}") }
      }, ReportLayer + ".R", ReportLayer + ".RU");

      container.Add(new CuiPanel
      {
        RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-250 8", OffsetMax = "0 43" },
        Image = { Color = HexToRustFormat("#D0C6BD33") }
      }, ReportLayer + ".C", ReportLayer + ".S");

      var searchCommand = UICommand((player, args, input) => {
        DrawReportInterface(player, 0, input, true);
      }, new {}, "searchForPlayer");

      container.Add(new CuiElement
      {
        Parent = ReportLayer + ".S",
        Components =
          {
              new CuiInputFieldComponent { 
                Text = $"{lang.GetMessage("Header.Search.Placeholder", this, player.UserIDString)}", 
                FontSize = 14, 
                Font = "robotocondensed-regular.ttf", 
                Color = HexToRustFormat("#D0C6BD80"), 
                Align = TextAnchor.MiddleLeft, 
                Command = searchCommand, 
                NeedsKeyboard = true
              },
              new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 0", OffsetMax = "-85 0"}
          }
      });

      container.Add(new CuiButton
      {
        RectTransform = { AnchorMin = "1 0", AnchorMax = "1 1", OffsetMin = "-75 0", OffsetMax = "0 0" },
        Button = { Color = HexToRustFormat("#D0C6BD"), Material = "assets/icons/greyout.mat" },
        Text = { Text = $"{lang.GetMessage("Header.Search", this, player.UserIDString)}", Font = "robotocondensed-bold.ttf", Color = HexToRustFormat("#443F3B"), FontSize = 14, Align = TextAnchor.MiddleCenter }
      }, ReportLayer + ".S", ReportLayer + ".SB");

      container.Add(new CuiPanel
      {
        RectTransform = { AnchorMin = "0 1", AnchorMax = "0.5 1", OffsetMin = "0 7", OffsetMax = "0 47" },
        Image = { Color = "0.8 0.8 0.8 0" }
      }, ReportLayer + ".C", ReportLayer + ".LT");

      container.Add(new CuiLabel()
      {
        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
        Text = { Text = $"{lang.GetMessage("Header.Find", this, player.UserIDString)} {(search != null && search.Length > 0 ? $"- {(search.Length > 20 ? search.Substring(0, 14).ToUpper() + "..." : search.ToUpper())}" : "")}", Font = "robotocondensed-bold.ttf", Color = HexToRustFormat("#D0C6BD"), FontSize = 24, Align = TextAnchor.UpperLeft }
      }, ReportLayer + ".LT");

      container.Add(new CuiLabel()
      {
        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
        Text = { Text = search == null || search.Length == 0 ? lang.GetMessage("Header.SubDefault", this, player.UserIDString) : finalList.Count() == 0 ? lang.GetMessage("Header.SubFindEmpty", this, player.UserIDString) : lang.GetMessage("Header.SubFindResults", this, player.UserIDString), Font = "robotocondensed-regular.ttf", Color = HexToRustFormat("#D0C6BD4D"), FontSize = 14, Align = TextAnchor.LowerLeft }
      }, ReportLayer + ".LT");

      container.Add(new CuiPanel
      {
        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "-40 0" },
        Image = { Color = "0 1 0 0" }
      }, ReportLayer + ".C", ReportLayer + ".L");

      for (var y = 0; y < Math.Max((int) Math.Ceiling((float) finalList.Count / lineAmount), 3); y++)
      {
        for (var x = 0; x < 6; x++)
        {
          var target = finalList.ElementAtOrDefault(y * 6 + x);
          if (target)
          {
            container.Add(new CuiPanel
            {
              RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x * size + lineMargin * x} -{(y + 1) * size + lineMargin * y}", OffsetMax = $"{(x + 1) * size + lineMargin * x} -{y * size + lineMargin * y}" },
              Image = { Color = HexToRustFormat("#D0C6BD33") }
            }, ReportLayer + ".L", ReportLayer + $".{target.UserIDString}");

            container.Add(new CuiElement
            {
              Parent = ReportLayer + $".{target.UserIDString}",
              Components =
              {
                new CuiRawImageComponent { Png = (string) plugins.Find("ImageLibrary").Call("GetImage", target.UserIDString), Sprite = "assets/icons/loading.png" },
                new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" }
              }
            });

            container.Add(new CuiPanel()
            {
              RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
              Image = { Sprite = "assets/content/ui/ui.background.transparent.linear.psd", Color = HexToRustFormat("#282828f2") }
            }, ReportLayer + $".{target.UserIDString}");

            string normaliseName = NormalizeString(target.displayName);

            string name = normaliseName.Length > 14 ? normaliseName.Substring(0, 15) + ".." : normaliseName;

            container.Add(new CuiLabel
            {
              RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "6 16", OffsetMax = "0 0" },
              Text = { Text = name, Align = TextAnchor.LowerLeft, Font = "robotocondensed-bold.ttf", FontSize = 13, Color = HexToRustFormat("#D0C6BD") }
            }, ReportLayer + $".{target.UserIDString}");

            container.Add(new CuiLabel
            {
              RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "6 5", OffsetMax = "0 0" },
              Text = { Text = target.UserIDString, Align = TextAnchor.LowerLeft, Font = "robotocondensed-regular.ttf", FontSize = 10, Color = HexToRustFormat("#D0C6BD80") }
            }, ReportLayer + $".{target.UserIDString}"); 

            var min = $"{x * size + lineMargin * x} -{(y + 1) * size + lineMargin * y}";
            var max = $"{(x + 1) * size + lineMargin * x} -{y * size + lineMargin * y}";

            var showPlayerCommand = UICommand((player, args, input) => {
              DrawPlayerReportReasons(player, args.steam_id, args.min, args.max, args.left);
            }, new { steam_id = target.UserIDString, min, max, left = x >= 3 }, "showPlayerReportReasons");

            container.Add(new CuiButton()
            {
              RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
              Button = { Color = "0 0 0 0", Command = showPlayerCommand },
              Text = { Text = "" }
            }, ReportLayer + $".{target.UserIDString}");

            var was_checked = _CheckInfo.LastChecks.ContainsKey(target.UserIDString) && CurrentTime() - _CheckInfo.LastChecks[target.UserIDString] < _Settings.report_ui_show_check_in * 24 * 60 * 60;
            if (was_checked)
            {
              container.Add(new CuiPanel
              {
                RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "5 -25", OffsetMax = "-5 -5" },
                Image = { Color = "0.239 0.568 0.294 1", Material = "assets/icons/greyout.mat" },
              }, ReportLayer + $".{target.UserIDString}", ReportLayer + $".{target.UserIDString}.Recent");

              container.Add(new CuiLabel
              {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
                Text = { Text = lang.GetMessage("UI.CheckMark", this, player.UserIDString), Align = TextAnchor.MiddleCenter, Font = "robotocondensed-regular.ttf", FontSize = 12, Color = "0.639 0.968 0.694 1" }
              }, ReportLayer + $".{target.UserIDString}.Recent");
            }
          }
          else
          {
            container.Add(new CuiPanel
            {
              RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x * size + lineMargin * x} -{(y + 1) * size + lineMargin * y}", OffsetMax = $"{(x + 1) * size + lineMargin * x} -{y * size + lineMargin * y}" },
              Image = { Color = HexToRustFormat("#D0C6BD33") }
            }, ReportLayer + ".L");
          }
        }
      }

      CuiHelper.AddUi(player, container);
    }

    private void DrawPlayerReportReasons(BasePlayer player, string targetId, string min, string max, bool leftAlign) {
      BasePlayer target = BasePlayer.Find(targetId) ?? BasePlayer.FindSleeping(targetId);

      Effect effect = new Effect("assets/prefabs/tools/detonator/effects/unpress.prefab", player, 0, new Vector3(), new Vector3());
      EffectNetwork.Send(effect, player.Connection);

      CuiElementContainer container = new CuiElementContainer();
      CuiHelper.DestroyUi(player, ReportLayer + $".T");

      container.Add(new CuiPanel
      {
        RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = min, OffsetMax = max },
        Image = { Color = "0 0 0 1" }
      }, ReportLayer + $".L", ReportLayer + $".T");


      container.Add(new CuiButton()
      {
        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"-500 -500", OffsetMax = $"500 500" },
        Button = { Close = $"{ReportLayer}.T", Color = "0 0 0 1", Sprite = "assets/content/ui/ui.circlegradient.png" }
      }, ReportLayer + $".T");


      container.Add(new CuiButton()
      {
        RectTransform = { AnchorMin = $"{(leftAlign ? -1 : 2)} 0", AnchorMax = $"{(leftAlign ? -2 : 3)} 1", OffsetMin = $"-500 -500", OffsetMax = $"500 500" },
        Button = { Close = $"{ReportLayer}.T", Color = HexToRustFormat("#343434"), Sprite = "assets/content/ui/ui.circlegradient.png" }
      }, ReportLayer + $".T");

      container.Add(new CuiButton()
      {
        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"-1111111 -1111111", OffsetMax = $"1111111 1111111" },
        Button = { Close = $"{ReportLayer}.T", Color = "0 0 0 0.5", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" }
      }, ReportLayer + $".T");


      container.Add(new CuiLabel
      {
        RectTransform = { AnchorMin = $"{(leftAlign ? "0" : "1")} 0", AnchorMax = $"{(leftAlign ? "0" : "1")} 1", OffsetMin = $"{(leftAlign ? "-350" : "20")} 0", OffsetMax = $"{(leftAlign ? "-20" : "350")} -5" },
        Text = { FadeIn = 0.4f, Text = lang.GetMessage("Subject.Head", this, player.UserIDString), Font = "robotocondensed-bold.ttf", Color = HexToRustFormat("#D0C6BD"), FontSize = 24, Align = leftAlign ? TextAnchor.UpperRight : TextAnchor.UpperLeft }
      }, ReportLayer + ".T");

      container.Add(new CuiLabel
      {
        RectTransform = { AnchorMin = $"{(leftAlign ? "0" : "1")} 0", AnchorMax = $"{(leftAlign ? "0" : "1")} 1", OffsetMin = $"{(leftAlign ? "-250" : "20")} 0", OffsetMax = $"{(leftAlign ? "-20" : "250")} -35" },
        Text = { FadeIn = 0.4f, Text = $"{lang.GetMessage("Subject.SubHead", this, player.UserIDString).Replace("%PLAYER%", $"<b>{target.displayName}</b>")}", Font = "robotocondensed-regular.ttf", Color = HexToRustFormat("#D0C6BD80"), FontSize = 14, Align = leftAlign ? TextAnchor.UpperRight : TextAnchor.UpperLeft }
      }, ReportLayer + ".T");

      container.Add(new CuiElement
      {
        Parent = ReportLayer + $".T",
        Components =
        {
            new CuiRawImageComponent { Png = (string) plugins.Find("ImageLibrary").Call("GetImage", target.UserIDString), Sprite = "assets/icons/loading.png" },
            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" }
        }
      });

      var was_checked = _CheckInfo.LastChecks.ContainsKey(target.UserIDString) && CurrentTime() - _CheckInfo.LastChecks[target.UserIDString] < _Settings.report_ui_show_check_in * 24 * 60 * 60;
      if (was_checked)
      {
        container.Add(new CuiPanel
        {
          RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "5 -25", OffsetMax = "-5 -5" },
          Image = { Color = "0.239 0.568 0.294 1", Material = "assets/icons/greyout.mat" },
        }, ReportLayer + $".T", ReportLayer + $".T.Recent");

        container.Add(new CuiLabel
        {
          RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
          Text = { Text = lang.GetMessage("UI.CheckMark", this, player.UserIDString), Align = TextAnchor.MiddleCenter, Font = "robotocondensed-regular.ttf", FontSize = 12, Color = "0.639 0.968 0.694 1" }
        }, ReportLayer + $".T.Recent");
      }

      for (var i = 0; i < _Settings.report_ui_reasons.Count; i++)
      {
        var offXMin = (20 + (i * 5)) + i * 80;
        var offXMax = 20 + (i * 5) + (i + 1) * 80;

        var sendReportCommand = UICommand((player, args, input) => {
          SendReport(player, args.target_id, args.reason);
        }, new { target_id = target.UserIDString, reason = _Settings.report_ui_reasons[i] }, "sendReportToPlayer");

        container.Add(new CuiButton()
        {
          RectTransform = { AnchorMin = $"{(leftAlign ? 0 : 1)} 0", AnchorMax = $"{(leftAlign ? 0 : 1)} 0", OffsetMin = $"{(leftAlign ? -offXMax : offXMin)} 15", OffsetMax = $"{(leftAlign ? -offXMin : offXMax)} 45" },
          Button = { FadeIn = 0.4f + i * 0.2f, Color = HexToRustFormat("#D0C6BD4D"), Command = sendReportCommand },
          Text = { FadeIn = 0.4f + i * 0.2f, Text = $"{_Settings.report_ui_reasons[i]}", Align = TextAnchor.MiddleCenter, Color = HexToRustFormat("#D0C6BD"), Font = "robotocondensed-bold.ttf", FontSize = 16 }
        }, ReportLayer + $".T");
      }

      CuiHelper.AddUi(player, container);
    }

    
    private const string CheckLayer = "RP_PrivateLayer";

    private void DrawNoticeInterface(BasePlayer player)
    {
      CuiHelper.DestroyUi(player, CheckLayer);
      CuiElementContainer container = new CuiElementContainer();

      container.Add(new CuiButton
      {
        RectTransform = { AnchorMin = "0 0.5", AnchorMax = "1 1", OffsetMin = $"-500 -500", OffsetMax = $"500 500" },
        Button = { Color = HexToRustFormat("#1C1C1C"), Sprite = "assets/content/ui/ui.circlegradient.png" },
        Text = { Text = "", Align = TextAnchor.MiddleCenter }
      }, "Under", CheckLayer);

      string text = lang.GetMessage("Check.Text", this, player.UserIDString);

      container.Add(new CuiLabel
      {
        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
        Text = { Text = text, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-regular.ttf", FontSize = 16 }
      }, CheckLayer);

      CuiHelper.AddUi(player, container);

      Effect effect = new Effect("ASSETS/BUNDLED/PREFABS/FX/INVITE_NOTICE.PREFAB".ToLower(), player, 0, new Vector3(), new Vector3());
      EffectNetwork.Send(effect, player.Connection);
    }
  
    #endregion

    #region Methods

    private List<CourtApi.CombatLogEventDto> GetCorrectCombatlog(ulong target, float timeLimit) {
      var allCombatlogs = CombatLog.Get(target);
      if (allCombatlogs == null || allCombatlogs.Count() == 0) {
        return null;
      } 

      var combatlog = allCombatlogs.Where(v => v.time < timeLimit).Reverse();
      if (combatlog.Count() == 0) {
        return null;
      } 

      var THRESHOLD_STREAK = 20;
      var THRESHOLD_MAX_LIMIT = 30;

      var container = new List<CourtApi.CombatLogEventDto>();

      CombatLog.Event previousEvent = combatlog.ElementAtOrDefault(0);
      
      foreach (var ev in combatlog) {
        var timeLeft = UnityEngine.Time.realtimeSinceStartup - ev.time - (float) ConVar.Server.combatlogdelay;

        if (ev.target != "player" && ev.target != "you") {
          continue;
        }

        if (ev.info == "killed" && Math.Abs(ev.time - timeLimit) > 1) {
          break;
        }

        var dto = new CourtApi.CombatLogEventDto(ev);

        if (Math.Abs(ev.time - previousEvent.time) < THRESHOLD_STREAK) {
          container.Add(dto);
        } else if (Math.Abs(ev.time - timeLimit) < THRESHOLD_MAX_LIMIT) {
          container.Add(dto);
        } else {
          break;
        }

        previousEvent = ev;
      }

      return container;
    }

    private HitInfo GetRealInfo(BasePlayer player, HitInfo info) {
      if (_RustAppEngine.KillsWorker == null) {
        return info;
      }

      var initiatorId = info.InitiatorPlayer?.userID;
      if (initiatorId == player.userID || info.InitiatorPlayer == null) { 
        if (_RustAppEngine.KillsWorker.WoundedHits.ContainsKey(player.UserIDString)) {
          return _RustAppEngine.KillsWorker.WoundedHits[player.UserIDString];
        }
      }

      return info;
    }

    private void DestroyAllUi() {
      foreach (var check in BasePlayer.activePlayerList) {
        CuiHelper.DestroyUi(check, CheckLayer);
        CuiHelper.DestroyUi(check, ReportLayer);
      }
    }

    private void SendReport(BasePlayer initiator, string targetId, string reason) {
      if (_RustAppEngine.ReportWorker == null) {
        CuiHelper.DestroyUi(initiator, ReportLayer);
        return;
      }

      if (!_RustAppEngine.ReportWorker.ReportCooldowns.ContainsKey(initiator.userID))
      {
        _RustAppEngine.ReportWorker.ReportCooldowns.Add(initiator.userID, 0);
      }

      if (_RustAppEngine.ReportWorker.ReportCooldowns[initiator.userID] > CurrentTime())
      {
        var msg = lang.GetMessage("Cooldown", this, initiator.UserIDString).Replace("%TIME%",
            $"{(_RustAppEngine.ReportWorker.ReportCooldowns[initiator.userID] - CurrentTime()).ToString("0")}");

        SoundToast(initiator, msg, SoundToastType.Error);
        return;
      }

      BasePlayer target = BasePlayer.Find(targetId) ?? BasePlayer.FindSleeping(targetId);

      RA_ReportSend(initiator.UserIDString, target.UserIDString, reason, "");
      CuiHelper.DestroyUi(initiator, ReportLayer);

      SoundToast(initiator, lang.GetMessage("Sent", this, initiator.UserIDString), SoundToastType.Info);

      if (!_RustAppEngine.ReportWorker.ReportCooldowns.ContainsKey(initiator.userID))
      {
        _RustAppEngine.ReportWorker.ReportCooldowns.Add(initiator.userID, 0);
      }

      _RustAppEngine.ReportWorker.ReportCooldowns[initiator.userID] = CurrentTime() + _Settings.report_ui_cooldown;
    }

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

    private void BanCreate(string steamId, CourtApi.PluginBanCreatePayload payload) {
      CourtApi.BanCreate(payload).Execute(
        (data, raw) => {
          Log($"Player {steamId} banned for {payload.reason}");
        },
        (err) => {
          Error($"Failed to ban {steamId}. Reason: {err}");
        }
      );
    }

    private void BanDelete(string steamId) {
      CourtApi.BanDelete(steamId)
        .Execute(
          (data, raw) =>
          {
            Log($"Player {steamId} unbanned");
          },
          (err) => Error(
            $"Failed to unban {steamId}. Reason: {err}"
        ));
    }

    private void CreatePlayerAlertsCustom(Plugin plugin, string message, object data = null, object meta = null) {
      CourtApi.PluginPlayerAlertCustomAlertMeta json = new CourtApi.PluginPlayerAlertCustomAlertMeta();

      try
      {
        if (meta != null)
        {
          json = JsonConvert.DeserializeObject<CourtApi.PluginPlayerAlertCustomAlertMeta>(JsonConvert.SerializeObject(meta ?? new CourtApi.PluginPlayerAlertCustomAlertMeta()));
        }
      }
      catch
      {
        Error("Wrong CustomAlertMeta params, default will be used!");
      }

      CourtApi.CreatePlayerAlertsCustom(new CourtApi.PluginPlayerAlertCustomDto {
        msg = message,
        data = data,

        custom_icon = json.custom_icon,
        hide_in_table = false,
        category = $"{plugin.Name} • {json.name}",
        custom_links = json.custom_links
      }).Execute(
        (data, raw) => {},
        (error) => {
          Debug($"Failed to send custom alert: {error}");
        }
      );
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

    #region Plugin API

    private void RA_DirectMessageHandler(string from, string to, string message)
    {
      _RustAppEngine.ChatWorker?.SaveChatMessage(new CourtApi.PluginChatMessageDto
      {
        steam_id = from,
        target_steam_id = to,
        is_team = false,

        text = message
      });
    }

    private void RA_ReportSend(string initiator_steam_id, string target_steam_id, string reason, string message = "")
    {
      if (initiator_steam_id == target_steam_id)
      {
        return;
      }

      var was_checked = _CheckInfo.LastChecks.ContainsKey(target_steam_id) && CurrentTime() - _CheckInfo.LastChecks[target_steam_id] < _Settings.report_ui_show_check_in * 24 * 60 * 60;
      Interface.Oxide.CallHook("RustApp_OnPlayerReported", initiator_steam_id, target_steam_id, reason, message, was_checked);

      _RustAppEngine.ReportWorker?.SendReport(new CourtApi.PluginReportDto
      {
        initiator_steam_id = initiator_steam_id,
        target_steam_id = target_steam_id,
        sub_targets_steam_ids = new List<string>(),
        message = message,
        reason = reason
      });
    }

    private void RA_BanPlayer(string steam_id, string reason, string duration, bool global, bool ban_ip, string comment = "")
    {
      BanCreate(steam_id, new CourtApi.PluginBanCreatePayload {
        reason = reason,
        ban_ip = ban_ip,
        comment = comment,
        global = global,
        target_steam_id = steam_id,
        duration = duration.Length > 0 ? duration : null
      });
    }

    private void RA_CreateAlert(Plugin plugin, string message, object data = null, object meta = null)
    {
      CreatePlayerAlertsCustom(plugin, message, data, meta);
    }

    #endregion

    #region Utils

    private void RegisterCommands() {
      _Settings.report_ui_commands.ForEach(v => cmd.AddChatCommand(v, this, nameof(CmdChatReportInterface)));
    
      cmd.AddChatCommand(_Settings.check_contact_command, this, nameof(CmdSendContact));
    }

    private bool CheckRequiredPlugins() {
      if (plugins.Find("RustAppLite") != null && plugins.Find("RustAppLite").IsLoaded)
      {
        Error(
          "Detected 'Lite' plugin version, to start you should delete plugin: RustAppLite.cs"
        );
        return false;
      }

      return true;
    }

    private static void Trace(string text) {
      #if TRACE
      
        _RustApp.Puts(text);
      
      #endif
    }

    private static void Debug(string text) {
      #if DEBUG
      
        _RustApp.Puts(text);
      
      #endif
    }
    
    private static void Log(string text) {
      _RustApp.Puts(text);
    }

    private static void Error(string text) {
      _RustApp.Puts(text);
    }

    private class RustAppWorker : MonoBehaviour {
      public void Awake() {
        Trace($"{this.GetType().Name} worker enabled");
      }

      public void OnDestroy() {
        Trace($"{this.GetType().Name} worker disabled");
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

    private static string HexToRustFormat(string hex)
    {
      if (string.IsNullOrEmpty(hex))
      {
        hex = "#FFFFFFFF";
      }

      var str = hex.Trim('#');

      if (str.Length == 6)
        str += "FF";

      if (str.Length != 8)
      {
        throw new Exception(hex);
        throw new InvalidOperationException("Cannot convert a wrong format.");
      }

      var r = byte.Parse(str.Substring(0, 2), NumberStyles.HexNumber);
      var g = byte.Parse(str.Substring(2, 2), NumberStyles.HexNumber);
      var b = byte.Parse(str.Substring(4, 2), NumberStyles.HexNumber);
      var a = byte.Parse(str.Substring(6, 2), NumberStyles.HexNumber);

      UnityEngine.Color color = new Color32(r, g, b, a);

      return string.Format("{0:F2} {1:F2} {2:F2} {3:F2}", color.r, color.g, color.b, color.a);
    }
    
    private static List<char> Letters = new List<char> { '☼', 's', 't', 'r', 'e', 'т', 'ы', 'в', 'о', 'ч', 'х', 'а', 'р', 'u', 'c', 'h', 'a', 'n', 'z', 'o', '^', 'm', 'l', 'b', 'i', 'p', 'w', 'f', 'k', 'y', 'v', '$', '+', 'x', '1', '®', 'd', '#', 'г', 'ш', 'к', '.', 'я', 'у', 'с', 'ь', 'ц', 'и', 'б', 'е', 'л', 'й', '_', 'м', 'п', 'н', 'g', 'q', '3', '4', '2', ']', 'j', '[', '8', '{', '}', '_', '!', '@', '#', '$', '%', '&', '?', '-', '+', '=', '~', ' ', 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z', 'а', 'б', 'в', 'г', 'д', 'е', 'ё', 'ж', 'з', 'и', 'й', 'к', 'л', 'м', 'н', 'о', 'п', 'р', 'с', 'т', 'у', 'ф', 'х', 'ц', 'ч', 'ш', 'щ', 'ь', 'ы', 'ъ', 'э', 'ю', 'я' };

    private static string NormalizeString(string text)
    {
      string name = "";

      foreach (var @char in text)
      {
        if (Letters.Contains(@char.ToString().ToLower().ToCharArray()[0]))
          name += @char;
      }

      return name;
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
          Error("Failed to call RaidBlock API");
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
      var player = BasePlayer.Find(steamId);
      if (player != null && player.IsConnected)
      {
        Log($"Closing connection with {steamId}: {reason} (by player)");
        player.Kick(reason);
        return true;
      }

      var connection = ConnectionAuth.m_AuthConnection.Find(v => v.userid.ToString() == steamId);
      if (connection != null)
      {
        Log($"Closing connection with {steamId}: {reason} (by m_AuthConnection)");
        Network.Net.sv.Kick(connection, reason);
        return true;
      }

      var loading = ServerMgr.Instance.connectionQueue.joining.Find(v => v.userid.ToString() == steamId);
      if (loading != null)
      {
        Log($"Closing connection with {steamId}: {reason} (by joining)");
        Network.Net.sv.Kick(loading, reason);
        return true;
      }

      var queued = ServerMgr.Instance.connectionQueue.queue.Find(v => v.userid.ToString() == steamId);
      if (queued != null)
      {
        Log($"Closing connection with {steamId}: {reason} (by queued)");
        Network.Net.sv.Kick(queued, reason);
        return true;
      }

      Error($"Failed to close connection with {steamId}: {reason}");

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

    private double CurrentTime() => DateTime.Now.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

    #region UI Commands references

    #region Variables

    private Dictionary<string, string> UICommands = new Dictionary<string, string>();

    #endregion

    #region Methods

    private string UICommand<T>(Action<BasePlayer, T, string> callback, T arg, string commandName) {
      var argument = $" {JsonConvert.SerializeObject(arg)}~INPUT_LIMITTER~";

      if (UICommands.ContainsKey(commandName)) {
        return UICommands[commandName] + argument;
      } 

      var commandUuid = CuiHelper.GetGuid(); 

      UICommands.Add(commandName, commandUuid);

      cmd.AddConsoleCommand(commandUuid, this, (args) => {
        var player = args.Player();
 
        try {
          var str = "";
          var input = "";

          args.Args.ToList().ForEach(v => {
            str += $"{v} ";
          });

          if (str.Contains("~INPUT_LIMITTER~")) {
            input = str.Split("~INPUT_LIMITTER~")[1].Trim();
            str = str.Split("~INPUT_LIMITTER~")[0];
          }

          var restoredArgument = JsonConvert.DeserializeObject<T>(str);
          
          callback(player, restoredArgument, input ?? "");
        }
        catch {
          Error("Failed to parse UICommand arguments");
        }

        return true; 
      });

      return commandUuid + argument;
    }

    #endregion

    #endregion

    #endregion
  
    #region SignFeed

    // A lot of code references at Discord Sign Logger by MJSU
    // Original author and plugin on UMod: https://umod.org/plugins/discord-sign-logger

    public abstract class BaseImageUpdate
    {
      public BasePlayer Player { get; }
      public ulong PlayerId { get; }
      public string DisplayName { get; }
      public BaseEntity Entity { get; }
      public int ItemId { get; protected set; }

      public uint TextureIndex { get; protected set; }
      public abstract bool SupportsTextureIndex { get; }

      protected BaseImageUpdate(BasePlayer player, BaseEntity entity)
      {
        Player = player;
        DisplayName = player.displayName;
        PlayerId = player.userID;
        Entity = entity;
      }

      public abstract byte[] GetImage();
    }

    public class FireworkUpdate : BaseImageUpdate
    {
      static readonly Hash<UnityEngine.Color, Brush> FireworkBrushes = new Hash<UnityEngine.Color, Brush>();

      public override bool SupportsTextureIndex => false;
      public PatternFirework Firework => (PatternFirework)Entity;

      public FireworkUpdate(BasePlayer player, PatternFirework entity) : base(player, entity)
      {

      }

      public override byte[] GetImage()
      {
        PatternFirework firework = Firework;
        List<Star> stars = firework.Design.stars;

        using (Bitmap image = new Bitmap(250, 250))
        {
          using (Graphics g = Graphics.FromImage(image))
          {
            for (int index = 0; index < stars.Count; index++)
            {
              Star star = stars[index];
              int x = (int)((star.position.x + 1) * 125);
              int y = (int)((-star.position.y + 1) * 125);
              g.FillEllipse(GetBrush(star.color), x, y, 19, 19);
            }

            return GetImageBytes(image);
          }
        }
      }

      private Brush GetBrush(UnityEngine.Color color)
      {
        Brush brush = FireworkUpdate.FireworkBrushes[color];
        if (brush == null)
        {
          brush = new SolidBrush(FromUnityColor(color));
          FireworkUpdate.FireworkBrushes[color] = brush;
        }

        return brush;
      }

      private Color FromUnityColor(UnityEngine.Color color)
      {
        int red = FromUnityColorField(color.r);
        int green = FromUnityColorField(color.g);
        int blue = FromUnityColorField(color.b);
        int alpha = FromUnityColorField(color.a);

        return Color.FromArgb(alpha, red, green, blue);
      }

      private int FromUnityColorField(float color)
      {
        return (int)(color * 255);
      }

      private byte[] GetImageBytes(Bitmap image)
      {
        MemoryStream stream = Facepunch.Pool.Get<MemoryStream>();
        image.Save(stream, ImageFormat.Png);
        byte[] bytes = stream.ToArray();
        Facepunch.Pool.FreeMemoryStream(ref stream);
        return bytes;
      }
    }

    public class PaintedItemUpdate : BaseImageUpdate
    {
      private readonly byte[] _image;

      public PaintedItemUpdate(BasePlayer player, PaintedItemStorageEntity entity, Item item, byte[] image) : base(player, entity)
      {
        _image = image;
        ItemId = item.info.itemid;
      }

      public override bool SupportsTextureIndex => false;
      public override byte[] GetImage()
      {
        return _image;
      }
    }

    public class SignageUpdate : BaseImageUpdate
    {
      public string Url { get; }
      public override bool SupportsTextureIndex => true;
      public ISignage Signage => (ISignage)Entity;

      public SignageUpdate(BasePlayer player, ISignage entity, uint textureIndex, string url = null) : base(player, (BaseEntity)entity)
      {
        TextureIndex = textureIndex;
        Url = url;
      }

      public override byte[] GetImage()
      {
        ISignage sign = Signage;
        uint crc = sign.GetTextureCRCs()[TextureIndex];

        return FileStorage.server.Get(crc, FileStorage.Type.png, sign.NetworkID, TextureIndex);
      }
    }

    private void OnImagePost(BasePlayer player, string url, bool raw, ISignage signage, uint textureIndex)
    {
      _RustAppEngine.SignageWorker?.SignageCreate(new SignageUpdate(player, signage, textureIndex, url));
    }

    private void OnSignUpdated(ISignage signage, BasePlayer player, int textureIndex = 0)
    {
      if (player == null)
      {
        return;
      }

      if (signage.GetTextureCRCs()[textureIndex] == 0)
      {
        return;
      }

      _RustAppEngine.SignageWorker?.SignageCreate(new SignageUpdate(player, signage, (uint)textureIndex));
    }

    private void OnItemPainted(PaintedItemStorageEntity entity, Item item, BasePlayer player, byte[] image)
    {
      if (entity._currentImageCrc == 0)
      {
        return;
      }

      PaintedItemUpdate update = new PaintedItemUpdate(player, entity, item, image);

      _RustAppEngine.SignageWorker?.SignageCreate(new PaintedItemUpdate(player, entity, item, image));
    }

    private void OnFireworkDesignChanged(PatternFirework firework, ProtoBuf.PatternFirework.Design design, BasePlayer player)
    {
      if (design?.stars == null || design.stars.Count == 0)
      {
        return;
      }

      _RustAppEngine.SignageWorker?.SignageCreate(new FireworkUpdate(player, firework));
    }

    private void OnEntityBuilt(Planner plan, GameObject go)
    {
      var player = plan.GetOwnerPlayer();
      var ent = go.ToBaseEntity();

      if (player == null || ent == null) return;

      string shortName = go.ToBaseEntity().ShortPrefabName;
      if (!shortName.Contains("sign"))
      {
        return;
      }

      var signage = go.ToBaseEntity().GetComponent<Signage>();

      NextTick(() =>
      { 
        if (signage == null)
        {
          return;
        }

        if (signage.GetTextureCRCs()[0] == 0)
        {
          return;
        }

        _RustAppEngine.SignageWorker?.SignageCreate(new SignageUpdate(player, signage, 0));
      });
    }

    #endregion
  }
}