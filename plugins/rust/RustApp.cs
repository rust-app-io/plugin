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


namespace Oxide.Plugins
{
  [Info("RustApp", "Hougan & Xacku & Olkuts", "1.0.0")]
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
        this.onComplete += onComplete;
        this.onException += onException;

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
            if (typeof(T) == typeof(String))
            {
              onComplete?.Invoke((T)(object)request.downloadHandler?.text, request.downloadHandler?.text);
            }
            else
            {
              var obj = JsonConvert.DeserializeObject<T>(request.downloadHandler?.text);

              onComplete.Invoke(obj, request.downloadHandler?.text);
            }
          }
          catch (Exception parseException)
          {
            var str = typeof(T).ToString();

            _RustApp.Error(
              "Не удалось разобрать ответ от сервера",
              "Failed to parse server response"
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
      public static string SendChat = $"{CourtUrls.Base}/plugin/chat";
      public static string SendReports = $"{CourtUrls.Base}/plugin/reports";
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

      public static string Fetch = $"{BanUrls.Base}/plugin";
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

    class PluginPairDto
    {
      [CanBeNull] public int ttl;
      [CanBeNull] public string token;
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

        Connect();
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

        @FetchBans(
          PlayersCollection,
          (steamId, ban) =>
          {
            if (PlayersCollection.ContainsKey(steamId))
            {
              PlayersCollection.Remove(steamId);
            }

            if (ban != null)
            {
              _RustApp.CloseConnection(steamId, "ban");
            }
          },
          () =>
          {
            _RustApp.Error(
              $"Ошибка проверки блокировок ({PlayersCollection.Keys.Count} шт.), пытаемся снова...",
              $"Ban check error ({PlayersCollection.Keys.Count} total), attempting again..."
            );
          }
        );
      }

      public void @FetchBans(Dictionary<string, string> entries, Action<string, BanEntry> onBan, Action onException)
      {
        _RustApp.Log(
          $"Проверяем блокировки игроков ({entries.Keys.Count} шт)",
          $"Fetch players bans ({entries.Keys.Count} pc)"
        );

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

                var active = exists.bans.FirstOrDefault();

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
      private List<PluginReportEntry> ReportCollection = new List<PluginReportEntry>();
      private List<PluginChatMessageEntry> ChatCollection = new List<PluginChatMessageEntry>();
      private Dictionary<string, string> DisconnectHistory = new Dictionary<string, string>();
      private Dictionary<string, string> TeamChangeHistory = new Dictionary<string, string>();

      protected override void OnReady()
      {
        CancelInvoke(nameof(SendChat));
        CancelInvoke(nameof(SendUpdate));
        CancelInvoke(nameof(SendReports));

        InvokeRepeating(nameof(SendChat), 0, 1f);
        InvokeRepeating(nameof(SendUpdate), 0, 5f);
        InvokeRepeating(nameof(SendReports), 0, 1f);
      }

      public void SaveChat(PluginChatMessageEntry message)
      {
        ChatCollection.Add(message);
      }

      public void SaveDisconnect(BasePlayer player, string reason)
      {
        if (!DisconnectHistory.ContainsKey(player.UserIDString))
        {
          DisconnectHistory.Add(player.UserIDString, reason);
        }

        DisconnectHistory[player.UserIDString] = reason;
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
        ReportCollection.Add(report);
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

        var players = BasePlayer.activePlayerList.Select(v => PluginPlayerPayload.FromPlayer(v)).ToList();

        players.AddRange(ServerMgr.Instance.connectionQueue.queue.Select(v => PluginPlayerPayload.FromConnection(v, "queued")));
        players.AddRange(ServerMgr.Instance.connectionQueue.joining.Select(v => PluginPlayerPayload.FromConnection(v, "joining")));

        var payload = new
        {
          slots = ConVar.Server.maxplayers,
          version = _RustApp.Version.ToString(),
          performance = _RustApp.TotalHookTime.ToString(),

          players,
          disconnected = DisconnectHistory,
          team_changes = TeamChangeHistory
        };

        Request<object>(CourtUrls.SendState, RequestMethod.PUT, payload)
          .Execute(
            (data, raw) =>
            {
              DisconnectHistory.Clear();
              TeamChangeHistory.Clear();
            },
            (err) => _RustApp.Error(
              $"Не удалось отправить состояние сервера ({err})",
              $"Failed to send server status ({err})"
            )
          );
      }
    }

    public class QueueWorker : BaseWorker
    {
      private Dictionary<string, bool> Notices = new Dictionary<string, bool>();

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
            try
            {
              var response = QueueProcess(queue);

              responses.Add(queue.id, response);
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
              _RustApp.Error(
                "Не удалось загрузить задачи из очередей",
                "Failed to retreive queue"
              );
            }
          );
      }

      public void QueueProcess(Dictionary<string, object> responses)
      {
        Request<List<QueueElement>>(QueueUrls.Fetch, RequestMethod.PUT, new { data = responses })
          .Execute(
            (data, raw) => { },
            (err) => { }
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
          _RustApp.SendGlobalMessage("Игрок был кикнут");
        }

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

          _RustApp.SendMessage(player, payload.message);
        }
        else
        {
          foreach (var player in BasePlayer.activePlayerList)
          {
            _RustApp.SendMessage(player, payload.message);
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

          CuiHelper.DestroyUi(player, CheckLayer);
        }
        else
        {
          _RustApp.Log(
            $"Игрок {player.userID} уведомлён о проверке",
            $"Player {player.userID} was notified about check"
          );

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

      protected bool IsReady()
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
      [JsonProperty("Ignore all players manipulation")]
      public bool do_not_interact_player = true;

      public static Configuration Generate()
      {
        return new Configuration
        {
          do_not_interact_player = true
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
      if (_Settings.do_not_interact_player)
      {
        return;
      }

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

      timer.Once(1, () =>
      {
        _Worker = ServerMgr.Instance.gameObject.AddComponent<CourtWorker>();
      });
    }

    private void Unload()
    {
      UnityEngine.Object.Destroy(_Worker);
    }

    #endregion

    #region API

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

    private void RA_ReportSend(string initiator_steam_id, string target_steam_id, string reason, [CanBeNull] string message)
    {
      _Worker?.Update.SaveReport(new PluginReportEntry
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

    private void CanUserLogin(string name, string id, string ipAddress)
    {
      _Worker?.Ban.FetchBan(id, ipAddress);
    }

    private void OnPlayerDisconnected(BasePlayer player, string reason)
    {
      _Worker?.Update.SaveDisconnect(player, reason);
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
      // Сохраняем только глобальный и командный чат
      if (channel != ConVar.Chat.ChatChannel.Team && channel != ConVar.Chat.ChatChannel.Global)
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

    #endregion

    #region UX

    [ChatCommand("contact")]
    private void CmdChatContact(BasePlayer player, string command, string[] args)
    {
      _Worker?.Action.SendContact(player.UserIDString, String.Join(" ", args), (accepted) =>
      {
        if (!accepted)
        {
          return;
        }

        // TODO: Сообщение должно быть локализовано
        // TODO: Должна быть поддержка отправки как через сообщение, так и через gametip
        SendMessage(player, "Ваши контактные данные отправлены модератору");
      });
    }

    #endregion

    #region Commands

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

    private void SendMessage(string steamId, string message)
    {
      var player = BasePlayer.Find(steamId);
      if (player == null || !player.IsConnected)
      {
        return;
      }

      SendMessage(player, message);
    }

    private void SendMessage(BasePlayer player, string message)
    {
      if (_Settings.do_not_interact_player)
      {
        return;
      }

      player.ChatMessage(message);
    }

    private bool CloseConnection(string steamId, string reason)
    {
      if (_Settings.do_not_interact_player)
      {
        Puts("Игнорируем закрытие соединения с игроком (отключено в настройках плагина)");
        return true;
      }

      var player = BasePlayer.Find(steamId);
      if (player != null && player.IsConnected)
      {
        player.Kick(reason);
        return true;
      }

      var loading = ServerMgr.Instance.connectionQueue.queue.Find(v => v.userid.ToString() == steamId);
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
  }
}