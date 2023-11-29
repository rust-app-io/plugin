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


namespace Oxide.Plugins
{
  [Info("RustApp.IO", "Hougan & Xacku & Olkuts", "1.0.0")]
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

    private class StableRequest<T>
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

      public StableRequest(string url, RequestMethod method, object data, string secret, string name)
      {
        this.url = url;
        this.method = method;

        this.data = data;
        this.headers = DefaultHeaders(secret);

        this.onException += (a) =>
        {
          AvailableRequests.Exceptions.Insert(0, new LastException(name, data, a, secret));

          if (AvailableRequests.Exceptions.Count > 10)
          {
            AvailableRequests.Exceptions.RemoveRange(10, 1);
          }
        };
      }

      public double Elapsed()
      {
        return (DateTime.Now - _created).TotalMilliseconds;
      }

      public void Execute()
      {
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
        var body = (request.downloadHandler?.text ?? "").ToLower();

        if (isError)
        {

          if (body.Contains("cloudflare"))
          {
            onException.Invoke("cloudflare");
          }
          else
          {
            onException.Invoke(body);
          }
        }
        else
        {
          try
          {
            var obj = JsonConvert.DeserializeObject<T>(request.downloadHandler?.text);
            onComplete.Invoke(obj, request.downloadHandler?.text);
          }
          catch (Exception parseException)
          {
            var str = typeof(T).ToString();
          }
        }
      }
    }

    private class LastException
    {
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

    private class AvailableRequests
    {
      public static string COURT_URL = "https://court.rustapp.io/plugin";
      public static string QUEUE_URL = "https://queue.rustapp.io";

      public static List<LastException> Exceptions = new List<LastException>();

      public static void Validate(string secret, [CanBeNull] Action onSuccess, [CanBeNull] Action<string> onException)
      {
        var request = new StableRequest<object>($"{COURT_URL}/", RequestMethod.GET, null, secret, "Validate configuration");

        _RustApp.Puts(_RustApp.msg(
          "Подождите, происходит проверка секретного ключа!",
          "Please hold-on, validating your api-key!"
        ));

        request.onComplete += (a, b) => onSuccess?.Invoke();
        request.onException += (a) =>
        {
          onException?.Invoke(_RustApp.msg($"Ошибка подключения ключа: {a}", $"Failed to validate key: {a}"));
        };

        request.Execute();
      }

      public static void SendUpdate(string secret, PluginStatePayload payload)
      {
        var request = new StableRequest<object>($"{COURT_URL}/state", RequestMethod.PUT, payload, secret, "Updating state");

        request.onException += (a) =>
        {
          _RustApp.PrintWarning(_RustApp.msg(
            $"Не удалось отправить информацию об игроках (Напишите 'ra_status' для деталей)",
            $"Failed to send players update (Use 'ra_status' for details)"
          ));
        };

        request.Execute();
      }

      public static void SendContact(string secret, PluginContactPayload payload, Action<bool> callback)
      {
        var request = new StableRequest<object>($"{COURT_URL}/contact", RequestMethod.POST, payload, secret, "Sending contact");

        request.onException += (a) => callback(false);
        request.onComplete += (a, b) => callback(true);

        request.Execute();
      }

      public static void SendChatMessages(string secret, PluginChatMessagesPayload payload, Action<PluginChatMessagesPayload> fallback)
      {
        var request = new StableRequest<object>($"{COURT_URL}/chat", RequestMethod.POST, payload, secret, "Updating state");

        request.onException += (a) =>
        {
          fallback(payload);
        };

        request.Execute();
      }

      public static void SendReports(string secret, PluginReportsPayload payload, Action<PluginReportsPayload> fallback)
      {
        var request = new StableRequest<object>($"{COURT_URL}/reports", RequestMethod.POST, payload, secret, "Updating reports");

        request.onException += (a) =>
        {
          fallback(payload);
        };

        request.Execute();
      }

      public static void QueueRetreive(string secret, Action<List<QueueElement>> callback)
      {
        var request = new StableRequest<List<QueueElement>>($"{QUEUE_URL}/", RequestMethod.GET, null, secret, "Queue retreive");

        request.onComplete += (a, b) =>
        {
          callback(a);
        };

        request.Execute();
      }

      public static void QueueProcess(string secret, Dictionary<string, object> responses)
      {
        var request = new StableRequest<List<QueueElement>>($"{QUEUE_URL}/", RequestMethod.PUT, new { data = responses }, secret, "Queue process");

        request.Execute();
      }
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

    class PluginReportsPayload
    {
      public List<PluginReportEntry> reports = new List<PluginReportEntry>();
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

    class PluginStatePayload
    {
      public int slots = ConVar.Server.maxplayers;
      public string version = _RustApp.Version.ToString();
      public string performance = _RustApp.TotalHookTime.ToString();

      public List<PluginPlayerPayload> players = new List<PluginPlayerPayload>();

      public PluginStatePayload(List<PluginPlayerPayload> players)
      {
        this.players = players;
      }
    }

    class PluginContactPayload
    {
      public string steam_id;
      public string message;

      public PluginContactPayload(string steam_id, string message)
      {
        this.steam_id = steam_id;
        this.message = message;
      }
    }

    class PluginPlayerPayload
    {
      public string steam_id;
      public string steam_name;
      public string ip;

      [CanBeNull] public string position;
      [CanBeNull] public string rotation;

      public bool can_build = false;
      public bool is_raiding = false;

      public string status;

      public List<string> team = new List<string>();

      public static PluginPlayerPayload FromPlayer(BasePlayer player)
      {
        var payload = new PluginPlayerPayload();

        payload.position = player.transform.position.ToString();

        payload.rotation = player.eyes.rotation.ToString();

        payload.steam_id = player.UserIDString;
        payload.steam_name = player.displayName;
        payload.ip = player.Connection.IPAddressWithoutPort();

        payload.status = "active";

        payload.can_build = player.IsBuildingAuthed();

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
        payload.steam_name = connection.username;
        payload.ip = connection.IPAddressWithoutPort();

        payload.status = status;

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
    }

    public class MetaInfo
    {
      public static MetaInfo Read()
      {
        if (!Interface.Oxide.DataFileSystem.ExistsDatafile("court_cloud_credentials"))
        {
          return null;
        }

        return Interface.Oxide.DataFileSystem.ReadObject<MetaInfo>("court_cloud_credentials");
      }

      public static void write(MetaInfo courtMeta)
      {
        Interface.Oxide.DataFileSystem.WriteObject("court_cloud_credentials", courtMeta);
      }

      [JsonProperty("It is access for RustApp Court, never edit or share it")]
      public string Value;
    }

    public class CourtWorker : MonoBehaviour
    {
      private List<PluginReportEntry> reports = new List<PluginReportEntry>();
      private List<PluginChatMessageEntry> messages = new List<PluginChatMessageEntry>();

      [CanBeNull] private string secret;

      public void Awake()
      {
        var meta = MetaInfo.Read();

        _RustApp.Puts(_RustApp.msg(
          "Добро пожаловать в RustApp.IO! Подключение к серверам панели...",
          "Welcome to the RustApp.IO! Connecting to servers..."
          ));

        if (meta == null)
        {
          _RustApp.PrintError(_RustApp.msg(
            "Плагин не настроен, откройте раздел 'сервера' и нажмите 'подключить' возле нужного сервера",
            "Plugin is not configured, open 'servers' sections and press 'connect' over selected server"
          ));
          _RustApp.PrintError(_RustApp.msg(
            "Если у вас остались вопросы: https://vk.com/rustapp",
            "If you have any question, please contact: https://vk.com/rustapp"
          ));
          return;
        }

        AvailableRequests.Validate(meta.Value, () =>
        {
          _RustApp.Puts(_RustApp.msg(
            $"Соединение успешно установлено, плагин корректно настроен",
            "Connection is established, plugin is configured"
          ));

          this.StartCycle(meta.Value);
        }, (a) =>
        {
          if (a.Contains("cloudflare"))
          {
            _RustApp.PrintWarning(_RustApp.msg(
              $"RustApp.IO временно недоступен, переподключение черз 10 секунд",
              $"RustApp.IO is temporary unavailable, retry in 10 secs"
            ));

            Invoke(nameof(Awake), 10);
            return;
          }

          _RustApp.PrintError(_RustApp.msg(
            $"Ключ не прошёл проверку: {a}",
            $"Key was not validated: ${a}"
          ));
        });
      }

      public void StartCycle(string secret)
      {
        this.secret = secret;

        CancelInvoke(nameof(SendUpdate));
        CancelInvoke(nameof(QueueRetreive));

        InvokeRepeating(nameof(SendMessages), 0, 1f);
        InvokeRepeating(nameof(SendReports), 0, 1f);
        InvokeRepeating(nameof(SendUpdate), 0, 5f);
        InvokeRepeating(nameof(QueueRetreive), 0, 1f);
      }

      public void AddMessage(PluginChatMessageEntry message)
      {
        messages.Add(message);
      }

      public void AddReport(PluginReportEntry report)
      {
        reports.Add(report);
      }

      public void SendMessages()
      {
        if (!CheckCondition() || messages.Count == 0)
        {
          return;
        }

        AvailableRequests.SendChatMessages(secret, new PluginChatMessagesPayload { messages = messages }, (fbMessages) =>
        {
          var resurrectMessages = new List<PluginChatMessageEntry>();

          resurrectMessages.AddRange(fbMessages.messages);
          resurrectMessages.AddRange(messages);

          messages = resurrectMessages;
        });

        messages = new List<PluginChatMessageEntry>();
      }

      public void SendContact(string steam_id, string message, Action<bool> callback)
      {
        if (!CheckCondition())
        {
          return;
        }

        AvailableRequests.SendContact(secret, new PluginContactPayload(steam_id, message), callback);
      }

      public void SendReports()
      {
        if (!CheckCondition() || reports.Count == 0)
        {
          return;
        }

        AvailableRequests.SendReports(secret, new PluginReportsPayload { reports = reports }, (fbReports) =>
        {
          var resurrectReports = new List<PluginReportEntry>();

          resurrectReports.AddRange(fbReports.reports);
          resurrectReports.AddRange(reports);

          reports = resurrectReports;
        });

        reports = new List<PluginReportEntry>();
      }

      public void QueueRetreive()
      {
        if (!CheckCondition())
        {
          return;
        }

        AvailableRequests.QueueRetreive(this.secret, (queue) =>
        {
          if (!CheckCondition())
          {
            return;
          }

          var responses = new Dictionary<string, object>();

          queue.ForEach(v =>
          {
            try
            {
              var response = QueueProcess(v);

              responses.Add(v.id, response);
            }
            catch (Exception exc)
            {
              _RustApp.PrintWarning(_RustApp.msg(
                "Не удалось обработать команду из очереди",
                "Failed to process queue command"
              ));
              responses.Add(v.id, $"!EXCEPTION!:{exc.ToString()}");
            }
          });

          if (responses.Keys.Count > 0)
          {
            AvailableRequests.QueueProcess(this.secret, responses);
          }
        });
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
              if (!_Settings.allow_dangerous_queue)
              {
                return "Disabled dangerous queue actions";
              }

              return OnQueueKick(JsonConvert.DeserializeObject<QueueKickPayload>(element.request.data.ToString()));
            }
          case "court/notice-state-get":
            {
              return OnNoticeStateGet(JsonConvert.DeserializeObject<QueueNoticeStateGetPayload>(element.request.data.ToString()));
            }
          case "court/notice-state-set":
            {
              if (!_Settings.allow_dangerous_queue)
              {
                return "Disabled dangerous queue actions";
              }

              return OnNoticeStateSet(JsonConvert.DeserializeObject<QueueNoticeStateSetPayload>(element.request.data.ToString()));
            }
          case "court/chat-message":
            {
              if (!_Settings.allow_dangerous_queue)
              {
                return "Disabled dangerous queue actions";
              }

              return OnChatMessage(JsonConvert.DeserializeObject<QueueChatMessage>(element.request.data.ToString()));
            }
          default:
            {
              _RustApp.Puts(_RustApp.msg(
                $"Неизвестная команда из очередей: {element.request.name}",
                $"Unknown queue command: {element.request.name}"
              ));
              break;
            }
        }

        return null;
      }

      private object OnQueueHealthCheck()
      {
        return true;
      }


      private class QueueKickPayload
      {
        public string steam_id;
        public string reason;
        public bool announce;
      }

      private object OnQueueKick(QueueKickPayload payload)
      {
        var player = BasePlayer.Find(payload.steam_id);
        if (player == null || !player.IsConnected)
        {
          _RustApp.Puts(_RustApp.msg(
            $"Не удалось кикнуть игрока {payload.steam_id}, игрок не найден или оффлайн",
            $"Failed to kick player {payload.steam_id}, player not found or disconnected"
          ));
          return "Player not found or offline";
        }

        _RustApp.Puts(_RustApp.msg(
          $"Игрок {payload.steam_id} кикнут по причине {payload.reason}",
          $"Player {payload.steam_id} was kicked for {payload.reason}"
        ));

        if (payload.announce)
        {
          _RustApp.Puts("Типо написали в чат, но на самом деле не писали");
        }
        else
        {
          _RustApp.Puts("Не пишем в чат, но типо пишем сюда, чтобы проверить что работает");
        }

        player.Kick(payload.reason);

        return true;
      }

      private object OnChatMessage(QueueChatMessage payload)
      {
        if (payload.target_steam_id is string)
        {
          var player = BasePlayer.Find(payload.target_steam_id);

          if (player == null || !player.IsConnected)
          {
            return "Player not found or offline";
          }

          SendMessage(player, payload);
        }
        else
        {
          foreach (var player in BasePlayer.activePlayerList)
          {
            SendMessage(player, payload);
          }
        }


        return true;
      }

      private void SendMessage(BasePlayer player, QueueChatMessage payload)
      {
        _RustApp.Puts($"Sent message to {player.displayName} {JsonConvert.SerializeObject(payload)}");
      }

      #region Notice 

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

      private Dictionary<string, bool> Notices = new Dictionary<string, bool>();

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

        if (!Notices.ContainsKey(payload.steam_id))
        {
          Notices.Add(payload.steam_id, payload.value);
        }

        Notices[payload.steam_id] = payload.value;

        return NoticeStateSet(payload.steam_id, payload.value);
      }

      private void NoticeClear()
      {
        Notices.Keys.ToList().ForEach(v => NoticeStateSet(v, false));
      }

      private object NoticeStateSet(string steam_id, bool value)
      {
        var player = BasePlayer.Find(steam_id);
        if (player == null || !player.IsConnected)
        {
          return "Player not found or offline";
        }

        if (!value)
        {
          _RustApp.Puts(_RustApp.msg(
            $"С игрока {player.userID} снято уведомление о проверке",
            $"Notify about check was removed from player {player.userID}"
          ));

          CuiHelper.DestroyUi(player, CheckLayer);
        }
        else
        {
          _RustApp.Puts(_RustApp.msg(
            $"Игрок {player.userID} уведомлён о проверке",
            $"Player {player.userID} was notified about check"
          ));

          _RustApp.DrawInterface(player);
        }

        return true;
      }

      #endregion

      public void SendUpdate()
      {
        if (!CheckCondition())
        {
          return;
        }

        var players = BasePlayer.activePlayerList.Select(v => PluginPlayerPayload.FromPlayer(v)).ToList();

        players.AddRange(ServerMgr.Instance.connectionQueue.queue.Select(v => PluginPlayerPayload.FromConnection(v, "queued")));
        players.AddRange(ServerMgr.Instance.connectionQueue.joining.Select(v => PluginPlayerPayload.FromConnection(v, "joining")));

        var payload = new PluginStatePayload(players);

        AvailableRequests.SendUpdate(this.secret, payload);
      }

      public bool CheckCondition()
      {
        if (_RustApp == null || !_RustApp.IsLoaded)
        {
          Interface.Oxide.LogWarning("Unexpected exception, plugin is already unloaded. Contact support: https://vk.com/rustapp");

          UnityEngine.Object.Destroy(this);
          return false;
        }

        if (this.secret == null)
        {
          Interface.Oxide.LogWarning("Unexpected exception, secret is missing. Contact support: https://vk.com/rustapp");
          return false;
        }

        return true;
      }

      public void OnDestroy()
      {

      }
    }

    #endregion

    #region Configuration

    private class Configuration
    {
      [JsonProperty("Allow execute dangerous methods from queue")]
      public bool allow_dangerous_queue = false;

      public static Configuration Generate()
      {
        return new Configuration
        {
          allow_dangerous_queue = false
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

      string text = "<color=#c6bdb4><size=32><b>ВЫ ВЫЗВАНЫ НА ПРОВЕРКУ</b></size></color>\n<color=#958D85>У вас есть <color=#c6bdb4><b>3 минуты</b></color> чтобы отправить дискорд и принять заявку в друзья.\nИспользуйте команду <b><color=#c6bdb4>/contact</color></b> чтобы отправить дискорд.\n\nДля связи с модератором вне дискорда - используйте чат.</color>";

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

      Color color = new Color32(r, g, b, a);

      return string.Format("{0:F2} {1:F2} {2:F2} {3:F2}", color.r, color.g, color.b, color.a);
    }

    #endregion

    #region Variables

    private static RustApp _RustApp;
    private static Configuration _Settings;

    private CourtWorker _Worker;

    #endregion

    #region Initialization

    private void OnServerInitialized()
    {
      _RustApp = this;

      _Worker = ServerMgr.Instance.gameObject.AddComponent<CourtWorker>();
    }

    private void Unload()
    {
      UnityEngine.Object.Destroy(_Worker);
    }

    #endregion

    #region API

    private void RA_DirectMessageHandler(string from, string to, string message)
    {
      _Worker.AddMessage(new PluginChatMessageEntry
      {
        steam_id = from,
        target_steam_id = to,
        is_team = false,

        text = message
      });
    }

    private void RA_ReportSend(string initiator_steam_id, string target_steam_id, string reason, [CanBeNull] string message)
    {
      _Worker.AddReport(new PluginReportEntry
      {
        initiator_steam_id = initiator_steam_id,
        target_steam_id = target_steam_id,
        sub_targets_steam_ids = new List<string>(),
        message = message,
        reason = reason
      });
    }

    #endregion

    #region Hooks

    private void OnPlayerChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel)
    {
      // Сохраняем только глобальный и командный чат
      if (channel != ConVar.Chat.ChatChannel.Team && channel != ConVar.Chat.ChatChannel.Global)
      {
        return;
      }

      _Worker.AddMessage(new PluginChatMessageEntry
      {
        steam_id = player.UserIDString,

        is_team = channel == ConVar.Chat.ChatChannel.Team,

        text = message
      });
    }

    #endregion

    #region UX

    [ChatCommand("contact")]
    private void CmdChatContact(BasePlayer player, string command, string[] args)
    {
      _Worker.SendContact(player.UserIDString, String.Join(" ", args), success =>
      {
        player.ChatMessage($"status: {success}");
      });
    }

    #endregion

    #region Commands

    [ConsoleCommand("ra_help")]
    private void CmdConsoleHelp(ConsoleSystem.Arg args)
    {
      if (args.Player() != null)
      {
        return;
      }

      Puts(msg("Помощь по командам RustApp.IO", "Command help for RustApp.IO"));
      Puts(msg(
        "ra_status <true?> - статус работы плагина (если передан true, покажет переданные данные)",
        "ra_status <true?> - plugin health-check (if true passed, with request payload)"
      ));
      Puts(msg(
        "ra_pair <secret> - настройка секретного ключа",
        "ra_pair <secret> - configure secret key"
      ));
      Puts(msg(
        "ra_genmap - генерация оноайн-карты (игроки могут быть кикнуты с сервера)",
        "ra_genmap - generate online-map (players will be kicked)"
      ));
    }

    [ConsoleCommand("ra_status")]
    private void CmdConsoleStatus(ConsoleSystem.Arg args)
    {
      Puts(msg(
        "Последние веб-ошибки:",
        "Last web exceptions:"
      ));

      if (AvailableRequests.Exceptions.Count == 0)
      {
        Puts(msg(
          "- ошибки не найдены",
          "- no exceptions found"
        ));
        return;
      }

      foreach (var value in AvailableRequests.Exceptions)
      {
        var diff = (DateTime.Now - value.time).TotalSeconds;

        string diffText = diff.ToString("F2") + msg("сек. назад", "secs ago");

        Puts($"{value.module} ({diffText})");

        if (args.Args?.Length >= 1)
        {
          Puts($"> {value.payload} [{value.secret}]");
        }
        Puts($"< {value.response}");
      }
    }

    [ConsoleCommand("ra_pair")]
    private void CmdConsoleCourtSetup(ConsoleSystem.Arg args)
    {
      if (args.Player() != null)
      {
        return;
      }

      if (args.Args.Length != 1)
      {
        PrintWarning(msg(
          "Вы неправильно используете команду! Формат: ra_pair <secret>",
          "Wrong command usage! Format: ra_pair <secret>"
        ));
        return;
      }

      var secret = args.Args[0].Replace("<", "").Replace(">", "").Replace("'", "").Replace("'", "");

      AvailableRequests.Validate(secret, () =>
      {
        Puts(msg(
          "Секретный ключ успешно прошёл проверку, перезагрузка...",
          "Secret key validate successfull, reloading..."
        ));
        MetaInfo.write(new MetaInfo { Value = secret });

        Server.Command("o.reload RustApp");
      }, (msg) =>
      {
        PrintWarning(msg);
      });
    }

    #endregion

    #region Messages

    public string msg(string ru, string en)
    {
#if RU
      return ru;
#else
      return en;
#endif
    }

    #endregion
  }
}