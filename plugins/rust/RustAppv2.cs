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

      private static readonly string CourtUrl = "https://court.rustapp.io";

      #region GetServerInfo

      public static StableRequest<object> GetServerInfo() {
        return new StableRequest<object>($"{CourtUrl}/plugin/", RequestMethod.GET, null);
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
        return new StableRequest<PluginPairResponse>($"{CourtUrl}/plugin/pair?code={code}", RequestMethod.POST, new PluginPairPayload());
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
          payload.ip = connection.IPAddressWithoutPort();

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
        return new StableRequest<object>($"{CourtUrl}/plugin/state", RequestMethod.PUT, data);
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

    #endregion

    #region Workers

    private class RustAppEngine : RustAppWorker {
      public GameObject ChildObjectToWorkers;

      public AuthWorker? AuthWorker;
      public StateWorker? StateWorker;
    
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

        Debug($"Set state worker: {StateWorker == null}");
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

    private class QueueWorker : RustAppWorker {
      
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
      _RustApp = this;

      MetaInfo.Read();

      RustAppEngineCreate();
    }

    private void Unload() {
      _RustAppEngine.StateWorker?.SendUpdate(true);

      RustAppEngineDestroy();
    }

    #endregion

    #region Disconnect hooks

    private void OnPlayerDisconnected(BasePlayer player, string reason)
    {
      /** TODO: Test
      if (.Queue.Notices.ContainsKey(player.UserIDString))
      {
        if (_Worker.Queue.Notices[player.UserIDString] == true) {
          Interface.Oxide.CallHook("RustApp_OnCheckNoticeHidden", player);
        }

        _Worker.Queue.Notices.Remove(player.UserIDString);
      }
      */ 

      SetPlayerDisconnected(player.UserIDString, reason);
    }

    private void OnClientDisconnect(Network.Connection connection, string reason)
    {
      SetPlayerDisconnected(connection.userid.ToString(), reason);
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

    #endregion

    #region Methods

    private void SetPlayerDisconnected(string steamId, string reason) {
      if (_RustAppEngine.StateWorker != null) {
        _RustAppEngine.StateWorker.DisconnectReasons[steamId] = reason;
      }
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

    #endregion
  }
}