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

namespace Oxide.Plugins
{
  [Info("RustApp", "Hougan & Xacku & Olkuts", "1.10.2")]
  public class RustApp : RustPlugin
  {
    #region Classes 

    // From RustStats by Sanlerus  
    public static class EncodingBase64
    {
      public static string Encode(string text)
      {
        var textAsBytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToBase64String(textAsBytes);
      }
      public static string Decode(string encodedText)
      {
        var textAsBytes = Convert.FromBase64String(encodedText);
        return Encoding.UTF8.GetString(textAsBytes);
      }
    }

    // Base code idea from RustStore by Sanlerus
    public class StableRequest<T>
    {
      private static Dictionary<string, string> DefaultHeaders(string secret)
      {
        return new Dictionary<string, string>
        {
          ["x-plugin-auth"] = secret,
          ["x-plugin-version"] = _RustApp.Version.ToString(),
          ["x-plugin-port"] = ConVar.Server.port.ToString()
        };
      }

      private string url;
      private RequestMethod method;

      private object data;
      public Dictionary<string, string> headers = new Dictionary<string, string>();

      public Action<string> onException;
      public Action<T, string> onComplete;

      public bool debug = true;
      public bool silent = true;

      private DateTime _created = DateTime.Now;

      public StableRequest(string url, RequestMethod method, object? data, string secret)
      {
        this.url = url;
        this.method = method;

        this.data = data;
        this.headers = DefaultHeaders(secret);

        this.onException += (a) =>
        {
          LastException.History.Insert(0, new LastException(url, data, a, secret));

          if (LastException.History.Count > 10)
          {
            LastException.History.RemoveRange(10, 1);
          }
        };
      }

      public double Elapsed()
      {
        return (DateTime.Now - _created).TotalMilliseconds;
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
            var str = typeof(T).ToString();

            _RustApp.Error(
              $"Не удалось разобрать ответ от сервера ({request.method.ToUpper()} {request.url}): {parseException} (Response: {request.downloadHandler?.text})",
              $"Failed to parse server response: ({request.method.ToUpper()} {request.url}): {parseException} (Response: {request.downloadHandler?.text})"
            );
          }
        }
      }
    }

    private class LastException
    {
      public static List<LastException> History = new List<LastException>();

      public string module;
      public string secret;
      public string payload;
      public string response;
      public DateTime time = DateTime.Now;

      public LastException(string module, object payload, string response, string secret)
      {
        this.module = module;
        this.secret = secret;
        this.payload = JsonConvert.SerializeObject(payload ?? "No request payload");
        this.response = response;
      }
    }

    private static class CourtUrls
    {
      private static string Base = "https://court.rustapp.io";

      public static string Pair = $"{CourtUrls.Base}/plugin/pair";

      public static string Validate = $"{CourtUrls.Base}/plugin/";
      public static string SendContact = $"{CourtUrls.Base}/plugin/contact";
      public static string SendState = $"{CourtUrls.Base}/plugin/state";
      public static string SendKills = $"{CourtUrls.Base}/plugin/kills";
      public static string SendChat = $"{CourtUrls.Base}/plugin/chat";
      public static string SendAlerts = $"{CourtUrls.Base}/plugin/alerts";
      public static string SendCustomAlert = $"{CourtUrls.Base}/plugin/custom-alert";
      public static string SendReports = $"{CourtUrls.Base}/plugin/reports";
      public static string SendDestroyedSignage = $"{CourtUrls.Base}/plugin/signage";
      public static string SendSignage = $"{CourtUrls.Base}/plugin/signage";
      public static string SendWipe = $"{CourtUrls.Base}/plugin/wipe";
      public static string BanCreate = $"{CourtUrls.Base}/plugin/ban";
      public static string BanDelete = $"{CourtUrls.Base}/plugin/unban";
    }

    private static class QueueUrls
    {
      private static string Base = "https://queue.rustapp.io";

      public static string Fetch = $"{QueueUrls.Base}/";
      public static string Response = $"{QueueUrls.Base}/";
    }

    private static class BanUrls
    {
      private static string Base = "https://ban.rustapp.io";

      public static string Fetch = $"{BanUrls.Base}/plugin/v2";
    }

    class PluginChatMessagesPayload
    {
      public List<PluginChatMessageEntry> messages = new List<PluginChatMessageEntry>();
    }

    public class PluginChatMessageEntry
    {
      public string steam_id;
      [CanBeNull]
      public string target_steam_id;

      public bool is_team;

      public string text;
    }

    public class PluginKillsPayload {
      public List<PluginKillEntry> kills = new List<PluginKillEntry>();
    }

    public class PluginKillEntry
    {
      public string initiator_steam_id;
      public string target_steam_id;
      public string game_time;
      public float distance;
      public string weapon;

      public bool is_headshot;

      public List<CombatLogEventExtended> hit_history = new List<CombatLogEventExtended>();
    }

    class PluginReportsPayload
    {
      public List<PluginReportEntry> reports = new List<PluginReportEntry>();
    }

    public static class PlayerAlertType
    {
      public static readonly string join_with_ip_ban = "join_with_ip_ban";
      public static readonly string dug_up_stash = "dug_up_stash";
      public static readonly string custom_api = "custom_api";
    }

    public class PluginPlayerAlertEntry
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

    public class PluginReportEntry
    {
      public string initiator_steam_id;
      [CanBeNull]
      public string target_steam_id;

      public List<string> sub_targets_steam_ids;

      public string reason;

      [CanBeNull]
      public string message;
    }

    class PluginPairDto
    {
      [CanBeNull] public int ttl;
      [CanBeNull] public string token;
    }

    class PluginPlayerPayload
    {
      private static bool IsRaidBlocked(BasePlayer player)
      {
        if (_RustApp == null || !_RustApp.IsLoaded)
        {
          return false;
        }


        if (_Settings.utils_use_own_raidblock)
        {
          var res = Interface.Oxide.CallHook("RustApp_IsInRaid", player.userID.Get());
          if (res is bool)
          {
            return (bool)res;
          }
          else
          {
            return false;
          }
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
            /**
            _RustApp.Warning(
              "Обнаружен плагин NoEscape, но не удалось вызвать API",
              "Detected plugin NoEscape, but failed to call API"
            );
            */
          }
        }

        return false;
      }



      private static bool IsBuildingAuthed(BasePlayer player)
      {
        var list = new List<BuildingPrivlidge>();

        Vis.Entities(player.transform.position, 16f, list, Layers.PreventBuilding);

        return list.FirstOrDefault()?.IsAuthed(player) ?? false;
      }

      private static bool NoLicense(Network.Connection connection)
      {
        var isMultifighting = _RustApp.MultiFighting != null && _RustApp.MultiFighting.IsLoaded;
        var isTirify = _RustApp.TirifyGamePluginRust != null && _RustApp.TirifyGamePluginRust.IsLoaded;

        if (isMultifighting) {
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

        if (isTirify) {
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

      public static PluginPlayerPayload FromPlayer(BasePlayer player)
      {
        var payload = new PluginPlayerPayload();

        payload.position = player.transform.position.ToString();
        payload.rotation = player.eyes.rotation.ToString();
        payload.coords = GridReference(player.transform.position);

        payload.steam_id = player.UserIDString;
        payload.steam_name = player.displayName.Replace("<blank>", "blank");
        payload.ip = player.Connection.IPAddressWithoutPort();

        payload.status = "active";

        payload.can_build = PluginPlayerPayload.IsBuildingAuthed(player);

        payload.is_raiding = PluginPlayerPayload.IsRaidBlocked(player);
        payload.no_license = PluginPlayerPayload.NoLicense(player.Connection);

        if (player.Team != null)
        {
          payload.team = player.Team.members
            .Select(v => v.ToString())
            .Where(v => v != player.UserIDString)
            .ToList();
        }

        return payload;
      }

      public static PluginPlayerPayload FromConnection(Network.Connection connection, string status)
      {
        var payload = new PluginPlayerPayload();

        payload.steam_id = connection.userid.ToString();
        payload.steam_name = connection.username.Replace("<blank>", "blank");
        payload.ip = connection.IPAddressWithoutPort();

        payload.status = status;

        payload.no_license = PluginPlayerPayload.NoLicense(connection);

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
      public string steam_id;
      public string steam_name;
      public string ip;

      [CanBeNull] public string position;
      [CanBeNull] public string rotation;
      [CanBeNull] public string coords;

      public bool can_build = false;
      public bool is_raiding = false;
      public bool no_license = false;

      public string status;

      public List<string> team = new List<string>();
    }

    public class MetaInfo
    {
      public static MetaInfo Read()
      {
        if (!Interface.Oxide.DataFileSystem.ExistsDatafile("RustApp_Configuration"))
        {
          return null;
        }

        return Interface.Oxide.DataFileSystem.ReadObject<MetaInfo>("RustApp_Configuration");
      }

      public static void write(MetaInfo courtMeta)
      {
        Interface.Oxide.DataFileSystem.WriteObject("RustApp_Configuration", courtMeta);
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

    public class PairWorker : BaseWorker
    {
      public string Code;

      public void EnterCode(string code)
      {
        @PairAssignData(code, (data) =>
        {
          Code = code;

          if (data.token != null && data.token.Length > 0)
          {
            Complete(data.token);
            return;
          }

          Notify(data.ttl);

          Invoke(nameof(PairWaitFinish), 1f);
        }, (err) =>
        {
          Destroy(this);
        });
      }

      public void Notify(int left)
      {
        _RustApp.Log(
          $"Соединение установлено, завершите подключение на сайте.",
          $"Connection established, complete pair on site."
        );
      }

      public void Complete(string token)
      {
        _RustApp.Log(
          "Соединение установлено, перезагрузка...",
          "Connection established, reloading..."
        );

        if (_RustApp != null && _RustApp._Worker != null)
        {
          MetaInfo.write(new MetaInfo { Value = token });

          _RustApp._Worker.Awake();
        }
      }

      public void PairWaitFinish()
      {
        if (Code == null)
        {
          Destroy(this);
          return;
        }

        @PairAssignData(Code, (data) =>
        {
          if (data.token == null || data.token.Length == 0)
          {
            Invoke(nameof(PairWaitFinish), 1f);
            Notify(data.ttl);
            return;
          }

          Complete(data.token);
        }, (err) =>
        {
          Invoke(nameof(PairWaitFinish), 1f);

          Interface.Oxide.LogError($"ошибка, {err}");
        });
      }

      private void @PairAssignData(string code, Action<PluginPairDto> onComplete, Action<string> onException)
      {
        var obj = new
        {
          name = ConVar.Server.hostname,
          level = SteamServer.MapName ?? ConVar.Server.level,
          description = ConVar.Server.description + " " + ConVar.Server.motd,

          avatar_big = ConVar.Server.logoimage,
          banner_url = ConVar.Server.headerimage,

          online = BasePlayer.activePlayerList.Count + ServerMgr.Instance.connectionQueue.queue.Count + ServerMgr.Instance.connectionQueue.joining.Count,
          slots = ConVar.Server.maxplayers,

          port = ConVar.Server.port,
          branch = ConVar.Server.branch,

          connected = _RustApp._Worker?.Ban.IsAuthed()
        };

        Request<PluginPairDto>(CourtUrls.Pair + $"?code={code}", RequestMethod.POST, obj)
          .Execute(
            (data, raw) =>
            {
              if (data.ttl != null)
              {
                data.ttl = (int)Math.Round((float)(data.ttl / 1000));
              }

              onComplete(data);
            },
            (err) =>
            {
              if (err.Contains("code not exists"))
              {
                _RustApp.Error(
                  "Ключ API более недействителен.",
                  "API key is no longer valid."
                );

                Destroy(this);
                return;
              }

              onException(err);
            }
          );
      }
    }

    public class CourtWorker : BaseWorker
    {
      public ActionWorker Action;
      public UpdateWorker Update;
      public QueueWorker Queue;
      public PairWorker Pair;
      public BanWorker Ban;

      private MetaInfo Meta;

      public void Awake()
      {
        OnDestroy();

        _RustApp.Log(
          "Добро пожаловать в RustApp! Подключение к серверам панели...",
          "Welcome to the RustApp! Connecting to servers..."
        );

        Action = gameObject.AddComponent<ActionWorker>();
        Update = gameObject.AddComponent<UpdateWorker>();
        Queue = gameObject.AddComponent<QueueWorker>();
        Ban = gameObject.AddComponent<BanWorker>();

        Meta = MetaInfo.Read();

        if (Meta != null)
        {
          Connect();
        }
        else
        {
          _RustApp.Warning(
            "Ваш плагин не настроен, создайте сервер на сайте и следуйте указаниям",
            "Your plugin is not configured, create server on site and follow instructions"
          );
        }
      }

      public void Connect()
      {
        Auth(Meta.Value);

        @ValidateSecret(
          () => EnableModules(),
          (err) =>
          {
            if (err.Contains("is lower than minimal"))
            {
              _RustApp.Warning(
                $"Ваша версия плагина слишком сильно устарела, необходимо скачать новую с сайта.",
                $"Your version of the plugin is too outdated, you need to download a new one from the website."
              );
              return;
            }

            if (err.Contains("Authorization secret is corrupted"))
            {
              _RustApp.Warning(
                $"Ваш ключ более недействителен.",
                $"Your key is no longer valid."
              );
              return;
            }

            if (err.Contains("Check server configuration, required ip") || err.Contains("Check server configuration, required port"))
            {
              _RustApp.Warning(
                $"Конфигурация вашего сервера изменилась, необходимо переподключить сервер через сайт.",
                $"Your server configuration has changed, you need to reconnect the server through the site."
              );
              return;
            }

            _RustApp.Error(
              $"Судя по всему RustApp временно недоступен, попробуем переподключиться через 10 секунд.",
              $"Apparently RustApp is temporarily unavailable, let's try reconnecting in 10 seconds."
            );

            Invoke(nameof(Connect), 10f);
          }
        );
      }

      public void EnableModules()
      {
        Action.Auth(Meta.Value);
        Update.Auth(Meta.Value);
        Queue.Auth(Meta.Value);
        Ban.Auth(Meta.Value);

        _RustApp.Log(
          "Соединение установлено, плагин готов к работе!",
          "Connection established, plugin is ready!"
        );

        _Settings.report_ui_commands.ForEach(v =>
        {
          _RustApp.cmd.AddChatCommand(v, _RustApp, nameof(_RustApp.ChatCmdReport));
        });

        _RustApp.cmd.AddChatCommand(_Settings.check_contact_command, _RustApp, nameof(_RustApp.CmdChatContact));
      }

      public void @ValidateSecret(Action? onComplete, Action<string>? onException)
      {
        Request<object>(CourtUrls.Validate, RequestMethod.GET)
          .Execute(
            (data, raw) => onComplete?.Invoke(),
            (err) => onException?.Invoke(err)
          );
      }

      public void StartPair(string code)
      {
        if (Pair != null)
        {
          _RustApp.Warning(
            "Вы уже подключаетесь, вы можете отменить запрос.",
            "You already connecting, you can reset state."
          );
          return;
        }

        Pair = gameObject.AddComponent<PairWorker>();

        Pair.EnterCode(code);
      }

      public void OnDestroy()
      {
        if (Action != null)
        {
          Destroy(Action);
        }

        if (Update != null)
        {
          Destroy(Update);
        }

        if (Queue != null)
        {
          Destroy(Queue);
        }

        if (Pair != null)
        {
          Destroy(Pair);
        }

        if (Ban != null)
        {
          Destroy(Ban);
        }
      }
    }

    public class ActionWorker : BaseWorker
    {
      public void @SendContact(string steam_id, string message, Action<bool> callback)
      {
        if (!IsReady())
        {
          return;
        }

        Request<object>(CourtUrls.SendContact, RequestMethod.POST, new { steam_id, message })
          .Execute(
            (data, raw) => callback(true),
            (err) => callback(false)
          );
      }
      public void @SendWipe(Action<bool> callback)
      {
        if (!IsReady())
        {
          return;
        }

        Request<object>(CourtUrls.SendWipe, RequestMethod.POST)
          .Execute(
            (data, raw) => callback(true),
            (err) => callback(false)
          );
      }

      public void @SendBan(string steam_id, string reason, string duration, bool global, bool ban_ip, string comment = "Ban via console")
      {
        if (!IsReady())
        {
          return;
        }

        Request<object>(CourtUrls.BanCreate, RequestMethod.POST, new
        {
          target_steam_id = steam_id,
          reason = reason,
          global = global,
          ban_ip = ban_ip,
          duration = duration.Length > 0 ? duration : null,
          comment
        })
        .Execute(
          (data, raw) =>
          {
            _RustApp.Log(
              $"Игрок {steam_id} заблокирован за {reason}",
              $"Player {steam_id} banned for {reason}"
            );

            _RustApp.CloseConnection(steam_id, reason);
          },
          (err) => _RustApp.Log(
            $"Не удалось заблокировать {steam_id}. Причина: {err}",
            $"Failed to ban {steam_id}. Reason: {err}"
          )
        );
      }

      public void @SendBanDelete(string steam_id)
      {
        if (!IsReady())
        {
          return;
        }

        Request<object>(CourtUrls.BanDelete, RequestMethod.POST, new
        {
          target_steam_id = steam_id,
        })
        .Execute(
          (data, raw) =>
          {
            _RustApp.Log(
              $"Игрок {steam_id} разблокирован",
              $"Player {steam_id} unbanned"
            );
          },
          (err) => _RustApp.Log(
            $"Не удалось разблокировать {steam_id}. Причина: {err}",
            $"Failed to unban {steam_id}. Reason: {err}"
          )
        );
      }

      class CustomAlertMeta
      {
        public string custom_icon = null;
        public string name = "";
        public List<string> custom_links = null;
      }

      public void @SendCustomAlert(Plugin plugin, string message, object data = null, object meta = null)
      {
        if (!IsReady())
        {
          return;
        }

        CustomAlertMeta json = new CustomAlertMeta();

        try
        {
          if (meta != null)
          {
            json = JsonConvert.DeserializeObject<CustomAlertMeta>(JsonConvert.SerializeObject(meta ?? new CustomAlertMeta()));
          }
        }
        catch
        {
          _RustApp.Error("Переданы неверные параметры CustomAlertMeta, будут использованы стандартные!", "Wrong CustomAlertMeta params, default will be used!");
        }


        Request<object>(CourtUrls.SendCustomAlert, RequestMethod.POST, new
        {
          msg = message,
          data = data,

          custom_icon = json.custom_icon,
          hide_in_table = false,
          category = $"{plugin.Name} • {json.name}",
          custom_links = json.custom_links
        })
          .Execute(
            null,
            (err) =>
            {
              _RustApp.Puts(err);
              _RustApp.Error(
                $"Не удалось отправить кастомное оповещение ({message})",
                $"Failed to send custom-alert ({message})"
              );
            }
          );
      }

      public void @SendSignage(BaseImageUpdate upload)
      {
        if (!IsReady())
        {
          return;
        }

        try
        {
          var obj = new
          {
            steam_id = upload.PlayerId.ToString(),
            net_id = upload.Entity.net.ID.Value,

            base64_image = Convert.ToBase64String(upload.GetImage()),

            type = upload.Entity.ShortPrefabName,
            position = upload.Entity.transform.position.ToString(),
            square = GridReference(upload.Entity.transform.position)
          };

          Request<string>(CourtUrls.SendSignage, RequestMethod.POST, obj)
            .Execute(
              (data, raw) =>
              {
              },
              (err) =>
              {
                _RustApp.Error(
                  $"Не удалось отправить табличку ({err})",
                  $"Failed to send signage ({err})"
                );
              }
            );
        }
        catch
        {
          return;
        }
      }
    }

    public class BanWorker : BaseWorker
    {
      public class BanFetchResponse
      {
        public List<BanFetchEntry> entries;
      }

      public class BanFetchPayload
      {
        public string steam_id;
        public string ip;
      }

      public class BanFetchEntry : BanFetchPayload
      {
        public List<BanEntry> bans;
      }

      public class BanEntry
      {
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

      private Dictionary<string, string> PlayersCollection = new Dictionary<string, string>();

      public void Awake()
      {
        foreach (var player in BasePlayer.activePlayerList)
        {
          FetchBan(player);
        }

        foreach (var queued in ServerMgr.Instance.connectionQueue.queue)
        {
          FetchBan(queued.userid.ToString(), queued.IPAddressWithoutPort());
        }

        foreach (var loading in ServerMgr.Instance.connectionQueue.joining)
        {
          FetchBan(loading.userid.ToString(), loading.IPAddressWithoutPort());
        }
      }

      protected override void OnReady()
      {
        InvokeRepeating(nameof(FetchBans), 0f, 2f);
      }

      public void FetchBan(BasePlayer player)
      {
        FetchBan(player.UserIDString, player.Connection.IPAddressWithoutPort());
      }

      public void FetchBan(string steamId, string ip)
      {
        // Вызов хука на возможность игнорировать проверку
        var over = Interface.Oxide.CallHook("RustApp_CanIgnoreBan", steamId);
        if (over != null)
        {
          return;
        }

        if (!PlayersCollection.ContainsKey(steamId))
        {
          PlayersCollection.Add(steamId, ip);
          return;
        }

        PlayersCollection[steamId] = ip;
      }

      private void FetchBans()
      {
        if (!IsReady() || PlayersCollection.Count == 0)
        {
          return;
        }

        var banChecks = PlayersCollection.ToDictionary(v => v.Key, v => v.Value);

        @FetchBans(
          banChecks,
          (steamId, ban) =>
          {
            if (PlayersCollection.ContainsKey(steamId))
            {
              PlayersCollection.Remove(steamId);
            }

            if (ban != null)
            {
              if (ban.sync_project_id != 0 && !ban.sync_should_kick)
              {
                return;
              }

              if (ban.steam_id == steamId)
              {
                var format = ban.expired_at == 0 ? _RustApp.lang.GetMessage("System.Ban.Perm.Kick", _RustApp, steamId) : _RustApp.lang.GetMessage("System.Ban.Temp.Kick", _RustApp, steamId);

                if (ban.sync_project_id != 0)
                {
                  format = ban.expired_at == 0 ? _RustApp.lang.GetMessage("System.BanSync.Perm.Kick", _RustApp, steamId) : _RustApp.lang.GetMessage("System.BanSync.Temp.Kick", _RustApp, steamId);
                }

                var time = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ban.expired_at + 3 * 60 * 60 * 1_000).ToString("dd.MM.yyyy HH:mm");

                var text = format.Replace("%REASON%", ban.reason).Replace("%TIME%", time);

                _RustApp.CloseConnection(steamId, text);
              }
              else
              {
                _RustApp.CloseConnection(steamId, _RustApp.lang.GetMessage("System.Ban.Ip.Kick", _RustApp, steamId));

                CreateAlertForIpBan(ban, steamId);
              }

            }
          },
          () =>
          {
            _RustApp.Error(
              $"Ошибка проверки блокировок ({banChecks.Keys.Count} шт.), пытаемся снова...",
              $"Ban check error ({banChecks.Keys.Count} total), attempting again..."
            );

            // Возвращаем неудачно отправленные сообщения обратно в массив
            var resurrectCollection = new Dictionary<string, string>();

            foreach (var ban in banChecks)
            {
              if (resurrectCollection.ContainsKey(ban.Key))
              {
                continue;
              }

              resurrectCollection.Add(ban.Key, ban.Value);
            }
            foreach (var ban in PlayersCollection)
            {
              if (resurrectCollection.ContainsKey(ban.Key))
              {
                continue;
              }

              resurrectCollection.Add(ban.Key, ban.Value);
            }

            PlayersCollection = resurrectCollection;
          }
        );

        PlayersCollection = new Dictionary<string, string>();
      }

      public void CreateAlertForIpBan(BanEntry ban, string steamId)
      {
        if (ban.steam_id == steamId)
        {
          return;
        }

        if (!ban.ban_ip_active)
        {
          return;
        }

        _RustApp._Worker.Update.SaveAlert(new PluginPlayerAlertEntry
        {
          type = PlayerAlertType.join_with_ip_ban,
          meta = new PluginPlayerAlertJoinWithIpBanMeta
          {
            steam_id = steamId,
            ip = ban.ban_ip,
            ban_id = ban.id
          }
        });
      }

      public void @FetchBans(Dictionary<string, string> entries, Action<string, BanEntry> onBan, Action onException)
      {
        /*
        _RustApp.Log(
          $"Проверяем блокировки игроков ({entries.Keys.Count} шт)",
          $"Fetch players bans ({entries.Keys.Count} pc)"
        );
        */

        var players = new List<BanFetchPayload>();

        foreach (var entry in entries)
        {
          players.Add(new BanFetchPayload { steam_id = entry.Key, ip = entry.Value });
        }

        Request<BanFetchResponse>(BanUrls.Fetch, RequestMethod.POST, new { players })
          .Execute(
            (data, raw) =>
            {
              foreach (var player in players)
              {
                var exists = data.entries.Find(v => v.steam_id == player.steam_id);
                if (exists == null)
                {
                  _RustApp.Error(
                    $"Ответ бан-сервиса не содержит запрошенного игрока: {player.steam_id}",
                    $"The ban service response does not contain the requested player: {player.steam_id}"
                  );
                  return;
                }

                var active = exists.bans.FirstOrDefault(v => v.computed_is_active);

                onBan?.Invoke(player.steam_id, active);
              }
            },
            (err) =>
            {
              Interface.Oxide.LogWarning(err);
              onException?.Invoke();
            }
          );
      }
    }

    public class UpdateWorker : BaseWorker
    {
      public bool IsDead = false;

      private List<PluginPlayerAlertEntry> PlayerAlertCollection = new List<PluginPlayerAlertEntry>();
      private List<PluginReportEntry> ReportCollection = new List<PluginReportEntry>();
      private List<PluginChatMessageEntry> ChatCollection = new List<PluginChatMessageEntry>();
      private List<PluginKillEntry> PlayerKillCollection = new List<PluginKillEntry>();
      private Dictionary<string, string> DisconnectHistory = new Dictionary<string, string>();
      private Dictionary<string, string> TeamChangeHistory = new Dictionary<string, string>();
      private List<string> DestroyedSignsCollection = new List<string>();

      protected override void OnReady()
      {
        CancelInvoke(nameof(SendChat));
        CancelInvoke(nameof(SendKills));
        CancelInvoke(nameof(SendUpdate));
        CancelInvoke(nameof(SendReports));
        CancelInvoke(nameof(SendAlerts));
        CancelInvoke(nameof(SendDestroyedSigns));

        InvokeRepeating(nameof(SendChat), 0, 1f);
        InvokeRepeating(nameof(SendUpdate), 0, 5f);
        InvokeRepeating(nameof(SendKills), 0, 5f);
        InvokeRepeating(nameof(SendReports), 0, 1f);
        InvokeRepeating(nameof(SendAlerts), 0, 5f);
        InvokeRepeating(nameof(SendDestroyedSigns), 0, 5f);
      }

      public void SaveChat(PluginChatMessageEntry message)
      {
        ChatCollection.Add(message);
      }

      public void SaveAlert(PluginPlayerAlertEntry playerAlert)
      {
        PlayerAlertCollection.Add(playerAlert);
      }

      public void SaveKill(PluginKillEntry playerKill)
      {
        PlayerKillCollection.Add(playerKill);
      }

      public void SaveDestroyedSign(string net_id)
      {
        DestroyedSignsCollection.Add(net_id);
      }

      public void SaveDisconnect(string steamId, string reason)
      {
        if (!DisconnectHistory.ContainsKey(steamId))
        {
          DisconnectHistory.Add(steamId, reason);
        }

        DisconnectHistory[steamId] = reason;
      }

      public void SaveTeamHistory(string initiator_steam_id, string target_steam_id)
      {
        if (!TeamChangeHistory.ContainsKey(target_steam_id))
        {
          TeamChangeHistory.Add(target_steam_id, initiator_steam_id);
        }

        TeamChangeHistory[target_steam_id] = initiator_steam_id;
      }

      public void SaveReport(PluginReportEntry report)
      {
        // Вызов хука на возможность игнорировать проверку
        var over = Interface.Oxide.CallHook("RustApp_CanIgnoreReport", report.target_steam_id, report.initiator_steam_id);
        if (over != null)
        {
          return;
        }

        ReportCollection.Add(report);
      }

      private void SendDestroyedSigns()
      {
        if (!IsReady() || DestroyedSignsCollection.Count == 0)
        {
          return;
        }

        var destroyed_signs = DestroyedSignsCollection.ToList();

        Request<string>(CourtUrls.SendDestroyedSignage, RequestMethod.DELETE, new { net_ids = destroyed_signs })
          .Execute(
            null,
            (err) =>
            {
              _RustApp.Puts(err);
              _RustApp.Error(
                $"Не удалось отправить удаленные картинки ({destroyed_signs.Count} шт)",
                $"Failed to send destroyed signs ({destroyed_signs.Count} pc)"
              );

              // Возвращаем неудачно отправленные сообщения обратно в массив
              var resurrectCollection = new List<string>();

              resurrectCollection.AddRange(destroyed_signs);
              resurrectCollection.AddRange(DestroyedSignsCollection);

              DestroyedSignsCollection = resurrectCollection;
            }
          );

        DestroyedSignsCollection = new List<string>();
      }

      private void SendAlerts()
      {
        if (!IsReady() || PlayerAlertCollection.Count == 0)
        {
          return;
        }

        var alerts = PlayerAlertCollection.ToList();

        Request<object>(CourtUrls.SendAlerts, RequestMethod.POST, new { alerts })
          .Execute(
            null,
            (err) =>
            {
              _RustApp.Puts(err);
              _RustApp.Error(
                $"Не удалось отправить алерты ({alerts.Count} шт)",
                $"Failed to send alerts for player ({alerts.Count} pc)"
              );

              // Возвращаем неудачно отправленные сообщения обратно в массив
              var resurrectCollection = new List<PluginPlayerAlertEntry>();

              resurrectCollection.AddRange(alerts);
              resurrectCollection.AddRange(PlayerAlertCollection);

              PlayerAlertCollection = resurrectCollection;
            }
          );

        PlayerAlertCollection = new List<PluginPlayerAlertEntry>();
      }

      private void SendChat()
      {
        if (!IsReady() || ChatCollection.Count == 0)
        {
          return;
        }

        var messages = ChatCollection.ToList();

        Request<object>(CourtUrls.SendChat, RequestMethod.POST, new { messages })
          .Execute(
            null,
            (err) =>
            {
              _RustApp.Error(
                $"Не удалось отправить сообщения из чата ({messages.Count} шт)",
                $"Failed to send messages from the chat ({messages.Count} pc)"
              );

              // Возвращаем неудачно отправленные сообщения обратно в массив
              var resurrectCollection = new List<PluginChatMessageEntry>();

              resurrectCollection.AddRange(messages);
              resurrectCollection.AddRange(ChatCollection);

              ChatCollection = resurrectCollection;
            }
          );

        ChatCollection = new List<PluginChatMessageEntry>();
      }


      private void SendKills()
      {
        if (!IsReady() || PlayerKillCollection.Count == 0)
        {
          return;
        }

        var kills = PlayerKillCollection.ToList();

        Request<object>(CourtUrls.SendKills, RequestMethod.POST, new { kills })
          .Execute(
            null,
            (err) =>
            {
              _RustApp.Error(
                $"Не удалось отправить историю убийств ({kills.Count} шт)",
                $"Failed to send kill feed ({kills.Count} pc)"
              );

              // Возвращаем неудачно отправленные сообщения обратно в массив
              var resurrectCollection = new List<PluginKillEntry>();

              resurrectCollection.AddRange(kills);
              resurrectCollection.AddRange(PlayerKillCollection);

              PlayerKillCollection = kills;
            }
          );

        PlayerKillCollection = new List<PluginKillEntry>();
      }

      public void SendReports()
      {
        if (!IsReady() || ReportCollection.Count == 0)
        {
          return;
        }

        var reports = ReportCollection.ToList();

        Request<object>(CourtUrls.SendReports, RequestMethod.POST, new { reports })
          .Execute(
            null,
            (err) =>
            {
              _RustApp.Error(
                $"Не удалось отправить жалобы ({reports.Count} шт)",
                $"Failed to send messages from the chat ({reports.Count} pc)"
              );

              // Возвращаем неудачно отправленные репорты обратно в массив
              var resurrectCollection = new List<PluginReportEntry>();

              resurrectCollection.AddRange(reports);
              resurrectCollection.AddRange(ReportCollection);

              ReportCollection = resurrectCollection;
            }
          );

        ReportCollection = new List<PluginReportEntry>();
      }

      public void SendUpdate()
      {
        if (!IsReady())
        {
          return;
        }

        var players = new List<PluginPlayerPayload>();

        foreach (var player in BasePlayer.activePlayerList)
        {
          try
          {
            if (IsDead)
            {
              if (!DisconnectHistory.ContainsKey(player.UserIDString))
              {
                DisconnectHistory.Add(player.UserIDString, "plugin-unload");
              }

              DisconnectHistory[player.UserIDString] = "plugin-unload";
            }
            else
            {
              players.Add(PluginPlayerPayload.FromPlayer(player));
            }
          }
          catch (Exception exc)
          {
          }
        }

        foreach (var player in ServerMgr.Instance.connectionQueue.queue)
        {
          try
          {
            if (IsDead)
            {
              if (!DisconnectHistory.ContainsKey(player.userid.ToString()))
              {
                DisconnectHistory.Add(player.userid.ToString(), "plugin-unload");
              }

              DisconnectHistory[player.userid.ToString()] = "plugin-unload";
            }
            else
            {
              players.Add(PluginPlayerPayload.FromConnection(player, "queued"));
            }
          }
          catch (Exception exc)
          {
          }
        }

        foreach (var player in ServerMgr.Instance.connectionQueue.joining)
        {
          try
          {
            if (IsDead)
            {
              if (!DisconnectHistory.ContainsKey(player.userid.ToString()))
              {
                DisconnectHistory.Add(player.userid.ToString(), "plugin-unload");
              }

              DisconnectHistory[player.userid.ToString()] = "plugin-unload";
            }
            else
            {
              players.Add(PluginPlayerPayload.FromConnection(player, "joining"));
            }
          }
          catch (Exception exc)
          {
          }
        }

        var disconnected = DisconnectHistory.ToDictionary(v => v.Key, v => v.Value);
        var team_changes = TeamChangeHistory.ToDictionary(v => v.Key, v => v.Value);

        var payload = new
        {
          hostname = ConVar.Server.hostname,
          level = SteamServer.MapName ?? ConVar.Server.level,

          avatar_url = ConVar.Server.logoimage,
          banner_url = ConVar.Server.headerimage,

          slots = ConVar.Server.maxplayers,
          version = _RustApp.Version.ToString(),
          protocol = Protocol.printable.ToString(),
          performance = _RustApp.TotalHookTime.ToString(),

          players,
          disconnected = disconnected,
          team_changes = team_changes
        };

        Request<object>(CourtUrls.SendState, RequestMethod.PUT, payload)
          .Execute(
            (data, raw) =>
            {
            },
            (err) =>
            {
              //_RustApp.Error(
              //  $"Не удалось отправить состояние сервера ({err})",
              //  $"Failed to send server status ({err})"
              //);

              /**
              Возвращаем неудачно отправленные дисконекты
              */

              var resurrectCollectionDisconnects = new Dictionary<string, string>();

              foreach (var disconnect in disconnected)
              {
                if (!resurrectCollectionDisconnects.ContainsKey(disconnect.Key))
                {
                  resurrectCollectionDisconnects.Add(disconnect.Key, disconnect.Value);
                }
              }

              foreach (var disconnect in DisconnectHistory)
              {
                if (!resurrectCollectionDisconnects.ContainsKey(disconnect.Key))
                {
                  resurrectCollectionDisconnects.Add(disconnect.Key, disconnect.Value);
                }
              }

              DisconnectHistory = resurrectCollectionDisconnects;

              /**
              Возвращаем неудачно отправленные изменения команды
              */

              var resurrectCollectionTeamChanges = new Dictionary<string, string>();

              foreach (var teamChange in team_changes)
              {
                if (!resurrectCollectionTeamChanges.ContainsKey(teamChange.Key))
                {
                  resurrectCollectionTeamChanges.Add(teamChange.Key, teamChange.Value);
                }
              }

              foreach (var teamChange in TeamChangeHistory)
              {
                if (!resurrectCollectionTeamChanges.ContainsKey(teamChange.Key))
                {
                  resurrectCollectionTeamChanges.Add(teamChange.Key, teamChange.Value);
                }
              }

              TeamChangeHistory = resurrectCollectionTeamChanges;
            }
          );

        DisconnectHistory = new Dictionary<string, string>();
        TeamChangeHistory = new Dictionary<string, string>();
      }
    }

    public class QueueWorker : BaseWorker
    {
      private List<string> ProcessedQueues = new List<string>();
      public Dictionary<string, bool> Notices = new Dictionary<string, bool>();

      public class QueueElement
      {
        public string id;

        public QueueRequest request;
      }

      public class QueueRequest
      {
        public string name;
        public JObject data;
      }

      private class QueueKickPayload
      {
        public string steam_id;
        public string reason;
        public bool announce;
      }

      private class QueueBanPayload
      {
        public string steam_id;
        public string name;
        public string reason;
      }

      private class QueueDebugLog
      {
        public string message;
      }

      private class QueuePaidAnnounceBan
      {
        public bool broadcast = false;

        public string suspect_name;
        public string suspect_id;

        public string reason;

        public List<string> targets = new List<string>();
      }

      private class QueuePaidAnnounceClean
      {
        public bool broadcast = false;

        public string suspect_name;
        public string suspect_id;

        public List<string> targets = new List<string>();
      }

      class QueueNoticeStateGetPayload
      {
        public string steam_id;
      }

      private class QueueNoticeStateSetPayload
      {
        public string steam_id;
        public bool value;
      }

      private class QueueChatMessage
      {
        public string initiator_name;
        public string initiator_steam_id;

        [CanBeNull] public string target_steam_id;

        public string message;

        public string mode;
      }

      private class QueueExecuteCommand
      {
        public List<string> commands;
      }

      private class QueueDeleteEntityPayload
      {
        public string net_id;
      }


      protected override void OnReady()
      {
        InvokeRepeating(nameof(QueueRetreive), 0f, 1f);
      }

      private void QueueRetreive()
      {
        if (!IsReady())
        {
          return;
        }

        @QueueRetreive((queues) =>
        {
          var responses = new Dictionary<string, object>();

          foreach (var queue in queues)
          {
            if (ProcessedQueues.Contains(queue.id))
            {
              return;
            }

            try
            {
              var response = QueueProcess(queue);

              responses.Add(queue.id, response);

              ProcessedQueues.Add(queue.id);
            }
            catch (Exception exc)
            {
              _RustApp.PrintWarning(
                "Не удалось обработать команду из очереди",
                "Failed to process queue command"
              );

              responses.Add(queue.id, $"!EXCEPTION!:{exc.ToString()}");
            }
          }

          if (responses.Keys.Count == 0)
          {
            return;
          }

          QueueProcess(responses);
        });
      }

      private void @QueueRetreive(Action<List<QueueElement>> callback)
      {
        Request<List<QueueElement>>(QueueUrls.Fetch, RequestMethod.GET, null)
          .Execute(
            (data, raw) => callback(data),
            (err) =>
            {
              //_RustApp.Error(
              //  "Не удалось загрузить задачи из очередей",
              //  "Failed to retreive queue"
              //);
            }
          );
      }

      public void QueueProcess(Dictionary<string, object> responses)
      {
        Request<List<QueueElement>>(QueueUrls.Fetch, RequestMethod.PUT, new { data = responses })
          .Execute(
            (data, raw) =>
            {
              ProcessedQueues = new List<string>();
            },
            (err) =>
            {
              ProcessedQueues = new List<string>();
            }
          );
      }

      private object OnQueueHealthCheck()
      {
        return true;
      }
      public object QueueProcess(QueueElement element)
      {
        switch (element.request.name)
        {
          case "court/health-check":
            {
              return OnQueueHealthCheck();
            }
          case "court/kick":
            {
              return OnQueueKick(JsonConvert.DeserializeObject<QueueKickPayload>(element.request.data.ToString()));
            }
          case "court/ban":
            {
              return OnQueueBan(JsonConvert.DeserializeObject<QueueBanPayload>(element.request.data.ToString()));
            }
          case "court/debug-log":
            {
              return OnQueueDebugLog(JsonConvert.DeserializeObject<QueueDebugLog>(element.request.data.ToString()));
            }
          case "court/notice-state-get":
            {
              return OnNoticeStateGet(JsonConvert.DeserializeObject<QueueNoticeStateGetPayload>(element.request.data.ToString()));
            }
          case "court/notice-state-set":
            {
              return OnNoticeStateSet(JsonConvert.DeserializeObject<QueueNoticeStateSetPayload>(element.request.data.ToString()));
            }
          case "court/chat-message":
            {
              return OnChatMessage(JsonConvert.DeserializeObject<QueueChatMessage>(element.request.data.ToString()));
            }
          case "court/execute-command":
            {
              return OnExecuteCommand(JsonConvert.DeserializeObject<QueueExecuteCommand>(element.request.data.ToString()));
            }
          case "court/delete-entity":
            {
              return OnDeleteEntity(JsonConvert.DeserializeObject<QueueDeleteEntityPayload>(element.request.data.ToString()));
            }

          // Работают только на платном тарифе
          case "court/paid-announce-ban":
            {
              return OnQueuePaidAnnounceBan(JsonConvert.DeserializeObject<QueuePaidAnnounceBan>(element.request.data.ToString()));
            }
          case "court/paid-announce-clean":
            {
              return OnQueuePaidAnnounceClean(JsonConvert.DeserializeObject<QueuePaidAnnounceClean>(element.request.data.ToString()));
            }
          // Конец платных событий

          default:
            {
              _RustApp.Log(
                $"Неизвестная команда из очередей: {element.request.name}",
                $"Unknown queue command: {element.request.name}"
              );
              break;
            }
        }

        return null;
      }

      private object OnDeleteEntity(QueueDeleteEntityPayload payload)
      {
        var ent = BaseNetworkable.serverEntities.ToList().Find(v => v.net.ID.Value.ToString() == payload.net_id);
        if (ent == null)
        {
          return false;
        }

        ent.Kill();

        return true;
      }

      private object OnNoticeStateGet(QueueNoticeStateGetPayload payload)
      {
        if (!Notices.ContainsKey(payload.steam_id))
        {
          return false;
        }

        return Notices[payload.steam_id];
      }

      private object OnNoticeStateSet(QueueNoticeStateSetPayload payload)
      {
        var player = BasePlayer.Find(payload.steam_id);
        if (player == null || !player.IsConnected)
        {
          return "Player not found or offline";
        }

        var over = Interface.Oxide.CallHook("RustApp_CanIgnoreCheck", player);
        if (over != null)
        {
          if (over is string)
          {
            return over;
          }

          return "Plugin declined notice change via hook";
        }

        if (!Notices.ContainsKey(payload.steam_id))
        {
          Notices.Add(payload.steam_id, payload.value);
        }

        Notices[payload.steam_id] = payload.value;

        return NoticeStateSet(player, payload.value);
      }
      private object OnQueueKick(QueueKickPayload payload)
      {
        var success = _RustApp.CloseConnection(payload.steam_id, payload.reason);
        if (!success)
        {
          _RustApp.Log(
            $"Не удалось кикнуть игрока {payload.steam_id}, игрок не найден или оффлайн",
            $"Failed to kick player {payload.steam_id}, player not found or disconnected"
          );
          return "Player not found or offline";
        }

        _RustApp.Log(
          $"Игрок {payload.steam_id} кикнут по причине {payload.reason}",
          $"Player {payload.steam_id} was kicked for {payload.reason}"
        );

        if (payload.announce)
        {
          // _RustApp.SendGlobalMessage("Игрок был кикнут");
        }

        return true;
      }

      private object OnQueueBan(QueueBanPayload payload)
      {
        // Вызов хука на возможность игнорировать проверку
        var over = Interface.Oxide.CallHook("RustApp_CanIgnoreBan", payload.steam_id);
        if (over != null)
        {
          return "Plugin overrided queue-ban";
        }

        if (_Settings.ban_enable_broadcast)
        {
          foreach (var player in BasePlayer.activePlayerList)
          {
            var msg = _RustApp.lang.GetMessage("System.Ban.Broadcast", _RustApp, player.UserIDString).Replace("%TARGET%", payload.name).Replace("%REASON%", payload.reason);

            _RustApp.SendMessage(player, msg);
          }
        }

        // Ip doesnot matter in this context
        _RustApp._Worker.Ban.FetchBan(payload.steam_id, "1.1.1.1");

        return true;
      }

      private object OnQueueDebugLog(QueueDebugLog payload)
      {
        _RustApp.PrintWarning(payload.message);

        return true;
      }

      private object OnQueuePaidAnnounceBan(QueuePaidAnnounceBan payload)
      {
        int received = 0;
        int error = 0;

        Interface.Oxide.CallHook("RustApp_OnPaidAnnounceBan", payload.suspect_id, payload.targets, payload.reason);

        if (!payload.broadcast)
        {
          return new { received, error };
        }

        foreach (var check in payload.targets)
        {
          var player = BasePlayer.Find(check);
          if (player == null || !player.IsConnected)
          {
            error++;
            continue;
          }

          var msg = _RustApp.lang.GetMessage("Paid.Announce.Ban", _RustApp, player.UserIDString);

          msg = msg.Replace("%SUSPECT_NAME%", payload.suspect_name).Replace("%SUSPECT_ID%", payload.suspect_id).Replace("%REASON%", payload.reason);

          _RustApp.SoundErrorToast(player, msg);

          received++;
        }

        return new { received, error };
      }

      private object OnQueuePaidAnnounceClean(QueuePaidAnnounceClean payload)
      {
        int received = 0;
        int error = 0;

        if (!_Checks.LastChecks.ContainsKey(payload.suspect_id))
        {
          _Checks.LastChecks.Add(payload.suspect_id, _RustApp.CurrentTime());
        }
        else
        {
          _Checks.LastChecks[payload.suspect_id] = _RustApp.CurrentTime();
        }

        Interface.Oxide.CallHook("RustApp_OnPaidAnnounceClean", payload.suspect_id, payload.targets);

        CheckInfo.write(_Checks);

        if (!payload.broadcast)
        {
          return new { received, error };
        }

        foreach (var check in payload.targets)
        {
          var player = BasePlayer.Find(check);
          if (player == null || !player.IsConnected)
          { 
            error++;
            continue;
          }

          var msg = _RustApp.lang.GetMessage("Paid.Announce.Clean", _RustApp, player.UserIDString);

          msg = msg.Replace("%SUSPECT_NAME%", payload.suspect_name).Replace("%SUSPECT_ID%", payload.suspect_id);

          _RustApp.SoundInfoToast(player, msg);

          received++;
        }

        return new { received, error };
      }

      private object OnExecuteCommand(QueueExecuteCommand payload)
      {
        var responses = new List<object>();

        var index = 0;

        payload.commands.ForEach((v) =>
        {
          if (_Settings.custom_actions_allow)
          {
            var res = ConsoleSystem.Run(ConsoleSystem.Option.Server, v);

            try
            {
              responses.Add(new
              {
                success = true,
                command = v,
                data = JsonConvert.DeserializeObject(res?.ToString() ?? "Command without response")
              });
            }
            catch
            {
              responses.Add(new
              {
                success = true,
                command = v,
                data = res
              });
            }
          }
          else
          {
            responses.Add(new
            {
              success = false,
              command = v,
              data = "Custom actions are disabled"
            });
          }

          index++;
        });

        return responses;
      }

      private object OnChatMessage(QueueChatMessage payload)
      {

        if (payload.target_steam_id is string)
        {
          var message = _RustApp.lang.GetMessage("System.Chat.Direct", _RustApp, payload.target_steam_id).Replace("%CLIENT_TAG%", payload.initiator_name).Replace("%MSG%", payload.message);

          var player = BasePlayer.Find(payload.target_steam_id);

          if (player == null || !player.IsConnected)
          {
            return "Player not found or offline";
          }

          _RustApp.SendMessage(player, message);
          _RustApp.SoundToast(player, _RustApp.lang.GetMessage("Chat.Direct.Toast", _RustApp, player.UserIDString), 2);
        }
        else
        {
          foreach (var player in BasePlayer.activePlayerList)
          {
            var message = _RustApp.lang.GetMessage("System.Chat.Global", _RustApp, player.UserIDString).Replace("%CLIENT_TAG%", payload.initiator_name).Replace("%MSG%", payload.message);

            _RustApp.SendMessage(player, message);
          }
        }


        return true;
      }

      private object NoticeStateSet(BasePlayer player, bool value)
      {
        if (!value)
        {
          _RustApp.Log(
            $"С игрока {player.userID} снято уведомление о проверке",
            $"Notify about check was removed from player {player.userID}"
          );

          Interface.Oxide.CallHook("RustApp_OnCheckNoticeHidden", player);

          CuiHelper.DestroyUi(player, CheckLayer);
        }
        else
        {
          _RustApp.Log(
            $"Игрок {player.userID} уведомлён о проверке",
            $"Player {player.userID} was notified about check"
          );

          Interface.Oxide.CallHook("RustApp_OnCheckNoticeShowed", player);

          _RustApp.DrawInterface(player);
        }

        return true;
      }
    }

    public class BaseWorker : MonoBehaviour
    {
      protected string secret = string.Empty;

      public void Auth(string secret)
      {
        this.secret = secret;

        OnReady();
      }

      protected StableRequest<T> Request<T>(string url, RequestMethod method, object? data = null)
      {
        var request = new StableRequest<T>(url, method, data, this.secret);

        return request;
      }

      public bool IsReady()
      {
        if (_RustApp == null || !_RustApp.IsLoaded)
        {
          Destroy(this);
          return false;
        }

        if (secret == null)
        {
          Interface.Oxide.LogWarning("Unexpected exception, secret is missing. Contact support: https://vk.com/rustapp");
          return false;
        }

        return true;
      }

      public bool IsAuthed()
      {
        return _RustApp != null && _RustApp.IsLoaded && secret != null && secret.Length > 0;
      }

      protected virtual void OnReady() { }
    }

    #endregion

    #region Configuration

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

    #endregion

    #region Interfaces

    private static string ReportLayer = "UI_RP_ReportPanelUI";
    private void DrawReportInterface(BasePlayer player, int page = 0, string search = "", bool redraw = false)
    {
      var lineAmount = 6;
      var lineMargin = 8;

      var size = (float)(700 - lineMargin * lineAmount) / lineAmount;
      var list = BasePlayer.activePlayerList
          .ToList();

      var finalList = list
          .FindAll(v => v.displayName.ToLower().Contains(search) || v.UserIDString.ToLower().Contains(search) || search == null)
          .Skip(page * 18)
          .Take(18);

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

      //↓ ↑

      container.Add(new CuiButton()
      {
        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.5", OffsetMin = "0 0", OffsetMax = "0 -4" },
        Button = { Color = HexToRustFormat($"#{(list.Count > 18 && finalList.Count() == 18 ? "D0C6BD4D" : "D0C6BD33")}"), Command = list.Count > 18 && finalList.Count() == 18 ? $"UI_RP_ReportPanel search {page + 1}" : "" },
        Text = { Text = "↓", Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf", FontSize = 24, Color = HexToRustFormat($"{(list.Count > 18 && finalList.Count() == 18 ? "D0C6BD" : "D0C6BD4D")}") }
      }, ReportLayer + ".R", ReportLayer + ".RD");

      container.Add(new CuiButton()
      {
        RectTransform = { AnchorMin = "0 0.5", AnchorMax = "1 1", OffsetMin = "0 4", OffsetMax = "0 0" },
        Button = { Color = HexToRustFormat($"#{(page == 0 ? "D0C6BD33" : "D0C6BD4D")}"), Command = page == 0 ? "" : $"UI_RP_ReportPanel search {page - 1}" },
        Text = { Text = "↑", Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf", FontSize = 24, Color = HexToRustFormat($"{(page == 0 ? "D0C6BD4D" : "D0C6BD")}") }
      }, ReportLayer + ".R", ReportLayer + ".RU");

      container.Add(new CuiPanel
      {
        RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-250 8", OffsetMax = "0 43" },
        Image = { Color = HexToRustFormat("#D0C6BD33") }
      }, ReportLayer + ".C", ReportLayer + ".S");

      container.Add(new CuiElement
      {
        Parent = ReportLayer + ".S",
        Components =
            {
                new CuiInputFieldComponent { Text = $"{lang.GetMessage("Header.Search.Placeholder", this, player.UserIDString)}", FontSize = 14, Font = "robotocondensed-regular.ttf", Color = HexToRustFormat("#D0C6BD80"), Align = TextAnchor.MiddleLeft, Command = "UI_RP_ReportPanel search 0", NeedsKeyboard = true},
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

      for (var y = 0; y < 3; y++)
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

            container.Add(new CuiButton()
            {
              RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
              Button = { Color = "0 0 0 0", Command = $"UI_RP_ReportPanel show {target.UserIDString} {x * size + lineMargin * x} -{(y + 1) * size + lineMargin * y} {(x + 1) * size + lineMargin * x} -{y * size + lineMargin * y}  {x >= 3}" },
              Text = { Text = "" }
            }, ReportLayer + $".{target.UserIDString}");

            var was_checked = _Checks.LastChecks.ContainsKey(target.UserIDString) && CurrentTime() - _Checks.LastChecks[target.UserIDString] < _Settings.report_ui_show_check_in * 24 * 60 * 60;
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

    private const string CheckLayer = "RP_PrivateLayer";

    private void DrawInterface(BasePlayer player)
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

    #endregion

    #region Variables

    private CourtWorker _Worker;
    private Dictionary<ulong, double> _Cooldowns = new Dictionary<ulong, double>();
    private Dictionary<string, HitInfo> _WoundedHits = new Dictionary<string, HitInfo>();

    private static RustApp _RustApp;
    private static Configuration _Settings;
    private static CheckInfo _Checks = CheckInfo.Read();



    // References for RB plugins to get RB status
    [PluginReference] private Plugin NoEscape, RaidZone, RaidBlock, MultiFighting, TirifyGamePluginRust, ExtRaidBlock;

    #endregion

    #region Initialization

    private void OnServerInitialized()
    {
      _RustApp = this;

      if (plugins.Find("RustAppLite") != null && plugins.Find("RustAppLite").IsLoaded)
      {
        Error(
          "Обнаружена 'Lite' версия плагина, для начала удалите RustAppLite.cs",
          "Detected 'Lite' version of plugin, delete RustAppLite.cs to start"
        );
        return;
      }

      if (plugins.Find("ImageLibrary") == null)
      {
        Error(
          "Для работы плагина необходим установленный ImageLibrary",
          "For plugin correct works need to install ImageLibrary"
        );
        return;
      }

      timer.Once(1, () =>
      {
        _Worker = ServerMgr.Instance.gameObject.AddComponent<CourtWorker>();
      });
    }

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


    private void Unload()
    {
      _Worker.Update.IsDead = true;
      _Worker.Update.SendUpdate();

      UnityEngine.Object.Destroy(_Worker);

      foreach (var player in BasePlayer.activePlayerList)
      {
        CuiHelper.DestroyUi(player, CheckLayer);
        CuiHelper.DestroyUi(player, ReportLayer);
      }
    }

    #endregion

    #region API

    private void RA_BanPlayer(string steam_id, string reason, string duration, bool global, bool ban_ip, string comment = "")
    {
      _Worker.Action.SendBan(steam_id, reason, duration, global, ban_ip, comment);
    }

    private void RA_DirectMessageHandler(string from, string to, string message)
    {
      _Worker?.Update.SaveChat(new PluginChatMessageEntry
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

      var was_checked = _Checks.LastChecks.ContainsKey(target_steam_id) && CurrentTime() - _Checks.LastChecks[target_steam_id] < _Settings.report_ui_show_check_in * 24 * 60 * 60;
      Interface.Oxide.CallHook("RustApp_OnPlayerReported", initiator_steam_id, target_steam_id, reason, message, was_checked);

      _Worker?.Update.SaveReport(new PluginReportEntry
      {
        initiator_steam_id = initiator_steam_id,
        target_steam_id = target_steam_id,
        sub_targets_steam_ids = new List<string>(),
        message = message,
        reason = reason
      });
    }

    private void RA_CreateAlert(Plugin plugin, string message, object data = null, object meta = null)
    {
      _Worker?.Action.SendCustomAlert(plugin, message, data, meta);
    }

    #endregion

    #region Interface




    #endregion

    #region Hooks

    private void OnPlayerWound( BasePlayer instance, HitInfo info )
    {
      _WoundedHits[instance.UserIDString] = info;
    }
    
    private void OnPlayerRespawn( BasePlayer instance )
    {
      if (_WoundedHits.ContainsKey(instance.UserIDString)) {
        _WoundedHits.Remove(instance.UserIDString);
      }
    }

    void OnPlayerRecovered(BasePlayer player)
    {
      if (_WoundedHits.ContainsKey(player.UserIDString)) {
        _WoundedHits.Remove(player.UserIDString);
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
        
          _Worker.Update.SaveKill(new PluginKillEntry {
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
          Error("Обнаружена ошибка в бета-алгоритме, сообщите разработчикам", "Detect error in beta-mechanism");
          PrintError(exc.ToString());
        }
      });
    }
 
    public class CombatLogEventExtended {
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

      public CombatLogEventExtended(CombatLog.Event ev) {
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

    private List<CombatLogEventExtended> GetCorrectCombatlog(ulong target, float timeLimit) {
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

      var container = new List<CombatLogEventExtended>();

      CombatLog.Event previousEvent = combatlog.ElementAtOrDefault(0);
      if (previousEvent == null) {
        return null;
      }
      
      foreach (var ev in combatlog) {
        var timeLeft = UnityEngine.Time.realtimeSinceStartup - ev.time - (float) ConVar.Server.combatlogdelay;

        if (ev.target != "player" && ev.target != "you") {
          continue;
        }

        if (ev.info == "killed" && Math.Abs(ev.time - timeLimit) > 1) {
          break;
        }

        var dto = new CombatLogEventExtended(ev);

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
      var initiatorId = info.InitiatorPlayer?.userID;
      if (initiatorId == player.userID || info.InitiatorPlayer == null) { 
        if (_WoundedHits.ContainsKey(player.UserIDString)) {
          return _WoundedHits[player.UserIDString];
        }
      }

      return info;
    }

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

      _Worker.Update.SaveAlert(new PluginPlayerAlertEntry
      {
        type = PlayerAlertType.dug_up_stash,
        meta = new PluginPlayerAlertDugUpStashMeta
        {
          owner_steam_id = owner.ToString(),
          position = player.transform.position.ToString(),
          square = GridReference(player.transform.position),
          steam_id = player.UserIDString
        }
      });
    }

    private void CanUserLogin(string name, string id, string ipAddress)
    {
      _Worker?.Ban.FetchBan(id, ipAddress);
    }

    private void OnPlayerDisconnected(BasePlayer player, string reason)
    {
      if (_Worker.Queue.Notices.ContainsKey(player.UserIDString))
      {
        _Worker.Queue.Notices.Remove(player.UserIDString);
      }

      _Worker?.Update.SaveDisconnect(player.UserIDString, reason);
    }

    private void OnClientDisconnect(Network.Connection connection, string reason)
    {
      if (_Worker.Queue.Notices.ContainsKey(connection.userid.ToString()))
      {
        _Worker.Queue.Notices.Remove(connection.userid.ToString());
      }

      _Worker?.Update.SaveDisconnect(connection.userid.ToString(), reason);
    }

    private void OnTeamKick(RelationshipManager.PlayerTeam team, BasePlayer player, ulong target)
    {
      _Worker?.Update.SaveTeamHistory(player.UserIDString, target.ToString());
    }

    private void OnTeamDisband(RelationshipManager.PlayerTeam team)
    {
      team.members.ForEach(v => _Worker?.Update.SaveTeamHistory(v.ToString(), v.ToString()));
    }

    private void OnTeamLeave(RelationshipManager.PlayerTeam team, BasePlayer player)
    {
      if (team.members.Count == 1)
      {
        return;
      }

      _Worker?.Update.SaveTeamHistory(player.UserIDString, player.UserIDString);
    }

    private void OnPlayerChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel)
    {
      if (channel != ConVar.Chat.ChatChannel.Team && channel != ConVar.Chat.ChatChannel.Global && channel != ConVar.Chat.ChatChannel.Local)
      {
        return;
      }

      _Worker?.Update.SaveChat(new PluginChatMessageEntry
      {
        steam_id = player.UserIDString,

        is_team = channel == ConVar.Chat.ChatChannel.Team,

        text = message
      });
    }

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

      if (_Worker?.Update == null || !_Worker.Update.IsReady())
      {
        return;
      }

      _Worker.Update.SaveDestroyedSign(entity.net.ID.Value.ToString());
    }

    private void OnNewSave(string saveName)
    {
      // Remove in 5 minutes
      timer.Once(300, () =>
      {
        _Worker.Action.SendWipe((a) => { });
      });
    }

    #endregion

    #region UX

    private void CmdChatContact(BasePlayer player, string command, string[] args)
    {
      if (args.Length == 0)
      {
        SendMessage(player, lang.GetMessage("Contact.Error", this, player.UserIDString));
        return;
      }

      _Worker?.Action.SendContact(player.UserIDString, String.Join(" ", args), (accepted) =>
      {
        if (!accepted)
        {
          return;
        }

        SendMessage(player, lang.GetMessage("Contact.Sent", this, player.UserIDString) + $"<color=#8393cd> {String.Join(" ", args)}</color>");
        SendMessage(player, lang.GetMessage("Contact.SentWait", this, player.UserIDString));
      });
    }

    #endregion

    #region Commands

    [ConsoleCommand("UI_RP_ReportPanel")]
    private void CmdConsoleReportPanel(ConsoleSystem.Arg args)
    {
      var player = args.Player();
      if (player == null || !args.HasArgs(1))
      {
        return;
      }

      switch (args.Args[0].ToLower())
      {
        case "search":
          {
            int page = args.HasArgs(2) ? int.Parse(args.Args[1]) : 0;
            string search = args.HasArgs(3) ? args.Args[2] : "";

            Effect effect = new Effect("assets/prefabs/tools/detonator/effects/unpress.prefab", player, 0, new Vector3(), new Vector3());
            EffectNetwork.Send(effect, player.Connection);

            DrawReportInterface(player, page, search, true);
            break;
          }
        case "show":
          {
            string targetId = args.Args[1];
            BasePlayer target = BasePlayer.Find(targetId) ?? BasePlayer.FindSleeping(targetId);

            Effect effect = new Effect("assets/prefabs/tools/detonator/effects/unpress.prefab", player, 0, new Vector3(), new Vector3());
            EffectNetwork.Send(effect, player.Connection);

            CuiElementContainer container = new CuiElementContainer();
            CuiHelper.DestroyUi(player, ReportLayer + $".T");

            container.Add(new CuiPanel
            {
              RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{args.Args[2]} {args.Args[3]}", OffsetMax = $"{args.Args[4]} {args.Args[5]}" },
              Image = { Color = "0 0 0 1" }
            }, ReportLayer + $".L", ReportLayer + $".T");


            container.Add(new CuiButton()
            {
              RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"-500 -500", OffsetMax = $"500 500" },
              Button = { Close = $"{ReportLayer}.T", Color = "0 0 0 1", Sprite = "assets/content/ui/ui.circlegradient.png" }
            }, ReportLayer + $".T");


            bool leftAlign = bool.Parse(args.Args[6]);
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

            var was_checked = _Checks.LastChecks.ContainsKey(target.UserIDString) && CurrentTime() - _Checks.LastChecks[target.UserIDString] < _Settings.report_ui_show_check_in * 24 * 60 * 60;
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

              container.Add(new CuiButton()
              {
                RectTransform = { AnchorMin = $"{(leftAlign ? 0 : 1)} 0", AnchorMax = $"{(leftAlign ? 0 : 1)} 0", OffsetMin = $"{(leftAlign ? -offXMax : offXMin)} 15", OffsetMax = $"{(leftAlign ? -offXMin : offXMax)} 45" },
                Button = { FadeIn = 0.4f + i * 0.2f, Color = HexToRustFormat("#D0C6BD4D"), Command = $"UI_RP_ReportPanel report {target.UserIDString} {_Settings.report_ui_reasons[i].Replace(" ", "0")}" },
                Text = { FadeIn = 0.4f + i * 0.2f, Text = $"{_Settings.report_ui_reasons[i]}", Align = TextAnchor.MiddleCenter, Color = HexToRustFormat("#D0C6BD"), Font = "robotocondensed-bold.ttf", FontSize = 16 }
              }, ReportLayer + $".T");
            }

            CuiHelper.AddUi(player, container);
            break;
          }
        case "report":
          {
            if (!_Cooldowns.ContainsKey(player.userID))
            {
              _Cooldowns.Add(player.userID, 0);
            }

            if (_Cooldowns[player.userID] > CurrentTime())
            {
              var msg = lang.GetMessage("Cooldown", this, player.UserIDString).Replace("%TIME%",
                  $"{(_Cooldowns[player.userID] - CurrentTime()).ToString("0")}");

              SoundToast(player, msg, 1);
              return;
            }

            string targetId = args.Args[1];
            string reason = args.Args[2].Replace("0", "");

            BasePlayer target = BasePlayer.Find(targetId) ?? BasePlayer.FindSleeping(targetId);

            RA_ReportSend(player.UserIDString, target.UserIDString, reason, "");
            CuiHelper.DestroyUi(player, ReportLayer);

            SoundToast(player, lang.GetMessage("Sent", this, player.UserIDString), 2);

            if (!_Cooldowns.ContainsKey(player.userID))
            {
              _Cooldowns.Add(player.userID, 0);
            }

            _Cooldowns[player.userID] = CurrentTime() + _Settings.report_ui_cooldown;
            break;
          }
      }
    }

    private void ChatCmdReport(BasePlayer player)
    {
      var over = Interface.Oxide.CallHook("RustApp_CanOpenReportUI", player);
      if (over != null)
      {
        return;
      }

      if (!_Cooldowns.ContainsKey(player.userID))
      {
        _Cooldowns.Add(player.userID, 0);
      }

      if (_Cooldowns[player.userID] > CurrentTime())
      {
        var msg = lang.GetMessage("Cooldown", this, player.UserIDString).Replace("%TIME%",
            $"{(_Cooldowns[player.userID] - CurrentTime()).ToString("0")}");

        SoundToast(player, msg, 1);
        return;
      }

      DrawReportInterface(player);
    }

    [ConsoleCommand("ra.help")]
    private void CmdConsoleHelp(ConsoleSystem.Arg args)
    {
      if (args.Player() != null)
      {
        return;
      }

      Log(
        "Помощь по командам RustApp",
        "Command help for RustApp"
      );
      Log(
        "ra.debug - показать список последних ошибок",
        "ra.debug - show list of recent errors"
      );
      Log(
        "ra.pair <key> - подключение сервера (ключ можно получить на сайте)",
        "ra.pair <key> - connect server (key can be obtained on the website)"
      );
    }

    [ConsoleCommand("ra.debug")]
    private void CmdConsoleStatus(ConsoleSystem.Arg args)
    {
      if (!args.IsAdmin)
      {
        return;
      }

      if (LastException.History.Count == 0)
      {
        Log(
          "Ни одной ошибки не найдено",
          "No errors found"
        );
        return;
      }

      Log(
        "Список последних ошибок в запросах:",
        "List of recent errors in requests:"
      );


      foreach (var value in LastException.History)
      {
        var diff = (DateTime.Now - value.time).TotalSeconds;

        string diffText = diff.ToString("F2") + Msg("сек. назад", "secs ago");

        Puts($"{value.module} ({diffText})");

        if (args.Args?.Length >= 1)
        {
          Puts($"> {value.payload} [{value.secret}]");
        }
        Puts($"< {value.response}");
      }
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
        Log(
          "Неверный формат команды!\nПравильный формат: ra.ban <steam-id> <причина> <время (необяз)>\n\nВозможны дополнительные опции:\n'--ban-ip' - заблокирует IP\n'--global' - заблокирует на всех серверах\n\nПример блокировки с IP, на всех серверах: ra.ban 7656119812110397 \"cheat\" 7d --ban-ip --global",
          "Incorrect command format!\nCorrect format: ra.ban <steam-id> <reason> <time (optional)>\n\nAdditional options are available:\n'--ban-ip' - bans IP\n'--global' - bans globally\n\nExample of banning with IP, globally: ra.ban 7656119812110397 \"cheat\" 7d --ban-ip --global"
        );
        return;
      }

      var steam_id = clearArgs[0];
      var reason = clearArgs[1];
      var duration = clearArgs.Count() == 3 ? clearArgs[2] : "";


      var global_bool = args.FullString.Contains("--global");
      var ip_bool = args.FullString.Contains("--ban-ip");

      _Worker.Action.SendBan(steam_id, reason, duration, global_bool, ip_bool);
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
        Log(
          "Неверный формат команды!\nПравильный формат: ra.unban <steam-id>",
          "Incorrect command format!\nCorrect format: ra.unban <steam-id>"
        );
        return;
      }

      var steam_id = clearArgs[0];

      _Worker.Action.SendBanDelete(steam_id);
    }

    [ConsoleCommand("ra.pair")]
    private void CmdConsoleCourtSetup(ConsoleSystem.Arg args)
    {
      if (args.Player() != null)
      {
        return;
      }

      if (!args.HasArgs(1))
      {
        return;
      }

      _Worker?.StartPair(args.Args[0]);
    }

    #endregion

    #region User Manipulation 

    private void SendGlobalMessage(string message)
    {
      foreach (var player in BasePlayer.activePlayerList)
      {
        SendMessage(player, message);
      }
    }

    private bool SendMessage(string steamId, string message)
    {
      var player = BasePlayer.Find(steamId);
      if (player == null || !player.IsConnected)
      {
        return false;
      }

      SendMessage(player, message);

      return true;
    }

    private void SendMessage(BasePlayer player, string message, string initiator_steam_id = "")
    {
      if (initiator_steam_id.Length == 0)
      {
        initiator_steam_id = _Settings.chat_default_avatar_steamid;
      }

      player.SendConsoleCommand("chat.add", 0, initiator_steam_id, message);
    }

    private void SoundInfoToast(BasePlayer player, string text)
    {
      SoundToast(player, text, 2);
    }

    private void SoundErrorToast(BasePlayer player, string text)
    {
      SoundToast(player, text, 1);
    }

    private void SoundToast(BasePlayer player, string text, int type)
    {
      Effect effect = new Effect("assets/bundled/prefabs/fx/notice/item.select.fx.prefab", player, 0, new Vector3(), new Vector3());
      EffectNetwork.Send(effect, player.Connection);

      player.Command("gametip.showtoast", type, text, 1);
    }

    private bool CloseConnection(string steamId, string reason)
    {
      Log(
        $"Закрываем соединение с {steamId}: {reason}",
        $"Closing connection with {steamId}: {reason}"
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

    #endregion

    #region Utils

    private double CurrentTime() => DateTime.Now.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

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

    private long getUnixTime()
    {
      return ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
    }

    private Network.Connection? getPlayerConnection(string steamId)
    {
      var player = BasePlayer.Find(steamId);
      if (player != null && player.IsConnected)
      {
        return player.Connection;
      }

      var joining = ServerMgr.Instance.connectionQueue.joining.Find(v => v.userid.ToString() == steamId);
      if (joining != null)
      {
        return joining;
      }

      var queued = ServerMgr.Instance.connectionQueue.queue.Find(v => v.userid.ToString() == steamId);
      if (queued != null)
      {
        return queued;
      }

      return null;
    }

    #endregion

    #region Messages

    public string Msg(string ru, string en)
    {
#if RU
      return ru;
#else
      return en;
#endif
    }

    public void Log(string ru, string en)
    {
#if RU
      Puts(ru);
#else
      Puts(en);
#endif
    }

    public void Warning(string ru, string en)
    {
#if RU
      PrintWarning(ru);
#else
      PrintWarning(en);
#endif
    }

    public void Error(string ru, string en)
    {
#if RU
      PrintError(ru);
#else
      PrintError(en);
#endif
    }

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
      _Worker.Action.SendSignage(new SignageUpdate(player, signage, textureIndex, url));
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

      _Worker.Action.SendSignage(new SignageUpdate(player, signage, (uint)textureIndex));
    }

    private void OnItemPainted(PaintedItemStorageEntity entity, Item item, BasePlayer player, byte[] image)
    {
      if (entity._currentImageCrc == 0)
      {
        return;
      }

      PaintedItemUpdate update = new PaintedItemUpdate(player, entity, item, image);

      _Worker.Action.SendSignage(new PaintedItemUpdate(player, entity, item, image));
    }

    private void OnFireworkDesignChanged(PatternFirework firework, ProtoBuf.PatternFirework.Design design, BasePlayer player)
    {
      if (design?.stars == null || design.stars.Count == 0)
      {
        return;
      }

      _Worker.Action.SendSignage(new FireworkUpdate(player, firework));
    }

    private object CanUpdateSign(BasePlayer player, BaseEntity entity)
    {
      // TODO: Logic to protect user from sign-usage

      //Client side the sign will still be updated if we block it here. We destroy the entity client side to force a redraw of the image.
      NextTick(() =>
      {
        entity.DestroyOnClient(player.Connection);
        entity.SendNetworkUpdate();
      });

      return null;
    }

    private object OnFireworkDesignChange(PatternFirework firework, ProtoBuf.PatternFirework.Design design, BasePlayer player)
    {
      // TODO: Logic to protect user from sign-usage

      return null;
    }

    #endregion

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

        _Worker.Action.SendSignage(new SignageUpdate(player, signage, 0));
      });
    }
  }
}