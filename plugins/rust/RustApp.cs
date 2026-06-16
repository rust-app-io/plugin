// Reference: ZString

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ConVar;
using Cysharp.Text;
using JetBrains.Annotations;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Rust;
using Steamworks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Color = System.Drawing.Color;
using Graphics = System.Drawing.Graphics;
using Pool = Facepunch.Pool;
using Star = ProtoBuf.PatternFirework.Star;

namespace Oxide.Plugins;

[Info("RustApp", "RustApp.io", "3.0.0")]
public class RustApp : RustPlugin
{
    #region Variables

    // References to other plugin with API
    [PluginReference] private Plugin NoEscape, RaidZone, RaidBlock, MultiFighting, TGPP, ExtRaidBlock;

    private static MetaInfo _MetaInfo = MetaInfo.Read();
    private static CheckInfo _CheckInfo = CheckInfo.Read();

    private static bool _TempWipeMarker = false;

    private static RustApp _RustApp;
    private static Configuration _Settings;

    private static RustAppEngine _RustAppEngine;

    private static JsonSerializer _jsonSerializer = JsonSerializer.CreateDefault(null);

    private static (string name, string value)[] _ApiHeaders = Array.Empty<(string, string)>();

    private static readonly object _false = false; // avoid boxing for hooks
    private static readonly object _true = true;   // avoid boxing for hooks

    private string _UiCommandName;
    private string _UiSearchCommand;

    #endregion

    #region Web API

    private static class Api
    {
        public static bool ErrorContains(string error, string text)
        {
            return error.Contains(text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static class CourtApi
    {
        #region Shared

        public class PluginServerDto : Pool.IPooled
        {
            public string name;
            public string hostname;

            public string level;
            public string level_url;
            public string level_image_url;
            public int world_size;
            public string description;
            public string branch;

            public string avatar_big;
            public string avatar_url;
            public string banner_url;

            public int online;
            public int slots;
            public int reserved;

            public string version;
            public string protocol;
            public string performance;

            public int port;
            public bool connected;

            public void FillSnapshot()
            {
                // Static — cache all fields that do not change during plugin uptime
                name = _cachedName ??= ConVar.Server.hostname;
                hostname = _cachedHostname ??= ConVar.Server.hostname;
                level = _cachedLevel ??= (SteamServer.MapName ?? ConVar.Server.level);
                level_url = _cachedLevelUrl ??= ConVar.Server.levelurl;
                level_image_url = _cachedLevelImageUrl ??= MapUploader.ImageUrl;
                world_size = _cachedWorldSize ??= ConVar.Server.worldsize;
                description = _cachedDescription ??= (ConVar.Server.description + " " + ConVar.Server.motd);
                branch = _cachedBranch ??= ConVar.Server.branch;
                avatar_big = _cachedAvatarBig ??= ConVar.Server.logoimage;
                avatar_url = _cachedAvatarUrl ??= ConVar.Server.logoimage;
                banner_url = _cachedBannerUrl ??= ConVar.Server.headerimage;
                version = _cachedVersion ??= _RustApp.Version.ToString();
                protocol = _cachedProtocol ??= Protocol.printable.ToString();
                port = _cachedPort ??= ConVar.Server.port;

                // Dynamic — fresh value each cycle.
                online = BasePlayer.activePlayerList.Count + ServerMgr.Instance.connectionQueue.queue.Count + ServerMgr.Instance.connectionQueue.joining.Count;
                slots = ConVar.Server.maxplayers;
                reserved = ServerMgr.Instance.connectionQueue.ReservedCount;
                performance = _RustApp.TotalHookTime.ToString();
                connected = _RustAppEngine?.AuthWorker.IsAuthed ?? false;
            }

            private static string _cachedName;
            private static string _cachedHostname;
            private static string _cachedLevel;
            private static string _cachedLevelUrl;
            private static string _cachedLevelImageUrl;
            private static int? _cachedWorldSize;
            private static string _cachedDescription;
            private static string _cachedBranch;
            private static string _cachedAvatarBig;
            private static string _cachedAvatarUrl;
            private static string _cachedBannerUrl;
            private static string _cachedVersion;
            private static string _cachedProtocol;
            private static int? _cachedPort;

            public static void ResetCache()
            {
                _cachedName = null;
                _cachedHostname = null;
                _cachedLevel = null;
                _cachedLevelUrl = null;
                _cachedLevelImageUrl = null;
                _cachedWorldSize = null;
                _cachedDescription = null;
                _cachedBranch = null;
                _cachedAvatarBig = null;
                _cachedAvatarUrl = null;
                _cachedBannerUrl = null;
                _cachedVersion = null;
                _cachedProtocol = null;
                _cachedPort = null;
            }

            public virtual void LeavePool() { }

            public virtual void EnterPool()
            {
                name = null;
                hostname = null;
                level = null;
                level_url = null;
                level_image_url = null;
                world_size = 0;
                description = null;
                branch = null;
                avatar_big = null;
                avatar_url = null;
                banner_url = null;
                online = 0;
                slots = 0;
                reserved = 0;
                version = null;
                protocol = null;
                performance = null;
                port = 0;
                connected = false;
            }
        }

        #endregion

        private const string BaseUrl = "https://court.rustapp.io";

        private const string PairUriPrefix = BaseUrl + "/plugin/pair?code=";
        private static readonly Uri UriServerInfo = new(BaseUrl + "/plugin/");
        private static readonly Uri UriState = new(BaseUrl + "/plugin/state");
        private static readonly Uri UriChat = new(BaseUrl + "/plugin/chat");
        private static readonly Uri UriReports = new(BaseUrl + "/plugin/reports");
        private static readonly Uri UriContact = new(BaseUrl + "/plugin/contact");
        private static readonly Uri UriWipe = new(BaseUrl + "/plugin/wipe");
        private static readonly Uri UriSleepingBag = new(BaseUrl + "/plugin/sleeping-bag");
        private static readonly Uri UriBan = new(BaseUrl + "/plugin/ban");
        private static readonly Uri UriUnban = new(BaseUrl + "/plugin/unban");
        private static readonly Uri UriAlerts = new(BaseUrl + "/plugin/alerts");
        private static readonly Uri UriCustomAlert = new(BaseUrl + "/plugin/custom-alert");
        private static readonly Uri UriSignage = new(BaseUrl + "/plugin/signage");
        private static readonly Uri UriKills = new(BaseUrl + "/plugin/kills");
        private static readonly Uri UriPlayerMuteGetActive = new(BaseUrl + "/plugin/player-mute/get-active");
        private static readonly Uri UriPlayerMuteCreate = new(BaseUrl + "/plugin/player-mute/mute-player");
        private static readonly Uri UriPlayerMuteDelete = new(BaseUrl + "/plugin/player-mute/unmute-player");

        #region GetServerInfo

        public static StableRequest<object> GetServerInfo()
        {
            return new StableRequest<object>(UriServerInfo, RequestMethod.GET, null);
        }

        #endregion

        #region SendPairDetails

        public class PluginPairPayload : PluginServerDto { }

        public class PluginPairResponse
        {
            [CanBeNull] public int ttl;
            [CanBeNull] public string token;
        }

        public static StableRequest<PluginPairResponse> SendPairDetails(string code)
        {
            PluginPairPayload payload = new();
            payload.FillSnapshot();

            return new StableRequest<PluginPairResponse>(ZString.Concat(PairUriPrefix, code), RequestMethod.POST, payload);
        }

        #endregion

        #region StateUpdate

        public class PluginStatePlayerMetaDto
        {
            public readonly Dictionary<string, string> tags = new();
            public readonly Dictionary<string, string> fields = new();
        }

        public static Dictionary<ulong, PluginStatePlayerDto> players = new();

        public class PluginStatePlayerDto
        {
            public static PluginStatePlayerDto FromConnection(Network.Connection connection, string status)
            {
                var userid = connection.userid;
                if (!players.TryGetValue(userid, out var payload))
                {
                    payload = new PluginStatePlayerDto();
                    players[userid] = payload;
                    payload.steam_id = connection.player is BasePlayer basePlayer ? basePlayer.UserIDString : RustApp.GetSteamIdString(userid);
                    payload.steam_name = connection.username.Replace("<blank>", "blank");
                    payload.ip = IPAddressWithoutPort(connection.ipaddress);
                    payload.no_license = DetectNoLicense(connection);
                    payload.team = Pool.Get<List<string>>();
                }

                payload.ping = Network.Net.sv.GetAveragePing(connection);
                payload.seconds_connected = (int)connection.GetSecondsConnected();
                payload.language = _RustApp.lang.GetLanguage(payload.steam_id);
                payload.status = status;
                try { payload.meta = CollectPlayerMeta(payload.steam_id, payload.meta); } catch (Exception ex) { Debug(ex.ToString()); }

                payload.team ??= Pool.Get<List<string>>();
                payload.team.Clear();

                RelationshipManager.PlayerTeam? newTeam = RelationshipManager.ServerInstance.FindPlayersTeam(userid);
                if (newTeam == null)
                {
                    return payload;
                }

                for (int index = 0; index < newTeam.members.Count; index++)
                {
                    ulong member = newTeam.members[index];
                    if (member != userid)
                        payload.team.Add(member.ToString());
                }

                return payload;
            }

            public static PluginStatePlayerDto FromPlayer(BasePlayer player)
            {
                PluginStatePlayerDto payload = FromConnection(player.Connection, "active");

                payload.position = player.transform.position.ToString();
                payload.rotation = player.eyes.rotation.ToString();
                payload.coords = MapHelper.PositionToString(player.transform.position);

                payload.can_build = DetectBuildingAuth(player);
                payload.is_raiding = DetectIsRaidBlock(player);
                payload.is_alive = player.IsAlive();

                return payload;
            }

            public string steam_id;
            public string steam_name;
            public string ip;
            public int ping = 0;
            public int seconds_connected;
            public string language;

            [CanBeNull] public string position;
            [CanBeNull] public string rotation;
            [CanBeNull] public string coords;

            public bool can_build = false;
            public bool is_raiding = false;
            public bool no_license = false;
            public bool is_alive = false;

            public string status;

            public PluginStatePlayerMetaDto meta = new();
            public List<string> team;

            public void FreePooledFields()
            {
                if (team != null) Pool.FreeUnmanaged(ref team);
            }
        }

        public class PluginStateUpdatePayload : PluginServerDto
        {
            public PluginServerDto server_info;

            public List<PluginStatePlayerDto> players;
            public Dictionary<string, string> disconnected;
            public Dictionary<string, string> team_changes;

            public override void LeavePool()
            {
                base.LeavePool();
                server_info = Pool.Get<PluginServerDto>();
            }

            public override void EnterPool()
            {
                base.EnterPool();

                if (server_info != null) Pool.Free(ref server_info);

                if (players != null) Pool.FreeUnmanaged(ref players);
                if (disconnected != null) Pool.FreeUnmanaged(ref disconnected);
                if (team_changes != null) Pool.FreeUnmanaged(ref team_changes);
            }
        }

        public static StableRequest<object> SendStateUpdate(PluginStateUpdatePayload data)
        {
            return new StableRequest<object>(UriState, RequestMethod.PUT, data);
        }

        #endregion

        #region SendChatMessages

        public class PluginChatMessageDto : Pool.IPooled
        {
            public string steam_id;
            public string target_steam_id;
            public bool is_team;
            public string text;

            public static PluginChatMessageDto Create(string steamId, string text, bool isTeam, [CanBeNull] string targetSteamId = null)
            {
                PluginChatMessageDto dto = Pool.Get<PluginChatMessageDto>();
                dto.steam_id = steamId;
                dto.target_steam_id = targetSteamId;
                dto.is_team = isTeam;
                dto.text = text;
                return dto;
            }

            public void LeavePool() { }

            public void EnterPool()
            {
                steam_id = null;
                target_steam_id = null;
                is_team = false;
                text = null;
            }
        }

        public class PluginChatMessagePayload : Pool.IPooled
        {
            public List<PluginChatMessageDto>? messages;

            public void LeavePool() => messages = Pool.Get<List<PluginChatMessageDto>>();
            public void EnterPool()
            {
                if (messages != null) Pool.Free(ref messages, freeElements: true);
            }
        }

        public static StableRequest<object> SendChatMessages(PluginChatMessagePayload payload)
        {
            return new StableRequest<object>(UriChat, RequestMethod.POST, payload);
        }

        #endregion

        #region SendReports

        public class PluginReportDto : Pool.IPooled
        {
            public string initiator_steam_id;
            public string target_steam_id;
            public List<string> sub_targets_steam_ids;
            public string reason;
            public string message;

            public static PluginReportDto Create(string initiatorSteamId, string targetSteamId, string reason, string message = null)
            {
                PluginReportDto? dto = Pool.Get<PluginReportDto>();
                dto.initiator_steam_id = initiatorSteamId;
                dto.target_steam_id = targetSteamId;
                dto.reason = reason;
                dto.message = message;
                return dto;
            }

            public void LeavePool() => sub_targets_steam_ids = Pool.Get<List<string>>();

            public void EnterPool()
            {
                initiator_steam_id = null;
                target_steam_id = null;
                reason = null;
                message = null;
                if (sub_targets_steam_ids != null) Pool.FreeUnmanaged(ref sub_targets_steam_ids);
            }
        }

        public class PluginReportBatchPayload : Pool.IPooled
        {
            public List<PluginReportDto>? reports;

            public void LeavePool() => reports = Pool.Get<List<PluginReportDto>>();
            public void EnterPool()
            {
                if (reports != null) Pool.Free(ref reports, freeElements: true);
            }
        }

        public static StableRequest<object> SendReports(PluginReportBatchPayload payload)
        {
            return new StableRequest<object>(UriReports, RequestMethod.POST, payload);
        }

        #endregion

        #region SendContact

        public static StableRequest<object> SendContact(string steamId, string contact)
        {
            return new StableRequest<object>(UriContact, RequestMethod.POST, new { steam_id = steamId, message = contact });
        }

        #endregion

        #region SendWipe

        public static StableRequest<object> SendWipe()
        {
            return new StableRequest<object>(UriWipe, RequestMethod.POST, null);
        }

        #endregion

        #region Sleeping bag

        public class PluginSleepingBagDto : Pool.IPooled
        {
            public string initiator_steam_id;
            public string target_steam_id;
            public string position;
            public bool are_friends;

            public static PluginSleepingBagDto Create(string initiatorSteamId, string targetSteamId, string position, bool areFriends)
            {
                PluginSleepingBagDto? dto = Pool.Get<PluginSleepingBagDto>();
                dto.initiator_steam_id = initiatorSteamId;
                dto.target_steam_id = targetSteamId;
                dto.position = position;
                dto.are_friends = areFriends;
                return dto;
            }

            public void LeavePool() { }

            public void EnterPool()
            {
                initiator_steam_id = null;
                target_steam_id = null;
                position = null;
                are_friends = false;
            }
        }

        public class PluginSleepingBagBatchDto : Pool.IPooled
        {
            public List<PluginSleepingBagDto> sleeping_bags;

            public void LeavePool() => sleeping_bags = Pool.Get<List<PluginSleepingBagDto>>();

            public void EnterPool()
            {
                if (sleeping_bags != null) Pool.Free(ref sleeping_bags, freeElements: true);
            }
        }

        public static StableRequest<object> SleepingBagCreate(PluginSleepingBagBatchDto payload)
        {
            return new StableRequest<object>(UriSleepingBag, RequestMethod.POST, payload);
        }

        #endregion

        #region BanCreate

        public class PluginBanCreatePayload : Pool.IPooled
        {
            public string target_steam_id;
            public string reason;
            public bool global;
            public bool ban_ip;
            public string? duration;
            public string? comment;

            public static PluginBanCreatePayload Create(string targetSteamId, string reason, string? duration, bool global, bool banIp, string comment)
            {
                PluginBanCreatePayload? dto = Pool.Get<PluginBanCreatePayload>();
                dto.target_steam_id = targetSteamId;
                dto.reason = reason;
                dto.duration = duration;
                dto.global = global;
                dto.ban_ip = banIp;
                dto.comment = comment;
                return dto;
            }

            public void LeavePool() { }

            public void EnterPool()
            {
                target_steam_id = null;
                reason = null;
                duration = null;
                global = false;
                ban_ip = false;
                comment = null;
            }
        }

        public static StableRequest<object> BanCreate(PluginBanCreatePayload payload)
        {
            return new StableRequest<object>(UriBan, RequestMethod.POST, payload);
        }

        #endregion

        #region BanDelete

        public static StableRequest<object> BanDelete(string steamId)
        {
            return new StableRequest<object>(UriUnban, RequestMethod.POST, new { target_steam_id = steamId });
        }

        #endregion

        #region PlayerAlert

        public static class PluginPlayerAlertType
        {
            public static readonly string join_with_ip_ban = "join_with_ip_ban";
            public static readonly string custom_api = "custom_api";
        }

        public class PluginPlayerAlertDto : Pool.IPooled
        {
            public string type;
            public object meta;

            public static PluginPlayerAlertDto Create(string type, object meta)
            {
                PluginPlayerAlertDto? dto = Pool.Get<PluginPlayerAlertDto>();
                dto.type = type;
                dto.meta = meta;
                return dto;
            }

            public void LeavePool() { }

            public void EnterPool()
            {
                type = null;

                switch (meta)
                {
                    case PluginPlayerAlertJoinWithIpBanMeta join:
                        PluginPlayerAlertJoinWithIpBanMeta? j = join;
                        Pool.Free(ref j);
                        break;
                        // Add new IPooled meta types here!!
                }
                meta = null;
            }
        }

        public class PluginPlayerAlertJoinWithIpBanMeta : Pool.IPooled
        {
            public string steam_id;
            public string ip;
            public int ban_id;

            public static PluginPlayerAlertJoinWithIpBanMeta Create(string steamId, string ip, int banId)
            {
                PluginPlayerAlertJoinWithIpBanMeta? dto = Pool.Get<PluginPlayerAlertJoinWithIpBanMeta>();
                dto.steam_id = steamId;
                dto.ip = ip;
                dto.ban_id = banId;
                return dto;
            }

            public void LeavePool() { }
            public void EnterPool()
            {
                steam_id = null;
                ip = null;
                ban_id = 0;
            }
        }

        public class PluginPlayerAlertDugUpStashMeta
        {
            public string steam_id;
            public string owner_steam_id;
            public string position;
            public string square;
        }

        public class PluginPlayerAlertsBatchPayload : Pool.IPooled
        {
            public List<PluginPlayerAlertDto> alerts;

            public void LeavePool() => alerts = Pool.Get<List<PluginPlayerAlertDto>>();

            public void EnterPool()
            {
                if (alerts != null) Pool.Free(ref alerts, freeElements: true);
            }
        }

        public static StableRequest<object> CreatePlayerAlerts(PluginPlayerAlertsBatchPayload payload)
        {
            return new StableRequest<object>(UriAlerts, RequestMethod.POST, payload);
        }

        #endregion

        #region PlayerAlertCustom

        public class PluginPlayerAlertCustomDto : Pool.IPooled
        {
            public string msg;
            public object data;

            public string custom_icon;
            public bool hide_in_table;
            public string category;
            public List<string> custom_links;

            public static PluginPlayerAlertCustomDto Create(string msg, object? data, string? customIcon, string category, List<string>? customLinks)
            {
                PluginPlayerAlertCustomDto? dto = Pool.Get<PluginPlayerAlertCustomDto>();
                dto.msg = msg;
                dto.data = data;
                dto.custom_icon = customIcon;
                dto.hide_in_table = false;
                dto.category = category;
                dto.custom_links = customLinks;
                return dto;
            }

            public void LeavePool() { }

            public void EnterPool()
            {
                msg = null;
                data = null;
                custom_icon = null;
                hide_in_table = false;
                category = null;
                custom_links = null;
            }
        }

        public class PluginPlayerAlertCustomAlertMeta
        {
            public string name = "";
            public string custom_icon = null;
            public List<string> custom_links = null;
        }

        public static StableRequest<object> CreatePlayerAlertsCustom(PluginPlayerAlertCustomDto payload)
        {
            return new StableRequest<object>(UriCustomAlert, RequestMethod.POST, payload);
        }

        #endregion

        #region SendSignage

        public class PluginSignageCreateDto : Pool.IPooled
        {
            public string steam_id;
            public ulong net_id;
            public byte[] base64_image;
            public string type;
            public string position;
            public string square;

            public static PluginSignageCreateDto Create(string steamId, ulong netId, byte[] base64Image, string type, string position, string square)
            {
                PluginSignageCreateDto? dto = Pool.Get<PluginSignageCreateDto>();
                dto.steam_id = steamId;
                dto.net_id = netId;
                dto.base64_image = base64Image;
                dto.type = type;
                dto.position = position;
                dto.square = square;
                return dto;
            }

            public void LeavePool() { }

            public void EnterPool()
            {
                steam_id = null;
                net_id = 0;
                base64_image = null;
                type = null;
                position = null;
                square = null;
            }
        }

        public static StableRequest<object> SendSignage(PluginSignageCreateDto payload)
        {
            return new StableRequest<object>(UriSignage, RequestMethod.POST, payload);
        }

        #endregion

        #region SendSignageDestroyed

        public class SignageDestroyedDto : Pool.IPooled
        {
            public List<string> net_ids;

            public void LeavePool() => net_ids = Pool.Get<List<string>>();

            public void EnterPool()
            {
                if (net_ids != null) Pool.FreeUnmanaged(ref net_ids);
            }
        }

        public static StableRequest<object> SendSignageDestroyed(SignageDestroyedDto payload)
        {
            return new StableRequest<object>(UriSignage, RequestMethod.DELETE, payload);
        }

        #endregion

        #region SendKills

        public class PluginKillsDto : Pool.IPooled
        {
            public List<PluginKillEntryDto> kills;

            public void LeavePool() => kills = Pool.Get<List<PluginKillEntryDto>>();

            public void EnterPool()
            {
                if (kills != null) Pool.Free(ref kills, freeElements: true);
            }
        }

        public class PluginKillEntryDto : Pool.IPooled
        {
            public string initiator_steam_id;
            public string target_steam_id;
            public string game_time;
            public float distance;
            public string weapon;
            public bool is_headshot;
            public List<CombatLogEventDto> hit_history;

            public static PluginKillEntryDto Create(string initiatorSteamId, string targetSteamId, string gameTime,
                float distance, string weapon, bool isHeadshot, List<CombatLogEventDto> hitHistory)
            {
                PluginKillEntryDto? dto = Pool.Get<PluginKillEntryDto>();
                dto.initiator_steam_id = initiatorSteamId;
                dto.target_steam_id = targetSteamId;
                dto.game_time = gameTime;
                dto.distance = distance;
                dto.weapon = weapon;
                dto.is_headshot = isHeadshot;
                dto.hit_history = hitHistory;
                return dto;
            }

            public void LeavePool() { }

            public void EnterPool()
            {
                initiator_steam_id = null;
                target_steam_id = null;
                game_time = null;
                distance = 0f;
                weapon = null;
                is_headshot = false;

                if (hit_history != null)
                    Pool.Free(ref hit_history, freeElements: true);
            }
        }

        public class CombatLogEventDto : Pool.IPooled
        {
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

            public static CombatLogEventDto Create(float time, CombatLog.Event ev, Dictionary<ulong, string>? idCache = null)
            {
                CombatLogEventDto? dto = Pool.Get<CombatLogEventDto>();
                dto.Init(time, ev, idCache);
                return dto;
            }

            private void Init(float time, CombatLog.Event ev, Dictionary<ulong, string>? idCache)
            {
                if (ev.attacker is "player" or "you")
                    attacker_steam_id = ResolveSteamId(ev.attacker_id, idCache);

                if (ev.target is "player" or "you")
                    target_steam_id = ResolveSteamId(ev.target_id, idCache);

                this.time = time - ev.time;
                attacker = ev.attacker;
                target = ev.target;
                weapon = NormalizeName(ev.weapon);
                ammo = ev.ammo;
                bone = ev.bone;
                distance = (float)Math.Round(ev.distance, 2);
                hp_old = (float)Math.Round(ev.health_old, 2);
                hp_new = (float)Math.Round(ev.health_new, 2);
                info = ev.info;
                proj_hits = ev.proj_hits;
                proj_travel = ev.proj_travel;
                desync = ev.desync;
                pi = ev.proj_integrity;
                pm = ev.proj_mismatch;
                ad = ev.attacker_dead;
            }

            public void LeavePool() { }

            public void EnterPool()
            {
                time = 0f;
                attacker_steam_id = null;
                target_steam_id = null;
                attacker = null;
                target = null;
                weapon = null;
                ammo = null;
                bone = null;
                distance = 0f;
                hp_old = 0f;
                hp_new = 0f;
                info = null;
                proj_hits = 0;
                pi = 0f;
                proj_travel = 0f;
                pm = 0f;
                desync = 0;
                ad = false;
            }

            private static string ResolveSteamId(ulong netId, Dictionary<ulong, string>? idCache)
            {
                if (idCache != null && idCache.TryGetValue(netId, out string? cached))
                    return cached;

                string? sid = BaseNetworkable.serverEntities.TryGetEntity<BasePlayer>(new NetworkableId(netId), out BasePlayer? player)
                    ? player.UserIDString
                    : "";

                if (idCache != null) idCache[netId] = sid;
                return sid;
            }

            private static readonly Dictionary<string, string> _normalizedWeaponNameCache = new();

            public static void ClearCache() => _normalizedWeaponNameCache.Clear();

            private static string NormalizeName(string fullName)
            {
                const string Suffix = ".entity.prefab";

                if (string.IsNullOrEmpty(fullName) || fullName == "N/A"
                    || !fullName.EndsWith(Suffix, StringComparison.Ordinal))
                    return fullName;

                if (_normalizedWeaponNameCache.TryGetValue(fullName, out string? normalized))
                    return normalized;

                int lastSlash = fullName.LastIndexOf('/');
                int start = lastSlash + 1;
                int len = fullName.Length - start - Suffix.Length;

                normalized = fullName.Substring(start, len);
                _normalizedWeaponNameCache[fullName] = normalized;
                return normalized;
            }
        }

        public static StableRequest<object> SendKills(PluginKillsDto payload)
        {
            return new StableRequest<object>(UriKills, RequestMethod.POST, payload);
        }

        #endregion

        #region PlayerMuteGetActive

        public class PlayerMuteDto
        {
            public string target_steam_id;
            public string reason;
            public long left_time_ms;
            public long received_at = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            private long GetExpiredAt()
            {
                return received_at + left_time_ms;
            }

            public string GetLeftTime()
            {
                long leftMs = LeftSeconds();
                if (leftMs <= 0) return RustApp._RustApp.lang.GetMessage("Time.Seconds", RustApp._RustApp, null).Replace("%COUNT%", "0");

                TimeSpan time = TimeSpan.FromMilliseconds(leftMs);
                List<string> parts = new();

                if (time.Days > 0)
                    parts.Add(RustApp._RustApp.lang.GetMessage("Time.Days", RustApp._RustApp, null).Replace("%COUNT%", time.Days.ToString()));
                if (time.Hours > 0)
                    parts.Add(RustApp._RustApp.lang.GetMessage("Time.Hours", RustApp._RustApp, null).Replace("%COUNT%", time.Hours.ToString()));
                if (time.Minutes > 0)
                    parts.Add(RustApp._RustApp.lang.GetMessage("Time.Minutes", RustApp._RustApp, null).Replace("%COUNT%", time.Minutes.ToString()));
                if (time.Seconds > 0)
                    parts.Add(RustApp._RustApp.lang.GetMessage("Time.Seconds", RustApp._RustApp, null).Replace("%COUNT%", time.Seconds.ToString()));

                return string.Join(", ", parts);
            }

            public string GetUnmuteDate()
            {
                DateTimeOffset date = DateTimeOffset.FromUnixTimeMilliseconds(GetExpiredAt());

                return $"{date.DateTime.ToShortDateString()} {date.DateTime.ToShortTimeString()}";
            }

            public long LeftSeconds()
            {
                return GetExpiredAt() - DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }
        }

        public class PlayerMuteDtoIn
        {
            public List<PlayerMuteDto> data;
        }

        public static StableRequest<PlayerMuteDtoIn> PlayerMuteGetActive()
        {
            return new StableRequest<PlayerMuteDtoIn>(UriPlayerMuteGetActive, RequestMethod.GET, null);
        }

        #endregion

        #region PlayerMuteCreate

        public class PlayerMuteCreateDto : Pool.IPooled
        {
            public string target_steam_id;
            public string reason;
            public string duration;
            public bool broadcast;
            public string? comment;
            public string? references_message;

            public static PlayerMuteCreateDto Create(string targetSteamId, string reason, string duration, bool broadcast = false, string? comment = null, string? referencesMessage = null)
            {
                PlayerMuteCreateDto? dto = Pool.Get<PlayerMuteCreateDto>();
                dto.target_steam_id = targetSteamId;
                dto.reason = reason;
                dto.duration = duration;
                dto.broadcast = broadcast;
                dto.comment = comment;
                dto.references_message = referencesMessage;
                return dto;
            }

            public void LeavePool() { }

            public void EnterPool()
            {
                target_steam_id = null;
                reason = null;
                duration = null;
                broadcast = false;
                comment = null;
                references_message = null;
            }
        }

        public static StableRequest<object> PlayerMuteCreate(PlayerMuteCreateDto data)
        {
            return new StableRequest<object>(UriPlayerMuteCreate, RequestMethod.POST, data);
        }

        #endregion

        #region PlayerMuteDelete

        public class PlayerMuteDeleteDto : Pool.IPooled
        {
            public string target_steam_id;

            public static PlayerMuteDeleteDto Create(string targetSteamId)
            {
                PlayerMuteDeleteDto? dto = Pool.Get<PlayerMuteDeleteDto>();
                dto.target_steam_id = targetSteamId;
                return dto;
            }

            public void LeavePool() { }
            public void EnterPool() => target_steam_id = null;
        }

        public static StableRequest<object> PlayerMuteDelete(PlayerMuteDeleteDto data)
        {
            return new StableRequest<object>(UriPlayerMuteDelete, RequestMethod.POST, data);
        }

        #endregion
    }

    private static class QueueApi
    {
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

        private const string BaseUrl = "https://queue.rustapp.io";

        private static readonly Uri UriQueueRoot = new(BaseUrl + "/");

        #region GetQueueTasks

        public static StableRequest<List<QueueTaskResponse>> GetQueueTasks()
        {
            return new StableRequest<List<QueueTaskResponse>>(UriQueueRoot, RequestMethod.GET, null);
        }

        #endregion

        #region ProcessQueueTasks

        public class QueueTaskResponsePayload
        {
            public Dictionary<string, object> data;
        }

        public static StableRequest<object> ProcessQueueTasks(QueueTaskResponsePayload payload)
        {
            return new StableRequest<object>(UriQueueRoot, RequestMethod.PUT, payload);
        }

        #endregion
    }

    private static class BanApi
    {
        private const string BaseUrl = "https://ban.rustapp.io";

        private static readonly Uri UriBanGetBatchV2 = new(BaseUrl + "/plugin/v2");

        #region BanGetBatch

        public class BanGetBatchEntryPayloadDto : Pool.IPooled
        {
            public string steam_id;
            public string ip;

            public static BanGetBatchEntryPayloadDto Create(string steamId, string ip)
            {
                BanGetBatchEntryPayloadDto? dto = Pool.Get<BanGetBatchEntryPayloadDto>();
                dto.steam_id = steamId;
                dto.ip = ip;
                return dto;
            }

            public void LeavePool() { }
            public void EnterPool()
            {
                steam_id = null;
                ip = null;
            }
        }

        public class BanGetBatchPayload : Pool.IPooled
        {
            public List<BanGetBatchEntryPayloadDto> players;

            public void LeavePool() => players = Pool.Get<List<BanGetBatchEntryPayloadDto>>();

            public void EnterPool()
            {
                if (players != null) Pool.FreeUnmanaged(ref players);
            }
        }

        public class BanGetBatchEntryResponseDto
        {
            public string steam_id;
            public string ip;

            public List<BanDto> bans;
        }

        public class BanDto
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

        public class BanGetBatchResponse
        {
            public List<BanGetBatchEntryResponseDto> entries;
        }

        public static StableRequest<BanGetBatchResponse> BanGetBatch(BanGetBatchPayload payload)
        {
            return new StableRequest<BanGetBatchResponse>(UriBanGetBatchV2, RequestMethod.POST, payload);
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
            const double ThirtyDaysSeconds = 30 * 24 * 60 * 60;
            double now = _RustApp.CurrentTime();

            List<string>? toRemove = Pool.Get<List<string>>();
            try
            {
                foreach (KeyValuePair<string, double> kv in courtMeta.LastChecks)
                {
                    if (now - kv.Value >= ThirtyDaysSeconds)
                        toRemove.Add(kv.Key);
                }

                for (int i = 0; i < toRemove.Count; i++)
                {
                    courtMeta.LastChecks.Remove(toRemove[i]);
                }
            }
            finally
            {
                Pool.FreeUnmanaged(ref toRemove);
            }

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

        [JsonProperty("[Components • Custom actions] Allow console command execution")]
        public bool components_custom_actions_enabled = true;

        [JsonProperty("[Components • Signages] Collect signages")]
        public bool components_signages_enabled = true;

        [JsonProperty("[Components • Kills] Collect kills")]
        public bool components_kills_enabled = true;

        [JsonProperty("[Components • Mutes] Support mutes system")]
        public bool components_mutes_enabled = true;


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

                components_custom_actions_enabled = true,
                components_kills_enabled = true,
                components_signages_enabled = true,
                components_mutes_enabled = true,
            };
        }
    }

    #endregion

    #region Workers

    private class RustAppEngine : RustAppWorker
    {
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
        public PlayerMuteWorker? PlayerMuteWorker;
        public SleepingBagWorker? SleepingBagWorker;

        private new void Awake()
        {
            base.Awake();

            SetupAuthWorker();
        }

        public bool IsPairingNow()
        {
            return this.gameObject.GetComponent<PairWorker>() != null;
        }

        private void SetupAuthWorker()
        {
            SetupHeaders();

            AuthWorker = gameObject.AddComponent<AuthWorker>();

            AuthWorker.OnAuthSuccess += () =>
            {
                Trace("Authed success, components enabled");

                CreateSubWorkers();
            };

            AuthWorker.OnAuthFailed += () =>
            {
                Trace("Auth failed, components disabled");

                DestroySubWorkers();
            };

            AuthWorker.CycleAuthUpdate();
        }

        public void SetupHeaders()
        {
            _ApiHeaders = new[]
            {
                ("x-plugin-auth", _MetaInfo?.Value ?? ""),
                ("x-plugin-version", _RustApp.Version.ToString()),
                ("x-plugin-port", ConVar.Server.port.ToString()),
                ("Content-Type", "application/json")
            };
        }

        private void CreateSubWorkers()
        {
            ChildObjectToWorkers = gameObject.CreateChild();

            StateWorker = ChildObjectToWorkers.AddComponent<StateWorker>();
            CheckWorker = ChildObjectToWorkers.AddComponent<CheckWorker>();
            QueueWorker = ChildObjectToWorkers.AddComponent<QueueWorker>();
            ChatWorker = ChildObjectToWorkers.AddComponent<ChatWorker>();
            BanWorker = ChildObjectToWorkers.AddComponent<BanWorker>();
            ReportWorker = ChildObjectToWorkers.AddComponent<ReportWorker>();
            PlayerAlertsWorker = ChildObjectToWorkers.AddComponent<PlayerAlertsWorker>();
            SignageWorker = ChildObjectToWorkers.AddComponent<SignageWorker>();
            KillsWorker = ChildObjectToWorkers.AddComponent<KillsWorker>();
            PlayerMuteWorker = ChildObjectToWorkers.AddComponent<PlayerMuteWorker>();
            SleepingBagWorker = ChildObjectToWorkers.AddComponent<SleepingBagWorker>();
        }

        private void DestroySubWorkers()
        {
            if (ChildObjectToWorkers == null)
            {
                return;
            }

            Destroy(ChildObjectToWorkers);
        }
    }

    private class AuthWorker : RustAppWorker
    {
        public bool? IsAuthed;

        public Action? OnAuthSuccess;
        public Action? OnAuthFailed;

        private Action? _cachedAuthSuccess;
        private Action<string>? _cachedAuthError;

        public void CycleAuthUpdate()
        {
            InvokeRepeating(nameof(CheckAuthStatus), 0f, 5f);
        }

        public void CheckAuthStatus()
        {
            _cachedAuthSuccess ??= HandleAuthSuccess;
            _cachedAuthError ??= HandleAuthError;

            CourtApi.GetServerInfo().Execute(_cachedAuthSuccess, _cachedAuthError);
        }

        private void HandleAuthSuccess()
        {
            if (IsAuthed == true)
            {
                return;
            }

            Log("Connection to the service established");

            IsAuthed = true;
            OnAuthSuccess?.Invoke();

            if (_TempWipeMarker)
            {
                _TempWipeMarker = false;
                CourtApi.SendWipe().Execute();
            }
        }

        private void HandleAuthFailure()
        {
            if (IsAuthed == false)
            {
                return;
            }

            IsAuthed = false;
            OnAuthFailed?.Invoke();
        }

        private void HandleAuthError(string err)
        {
            // secret = ""
            var codeError1 = Api.ErrorContains(err, "some of required headers are wrong or missing");
            // secret = "123"
            var codeError2 = Api.ErrorContains(err, "authorization secret is corrupted");
            // server.ip != this.ip || server.port != this.port
            var codeError3 = Api.ErrorContains(err, "Check server configuration, required");

            if (codeError1 || codeError2 || codeError3)
            {
                if (IsAuthed != false)
                {
                    Error("Your server is not paired with our network, follow instructions to pair server:");
                    Error("1. If you already start pairing, enter 'ra.pair %code%' which you get from our site");
                    Error("2. Open servers page, press 'connect server', and enter command which you get on it");
                }

                HandleAuthFailure();
                return;
            }

            // version < minVersion
            var versionError1 = Api.ErrorContains(err, " is lower than minimal: ");
            // if we block some version
            var versionError2 = Api.ErrorContains(err, "This version contains serious bug, please update plugin");

            if (versionError1 || versionError2)
            {
                Error("Your plugin is outdated, you should download new version!");
                Error("1. Open servers page, press 'update' near server to download new version, then just replace plugin");
                Error("2. If you don't have 'update' button, press settings icon and choose 'download plugin' button");

                HandleAuthFailure();
                return;
            }

            // if tariff finished/balance zero
            var paymentError1 = Api.ErrorContains(err, "У вас кончились средства на балансе проекта, пополните на");
            // If some limits broken
            var paymentError2 = Api.ErrorContains(err, "Вы превысили лимиты по");

            if (paymentError1)
            {
                Error("Your balance is not enough to continue working with our service, top-up it");
                HandleAuthFailure();
                return;
            }

            if (paymentError2)
            {
                Error("You have reached your limits, please upgrade your plan");
                HandleAuthFailure();
                return;
            }

            Debug($"Unknown exception in auth: {err}");
        }
    }

    private class PairWorker : RustAppWorker
    {
        private string EnteredCode;

        public void StartPairing(string code)
        {
            EnteredCode = code;

            CourtApi.SendPairDetails(EnteredCode).Execute(() =>
            {
                InvokeRepeating(nameof(WaitPairFinish), 0f, 1f);
            },
            (err) =>
            {
                if (Api.ErrorContains(err, "code not exists"))
                {
                    Error("Pairing failed: requested code not exists");
                }
                else if (Api.ErrorContains(err, "pairing prevented from abuse"))
                {
                    Error("Pairing failed: seems this server was already connected to another project, please contact TG: @rustapp_help if you think, that it is wrong");
                }
                else
                {
                    Debug($"Pairing failed: unknown exception {err}");
                }

                Destroy(this);
            });
        }

        public void WaitPairFinish()
        {
            CourtApi.SendPairDetails(EnteredCode).Execute((data) =>
            {
                if (data?.token == null || data.token.Length == 0)
                {
                    Log("Complete pairing on site...");
                    return;
                }

                if (_RustAppEngine?.StateWorker != null)
                    _RustAppEngine?.StateWorker?.SendUpdate(SaveData);
                else
                    SaveData();

                return;

                void SaveData()
                {
                    MetaInfo.write(new MetaInfo
                    {
                        Value = data.token
                    });

                    _RustApp.timer.Once(1f, () => _RustAppEngine?.AuthWorker?.CheckAuthStatus());

                    _MetaInfo = MetaInfo.Read();
                    _RustAppEngine.SetupHeaders();

                    Log("Pairing completed, reloading...");

                    Destroy(this);
                }
            },
            (err) =>
            {
                if (Api.ErrorContains(err, "code not exists"))
                {
                    Error("Pairing failed: seems you closed modal on site");
                }
                else
                {
                    Error($"Pairing failed: unknown exception {err}");
                }

                Destroy(this);
            });
        }
    }

    private class StateWorker : RustAppWorker
    {
        public readonly Dictionary<string, string> DisconnectReasons = new();
        public readonly Dictionary<string, string> TeamChanges = new();

        private new void Awake()
        {
            base.Awake();

            InvokeRepeating(nameof(CycleSendUpdate), 0f, 5f);
        }

        public void CycleSendUpdate()
        {
            SendUpdate();
        }

        public void SendUpdate(Action? onFinished = null)
        {

            CourtApi.PluginStateUpdatePayload? payload = Pool.Get<CourtApi.PluginStateUpdatePayload>();
            payload.FillSnapshot();
            payload.server_info.FillSnapshot();

            payload.players = Pool.Get<List<CourtApi.PluginStatePlayerDto>>();
            CollectPlayers(payload.players);

            payload.disconnected = Pool.Get<Dictionary<string, string>>();
            ResurrectDictionary(DisconnectReasons, payload.disconnected);

            payload.team_changes = Pool.Get<Dictionary<string, string>>();
            ResurrectDictionary(TeamChanges, payload.team_changes);

            DisconnectReasons.Clear();
            TeamChanges.Clear();

            CourtApi.SendStateUpdate(payload).Execute(() =>
            {
                Pool.Free(ref payload);
                Trace("State was sent successfull");
                onFinished?.Invoke();
            },
            (err) =>
            {
                onFinished?.Invoke();
                Debug($"State sent error: {err}");

                ResurrectDictionary(payload.disconnected, DisconnectReasons);
                ResurrectDictionary(payload.team_changes, TeamChanges);
                Pool.Free(ref payload);
            });
        }

        private void CollectPlayers(List<CourtApi.PluginStatePlayerDto> playerStateDtos)
        {
            foreach (BasePlayer? player in BasePlayer.activePlayerList)
            {
                try { playerStateDtos.Add(CourtApi.PluginStatePlayerDto.FromPlayer(player)); } catch (Exception ex) { Debug($"FromPlayer failed: {ex}"); }
            }

            foreach (Connection? connection in ServerMgr.Instance.connectionQueue.joining)
            {
                try { playerStateDtos.Add(CourtApi.PluginStatePlayerDto.FromConnection(connection, "joining")); } catch (Exception ex) { Debug($"FromConnection(joining) failed: {ex}"); }
            }

            foreach (Connection? connection in ServerMgr.Instance.connectionQueue.queue)
            {
                if (connection == null)
                {
                    continue;
                }
                try { playerStateDtos.Add(CourtApi.PluginStatePlayerDto.FromConnection(connection, "queued")); } catch (Exception ex) { Debug($"FromConnection(queued) failed: {ex}"); }
            }
        }

        public new void OnDestroy()
        {
            base.OnDestroy();
        }
    }

    private class CheckWorker : RustAppWorker
    {
        private readonly Dictionary<string, bool> ShowedNoticyCache = new();

        public bool IsNoticeActive(string steamId) => ShowedNoticyCache.GetValueOrDefault(steamId, false);

        public void SetNoticeActive(string steamId, bool value)
        {
            BasePlayer? player = null;

            if (ulong.TryParse(steamId, out ulong userid))
                player = BasePlayer.FindByID(userid);

            if (player != null)
            {
                // TODO: Deprecated
                if (value)
                {
                    Interface.Oxide.CallHook("RustApp_OnCheckNoticeShowed", player);
                }
                else
                {
                    Interface.Oxide.CallHook("RustApp_OnCheckNoticeHidden", player);
                }
            }

            // TODO: New hook
            Interface.Oxide.CallHook("RustApp_CheckNoticeState", steamId, value);

            ShowedNoticyCache[steamId] = value;

            if (player != null)
            {
                if (value)
                {
                    _RustApp.DrawNoticeInterface(player);
                }
                else
                {
                    CuiHelper.DestroyUi(player, CheckLayer);
                }
            }
        }

        public new void OnDestroy()
        {
            base.OnDestroy();

            foreach (KeyValuePair<string, bool> check in ShowedNoticyCache.ToList())
            {
                if (!check.Value)
                    continue;

                SetNoticeActive(check.Key, false);
            }
        }
    }

    private class QueueWorker : RustAppWorker
    {
        private readonly HashSet<string> QueueProcessedIds = new();

        private new void Awake()
        {
            base.Awake();

            InvokeRepeating(nameof(GetQueueTasks), 0f, 1f);
        }

        private void GetQueueTasks()
        {
            QueueApi.GetQueueTasks().Execute(
            CallQueueTasks,
            (error) =>
            {
                Debug($"Queue retreive failed {error}");
            });
        }

        private void CallQueueTasks(List<QueueApi.QueueTaskResponse>? queuesTasks)
        {
            if (queuesTasks == null || queuesTasks.Count == 0)
                return;

            Dictionary<string, object> queueResponses = new Dictionary<string, object>();

            foreach (QueueApi.QueueTaskResponse? task in queuesTasks)
            {
                if (QueueProcessedIds.Contains(task.id))
                {
                    Debug($"This task was already processed: {task.id}");
                    continue;
                }

                try
                {
                    Trace($"Calling: {ConvertToRustAppQueueFormat(task.request.name, true)}");
                    // To get our official response
                    object? response = (object)_RustApp.Call(ConvertToRustAppQueueFormat(task.request.name, true), task.request.data);

                    QueueProcessedIds.Add(task.id);
                    queueResponses.Add(task.id, response);

                    // Just to broadcast event RustApp_Queue%name%
                    Interface.Oxide.CallHook(ConvertToRustAppQueueFormat(task.request.name, false), task.request.data);
                }
                catch (Exception exc)
                {
                    Error($"Failed to process task {task.id} {task.request.name}: {exc.ToString()}");

                    queueResponses.Add(task.id, null);
                }
            }

            ProcessQueueTasks(queueResponses);
        }

        private void ProcessQueueTasks(Dictionary<string, object> queueResponses)
        {
            if (queueResponses.Keys.Count == 0)
                return;

            QueueApi.ProcessQueueTasks(new QueueApi.QueueTaskResponsePayload { data = queueResponses }).Execute(() =>
            {
                QueueProcessedIds.Clear();
                Trace("Ответ по очередям успешно доставлен");
            },
            (err) =>
            {
                Debug($"Failed to process queue: {err}");
            });
        }


        private static readonly Dictionary<(string name, bool internalCall), string> _queueNameCache = new();

        public static void ClearCache() => _queueNameCache.Clear();

        private string ConvertToRustAppQueueFormat(string input, bool isInternalCall)
        {
            (string input, bool isInternalCall) key = (input, isInternalCall);
            if (_queueNameCache.TryGetValue(key, out string? cached))
                return cached;

            string[]? words = input.Replace("court/", "").Split('-');

            for (int i = 0; i < words.Length; i++)
            {
                words[i] = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words[i]);
            }

            string result = $"RustApp_{(isInternalCall ? "Internal" : "")}Queue_" + string.Join("", words);
            _queueNameCache[key] = result;
            return result;
        }
    }

    private class BanWorker : RustAppWorker
    {
        private readonly Dictionary<string, BanApi.BanGetBatchEntryPayloadDto> BanUpdateQueue = new();

        public new void Awake()
        {
            base.Awake();

            DefaultScanAllPlayers();

            InvokeRepeating(nameof(CycleBanUpdate), 0f, 2f);
        }

        private void DefaultScanAllPlayers()
        {
            foreach (BasePlayer? player in BasePlayer.activePlayerList)
            {
                try { CheckBans(player); } catch (Exception ex) { Debug($"CheckBans(player) failed: {ex}"); }
            }

            foreach (Connection? queued in ServerMgr.Instance.connectionQueue.queue)
            {
                try { CheckBans(GetSteamIdString(queued.userid), IPAddressWithoutPort(queued.ipaddress)); } catch (Exception ex) { Debug($"CheckBans(queued) failed: {ex}"); }
            }

            foreach (Connection? loading in ServerMgr.Instance.connectionQueue.joining)
            {
                try { CheckBans(GetSteamIdString(loading.userid), IPAddressWithoutPort(loading.ipaddress)); } catch (Exception ex) { Debug($"CheckBans(loading) failed: {ex}"); }
            }
        }

        public void CheckBans(BasePlayer player)
        {
            CheckBans(player.UserIDString, IPAddressWithoutPort(player.Connection.ipaddress));
        }

        public void CheckBans(string steamId, string ip)
        {
            object? over = Interface.Oxide.CallHook("RustApp_CanIgnoreBan", steamId);
            if (over != null)
                return;

            if (BanUpdateQueue.TryGetValue(steamId, out BanApi.BanGetBatchEntryPayloadDto? existing))
            {
                existing.ip = ip;
                return;
            }

            BanUpdateQueue[steamId] = BanApi.BanGetBatchEntryPayloadDto.Create(steamId, ip);
        }

        private void CycleBanUpdate()
        {
            if (BanUpdateQueue.Count == 0)
            {
                return;
            }

            CycleBanUpdateWrapper((steamId, ban) =>
            {
                BanUpdateQueue.Remove(steamId);

                if (ban == null)
                    return;

                if (ban.sync_project_id != 0 && !ban.sync_should_kick)
                    return;

                if (ban.steam_id == steamId)
                    ReactOnDirectBan(steamId, ban);
                else
                    ReactOnIpBan(steamId, ban);
            });
        }

        private void CycleBanUpdateWrapper(Action<string, BanApi.BanDto?> callback)
        {
            if (BanUpdateQueue.Count == 0)
            {
                return;
            }

            BanApi.BanGetBatchPayload? payload = Pool.Get<BanApi.BanGetBatchPayload>();

            foreach (BanApi.BanGetBatchEntryPayloadDto? entry in BanUpdateQueue.Values)
                payload.players.Add(entry);

            BanUpdateQueue.Clear();

            BanApi.BanGetBatch(payload).Execute((data) =>
            {
                Dictionary<string, BanApi.BanGetBatchEntryResponseDto>? entriesByPid = Pool.Get<Dictionary<string, BanApi.BanGetBatchEntryResponseDto>>();
                try
                {
                    if (data?.entries != null)
                    {
                        for (int i = 0; i < data.entries.Count; i++)
                        {
                            BanApi.BanGetBatchEntryResponseDto? e = data.entries[i];
                            if (e?.steam_id != null) entriesByPid[e.steam_id] = e;
                        }
                    }

                    for (int i = 0; i < payload.players.Count; i++)
                    {
                        BanApi.BanGetBatchEntryPayloadDto? originalPlayer = payload.players[i];
                        BanApi.BanDto? active = null;
                        if (entriesByPid.TryGetValue(originalPlayer.steam_id, out BanApi.BanGetBatchEntryResponseDto? entry) && entry.bans != null)
                        {
                            for (int j = 0; j < entry.bans.Count; j++)
                            {
                                if (entry.bans[j].computed_is_active)
                                {
                                    active = entry.bans[j];
                                    break;
                                }
                            }
                        }
                        callback.Invoke(originalPlayer.steam_id, active);
                    }
                }
                finally
                {
                    Pool.FreeUnmanaged(ref entriesByPid);
                    Pool.Free(ref payload.players, freeElements: true);
                    Pool.Free(ref payload);
                }
            },
            (_) =>
            {
                Error($"Failed to process ban checks ({payload.players.Count}), retrying...");
                for (int i = 0; i < payload.players.Count; i++)
                {
                    BanApi.BanGetBatchEntryPayloadDto? p = payload.players[i];
                    BanUpdateQueue[p.steam_id] = p;
                }
                payload.players.Clear();
                Pool.Free(ref payload);
            });
        }

        public new void OnDestroy()
        {
            base.OnDestroy();

            foreach (BanApi.BanGetBatchEntryPayloadDto? entry in BanUpdateQueue.Values)
            {
                if (entry == null) continue;
                BanApi.BanGetBatchEntryPayloadDto? e = entry;
                Pool.Free(ref e);
            }
            BanUpdateQueue.Clear();
        }

        public void ReactOnDirectBan(string steamId, BanApi.BanDto ban)
        {
            string? format = "";

            if (ban.sync_project_id != 0)
            {
                // Get format for sync ban
                format = ban.expired_at == 0
                  ? _RustApp.lang.GetMessage("System.BanSync.Perm.Kick", _RustApp, steamId)
                  : _RustApp.lang.GetMessage("System.BanSync.Temp.Kick", _RustApp, steamId);
            }
            else
            {
                // Get format for your own ban
                format = ban.expired_at == 0
                  ? _RustApp.lang.GetMessage("System.Ban.Perm.Kick", _RustApp, steamId)
                  : _RustApp.lang.GetMessage("System.Ban.Temp.Kick", _RustApp, steamId);
            }

            string time = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)
              .AddMilliseconds(ban.expired_at + 3 * 60 * 60 * 1_000)
              .ToString("dd.MM.yyyy HH:mm");

            string finalText = format
              .Replace("%REASON%", ban.reason)
              .Replace("%TIME%", time);

            _RustApp.CloseConnection(steamId, finalText);
        }

        public void ReactOnIpBan(string steamId, BanApi.BanDto ban)
        {
            _RustApp.CloseConnection(steamId, _RustApp.lang.GetMessage("System.Ban.Ip.Kick", _RustApp, steamId));

            PlayerAlertsWorker? worker = _RustAppEngine?.PlayerAlertsWorker;
            if (worker == null) return;

            CourtApi.PluginPlayerAlertJoinWithIpBanMeta meta = CourtApi.PluginPlayerAlertJoinWithIpBanMeta.Create(steamId, ban.ban_ip, ban.id);
            worker.SavePlayerAlert(CourtApi.PluginPlayerAlertDto.Create(
                CourtApi.PluginPlayerAlertType.join_with_ip_ban, meta));
        }
    }

    private class ChatWorker : RustAppWorker
    {
        private readonly List<CourtApi.PluginChatMessageDto> QueueMessages = new();

        private new void Awake()
        {
            base.Awake();

            InvokeRepeating(nameof(SendChatMessages), 0f, 1f);
        }

        public void SaveChatMessage(CourtApi.PluginChatMessageDto message)
        {
            QueueMessages.Add(message);
        }

        private void SendChatMessages()
        {
            if (QueueMessages.Count == 0)
            {
                return;
            }

            CourtApi.PluginChatMessagePayload payload = Pool.Get<CourtApi.PluginChatMessagePayload>();
            payload.messages.AddRange(QueueMessages);
            QueueMessages.Clear();

            CourtApi.SendChatMessages(payload).Execute(() =>
            {
                Pool.Free(ref payload.messages, freeElements: true);
                Pool.Free(ref payload);
            },
            (_) =>
            {
                QueueMessages.AddRange(payload.messages);
                payload.messages.Clear();
                Pool.Free(ref payload);
            });
        }

        public new void OnDestroy()
        {
            base.OnDestroy();

            for (int i = 0; i < QueueMessages.Count; i++)
            {
                CourtApi.PluginChatMessageDto? item = QueueMessages[i];
                if (item == null) continue;
                Pool.Free(ref item);
            }
            QueueMessages.Clear();
        }
    }

    private class ReportWorker : RustAppWorker
    {
        public readonly Dictionary<ulong, double> ReportCooldowns = new();
        private readonly List<CourtApi.PluginReportDto> QueueReportSend = new();

        public new void Awake()
        {
            base.Awake();

            InvokeRepeating(nameof(CycleReportSend), 0f, 1f);
        }

        public void SendReport(CourtApi.PluginReportDto report)
        {
            QueueReportSend.Add(report);
        }

        private void CycleReportSend()
        {
            if (QueueReportSend.Count == 0)
            {
                return;
            }

            CourtApi.PluginReportBatchPayload? payload = Pool.Get<CourtApi.PluginReportBatchPayload>();
            payload.reports.AddRange(QueueReportSend);
            QueueReportSend.Clear();

            CourtApi.SendReports(payload).Execute(() =>
            {
                Pool.Free(ref payload.reports, freeElements: true);
                Pool.Free(ref payload);
            },
            (_) =>
            {
                QueueReportSend.AddRange(payload.reports);
                payload.reports.Clear();
                Pool.Free(ref payload);
            });
        }

        public new void OnDestroy()
        {
            base.OnDestroy();

            for (int i = 0; i < QueueReportSend.Count; i++)
            {
                CourtApi.PluginReportDto item = QueueReportSend[i];
                if (item == null) continue;
                Pool.Free(ref item);
            }
            QueueReportSend.Clear();
        }
    }

    private class PlayerAlertsWorker : RustAppWorker
    {
        private readonly List<CourtApi.PluginPlayerAlertDto> PlayerAlertQueue = new();

        private new void Awake()
        {
            base.Awake();

            InvokeRepeating(nameof(CycleSendPlayerAlerts), 0f, 5f);
        }

        public void SavePlayerAlert(CourtApi.PluginPlayerAlertDto alert) => PlayerAlertQueue.Add(alert);

        private void CycleSendPlayerAlerts()
        {
            if (PlayerAlertQueue.Count == 0)
            {
                return;
            }

            CourtApi.PluginPlayerAlertsBatchPayload? payload = Pool.Get<CourtApi.PluginPlayerAlertsBatchPayload>();
            payload.alerts.AddRange(PlayerAlertQueue);
            PlayerAlertQueue.Clear();

            CourtApi.CreatePlayerAlerts(payload).Execute(() =>
            {
                Pool.Free(ref payload.alerts, freeElements: true);
                Pool.Free(ref payload);
            },
            (_) =>
            {
                PlayerAlertQueue.AddRange(payload.alerts);
                payload.alerts.Clear();
                Pool.Free(ref payload);
            });
        }

        public new void OnDestroy()
        {
            base.OnDestroy();

            for (int i = 0; i < PlayerAlertQueue.Count; i++)
            {
                CourtApi.PluginPlayerAlertDto? item = PlayerAlertQueue[i];
                if (item == null) continue;
                Pool.Free(ref item);
            }
            PlayerAlertQueue.Clear();
        }
    }

    private class SignageWorker : RustAppWorker
    {
        private readonly List<string> DestroyedSignagesQueue = new();

        private new void Awake()
        {
            base.Awake();

            InvokeRepeating(nameof(CycleSendUpdate), 5f, 5f);
        }

        public void AddSignageDestroy(string netId)
        {
            DestroyedSignagesQueue.Add(netId);
        }

        public void SignageCreate(BaseImageUpdate update)
        {
            try
            {
                Vector3 pos = update.Entity.transform.position;
                CourtApi.PluginSignageCreateDto? obj = CourtApi.PluginSignageCreateDto.Create(
                    update.PlayerId,
                    update.Entity.net.ID.Value,
                    update.GetImage(),
                    update.Entity.ShortPrefabName,
                    pos.ToString(),
                    MapHelper.PositionToString(pos));

                CourtApi.SendSignage(obj).Execute();

                Pool.Free(ref obj);
            }
            catch
            {
                // ignored
            }
        }

        private void CycleSendUpdate()
        {
            if (DestroyedSignagesQueue.Count == 0)
            {
                return;
            }

            CourtApi.SignageDestroyedDto? payload = Pool.Get<CourtApi.SignageDestroyedDto>();
            payload.net_ids.AddRange(DestroyedSignagesQueue);
            DestroyedSignagesQueue.Clear();

            CourtApi.SendSignageDestroyed(payload).Execute(() =>
            {
                Pool.Free(ref payload);
            },
            (_) =>
            {
                DestroyedSignagesQueue.AddRange(payload.net_ids);
                payload.net_ids.Clear();
                Pool.Free(ref payload);
            });
        }

        public new void OnDestroy()
        {
            base.OnDestroy();
            DestroyedSignagesQueue.Clear();
        }
    }

    private class SleepingBagWorker : RustAppWorker
    {
        private readonly List<CourtApi.PluginSleepingBagDto> SleepingBags = new();

        private new void Awake()
        {
            base.Awake();

            InvokeRepeating(nameof(CycleSendSleepingBags), 5f, 5f);
        }

        public void AddSleepingBag(CourtApi.PluginSleepingBagDto data)
        {
            SleepingBags.Add(data);
        }

        private void CycleSendSleepingBags()
        {
            if (SleepingBags.Count == 0)
            {
                return;
            }

            CourtApi.PluginSleepingBagBatchDto? payload = Pool.Get<CourtApi.PluginSleepingBagBatchDto>();
            payload.sleeping_bags.AddRange(SleepingBags);
            SleepingBags.Clear();

            CourtApi.SleepingBagCreate(payload).Execute(() =>
            {
                Pool.Free(ref payload.sleeping_bags, freeElements: true);
                Pool.Free(ref payload);
            },
            (_) =>
            {
                SleepingBags.AddRange(payload.sleeping_bags);
                payload.sleeping_bags.Clear();
                Pool.Free(ref payload);
            });
        }

        public new void OnDestroy()
        {
            base.OnDestroy();

            for (int i = 0; i < SleepingBags.Count; i++)
            {
                CourtApi.PluginSleepingBagDto? item = SleepingBags[i];
                if (item == null) continue;
                Pool.Free(ref item);
            }
            SleepingBags.Clear();
        }
    }

    private class KillsWorker : RustAppWorker
    {
        public readonly Dictionary<string, HitRecord> WoundedHits = new();
        private readonly List<CourtApi.PluginKillEntryDto> KillsQueue = new();

        private new void Awake()
        {
            base.Awake();

            InvokeRepeating(nameof(CycleSendKills), 5f, 5f);
        }

        public void AddKill(CourtApi.PluginKillEntryDto data)
        {
            KillsQueue.Add(data);
        }

        private void CycleSendKills()
        {
            if (KillsQueue.Count == 0)
            {
                return;
            }

            CourtApi.PluginKillsDto? payload = Pool.Get<CourtApi.PluginKillsDto>();
            payload.kills.AddRange(KillsQueue);
            KillsQueue.Clear();

            CourtApi.SendKills(payload).Execute(() =>
            {
                Pool.Free(ref payload.kills, freeElements: true);
                Pool.Free(ref payload);
            },
            (_) =>
            {
                KillsQueue.AddRange(payload.kills);
                payload.kills.Clear();
                Pool.Free(ref payload);
            });
        }

        public new void OnDestroy()
        {
            base.OnDestroy();

            for (int i = 0; i < KillsQueue.Count; i++)
            {
                CourtApi.PluginKillEntryDto? item = KillsQueue[i];
                if (item == null) continue;
                Pool.Free(ref item);
            }
            KillsQueue.Clear();
        }
    }

    private class PlayerMuteWorker : RustAppWorker
    {
        private readonly Dictionary<ulong, CourtApi.PlayerMuteDto> PlayerMutes = new();

        private new void Awake()
        {
            base.Awake();

            InvokeRepeating(nameof(CycleUpdateMutes), 0f, 300f);
        }

        private void CycleUpdateMutes()
        {
            StableRequest<CourtApi.PlayerMuteDtoIn> request = CourtApi.PlayerMuteGetActive();

            request.Execute((data) =>
            {
                PlayerMutes.Clear();
                data?.data?.ForEach(AddPlayerMute);
            },
            (_) => { });
        }

        public void AddPlayerMute(CourtApi.PlayerMuteDto playerMuteDto)
        {
            ulong steamId = ulong.Parse(playerMuteDto.target_steam_id);
            PlayerMutes[steamId] = playerMuteDto;
        }

        public void RemovePlayerMute(CourtApi.PlayerMuteDto playerMuteDto)
        {
            ulong steamId = ulong.Parse(playerMuteDto.target_steam_id);
            PlayerMutes.Remove(steamId);
        }

        public CourtApi.PlayerMuteDto? GetMute(ulong steamId) => PlayerMutes.GetValueOrDefault(steamId);
    }

    #endregion

    #region Commands

    private void CmdSendContact(BasePlayer player, string contact, string[] args)
    {
        if (args.Length == 0)
        {
            SendMessage(player, lang.GetMessage("Contact.Error", this, player.UserIDString));
            return;
        }

        CourtApi.SendContact(player.UserIDString, string.Join(" ", args)).Execute(() =>
        {
            SendMessage(player, lang.GetMessage("Contact.Sent", this, player.UserIDString) + $"<color=#8393cd> {string.Join(" ", args)}</color>");
            SendMessage(player, lang.GetMessage("Contact.SentWait", this, player.UserIDString));
        },
        (_) => { });
    }

    private void CmdChatReportInterface(BasePlayer player)
    {
        if (_RustAppEngine?.ReportWorker == null)
        {
            return;
        }

        var now = CurrentTime();
        if (_RustAppEngine.ReportWorker.ReportCooldowns.TryGetValue(player.userID, out var cooldownUntil) && cooldownUntil > now)
        {
            var msg = lang.GetMessage("Cooldown", this, player.UserIDString)
                .Replace("%TIME%", $"{(cooldownUntil - now).ToString("0")}");

            SoundToast(player, msg, SoundToastType.Error);
            return;
        }

        DrawReportInterface(player);
    }

    [ConsoleCommand("ra.pair")]
    private void StartPairing(ConsoleSystem.Arg args)
    {
        if (args.Player() != null || args.Args.Length == 0)
        {
            return;
        }

        string? code = args.GetString(0);

        _RustAppEngine?.gameObject.AddComponent<PairWorker>().StartPairing(code);
    }

    [ConsoleCommand("ra.mute")]
    private void CmdConsoleMute(ConsoleSystem.Arg args)
    {
        var caller = args.Player();
        if (caller != null && !caller.IsAdmin)
        {
            return;
        }

        bool broadcast = args.HasArg("--broadcast", true);

        if (!args.HasArgs(3))
        {
            Error("Incorrect command format!\nCorrect format: ra.mute <steam-id> <reason> <time>\n\nAdditional options are available:\n'--broadcast' - broadcast mute");
            return;
        }

        string steamId = args.GetString(0);
        string reason = args.GetString(1);
        string duration = args.GetString(2);

        RustApp_PlayerMuteCreate(steamId, reason, duration, null, null, broadcast);
    }

    [ConsoleCommand("ra.unmute")]
    private void CmdConsoleUnmute(ConsoleSystem.Arg args)
    {
        BasePlayer? caller = args.Player();
        if (caller != null && !caller.IsAdmin)
        {
            return;
        }

        if (!args.HasArgs(1))
        {
            Error("Incorrect command format!\nCorrect format: ra.unmute <steam-id>");
            return;
        }

        string? steamId = args.GetString(0);

        RustApp_PlayerMuteDelete(steamId);
    }

    [ConsoleCommand("ra.ban")]
    private void CmdConsoleBan(ConsoleSystem.Arg args)
    {
        BasePlayer? caller = args.Player();
        if (caller != null && !caller.IsAdmin)
        {
            return;
        }

        bool banIp = args.HasArg("--ban-ip", true);
        bool global = args.HasArg("--global", true);

        if (!args.HasArgs(2))
        {
            Error("Incorrect command format!\nCorrect format: ra.ban <steam-id> <reason> <time (optional)>\n\nAdditional options are available:\n'--ban-ip' - bans IP\n'--global' - bans globally\n\nExample of banning with IP, globally: ra.ban 7656119812110397 \"cheat\" 7d --ban-ip --global");
            return;
        }

        string steamId = args.GetString(0);
        string reason = args.GetString(1);
        string duration = args.GetString(2);

        BanCreate(steamId, CourtApi.PluginBanCreatePayload.Create(
            steamId, reason, duration.Length > 0 ? duration : null, global, banIp, "Ban via console"));
    }

    [ConsoleCommand("ra.unban")]
    private void CmdConsoleBanDelete(ConsoleSystem.Arg args)
    {
        BasePlayer? caller = args.Player();
        if (caller != null && !caller.IsAdmin)
        {
            return;
        }

        if (!args.HasArgs(1))
        {
            Error("Incorrect command format!\nCorrect format: ra.unban <steam-id>");
            return;
        }

        string steamId = args.GetString(0);

        BanDelete(steamId);
    }

    private void OnUiCommand(ConsoleSystem.Arg args)
    {
        BasePlayer player = args.Player();
        if (player == null) return;

        switch (args.GetString(0))
        {
            case "page":
                DrawReportInterface(player, args.GetInt(1), args.GetString(2), redraw: true);
                PlayEffect(player, FxButtonUnpress);
                break;

            case "search":
                string typed;
                if (args.HasArgs(2))
                {
                    StringBuilder sb = Pool.Get<StringBuilder>();
                    try
                    {
                        for (int i = 1; i < args.Args.Length; i++)
                        {
                            if (i > 1) sb.Append(' ');
                            sb.Append(args.GetString(i));
                        }
                        typed = sb.ToString();
                    }
                    finally
                    {
                        Pool.FreeUnmanaged(ref sb);
                    }
                }
                else typed = "";

                DrawReportInterface(player, 0, typed, redraw: true);
                break;

            case "show":
                DrawPlayerReportReasons(player, args.GetString(1), args.GetInt(2), args.GetInt(3));
                break;

            case "send":
                SendReport(player, args.GetString(1), args.GetString(2));
                break;
        }
    }

    #endregion

    #region Hooks

    #region System hooks

    private void Init()
    {
        _MetaInfo = MetaInfo.Read();
        _CheckInfo = CheckInfo.Read();

        CourtApi.players = new Dictionary<ulong, CourtApi.PluginStatePlayerDto>();

        _UiCommandName = "x" + Guid.NewGuid().ToString("N");
        _UiSearchCommand = _UiCommandName + " search ";
    }

    private void OnServerInitialized()
    {
        _RustApp = this;

        Log("Welcome to the RustApp.io!");

        if (!CheckRequiredPlugins())
        {
            Error("Fix pending errors, and use 'o.reload RustApp'");
            return;
        }

        if (!_Settings.components_kills_enabled)
        {
            Unsubscribe(nameof(OnPlayerWound));
            Unsubscribe(nameof(OnPlayerRespawn));
            Unsubscribe(nameof(OnPlayerRecovered));
            Unsubscribe(nameof(OnPlayerDeath));
        }

        if (!_Settings.components_signages_enabled)
        {
            Unsubscribe(nameof(OnEntityKill));
            Unsubscribe(nameof(OnImagePost));
            Unsubscribe(nameof(OnSignUpdated));
            Unsubscribe(nameof(OnItemPainted));
            Unsubscribe(nameof(OnFireworkDesignChanged));
            Unsubscribe(nameof(OnEntityBuilt));
        }

        if (!_Settings.components_mutes_enabled)
        {
            Unsubscribe(nameof(OnClientCommand));
        }

#if OXIDE
        _chatCommandPrefixes = Interface.Oxide.Config.Commands.ChatPrefix.ToArray();
#endif

#if CARBON
        _chatCommandPrefixes = API.Commands.Command.Prefixes.Select(p => p.Value).ToArray();
#endif

        timer.Once(1f, () =>
        {
            MetaInfo.Read();

            RustAppEngineCreate();
            RegisterCommands();
        });
    }

    private void Unload()
    {
        _tempDisconnectReasons.Clear();
        _pendingKick.Clear();
        _disconnectProcessed.Clear();

        RustAppEngineDestroy();
        DestroyAllUi();

        _steamIdStringCache.Clear();

        CourtApi.PluginServerDto.ResetCache();
        CourtApi.CombatLogEventDto.ClearCache();
        QueueWorker.ClearCache();

        // Reset Facepunch.Pool buckets owned by this plugin: nested DTOs (Oxide.Plugins.RustApp+CourtApi+...) and generics over them (List`1[[Oxide.Plugins.RustApp+...]]).
        Pool.Clear(Name);
        foreach (KeyValuePair<Type, Pool.IPoolCollection> kv in Pool.Directory.Where(kv => kv.Key.FullName != null && kv.Key.FullName.Contains(Name)).ToList())
            Pool.Directory.TryRemove(kv.Key, out _);
    }

    private void OnNewSave(string saveName)
    {
        _TempWipeMarker = true;
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
            ["Check.Started"] = "Player <color=#5af>%NAME%</color> was called for a inspection!",
            ["Check.FinishedClear"] = "Inspection of <color=#5af>%NAME%</color> finished, player is clear!",
            ["Header.Find"] = "FIND PLAYER",
            ["Header.SubDefault"] = "Who do you want to report?",
            ["Header.SubFindResults"] = "Here are players, which we found",
            ["Header.SubFindEmpty"] = "No players was found",
            ["Header.Search"] = "Search",
            ["Header.Search.Placeholder"] = "Enter nickname/steamid",
            ["Subject.Head"] = "Select the reason for the report",
            ["Subject.SubHead"] = "For player %PLAYER%",
            ["Cooldown"] = "Wait %TIME% sec.",
            ["Sent"] = "Complaint successfully submitted!",
            ["Sent.F7"] = "Your complaint has been submitted and will be reviewed shortly.",
            ["Contact.Error"] = "You did not sent your Discord",
            ["Contact.Sent"] = "You sent:",
            ["Contact.SentWait"] = "If you sent the correct discord - wait for a friend request.",
            ["Check.Text"] = "<color=#c6bdb4><size=32><b>YOU ARE SUMMONED FOR A CHECK-UP</b></size></color>\n<color=#958D85>You have <color=#c6bdb4><b>3 minutes</b></color> to send discord and accept the friend request.\nUse the <b><color=#c6bdb4>%COMMAND%</color></b> command to send discord.\n\nTo contact a moderator - use chat, not a command.</color>",
            ["Chat.Direct.Toast"] = "Received a message from admin, look at the chat!",
            ["UI.CheckMark"] = "Checked",
            ["Paid.Announce.Clean"] = "Your complaint about \"%SUSPECT_NAME%\" has been checked!\n<size=12><color=#81C5F480>As a result of the check, no violations were found</color></size>",
            ["Paid.Announce.Ban"] = "Your complaint about \"%SUSPECT_NAME%\" has been verified!\n<color=#F7D4D080><size=12>Player banned, reason: %REASON%</size></color>",

            ["System.Chat.Direct"] = "<size=12><color=#ffffffB3>DM from Administration</color></size>\n<color=#AAFF55>%CLIENT_TAG%</color>: %MSG%",
            ["System.Chat.Global"] = "<size=12><color=#ffffffB3>Message from Administration</color></size>\n<color=#AAFF55>%CLIENT_TAG%</color>: %MSG%",

            ["System.Mute.Broadcast.Mute"] = "Player <color=#5af>%TARGET%</color> was muted.\n<size=12>- reason: %REASON%\n- duration: %TIME%</size>",
            ["System.Mute.Message.Self"] = "You are muted!<size=12>\n- reason: %REASON%\n- left: %TIME%</size>",

            ["System.Ban.Broadcast"] = "Player <color=#5af>%TARGET%</color> was banned.\n<size=12>- reason: %REASON%</size>",
            ["System.Ban.Temp.Kick"] = "You are banned until %TIME% (UTC+3), reason: %REASON%",
            ["System.Ban.Perm.Kick"] = "You have perm ban, reason: %REASON%",
            ["System.Ban.Ip.Kick"] = "You are restricted from entering the server!",

            ["System.BanSync.Temp.Kick"] = "Detected ban on another project until %TIME% (UTC+3), reason: %REASON%",
            ["System.BanSync.Perm.Kick"] = "Detected ban on another project, reason: %REASON%",

            ["Time.Days"] = "%COUNT% day",
            ["Time.Hours"] = "%COUNT% hour",
            ["Time.Minutes"] = "%COUNT% min",
            ["Time.Seconds"] = "%COUNT% sec",
        }, this, "en");

        lang.RegisterMessages(new Dictionary<string, string>
        {
            ["Check.Started"] = "Игрок <color=#5af>%NAME%</color> был вызван на проверку!",
            ["Check.FinishedClear"] = "Проверка игрока <color=#5af>%NAME%</color> завершена, игрок чист!",
            ["Header.Find"] = "НАЙТИ ИГРОКА",
            ["Header.SubDefault"] = "На кого вы хотите пожаловаться?",
            ["Header.SubFindResults"] = "Вот игроки, которых мы нашли",
            ["Header.SubFindEmpty"] = "Игроки не найдены",
            ["Header.Search"] = "Поиск",
            ["Header.Search.Placeholder"] = "Введите ник/steamid",
            ["Subject.Head"] = "Выберите причину репорта",
            ["Subject.SubHead"] = "На игрока %PLAYER%",
            ["Cooldown"] = "Подожди %TIME% сек.",
            ["Sent"] = "Жалоба успешно отправлена!",
            ["Sent.F7"] = "Ваша жалоба отправлена и будет рассмотрена в ближайшее время.",
            ["Contact.Error"] = "Вы не отправили свой Discord",
            ["Contact.Sent"] = "Вы отправили:",
            ["Contact.SentWait"] = "<size=12>Если вы отправили корректный дискорд - ждите заявку в друзья.</size>",
            ["Check.Text"] = "<color=#c6bdb4><size=32><b>ВЫ ВЫЗВАНЫ НА ПРОВЕРКУ</b></size></color>\n<color=#958D85>У вас есть <color=#c6bdb4><b>3 минуты</b></color> чтобы отправить дискорд и принять заявку в друзья.\nИспользуйте команду <b><color=#c6bdb4>%COMMAND%</color></b> чтобы отправить дискорд.\n\nДля связи с модератором - используйте чат, а не команду.</color>",
            ["Chat.Direct.Toast"] = "Получено сообщение от админа, посмотрите в чат!",
            ["UI.CheckMark"] = "Проверен",
            ["Paid.Announce.Clean"] = "Ваша жалоба на \"%SUSPECT_NAME%\" была проверена!\n<size=12><color=#81C5F480>В результате проверки, нарушений не обнаружено</color></size>",
            ["Paid.Announce.Ban"] = "Ваша жалоба на \"%SUSPECT_NAME%\" была проверена!\n<color=#F7D4D080><size=12>Игрок заблокирован, причина: %REASON%</size></color>",

            ["System.Chat.Direct"] = "<size=12><color=#ffffffB3>ЛС от Администратора</color></size>\n<color=#AAFF55>%CLIENT_TAG%</color>: %MSG%",
            ["System.Chat.Global"] = "<size=12><color=#ffffffB3>Сообщение от Администратора</color></size>\n<color=#AAFF55>%CLIENT_TAG%</color>: %MSG%",

            ["System.Mute.Broadcast.Mute"] = "Игрок <color=#5af>%TARGET%</color> получил мут.\n<size=12>- причина: %REASON%\n- срок: %TIME%</size>",
            ["System.Mute.Message.Self"] = "Вы замьючены!<size=12>\n- причина: %REASON%\n- осталось: %TIME%</size>",

            ["System.Ban.Broadcast"] = "Игрок <color=#5af>%TARGET%</color> был заблокирован.\n<size=12>- причина: %REASON%</size>",
            ["System.Ban.Temp.Kick"] = "Вы забанены на этом сервере до %TIME% (МСК), причина: %REASON%",
            ["System.Ban.Perm.Kick"] = "Вы навсегда забанены на этом сервере, причина: %REASON%",
            ["System.Ban.Ip.Kick"] = "Вам ограничен вход на сервер!",

            ["System.BanSync.Temp.Kick"] = "Обнаружена блокировка на другом проекте до %TIME% (МСК), причина: %REASON%",
            ["System.BanSync.Perm.Kick"] = "Обнаружена блокировка на другом проекте, причина: %REASON%",

            ["Time.Days"] = "%COUNT% дн",
            ["Time.Hours"] = "%COUNT% час",
            ["Time.Minutes"] = "%COUNT% мин",
            ["Time.Seconds"] = "%COUNT% сек",
        }, this, "ru");
    }

    #endregion

    #region Connect hooks

    private void CanUserLogin(string name, string id, string ipAddress)
    {
        if (ulong.TryParse(id, out ulong userid))
            _tempDisconnectReasons.Remove(userid);

        _disconnectProcessed.Remove(id);

        OnPlayerConnectedNormalized(id, IPAddressWithoutPort(ipAddress));
    }

    private void OnPlayerConnected(BasePlayer player)
    {
        _tempDisconnectReasons.Remove(player.userID);
        _disconnectProcessed.Remove(player.UserIDString);

        // Deferred kick: CloseConnection ran during a transition gap (e.g. between JoinedGame() and BasePlayer.Spawn()), Connection was in no list yet.
        if (_pendingKick.Remove(player.userID, out string? pendingReason))
        {
            Log($"Closing connection with {player.UserIDString}: {pendingReason} (deferred → OnPlayerConnected)");
            player.Kick(pendingReason);
            OnPlayerDisconnectedNormalized(player.UserIDString, pendingReason);
            return;
        }

        OnPlayerConnectedNormalized(player.UserIDString, IPAddressWithoutPort(player.Connection.ipaddress));
    }

    #endregion

    #region Disconnect hooks

    private readonly Dictionary<ulong, string> _tempDisconnectReasons = new();

    // CloseConnection may run during a transition gap between Connection lists (after JoinedGame removes from joining, before BasePlayer.Spawn registers in activePlayerList).
    private readonly Dictionary<ulong, string> _pendingKick = new();

    private void OnPlayerDisconnected(BasePlayer player, string reason)
    {
        OnPlayerDisconnectedNormalized(player.UserIDString, reason);
    }

    private void OnClientDisconnect(Connection connection, string reason)
    {
        _tempDisconnectReasons[connection.userid] = reason;

        _pendingKick.Remove(connection.userid);
    }

    private void OnClientDisconnected(Connection connection, string reason)
    {
        // Prevent double call
        if (connection.guid == 0uL)
        {
            return;
        }

        ulong userid = connection.userid;
        string reasonFinal = reason;

        if (_tempDisconnectReasons.TryGetValue(userid, out string? tempReason))
        {
            reasonFinal = $"{reason}: {tempReason}";
        }

        string? steamId = connection.player is BasePlayer basePlayer ? basePlayer.UserIDString : GetSteamIdString(userid);
        OnPlayerDisconnectedNormalized(steamId, reasonFinal);

        if (CourtApi.players != null && CourtApi.players.TryGetValue(userid, out CourtApi.PluginStatePlayerDto? dto))
        {
            dto.FreePooledFields();
            CourtApi.players.Remove(userid);
        }
        _tempDisconnectReasons.Remove(userid);
    }

    #endregion

    #region Team hooks

    private void OnTeamKick(RelationshipManager.PlayerTeam team, BasePlayer player, ulong target)
    {
        SetTeamChange(player.UserIDString, GetSteamIdString(target));
    }

    private void OnTeamDisband(RelationshipManager.PlayerTeam team)
    {
        List<ulong>? members = team.members;
        for (int i = 0; i < members.Count; i++)
        {
            string id = GetSteamIdString(members[i]);
            SetTeamChange(id, id);
        }
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

    private static string[] _chatCommandPrefixes = new[] { "/" };

    private object OnClientCommand(Connection connection, string text)
    {
        if (!IsChatSayCommand(text) || HasCommandPrefix(text))
        {
            return null;
        }

        var mute = _RustAppEngine?.PlayerMuteWorker?.GetMute(connection.userid);
        if (mute != null && mute.LeftSeconds() > 0)
        {
            var msg = _RustApp.lang.GetMessage("System.Mute.Message.Self", _RustApp, GetSteamIdString(connection.userid))
                .Replace("%REASON%", mute.reason)
                .Replace("%TIME%", mute.GetLeftTime());

            if (connection.player is BasePlayer player)
            {
                SendMessage(player, msg);
            }

            return _false;
        }

        return null;

        static bool IsChatSayCommand(string text)
        {
            return text.StartsWith("chat.say", StringComparison.OrdinalIgnoreCase);
        }

        static bool HasCommandPrefix(string text)
        {
            const int ChatSayLength = 8;

            if (text.Length < ChatSayLength + 2)
            {
                return false;
            }

            var textSpan = text.AsSpan().Slice(ChatSayLength); // here we have at least 2 chars in textSpan
            textSpan = textSpan.TrimStart().TrimStart('"'); // remove whitespaces and quotes
            if (textSpan.IsEmpty)
            {
                return false;
            }

            foreach (var prefix in _chatCommandPrefixes)
            {
                if (textSpan.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void OnPlayerChat(BasePlayer player, string message, Chat.ChatChannel channel)
    {
        if (channel is not Chat.ChatChannel.Team and not Chat.ChatChannel.Global and not Chat.ChatChannel.Local)
            return;

        ChatWorker? worker = _RustAppEngine?.ChatWorker;
        if (worker == null) return;

        worker.SaveChatMessage(CourtApi.PluginChatMessageDto.Create(player.UserIDString, message, channel == Chat.ChatChannel.Team));
    }

    #endregion

    #region Sleeping bag

    private void CanAssignBed(BasePlayer player, SleepingBag bag, ulong targetPlayerId)
    {
        SleepingBagWorker? worker = _RustAppEngine?.SleepingBagWorker;
        if (worker == null) return;

        worker.AddSleepingBag(CourtApi.PluginSleepingBagDto.Create(
            player.UserIDString,
            GetSteamIdString(targetPlayerId),
            bag.transform.position.ToString(),
            player.Team?.members?.Contains(targetPlayerId) ?? false));
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
        SendMessage(reporter, lang.GetMessage("Sent.F7", this, reporter.UserIDString));
    }

    #endregion

    #region Queue hooks

    #region Health check

    private object RustApp_InternalQueue_HealthCheck(JObject raw)
    {
        return _true;
    }

    #endregion

    #region PlayerMute

    private class QueuePlayerMute
    {
        public string type;

        public CourtApi.PlayerMuteDto data;

        public bool chat_broadcast;
    }

    private object RustApp_InternalQueue_PlayerMute(JObject raw)
    {
        var mute = raw.ToObject<QueuePlayerMute>();

        if (mute.type == "created")
        {
            _RustAppEngine?.PlayerMuteWorker?.AddPlayerMute(mute.data);

            if (mute.chat_broadcast)
            {
                var target = BasePlayer.Find(mute.data.target_steam_id);
                if (target == null)
                {
                    return _true;
                }

                foreach (var player in BasePlayer.activePlayerList)
                {
                    var msg = _RustApp.lang.GetMessage("System.Mute.Broadcast.Mute", _RustApp, player.UserIDString).Replace("%TARGET%", target.displayName).Replace("%REASON%", mute.data.reason).Replace("%TIME%", mute.data.GetLeftTime());
                    _RustApp.SendMessage(player, msg);
                }
            }
        }
        else
        {
            _RustAppEngine?.PlayerMuteWorker?.RemovePlayerMute(mute.data);
        }

        return _true;
    }

    #endregion

    #region Kick

    private class QueueTaskKickDto
    {
        public string steam_id;
        public string reason;
        public bool announce;
    }

    private object RustApp_InternalQueue_Kick(JObject raw)
    {
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

        return _true;
    }

    #endregion

    #region Ban

    private class QueueTaskBanDto
    {
        public string steam_id;
        public string name;
        public string reason;

        public bool broadcast;
    }

    private object RustApp_InternalQueue_Ban(JObject raw)
    {
        var data = raw.ToObject<QueueTaskBanDto>();

        var ignoreQueueBan = Interface.Oxide.CallHook("RustApp_CanIgnoreBan", data.steam_id);
        if (ignoreQueueBan != null)
        {
            return "An external plugin has overridden queue-ban (RustApp_CanIgnoreBan)";
        }

        // IP address is not relevant in this case
        _RustAppEngine?.BanWorker?.CheckBans(data.steam_id, "1.1.1.1");

        if (!data.broadcast)
        {
            return _true;
        }

        foreach (var player in BasePlayer.activePlayerList)
        {
            var msg = _RustApp.lang.GetMessage("System.Ban.Broadcast", _RustApp, player.UserIDString).Replace("%TARGET%", data.name).Replace("%REASON%", data.reason);

            _RustApp.SendMessage(player, msg);
        }

        return _true;
    }

    #endregion

    #region NoticeStateGet

    private class QueueTaskNoticeStateGetDto
    {
        public string steam_id;
    }

    private object RustApp_InternalQueue_NoticeStateGet(JObject raw)
    {
        var data = raw.ToObject<QueueTaskNoticeStateGetDto>();
        if (_RustAppEngine?.CheckWorker == null)
        {
            return _false;
        }

        return (_RustAppEngine?.CheckWorker?.IsNoticeActive(data.steam_id) ?? false) ? _true : _false;
    }

    #endregion

    #region NoticeStateSet

    private class QueueTaskNoticeStateSetDto
    {
        public string steam_id;
        public bool value;
    }

    private object RustApp_InternalQueue_NoticeStateSet(JObject raw)
    {
        var data = raw.ToObject<QueueTaskNoticeStateSetDto>();
        if (_RustAppEngine?.CheckWorker == null)
        {
            return _false;
        }

        _RustAppEngine?.CheckWorker?.SetNoticeActive(data.steam_id, data.value);

        return _true;
    }

    #endregion

    #region ChatMessage

    private class QueueTaskChatMessageDto
    {
        public string initiator_name;
        [CanBeNull] public string initiator_steam_id;

        [CanBeNull] public string target_steam_id;

        public string message;

        public string mode;
    }

    private object RustApp_InternalQueue_ChatMessage(JObject raw)
    {
        Debug("Queue chat message received");

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

            _RustApp.SendMessage(player, message, data.initiator_steam_id ?? "");

            _RustApp.SoundToast(player, _RustApp.lang.GetMessage("Chat.Direct.Toast", _RustApp, player.UserIDString), SoundToastType.Error);
        }
        else
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                var message = _RustApp.lang.GetMessage("System.Chat.Global", _RustApp, player.UserIDString)
                  .Replace("%CLIENT_TAG%", data.initiator_name)
                  .Replace("%MSG%", data.message);

                _RustApp.SendMessage(player, message, data.initiator_steam_id ?? "");
            }
        }

        return _true;
    }

    #endregion

    #region ExecuteCommand

    private class QueueTaskExecuteCommandDto
    {
        public List<string> commands;
    }

    private object RustApp_InternalQueue_ExecuteCommand(JObject raw)
    {
        if (!_Settings.components_custom_actions_enabled)
        {
            return "Console command execution is disabled";
        }

        var data = raw.ToObject<QueueTaskExecuteCommandDto>();

        var responses = new List<object>();

        for (var i = 0; i < data.commands.Count; i++)
        {
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

    private object RustApp_InternalQueue_DeleteEntity(JObject raw)
    {
        var data = raw.ToObject<QueueTaskDeleteEntityDto>();
        if (!ulong.TryParse(data.net_id, out var netIdParsed))
        {
            return _false;
        }

        var ent = BaseNetworkable.serverEntities.Find(new NetworkableId(netIdParsed));
        if (ent == null)
        {
            return _false;
        }

        ent.Kill();
        return _true;
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

    private object RustApp_InternalQueue_PaidAnnounceBan(JObject raw)
    {
        return this.RustApp_InternalQueue_BanEventCreated(raw);
    }

    private object RustApp_InternalQueue_BanEventCreated(JObject raw)
    {
        var data = raw.ToObject<QueueTaskPaidAnnounceBanDto>();

        // TODO: Deprecated
        Interface.Oxide.CallHook("RustApp_OnPaidAnnounceBan", data.suspect_id, data.targets, data.reason);

        if (!data.broadcast)
        {
            return _true;
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

        return _true;
    }

    #endregion

    #region PaidAnnounceClean (deprecated) -> AnnounceReportProcessed

    private class QueueTaskAnnounceReportProcessedDto
    {
        public bool broadcast = false;

        public string suspect_name;
        public string suspect_id;

        public List<string> targets = new List<string>();
    }

    private object RustApp_InternalQueue_PaidAnnounceClean(JObject raw)
    {
        return this.RustApp_InternalQueue_AnnounceReportProcessed(raw);
    }

    private object RustApp_InternalQueue_AnnounceReportProcessed(JObject raw)
    {
        var data = raw.ToObject<QueueTaskAnnounceReportProcessedDto>();

        _CheckInfo.LastChecks[data.suspect_id] = _RustApp.CurrentTime();

        Interface.Oxide.CallHook("RustApp_OnPaidAnnounceClean", data.suspect_id, data.targets);

        CheckInfo.write(_CheckInfo);

        if (!data.broadcast)
        {
            return _true;
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

        return _true;
    }

    #endregion

    #region CheckStarted

    private class QueueTaskCheckStartedDto
    {
        public string steam_id;
        public bool broadcast;
    }

    private object RustApp_InternalQueue_CheckStarted(JObject raw)
    {
        var data = raw.ToObject<QueueTaskCheckStartedDto>();
        if (!data.broadcast)
        {
            return _true;
        }

        foreach (var check in BasePlayer.activePlayerList)
        {
            SendMessage(check, lang.GetMessage("Check.Started", this, check.UserIDString).Replace("%NAME%", permission.GetUserData(data.steam_id).LastSeenNickname));
        }

        return _true;
    }

    #endregion

    #region CheckFinished

    private class QueueTaskCheckFinishedDto
    {
        public string steam_id;

        public bool is_canceled;
        public bool is_clear;
        public bool is_ban;

        public bool broadcast;
    }

    private object RustApp_InternalQueue_CheckFinished(JObject raw)
    {
        var data = raw.ToObject<QueueTaskCheckFinishedDto>();
        if (!data.broadcast || !data.is_clear)
        {
            return _true;
        }

        foreach (var check in BasePlayer.activePlayerList)
        {
            SendMessage(check, lang.GetMessage("Check.FinishedClear", this, check.UserIDString).Replace("%NAME%", permission.GetUserData(data.steam_id).LastSeenNickname));
        }

        return _true;
    }

    #endregion

    #endregion

    #region Kills

    private readonly struct HitRecord
    {
        public readonly string? InitiatorSteamId;
        public readonly string Weapon;
        public readonly float Distance;
        public readonly bool IsHeadshot;

        public HitRecord(HitInfo? info)
        {
            if (info is null)
                return;

            InitiatorSteamId = info.InitiatorPlayer != null ? info.InitiatorPlayer.UserIDString : null;

            Distance = info.ProjectileDistance;
            IsHeadshot = info.isHeadshot;

            Weapon = ResolveWeaponName(info);
        }
    }

    private void OnPlayerWound(BasePlayer player, HitInfo? info)
    {
        if (_RustAppEngine?.KillsWorker == null || !IsRealSteamId(player) || info?.InitiatorPlayer is null || info.InitiatorPlayer == player)
        {
            return;
        }

        // Bombardir: We can't save HitInfo there as it's pooled and will be invalid after this function completes.
        _RustAppEngine.KillsWorker.WoundedHits[player.UserIDString] = new HitRecord(info);
    }

    private void OnPlayerRespawn(BasePlayer player)
    {
        if (!IsRealSteamId(player))
            return;

        _RustAppEngine?.KillsWorker?.WoundedHits.Remove(player.UserIDString);
    }

    private void OnPlayerRecovered(BasePlayer player)
    {
        if (!IsRealSteamId(player))
            return;

        _RustAppEngine?.KillsWorker?.WoundedHits.Remove(player.UserIDString);
    }

    private void OnPlayerDeath(BasePlayer player, HitInfo info)
    {
        if (!IsRealSteamId(player))
            return;

        HitRecord hitRecord = GetRealInfo(player, info);
        string? targetId = player.UserIDString;
        if (hitRecord.InitiatorSteamId == null || hitRecord.InitiatorSteamId == targetId)
        {
            return;
        }

        ulong playerUserId = player.userID.Get();

        NextFrame(() =>
        {
            KillsWorker? worker = _RustAppEngine?.KillsWorker;
            if (worker == null) return;

            List<CourtApi.CombatLogEventDto> log = GetCorrectCombatlog(playerUserId);
            worker.AddKill(CourtApi.PluginKillEntryDto.Create(
                hitRecord.InitiatorSteamId,
                targetId,
                Env.time.ToTimeSpan().ToShortString(),
                hitRecord.Distance,
                hitRecord.Weapon,
                hitRecord.IsHeadshot,
                log));
        });
    }

    #endregion

    #endregion

    #region Interface

    private const string ReportLayer = "UI_RP_ReportPanelUI";
    private const string CheckLayer = "RP_PrivateLayer";

    private void DrawReportInterface(BasePlayer player, int page = 0, string search = "", bool redraw = false)
    {
        const int ColumnCount = 6;
        const int PageSize = 18;
        const int MinRows = 3;
        const int LineMargin = 8;
        const float size = (float)(700 - LineMargin * ColumnCount) / ColumnCount;

        bool hasSearch = !string.IsNullOrEmpty(search);
        List<BasePlayer> filtered = Pool.Get<List<BasePlayer>>();
        try
        {
            ListHashSet<BasePlayer>? src = BasePlayer.activePlayerList;
            if (!hasSearch)
            {
                for (int i = 0; i < src.Count; i++) filtered.Add(src[i]);
            }
            else
            {
                for (int i = 0; i < src.Count; i++)
                {
                    BasePlayer? p = src[i];
                    if (p.displayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                     || p.UserIDString.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                        filtered.Add(p);
                }
            }

            int total = filtered.Count;
            int start = page * PageSize;
            int end = Math.Min(start + PageSize, total);
            int shown = Math.Max(0, end - start);

            if (shown == 0 && !hasSearch && page > 0)
            {
                DrawReportInterface(player, page - 1);
                return;
            }

            bool hasNextPage = total > end;
            bool hasPrevPage = page > 0;

            double nowTs = CurrentTime();
            double checkExpireSeconds = _Settings.report_ui_show_check_in * 24 * 60 * 60;

            CuiElementContainer container = new CuiElementContainer();

            if (!redraw)
            {
                container.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { OffsetMax = "0 0" },
                    Image = { Color = "0 0 0 0.8", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" }
                }, "Overlay", ReportLayer, ReportLayer);

                container.Add(new CuiButton()
                {
                    RectTransform = { OffsetMax = "0 0" },
                    Button = { Color = "0.20 0.20 0.20 1.00", Sprite = "assets/content/ui/ui.background.transparent.radial.psd", Close = ReportLayer },
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
                RectTransform = { AnchorMin = "1 0", OffsetMin = "-36 0", OffsetMax = "0 0" },
                Image = { Color = "0 0 1 0" }
            }, ReportLayer + ".C", ReportLayer + ".R");

            int nextPageNum = hasNextPage ? page + 1 : page;
            int prevPageNum = hasPrevPage ? page - 1 : 0;

            container.Add(new CuiButton()
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.5", OffsetMin = "0 0", OffsetMax = "0 -4" },
                Button = {
                  Color = hasNextPage ? "0.816 0.776 0.741 0.3" : "0.816 0.776 0.741 0.2",
                  Command = ZString.Format("{0} page {1} {2}", _UiCommandName, nextPageNum, search)
                },
                Text = { Text = "↓", Align = TextAnchor.MiddleCenter, FontSize = 24, Color = hasNextPage ? "0.816 0.776 0.741" : "0.816 0.776 0.741 0.3" }
            }, ReportLayer + ".R", ReportLayer + ".RD");

            container.Add(new CuiButton()
            {
                RectTransform = { AnchorMin = "0 0.5", AnchorMax = "1 1", OffsetMin = "0 4", OffsetMax = "0 0" },
                Button = {
                  Color = hasPrevPage ? "0.816 0.776 0.741 0.3" : "0.816 0.776 0.741 0.2",
                  Command = ZString.Format("{0} page {1} {2}", _UiCommandName, prevPageNum, search)
                },
                Text = { Text = "↑", Align = TextAnchor.MiddleCenter, FontSize = 24, Color = hasPrevPage ? "0.816 0.776 0.741" : "0.816 0.776 0.741 0.3" }
            }, ReportLayer + ".R", ReportLayer + ".RU");

            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "1 1", OffsetMin = "-250 8", OffsetMax = "0 43" },
                Image = { Color = "0.816 0.776 0.741 0.2" }
            }, ReportLayer + ".C", ReportLayer + ".S");

            string searchCommand = _UiSearchCommand;

            container.Add(new CuiElement
            {
                Parent = ReportLayer + ".S",
                Components =
                {
                    new CuiInputFieldComponent {
                        Text = $"{lang.GetMessage("Header.Search.Placeholder", this, player.UserIDString)}",
                        FontSize = 14,
                        Font = "robotocondensed-regular.ttf",
                        Color = "0.816 0.776 0.741 0.5",
                        Align = TextAnchor.MiddleLeft,
                        Command = searchCommand,
                        NeedsKeyboard = true
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 0", OffsetMax = "-85 0"}
                }
            });

            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "1 0", OffsetMin = "-75 0", OffsetMax = "0 0" },
                Button = { Color = "0.816 0.776 0.741", Material = "assets/icons/greyout.mat" },
                Text = { Text = $"{lang.GetMessage("Header.Search", this, player.UserIDString)}", Color = "0.267 0.247 0.231", Align = TextAnchor.MiddleCenter }
            }, ReportLayer + ".S", ReportLayer + ".SB");

            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0.5 1", OffsetMin = "0 7", OffsetMax = "0 47" },
                Image = { Color = "0.8 0.8 0.8 0" }
            }, ReportLayer + ".C", ReportLayer + ".LT");

            container.Add(new CuiLabel()
            {
                RectTransform = { OffsetMax = "0 0" },
                Text = { Text = $"{lang.GetMessage("Header.Find", this, player.UserIDString)} {(hasSearch ? $"- {(search.Length > 20 ? search.Substring(0, 14).ToUpper() + "..." : search.ToUpper())}" : "")}", Color = "0.816 0.776 0.741", FontSize = 24, Align = TextAnchor.UpperLeft }
            }, ReportLayer + ".LT");

            container.Add(new CuiLabel()
            {
                RectTransform = { OffsetMax = "0 0" },
                Text = { Text = !hasSearch ? lang.GetMessage("Header.SubDefault", this, player.UserIDString) : total == 0 ? lang.GetMessage("Header.SubFindEmpty", this, player.UserIDString) : lang.GetMessage("Header.SubFindResults", this, player.UserIDString), Font = "robotocondensed-regular.ttf", Color = "0.816 0.776 0.741 0.3", Align = TextAnchor.LowerLeft }
            }, ReportLayer + ".LT");

            container.Add(new CuiPanel
            {
                RectTransform = { OffsetMax = "-40 0" },
                Image = { Color = "0 1 0 0" }
            }, ReportLayer + ".C", ReportLayer + ".L");

            int rowCount = Math.Max((shown + ColumnCount - 1) / ColumnCount, MinRows);
            for (int y = 0; y < rowCount; y++)
            {
                for (int x = 0; x < ColumnCount; x++)
                {
                    int idx = start + y * ColumnCount + x;
                    BasePlayer? target = idx < end ? filtered[idx] : null;

                    if (target != null)
                    {
                        string? targetId = target.UserIDString;

                        container.Add(new CuiPanel
                        {
                            RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x * size + LineMargin * x} -{(y + 1) * size + LineMargin * y}", OffsetMax = $"{(x + 1) * size + LineMargin * x} -{y * size + LineMargin * y}" },
                            Image = { Color = "0.816 0.776 0.741 0.2" }
                        }, ReportLayer + ".L", ReportLayer + $".{targetId}");

                        container.Add(new CuiElement
                        {
                            Parent = ReportLayer + $".{targetId}",
                            Components =
                            {
                                // Do not change in devblogs
                                new CuiRawImageComponent { SteamId = targetId, Sprite = "assets/icons/loading.png" },
                                new CuiRectTransformComponent { OffsetMax = "0 0" }
                            }
                        });

                        container.Add(new CuiPanel()
                        {
                            RectTransform = { OffsetMax = "0 0" },
                            Image = { Sprite = "assets/content/ui/ui.background.transparent.linear.psd", Color = "0.157 0.157 0.157 0.95" }
                        }, ReportLayer + $".{targetId}");

                        string normaliseName = NormalizeString(target.displayName);
                        string name = normaliseName.Length > 14 ? normaliseName.Substring(0, 15) + ".." : normaliseName;

                        container.Add(new CuiLabel
                        {
                            RectTransform = { OffsetMin = "6 16", OffsetMax = "0 0" },
                            Text = { Text = name, Align = TextAnchor.LowerLeft, FontSize = 13, Color = "0.816 0.776 0.741" }
                        }, ReportLayer + $".{targetId}");

                        container.Add(new CuiLabel
                        {
                            RectTransform = { OffsetMin = "6 5", OffsetMax = "0 0" },
                            Text = { Text = targetId, Align = TextAnchor.LowerLeft, Font = "robotocondensed-regular.ttf", FontSize = 10, Color = "0.816 0.776 0.741 0.5" }
                        }, ReportLayer + $".{targetId}");

                        string showPlayerCommand = ZString.Format("{0} show {1} {2} {3}", _UiCommandName, targetId, x, y);

                        container.Add(new CuiButton()
                        {
                            RectTransform = { OffsetMax = "0 0" },
                            Button = { Color = "0 0 0 0", Command = showPlayerCommand },
                            Text = { Text = "" }
                        }, ReportLayer + $".{targetId}");

                        bool wasChecked = _CheckInfo.LastChecks.TryGetValue(targetId, out double lastCheck)
                                          && nowTs - lastCheck < checkExpireSeconds;
                        if (wasChecked)
                        {
                            container.Add(new CuiPanel
                            {
                                RectTransform = { AnchorMin = "0 1", OffsetMin = "5 -25", OffsetMax = "-5 -5" },
                                Image = { Color = "0.239 0.568 0.294 1", Material = "assets/icons/greyout.mat" },
                            }, ReportLayer + $".{targetId}", ReportLayer + $".{targetId}.Recent");

                            container.Add(new CuiLabel
                            {
                                RectTransform = { OffsetMax = "0 0" },
                                Text = { Text = lang.GetMessage("UI.CheckMark", this, player.UserIDString), Align = TextAnchor.MiddleCenter, Font = "robotocondensed-regular.ttf", FontSize = 12, Color = "0.639 0.968 0.694 1" }
                            }, ReportLayer + $".{targetId}.Recent");
                        }
                    }
                    else
                    {
                        container.Add(new CuiPanel
                        {
                            RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x * size + LineMargin * x} -{(y + 1) * size + LineMargin * y}", OffsetMax = $"{(x + 1) * size + LineMargin * x} -{y * size + LineMargin * y}" },
                            Image = { Color = "0.816 0.776 0.741 0.2" }
                        }, ReportLayer + ".L");
                    }
                }
            }

            CuiHelper.AddUi(player, container);
        }
        finally
        {
            Pool.FreeUnmanaged(ref filtered);
        }
    }

    private void DrawPlayerReportReasons(BasePlayer player, string targetId, int gridX, int gridY)
    {
        BasePlayer target = BasePlayer.Find(targetId) ?? BasePlayer.FindSleeping(targetId);
        if (target == null)
        {
            Puts($"Trying report not exists player: {targetId}");
            return;
        }

        PlayEffect(player, FxButtonUnpress);

        const int ColumnCount = 6;
        const int LineMargin = 8;
        const float size = (float)(700 - LineMargin * ColumnCount) / ColumnCount;

        string min = $"{gridX * size + LineMargin * gridX} -{(gridY + 1) * size + LineMargin * gridY}";
        string max = $"{(gridX + 1) * size + LineMargin * gridX} -{gridY * size + LineMargin * gridY}";
        bool leftAlign = gridX >= 3;

        CuiElementContainer container = new CuiElementContainer();
        CuiHelper.DestroyUi(player, ReportLayer + $".T");

        container.Add(new CuiPanel
        {
            RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = min, OffsetMax = max },
            Image = { Color = "0 0 0 1" }
        }, ReportLayer + $".L", ReportLayer + $".T");


        container.Add(new CuiButton()
        {
            RectTransform = { OffsetMin = $"-500 -500", OffsetMax = $"500 500" },
            Button = { Close = $"{ReportLayer}.T", Color = "0 0 0 1", Sprite = "assets/content/ui/gameui/attackheli/compass/ui.soft.radial.png" }
        }, ReportLayer + $".T");


        container.Add(new CuiButton()
        {
            RectTransform = { AnchorMin = $"{(leftAlign ? -1 : 2)} 0", AnchorMax = $"{(leftAlign ? -2 : 3)} 1", OffsetMin = $"-500 -500", OffsetMax = $"500 500" },
            Button = { Close = $"{ReportLayer}.T", Color = "0.204 0.204 0.204", Sprite = "assets/content/ui/gameui/attackheli/compass/ui.soft.radial.png" }
        }, ReportLayer + $".T");

        container.Add(new CuiButton()
        {
            RectTransform = { AnchorMax = "1 1", OffsetMin = $"-1111111 -1111111", OffsetMax = $"1111111 1111111" },
            Button = { Close = $"{ReportLayer}.T", Color = "0 0 0 0.5", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" }
        }, ReportLayer + $".T");


        container.Add(new CuiLabel
        {
            RectTransform = { AnchorMin = $"{(leftAlign ? "0" : "1")} 0", AnchorMax = $"{(leftAlign ? "0" : "1")} 1", OffsetMin = $"{(leftAlign ? "-350" : "20")} 0", OffsetMax = $"{(leftAlign ? "-20" : "350")} -5" },
            Text = { FadeIn = 0.4f, Text = lang.GetMessage("Subject.Head", this, player.UserIDString), Color = "0.816 0.776 0.741", FontSize = 24, Align = leftAlign ? TextAnchor.UpperRight : TextAnchor.UpperLeft }
        }, ReportLayer + ".T");

        container.Add(new CuiLabel
        {
            RectTransform = { AnchorMin = $"{(leftAlign ? "0" : "1")} 0", AnchorMax = $"{(leftAlign ? "0" : "1")} 1", OffsetMin = $"{(leftAlign ? "-250" : "20")} 0", OffsetMax = $"{(leftAlign ? "-20" : "250")} -35" },
            Text = { FadeIn = 0.4f, Text = $"{lang.GetMessage("Subject.SubHead", this, player.UserIDString).Replace("%PLAYER%", $"<b>{target.displayName}</b>")}", Font = "robotocondensed-regular.ttf", Color = "0.816 0.776 0.741 0.5", FontSize = 14, Align = leftAlign ? TextAnchor.UpperRight : TextAnchor.UpperLeft }
        }, ReportLayer + ".T");

        container.Add(new CuiElement
        {
            Parent = ReportLayer + $".T",
            Components = {
                // Do not change in devblogs
                new CuiRawImageComponent { SteamId = target.UserIDString, Sprite = "assets/icons/loading.png" },
                new CuiRectTransformComponent { OffsetMax = "0 0" }
            }
        });

        try
        {
            bool was_checked = _CheckInfo.LastChecks.TryGetValue(target.UserIDString, out var lastCheck)
                               && CurrentTime() - lastCheck < _Settings.report_ui_show_check_in * 24 * 60 * 60;
            if (was_checked)
            {
                container.Add(new CuiPanel
                {
                    RectTransform = { AnchorMin = "0 1", OffsetMin = "5 -25", OffsetMax = "-5 -5" },
                    Image = { Color = "0.239 0.568 0.294 1", Material = "assets/icons/greyout.mat" },
                }, ReportLayer + $".T", ReportLayer + $".T.Recent");

                container.Add(new CuiLabel
                {
                    RectTransform = { OffsetMax = "0 0" },
                    Text = { Text = lang.GetMessage("UI.CheckMark", this, player.UserIDString), Align = TextAnchor.MiddleCenter, Font = "robotocondensed-regular.ttf", FontSize = 12, Color = "0.639 0.968 0.694 1" }
                }, ReportLayer + $".T.Recent");
            }
        }
        catch
        {
            Puts($"Failed to apply was_checked mark for {targetId}");
        }

        try
        {
            for (int i = 0; i < _Settings.report_ui_reasons.Count; i++)
            {
                int offXMin = (20 + (i * 5)) + i * 80;
                int offXMax = 20 + (i * 5) + (i + 1) * 80;

                string sendReportCommand = ZString.Format("{0} send {1} {2}", _UiCommandName, target.UserIDString, _Settings.report_ui_reasons[i]);

                container.Add(new CuiButton()
                {
                    RectTransform = { AnchorMin = $"{(leftAlign ? 0 : 1)} 0", AnchorMax = $"{(leftAlign ? 0 : 1)} 0", OffsetMin = $"{(leftAlign ? -offXMax : offXMin)} 15", OffsetMax = $"{(leftAlign ? -offXMin : offXMax)} 45" },
                    Button = { FadeIn = 0.4f + i * 0.2f, Color = "0.816 0.776 0.741 0.3", Command = sendReportCommand },
                    Text = { FadeIn = 0.4f + i * 0.2f, Text = $"{_Settings.report_ui_reasons[i]}", Align = TextAnchor.MiddleCenter, Color = "0.816 0.776 0.741", FontSize = 16 }
                }, ReportLayer + $".T");
            }
        }
        catch
        {
            Puts($"Failed to add report reasons for {targetId}");
        }

        CuiHelper.AddUi(player, container);
    }

    private void DrawNoticeInterface(BasePlayer player)
    {
        CuiHelper.DestroyUi(player, CheckLayer);
        CuiElementContainer container = new CuiElementContainer();

        container.Add(new CuiButton
        {
            RectTransform = { AnchorMin = "0 0.5", OffsetMin = $"-500 -500", OffsetMax = $"500 500" },
            Button = { Color = "0.11 0.11 0.11", Sprite = "assets/content/ui/gameui/attackheli/compass/ui.soft.radial.png" },
            Text = { Text = "", Align = TextAnchor.MiddleCenter }
        }, "Under", CheckLayer);

        string text = lang.GetMessage("Check.Text", this, player.UserIDString).Replace("%COMMAND%", "/" + _Settings.check_contact_command);

        container.Add(new CuiLabel
        {
            RectTransform = { OffsetMax = "0 0" },
            Text = { Text = text, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-regular.ttf", FontSize = 16 }
        }, CheckLayer);

        CuiHelper.AddUi(player, container);

        PlayEffect(player, FxInviteNotice);
    }

    #endregion

    #region Methods

    private static string ResolveWeaponName(HitInfo? info)
    {
        if (info == null) return "unknown";

        Item? item = info.Weapon != null ? info.Weapon.GetItem() : null;
        string? itemShortname = item?.info?.shortname;
        if (!string.IsNullOrEmpty(itemShortname))
            return itemShortname!;

        string? weaponPrefabShort = info.WeaponPrefab != null ? info.WeaponPrefab.ShortPrefabName : null;
        if (!string.IsNullOrEmpty(weaponPrefabShort))
            return StripWeaponSuffixes(weaponPrefabShort!);

        if (info.ProjectilePrefab != null && !string.IsNullOrEmpty(info.ProjectilePrefab.name))
            return StripWeaponSuffixes(info.ProjectilePrefab.name);

        if (info.Initiator != null && !string.IsNullOrEmpty(info.Initiator.ShortPrefabName))
            return StripWeaponSuffixes(info.Initiator.ShortPrefabName);

        if (info.damageTypes != null)
        {
            DamageType dt = info.damageTypes.GetMajorityDamageType();
            if (dt != DamageType.Generic)
                return dt.ToString().ToLowerInvariant();
        }

        return "unknown";
    }

    private static string StripWeaponSuffixes(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        const string SuffixEntity = ".entity";
        const string SuffixDeployed = ".deployed";

        if (name.EndsWith(SuffixDeployed, StringComparison.Ordinal))
            name = name.Substring(0, name.Length - SuffixDeployed.Length);
        if (name.EndsWith(SuffixEntity, StringComparison.Ordinal))
            name = name.Substring(0, name.Length - SuffixEntity.Length);

        return name;
    }

    private static List<CourtApi.CombatLogEventDto> GetCorrectCombatlog(ulong target)
    {
        const int THRESHOLD_STREAK = 20;
        const int THRESHOLD_MAX_LIMIT = 30;

        Queue<CombatLog.Event>? allCombatlogs = CombatLog.Get(target);

        if (allCombatlogs == null || allCombatlogs.Count == 0)
            return null;


        List<CombatLog.Event>? logsList = Pool.Get<List<CombatLog.Event>>();
        Dictionary<ulong, string>? idCache = Pool.Get<Dictionary<ulong, string>>();

        try
        {
            logsList.AddRange(allCombatlogs);

            int logsLastIndex = logsList.Count - 1;
            CombatLog.Event killLog = logsList[logsLastIndex];

            List<CourtApi.CombatLogEventDto>? container = Pool.Get<List<CourtApi.CombatLogEventDto>>();
            container.Add(CourtApi.CombatLogEventDto.Create(killLog.time, killLog, idCache));

            for (int i = logsLastIndex - 1; i >= 0; i--)
            {
                CombatLog.Event ev = logsList[i];

                if (ev.target != "player" && ev.target != "you")
                    continue;

                if (ev.info == "killed")
                    break;

                float timeSincePreviousEvent = ev.time - logsList[i + 1].time;
                if (timeSincePreviousEvent > THRESHOLD_STREAK)
                    break;

                float timeSinceEvent = killLog.time - ev.time;
                if (timeSinceEvent > THRESHOLD_MAX_LIMIT)
                    break;

                container.Add(CourtApi.CombatLogEventDto.Create(killLog.time, ev, idCache));
            }

            return container;
        }
        finally
        {
            Pool.FreeUnmanaged(ref logsList);
            Pool.FreeUnmanaged(ref idCache);
        }
    }

    private HitRecord GetRealInfo(BasePlayer player, HitInfo info)
    {
        if (_RustAppEngine?.KillsWorker == null)
        {
            return new HitRecord(info);
        }

        if (info?.InitiatorPlayer is null || info.InitiatorPlayer == player)
        {
            if (_RustAppEngine.KillsWorker.WoundedHits.TryGetValue(player.UserIDString, out var realInfo))
            {
                return realInfo;
            }
        }

        return new HitRecord(info);
    }

    private void DestroyAllUi()
    {
        foreach (var check in BasePlayer.activePlayerList)
        {
            CuiHelper.DestroyUi(check, CheckLayer);
            CuiHelper.DestroyUi(check, ReportLayer);
        }
    }

    private void SendReport(BasePlayer initiator, string targetId, string reason)
    {
        if (_RustAppEngine?.ReportWorker == null)
        {
            CuiHelper.DestroyUi(initiator, ReportLayer);
            return;
        }

        var cooldowns = _RustAppEngine.ReportWorker.ReportCooldowns;
        var now = CurrentTime();

        if (cooldowns.TryGetValue(initiator.userID, out var until) && until > now)
        {
            var msg = lang.GetMessage("Cooldown", this, initiator.UserIDString)
                .Replace("%TIME%", $"{(until - now).ToString("0")}");

            SoundToast(initiator, msg, SoundToastType.Error);
            return;
        }

        RA_ReportSend(initiator.UserIDString, targetId, reason, "");

        CuiHelper.DestroyUi(initiator, ReportLayer);

        SoundToast(initiator, lang.GetMessage("Sent", this, initiator.UserIDString), SoundToastType.Info);

        cooldowns[initiator.userID] = now + _Settings.report_ui_cooldown;
    }

    private void OnPlayerConnectedNormalized(string steamId, string ip)
    {
        _RustAppEngine?.BanWorker?.CheckBans(steamId, ip);
    }

    private static readonly HashSet<string> _disconnectProcessed = new();

    private static void OnPlayerDisconnectedNormalized(string steamId, string reason)
    {
        if (!_disconnectProcessed.Add(steamId))
            return;

        if (_RustAppEngine?.StateWorker != null)
        {
            _RustAppEngine.StateWorker.DisconnectReasons[steamId] = reason;
        }

        _RustAppEngine?.CheckWorker?.SetNoticeActive(steamId, false);
    }

    private void SetTeamChange(string initiatorSteamId, string targetSteamId)
    {
        if (_RustAppEngine?.StateWorker != null)
        {
            _RustAppEngine.StateWorker.TeamChanges[initiatorSteamId] = targetSteamId;
        }
    }

    private void RustAppEngineCreate()
    {
        var obj = ServerMgr.Instance.gameObject.CreateChild();

        _RustAppEngine = obj.AddComponent<RustAppEngine>();
    }

    private void RustAppEngineDestroy()
    {
        UnityEngine.Object.Destroy(_RustAppEngine?.gameObject);


        if (CourtApi.players != null)
            foreach (CourtApi.PluginStatePlayerDto? dto in CourtApi.players.Values)
                dto.FreePooledFields();

        // Clean-up stale static references
        _RustApp = null;
        _MetaInfo = null;
        _CheckInfo = null;
        _Settings = null;
        _ApiHeaders = null;

        CourtApi.players = null;
    }

    private void BanCreate(string steamId, CourtApi.PluginBanCreatePayload payload)
    {
        string reason = payload.reason;

        CourtApi.BanCreate(payload).Execute(
            () => Log($"Player {steamId} banned for {reason}"),
            (err) => Error($"Failed to ban {steamId}. Reason: {err}"));

        Pool.Free(ref payload);
    }

    private void BanDelete(string steamId)
    {
        CourtApi.BanDelete(steamId).Execute(() =>
        {
            Log($"Player {steamId} unbanned");
        },
        (err) => Error($"Failed to unban {steamId}. Reason: {err}"));
    }

    private void CreatePlayerAlertsCustom(Plugin plugin, string message, object? data = null, object? meta = null)
    {
        CourtApi.PluginPlayerAlertCustomAlertMeta? json = meta as CourtApi.PluginPlayerAlertCustomAlertMeta;
        if (meta != null && json == null)
        {
            Error("Wrong CustomAlertMeta params, default will be used!");
        }

        CourtApi.PluginPlayerAlertCustomDto? payload = CourtApi.PluginPlayerAlertCustomDto.Create(
            message,
            data,
            customIcon: json?.custom_icon,
            category: $"{plugin.Name} • {(json?.name ?? "")}",
            customLinks: json?.custom_links);

        CourtApi.CreatePlayerAlertsCustom(payload).Execute(
            (error) => Debug($"Failed to send custom alert: {error}")
        );

        Pool.Free(ref payload);
    }

    #endregion

    #region StableRequest

    [ThreadStatic]
    private static char[] _reusableCharBuffer;
    private static readonly UTF8Encoding _encoding = new(false);

    public sealed class PooledTextReaderUtf8 : TextReader
    {
        private readonly int _length;
        private int _pos;

        public PooledTextReaderUtf8(ReadOnlySpan<byte> data)
        {
            var requiredBufferLen = _encoding.GetMaxCharCount(data.Length);
            if (_reusableCharBuffer == null || _reusableCharBuffer.Length < requiredBufferLen)
            {
                var length = _reusableCharBuffer?.Length ?? 2048;
                _reusableCharBuffer = new char[Math.Max(length * 2, requiredBufferLen)];
            }
            _length = _encoding.GetChars(data, _reusableCharBuffer);
            _pos = 0;

        }

        public override int Read(char[] buffer, int index, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer), "Buffer cannot be null.");
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), "Non-negative number required.");
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Non-negative number required.");
            if (buffer.Length - index < count)
                throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            int readCount = _length - _pos;
            if (readCount > 0)
            {
                if (readCount > count)
                    readCount = count;
                _reusableCharBuffer.AsSpan(_pos, readCount).CopyTo(buffer.AsSpan(index));
                _pos += readCount;
            }
            return readCount;
        }
    }

    public sealed class PooledTextWriterUtf8 : TextWriter
    {
        [ThreadStatic]
        private static byte[] _reusableByteBuffer;

        private int _pos;

        public PooledTextWriterUtf8() : base(CultureInfo.InvariantCulture)
        {
            _reusableCharBuffer ??= new char[4096];
            _pos = 0;
        }

        public ArraySegment<byte> AsArraySegment()
        {
            var requiredBufferLen = _encoding.GetMaxByteCount(_pos);
            if (_reusableByteBuffer == null || _reusableByteBuffer.Length < requiredBufferLen)
            {
                var length = _reusableByteBuffer?.Length ?? 2048;
                _reusableByteBuffer = new byte[Math.Max(length * 2, requiredBufferLen)];
            }
            var len = _encoding.GetBytes(_reusableCharBuffer, 0, _pos, _reusableByteBuffer, 0);
            return new ArraySegment<byte>(_reusableByteBuffer, 0, len);
        }

        public override Encoding Encoding => _encoding;

        public override void Write(char value)
        {
            Grow(1);
            _reusableCharBuffer[_pos++] = value;
        }

        public override void Write(char[] buffer, int index, int count)
        {
            Grow(count);
            buffer.AsSpan(index, count).CopyTo(_reusableCharBuffer.AsSpan(_pos));
            _pos += count;
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            Grow(buffer.Length);
            buffer.CopyTo(_reusableCharBuffer.AsSpan(_pos));
            _pos += buffer.Length;
        }

        public override void Write(string value)
        {
            if (value == null)
                return;
            Grow(value.Length);
            value.CopyTo(0, _reusableCharBuffer, _pos, value.Length);
            _pos += value.Length;
        }

        private void Grow(int requestedSize)
        {
            var freeSize = _reusableCharBuffer.Length - _pos;
            if (freeSize < requestedSize)
            {
                var newBuffer = new char[Math.Max(_reusableCharBuffer.Length * 2, _reusableCharBuffer.Length + requestedSize)];
                _reusableCharBuffer.AsSpan().CopyTo(newBuffer);
                _reusableCharBuffer = newBuffer;
            }
        }
    }

    public class StableRequest<T> where T : class
    {
        private Uri url;
        private string method;
        private object? data;

        public StableRequest(Uri url, RequestMethod requestMethod, object? data)
        {
            method = requestMethod switch
            {
                RequestMethod.GET => UnityWebRequest.kHttpVerbGET,
                RequestMethod.PUT => UnityWebRequest.kHttpVerbPUT,
                RequestMethod.POST => UnityWebRequest.kHttpVerbPOST,
                RequestMethod.DELETE => UnityWebRequest.kHttpVerbDELETE,
                _ => throw new ArgumentOutOfRangeException(nameof(requestMethod), requestMethod, null)
            };

            this.url = url;
            this.data = data;
        }

        public StableRequest(string url, RequestMethod requestMethod, object? data)
            : this(new Uri(url), requestMethod, data) { }

        public void Execute()
        {
            Rust.Global.Runner.StartCoroutine(SendWebRequestDeserialize(onComplete: null, onException: null));
        }

        public void Execute(Action<string> onException)
        {
            Rust.Global.Runner.StartCoroutine(SendWebRequestDeserialize(onComplete: null, onException));
        }

        public void Execute(Action<T> onComplete, Action<string> onException)
        {
            Rust.Global.Runner.StartCoroutine(SendWebRequestDeserialize(onComplete, onException));
        }

        // Overload for requests when we don't need to deserialize response
        public void Execute(Action onComplete, Action<string> onException)
        {
            Rust.Global.Runner.StartCoroutine(SendWebRequest(onComplete, onException));
        }

        private IEnumerator SendWebRequest(Action onComplete, Action<string> onException)
        {
            using var request = CreateWebRequest();

            yield return request.SendWebRequest();

            if (TryGetError(request, out var error))
            {
                onException?.Invoke(error);
                yield break;
            }

            onComplete?.Invoke();
        }

        private IEnumerator SendWebRequestDeserialize(Action<T> onComplete, Action<string> onException)
        {
            using var request = CreateWebRequest();

            yield return request.SendWebRequest();

            if (TryGetError(request, out var error))
            {
                onException?.Invoke(error);
                yield break;
            }

            if (onComplete == null)
            {
                yield break;
            }

            try
            {
                var obj = DeserializeWebResponse(request);
                onComplete.Invoke(obj);
            }
            catch (Exception parseException)
            {
                Error($"Failed to parse response ({request.method.ToUpper()} {request.url}): {parseException} (Response: {request.downloadHandler?.text})");
            }
        }

        private UnityWebRequest CreateWebRequest()
        {
            var request = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 10
            };

            foreach (var (name, value) in _ApiHeaders)
            {
                request.SetRequestHeader(name, value);
            }

            SetWebRequestPayload(request, data);
            return request;
        }

        private static void SetWebRequestPayload(UnityWebRequest request, object data)
        {
            if (data == null)
            {
                return;
            }

            using var stringWriter = new PooledTextWriterUtf8();
            _jsonSerializer.Serialize(stringWriter, data);
            var dataArray = stringWriter.AsArraySegment();

            var dataNativeArray = new NativeArray<byte>(dataArray.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            NativeArray<byte>.Copy(dataArray.Array, dataNativeArray, dataArray.Count);

            request.uploadHandler = new UploadHandlerRaw(dataNativeArray, transferOwnership: true)
            {
                contentType = "application/json"
            };
        }

        private static bool TryGetError(UnityWebRequest request, out string error)
        {
            if (request.result == UnityWebRequest.Result.Success)
            {
                error = null;
                return false;
            }

            error = $"Error: {request.result}. Message: {request.downloadHandler?.text.ToLower() ?? "possible network errors, contact @rustapp_help if you see > 5 minutes"}";
            if (error.Contains("502 bad gateway") || error.Contains("cloudflare"))
            {
                error = "rustapp is restarting, wait please";
            }
            return true;
        }

        private static T DeserializeWebResponse(UnityWebRequest request)
        {
            var data = request.downloadHandler.nativeData;
            if (data.Length == 0)
            {
                return default;
            }

            if (data.Length == 2 && data[0] == '[' && data[1] == ']')
            {
                return default;
            }

            using var textReader = new PooledTextReaderUtf8(data.AsReadOnlySpan());
            using var reader = new JsonTextReader(textReader);
            return _jsonSerializer.Deserialize<T>(reader);
        }
    }

    #endregion

    #region Plugin API

    private void RustApp_PlayerMuteCreate(string targetSteamId, string reason, string duration, string comment = null, string referenceMessageText = null, bool broadcast = false)
    {
        CourtApi.PlayerMuteCreateDto? payload = CourtApi.PlayerMuteCreateDto.Create(targetSteamId, reason, duration, broadcast, comment, referenceMessageText);

        CourtApi.PlayerMuteCreate(payload).Execute(
            () => Puts($"Player ({targetSteamId}) is muted"),
            (err) => PrintError($"Failed to mute player: {err}"));

        Pool.Free(ref payload);
    }

    private void RustApp_PlayerMuteDelete(string targetSteamId)
    {
        CourtApi.PlayerMuteDeleteDto? payload = CourtApi.PlayerMuteDeleteDto.Create(targetSteamId);

        CourtApi.PlayerMuteDelete(payload).Execute(
            () => Puts($"Player ({targetSteamId}) is unmuted"),
            (err) => PrintError($"Failed to unmute player: {err}"));

        Pool.Free(ref payload);
    }

    private long? RA_IsPlayerMuted(BasePlayer player)
    {
        CourtApi.PlayerMuteDto? mute = _RustAppEngine?.PlayerMuteWorker?.GetMute(player.userID);

        return mute?.LeftSeconds();
    }

    private void RA_DirectMessageHandler(string from, string to, string message)
    {
        ChatWorker? worker = _RustAppEngine?.ChatWorker;
        if (worker == null) return;

        worker.SaveChatMessage(CourtApi.PluginChatMessageDto.Create(from, message, isTeam: false, targetSteamId: to));
    }

    private void RA_ReportSend(string initiator_steam_id, string target_steam_id, string reason, string message = "")
    {
        if (initiator_steam_id == target_steam_id)
        {
            return;
        }

        bool was_checked = _CheckInfo.LastChecks.TryGetValue(target_steam_id, out var lastCheck)
                           && CurrentTime() - lastCheck < _Settings.report_ui_show_check_in * 24 * 60 * 60;
        Interface.Oxide.CallHook("RustApp_OnPlayerReported", initiator_steam_id, target_steam_id, reason, message, was_checked);

        ReportWorker? worker = _RustAppEngine?.ReportWorker;
        if (worker == null) return;

        worker.SendReport(CourtApi.PluginReportDto.Create(initiator_steam_id, target_steam_id, reason, message));
    }

    private void RA_BanPlayer(string steam_id, string reason, string duration, bool global, bool ban_ip, string comment = "")
    {
        BanCreate(steam_id, CourtApi.PluginBanCreatePayload.Create(
            steam_id, reason, duration.Length > 0 ? duration : null, global, ban_ip, comment));
    }

    private void RA_CreateAlert(Plugin plugin, string message, object data = null, object meta = null)
    {
        CreatePlayerAlertsCustom(plugin, message, data, meta);
    }

    #endregion

    #region Utils

    private void RegisterCommands()
    {
        _Settings.report_ui_commands.ForEach(v => cmd.AddChatCommand(v, this, nameof(CmdChatReportInterface)));

        cmd.AddChatCommand(_Settings.check_contact_command, this, nameof(CmdSendContact));
        cmd.AddConsoleCommand(_UiCommandName, this, nameof(OnUiCommand));
    }

    private bool CheckRequiredPlugins()
    {
        if (plugins.Find("RustAppLite") != null && plugins.Find("RustAppLite").IsLoaded)
        {
            Error(
              "Detected 'Lite' plugin version, to start you should delete plugin: RustAppLite.cs"
            );
            return false;
        }

        return true;
    }

    private static void Trace(string text)
    {
#if TRACE

        _RustApp?.Puts($"TRACE | {text}");

#endif
    }

    private static void Debug(string text)
    {
#if DEBUG

        _RustApp?.Puts($"DEBUG | {text}");

#endif
    }

    private static void Log(string text)
    {
        _RustApp?.Puts(text);
    }

    private static void Error(string text)
    {
        _RustApp?.Puts(text);
    }

    private class RustAppWorker : MonoBehaviour
    {
        public void Awake()
        {
            Trace($"{this.GetType().Name} worker enabled");
        }

        public void OnDestroy()
        {
            Trace($"{this.GetType().Name} worker disabled");
        }
    }

    private enum SoundToastType
    {
        Info = 2,
        Error = 1
    }

    private const string FxToastSelect = "assets/bundled/prefabs/fx/notice/item.select.fx.prefab";
    private const string FxButtonUnpress = "assets/prefabs/tools/detonator/effects/unpress.prefab";
    private const string FxInviteNotice = "assets/bundled/prefabs/fx/invite_notice.prefab";

    private static void PlayEffect(BasePlayer player, string prefabPath)
    {
        Effect? effect = Effect.reusableInstance;
        effect.Init(Effect.Type.Generic, player, 0, Vector3.zero, Vector3.zero);
        effect.pooledString = prefabPath;
        EffectNetwork.Send(effect, player.Connection);
    }

    private void SoundToast(BasePlayer player, string text, SoundToastType type)
    {
        PlayEffect(player, FxToastSelect);

        player.Command("gametip.showtoast", (int)type, text, 1);
    }

    private static readonly HashSet<uint> _buildAuthSeen = new HashSet<uint>();
    private static readonly BaseEntity[] _buildAuthSearchArr = new BaseEntity[16384];
    private static readonly Func<BaseEntity, bool> _buildAuthIsBlock = static e => e is BuildingBlock;

    private static bool DetectBuildingAuth(BasePlayer player)
    {
        const float SearchRadius = 16f;
        const float SqrRadius = SearchRadius * SearchRadius;

        HashSet<uint> seen = _buildAuthSeen;
        BaseEntity[] arr = _buildAuthSearchArr;
        
        OBB playerObb = player.WorldSpaceBounds();
        ulong uid = player.userID;

        int entCount = BaseEntity.Query.Server.GetInSphereFast(playerObb.position, SearchRadius, arr, _buildAuthIsBlock);

        try
        {
            for (int i = 0; i < entCount; i++)
            {
                BuildingBlock block = (BuildingBlock)arr[i];

                if (seen.Contains(block.buildingID)) continue;

                if (playerObb.SqrDistance(block.WorldSpaceBounds()) > SqrRadius) continue;

                BuildingManager.Building? building = block.GetBuilding();
                if (building == null) continue;

                seen.Add(block.buildingID);

                ListHashSet<BuildingPrivlidge>? privs = building.buildingPrivileges;
                if (privs == null) continue;

                int tcCount = privs.Count;
                for (int j = 0; j < tcCount; j++)
                {
                    BuildingPrivlidge? tc = privs[j];
                    if (tc != null && tc.authorizedPlayers.Contains(uid))
                        return true;
                }
            }
            return false;
        }
        finally
        {
            seen.Clear();
            Array.Clear(arr, 0, entCount);
        }
    }

    private static readonly HashSet<char> _normalizeAllowed = new()
    {
        '☼', '^', '$', '+', '®', '#', '.', '_', ']', '[', '{', '}', '!', '@', '%', '&', '?', '-', '=', '~', ' ',
        '1', '2', '3', '4', '8',
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
        'а', 'б', 'в', 'г', 'д', 'е', 'ё', 'ж', 'з', 'и', 'й', 'к', 'л', 'м', 'н', 'о', 'п', 'р', 'с', 'т', 'у', 'ф', 'х', 'ц', 'ч', 'ш', 'щ', 'ь', 'ы', 'ъ', 'э', 'ю', 'я'
    };

    private static string NormalizeString(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        StringBuilder? sb = Pool.Get<StringBuilder>();
        try
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (_normalizeAllowed.Contains(char.ToLowerInvariant(c)))
                    sb.Append(c);
            }
            return sb.Length == 0 ? string.Empty : sb.ToString();
        }
        finally
        {
            Pool.FreeUnmanaged(ref sb);
        }
    }

    private static bool DetectIsRaidBlock(BasePlayer player)
    {
        if (_RustApp is not { IsLoaded: true } || _RustApp.NoEscape == null && _RustApp.RaidZone == null && _RustApp.ExtRaidBlock == null && _RustApp.RaidBlock == null)
        {
            return false;
        }

        try
        {
            if (_RustApp.NoEscape != null)
            {
                return (bool)_RustApp.NoEscape.Call("IsRaidBlocked", player);
            }

            if (_RustApp.RaidZone != null)
            {
                return (bool)_RustApp.RaidZone.Call("HasBlock", player.userID.Get());
            }

            if (_RustApp.ExtRaidBlock != null)
            {
                return (bool)_RustApp.ExtRaidBlock.Call("IsRaidBlock", player.userID.Get());
            }

            if (_RustApp.RaidBlock != null)
            {
                try
                {
                    return (bool)_RustApp.RaidBlock.Call("IsInRaid", player);
                }
                catch
                {
                    return (bool)_RustApp.RaidBlock.Call("IsRaidBlocked", player);
                }
            }
        }
        catch (Exception e)
        {
            Error("Failed to call RaidBlock API");
        }

        return false;
    }

    private static bool DetectNoLicense(Network.Connection connection)
    {
        if (_RustApp.MultiFighting != null && _RustApp.MultiFighting.IsLoaded)
        {
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

        if (_RustApp.TGPP != null && _RustApp.TGPP.IsLoaded)
        {
            try
            {
                var isSteam = (bool)_RustApp.TGPP.Call("IsSteam", connection);

                return !isSteam;
            }
            catch
            {
                return false;
            }
        }

        return connection.os == "nosteam" || connection.os == "nosteam-unsecured";
    }

    private static void ResurrectDictionary<T, V>(Dictionary<T, V> oldDict, Dictionary<T, V> newDict)
    {
        foreach (KeyValuePair<T, V> old in oldDict)
        {
            newDict[old.Key] = old.Value;
        }
    }

    private static CourtApi.PluginStatePlayerMetaDto CollectPlayerMeta(string steamId, CourtApi.PluginStatePlayerMetaDto meta)
    {
        Interface.Oxide.CallHook("RustApp_CollectPlayerTags", steamId, meta.tags);
        Interface.Oxide.CallHook("RustApp_CollectPlayerFields", steamId, meta.fields);

        return meta;
    }

    private bool CloseConnection(string steamId, string reason)
    {
        if (!ulong.TryParse(steamId, out ulong targetUserId))
        {
            Error($"Failed to close connection with {steamId}: {reason} (not a valid ulong)");
            return false;
        }

        BasePlayer? player = BasePlayer.FindByID(targetUserId);
        if (player != null && player.IsConnected)
        {
            Log($"Closing connection with {steamId}: {reason} (by player)");
            player.Kick(reason);
            OnPlayerDisconnectedNormalized(steamId, reason);
            return true;
        }

        List<Connection>? authList = ConnectionAuth.m_AuthConnection;
        for (int i = 0; i < authList.Count; i++)
        {
            Connection? c = authList[i];
            if (c.userid != targetUserId)
                continue;

            Log($"Closing connection with {steamId}: {reason} (by m_AuthConnection)");
            Network.Net.sv.Kick(c, reason);
            OnPlayerDisconnectedNormalized(steamId, reason);
            return true;
        }

        List<Connection>? joining = ServerMgr.Instance.connectionQueue.joining;
        for (int i = 0; i < joining.Count; i++)
        {
            Connection? c = joining[i];
            if (c.userid != targetUserId)
                continue;

            Log($"Closing connection with {steamId}: {reason} (by joining)");
            Network.Net.sv.Kick(c, reason);
            OnPlayerDisconnectedNormalized(steamId, reason);
            return true;
        }

        List<Connection>? queue = ServerMgr.Instance.connectionQueue.queue;
        for (int i = 0; i < queue.Count; i++)
        {
            Connection? c = queue[i];
            if (c.userid != targetUserId)
                continue;

            Log($"Closing connection with {steamId}: {reason} (by queued)");
            Network.Net.sv.Kick(c, reason);
            OnPlayerDisconnectedNormalized(steamId, reason);
            return true;
        }

        // Steam auth pending — Connection is also in m_AuthConnection normally, but check as a safety net
        List<Connection>? steamWaiting = Auth_Steam.waitingList;
        for (int i = 0; i < steamWaiting.Count; i++)
        {
            Connection? c = steamWaiting[i];
            if (c.userid != targetUserId)
                continue;

            Log($"Closing connection with {steamId}: {reason} (by Auth_Steam.waitingList)");
            Network.Net.sv.Kick(c, reason);
            OnPlayerDisconnectedNormalized(steamId, reason);
            return true;
        }

        // Connection is in a transition gap (between JoinedGame removing it from joining and BasePlayer.Spawn registering it).
        // Defer the kick — OnPlayerConnected will finish it once BasePlayer exists.
        _pendingKick[targetUserId] = reason;
        Log($"Closing connection with {steamId}: {reason} (deferred → not in any list, will kick on OnPlayerConnected)");
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

    public static string IPAddressWithoutPort(string ipWithPort)
    {
        int num = ipWithPort.LastIndexOf(':');
        if (num != -1)
        {
            return ipWithPort.Substring(0, num);
        }

        return ipWithPort;
    }

    private const ulong SteamId64Base = 76561197960265728UL;

    public static bool IsRealSteamId(ulong userId) => userId >= SteamId64Base;

    public static bool IsRealSteamId(string steamId)
        => !string.IsNullOrEmpty(steamId) && ulong.TryParse(steamId, out ulong id) && id >= SteamId64Base;

    public static bool IsRealSteamId(BasePlayer player)
        => player.userID >= SteamId64Base;

    private static readonly Dictionary<ulong, string> _steamIdStringCache = new();

    public static string GetSteamIdString(ulong userId)
    {
        if (_steamIdStringCache.TryGetValue(userId, out string? cached))
            return cached;
        return _steamIdStringCache[userId] = userId.ToString();
    }

    private double CurrentTime() => DateTime.Now.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

    #endregion

    #region SignFeed

    // A lot of code references at Discord Sign Logger by MJSU
    // Original author and plugin on UMod: https://umod.org/plugins/discord-sign-logger

    public abstract class BaseImageUpdate
    {
        public BasePlayer Player { get; }
        public string PlayerId { get; }
        public string DisplayName { get; }
        public BaseEntity Entity { get; }
        public int ItemId { get; protected set; }

        public uint TextureIndex { get; protected set; }
        public abstract bool SupportsTextureIndex { get; }

        protected BaseImageUpdate(BasePlayer player, BaseEntity entity)
        {
            Player = player;
            DisplayName = player.displayName;
            PlayerId = player.UserIDString;
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
            MemoryStream? stream = Pool.Get<MemoryStream>();
            try
            {
                image.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
            finally
            {
                Pool.FreeUnmanaged(ref stream);
            }
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

    private void OnEntityKill(BaseNetworkable entity)
    {
        if (entity is not ISignage || entity.net is null)
        {
            return;
        }

        _RustAppEngine?.SignageWorker?.AddSignageDestroy(entity.net.ID.Value.ToString());
    }

    private void OnImagePost(BasePlayer player, string url, bool raw, ISignage signage, uint textureIndex)
    {
        _RustAppEngine?.SignageWorker?.SignageCreate(new SignageUpdate(player, signage, textureIndex, url));
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

        _RustAppEngine?.SignageWorker?.SignageCreate(new SignageUpdate(player, signage, (uint)textureIndex));
    }

    private void OnItemPainted(PaintedItemStorageEntity entity, Item item, BasePlayer player, byte[] image)
    {
        if (entity._currentImageCrc == 0)
        {
            return;
        }

        _RustAppEngine?.SignageWorker?.SignageCreate(new PaintedItemUpdate(player, entity, item, image));
    }

    private void OnFireworkDesignChanged(PatternFirework firework, ProtoBuf.PatternFirework.Design design, BasePlayer player)
    {
        if (design?.stars == null || design.stars.Count == 0)
        {
            return;
        }

        _RustAppEngine?.SignageWorker?.SignageCreate(new FireworkUpdate(player, firework));
    }

    private void OnEntityBuilt(Planner plan, GameObject go)
    {
        if (go.ToBaseEntity() is not ISignage signage || plan.GetOwnerPlayer() is not BasePlayer player)
        {
            return;
        }

        NextTick(() =>
        {
            if (signage.IsUnityNull() || signage.GetTextureCRCs()[0] == 0)
            {
                return;
            }
            _RustAppEngine?.SignageWorker?.SignageCreate(new SignageUpdate(player, signage, 0));
        });
    }

    #endregion
}