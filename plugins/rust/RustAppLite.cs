using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;


namespace Oxide.Plugins
{
  [Info("RustAppLite", "RustApp.IO", "1.0.1")]
  [Description("Simple plugin for receiving reports.")]
  public class RustAppLite : RustPlugin
  {
    #region Configuration

    private class Configuration
    {
      [JsonProperty("[General] Language (en/ru)")]
      public string language = "ru";

      [JsonProperty("[UI] Chat commands")]
      public List<string> report_ui_commands = new List<string>();

      [JsonProperty("[UI] Report reasons")]
      public List<string> report_ui_reasons = new List<string>();

      [JsonProperty("[UI] Cooldown between reports (seconds)")]
      public int report_ui_cooldown = 300;

      [JsonProperty("[UI] Auto-parse reports from F7 (ingame reports)")]
      public bool report_ui_auto_parse = true;

      [JsonProperty("[Discord] Webhook to send reports")]
      public string discord_webhook = "";

      public static Configuration Generate()
      {
        return new Configuration
        {
          language = "ru",

          report_ui_commands = new List<string> { "report", "reports" },
          report_ui_reasons = new List<string> { "Cheat", "Abusive", "Spam" },
          report_ui_cooldown = 300,
          report_ui_auto_parse = true,
          discord_webhook = ""
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

    #region DiscordEmbedMessage
    public class DiscordMessage
    {
      public string content { get; set; }
      public DiscordEmbed[] embeds { get; set; }

      public DiscordMessage(string Content, DiscordEmbed[] Embeds = null)
      {
        content = Content;
        embeds = Embeds;
      }

      public void Send(string url)
      {
        _RustAppLite.webrequest.Enqueue(url, JsonConvert.SerializeObject(this), (code, response) => { }, _RustAppLite, Core.Libraries.RequestMethod.POST, _Headeers, 30f);
      }

      public static Dictionary<string, string> _Headeers = new Dictionary<string, string>
      {
        ["Content-Type"] = "application/json"
      };
    }
    public class DiscordEmbed
    {
      public string title { get; set; }
      public string description { get; set; }
      public int? color { get; set; }
      public DiscordField[] fields { get; set; }
      public DiscordFooter footer { get; set; }
      public DiscordAuthor author { get; set; }

      public DiscordEmbed(string Title, string Description, int? Color = null, DiscordField[] Fields = null, DiscordFooter Footer = null, DiscordAuthor Author = null)
      {
        title = Title;
        description = Description;
        color = Color;
        fields = Fields;
        footer = Footer;
        author = Author;
      }
    }
    public class DiscordFooter
    {
      public string text { get; set; }
      public string icon_url { get; set; }
      public string proxy_icon_url { get; set; }

      public DiscordFooter(string Text, string Icon_url, string Proxy_icon_url = null)
      {
        text = Text;
        icon_url = Icon_url;
        proxy_icon_url = Proxy_icon_url;
      }
    }
    public class DiscordAuthor
    {
      public string name { get; set; }
      public string url { get; set; }
      public string icon_url { get; set; }
      public string proxy_icon_url { get; set; }

      public DiscordAuthor(string Name, string Url, string Icon_url, string Proxy_icon_url = null)
      {
        name = Name;
        url = Url;
        icon_url = Icon_url;
        proxy_icon_url = Proxy_icon_url;
      }
    }
    public class DiscordField
    {
      public string name { get; set; }
      public string value { get; set; }
      public bool inline { get; set; }

      public DiscordField(string Name, string Value, bool Inline = false)
      {
        name = Name;
        value = Value;
        inline = Inline;
      }

    }
    #endregion DiscordEmbedMessage

    #region Interfaces

    private static string ReportLayer = "RAL_CommandHandlerUI";
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
        Button = { Color = HexToRustFormat($"#{(list.Count > 18 && finalList.Count() == 18 ? "D0C6BD4D" : "D0C6BD33")}"), Command = list.Count > 18 && finalList.Count() == 18 ? $"RAL_CommandHandler search {page + 1}" : "" },
        Text = { Text = "↓", Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf", FontSize = 24, Color = HexToRustFormat($"{(list.Count > 18 && finalList.Count() == 18 ? "D0C6BD" : "D0C6BD4D")}") }
      }, ReportLayer + ".R", ReportLayer + ".RD");

      container.Add(new CuiButton()
      {
        RectTransform = { AnchorMin = "0 0.5", AnchorMax = "1 1", OffsetMin = "0 4", OffsetMax = "0 0" },
        Button = { Color = HexToRustFormat($"#{(page == 0 ? "D0C6BD33" : "D0C6BD4D")}"), Command = page == 0 ? "" : $"RAL_CommandHandler search {page - 1}" },
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
                new CuiInputFieldComponent { Text = $"{lang.GetMessage("Header.Search.Placeholder", this, player.UserIDString)}", FontSize = 14, Font = "robotocondensed-regular.ttf", Color = HexToRustFormat("#D0C6BD80"), Align = TextAnchor.MiddleLeft, Command = "RAL_CommandHandler search 0", NeedsKeyboard = true},
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
              Button = { Color = "0 0 0 0", Command = $"RAL_CommandHandler show {target.UserIDString} {x * size + lineMargin * x} -{(y + 1) * size + lineMargin * y} {(x + 1) * size + lineMargin * x} -{y * size + lineMargin * y}  {x >= 3}" },
              Text = { Text = "" }
            }, ReportLayer + $".{target.UserIDString}");
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


    private static RustAppLite _RustAppLite;
    private static Configuration _Settings;
    private Dictionary<ulong, double> _Cooldowns = new Dictionary<ulong, double>();

    #endregion

    #region Initialization

    private void OnServerInitialized()
    {
      _RustAppLite = this;

      if (plugins.Find("ImageLibrary") == null)
      {
        Error(
          "Для работы плагина необходим установленный ImageLibrary",
          "For plugin correct works need to install ImageLibrary"
        );
        return;
      }

      if (_Settings.discord_webhook == null || _Settings.discord_webhook.Length < 5)
      {
        Error(
          "Установите discord_webhook в конфигурации плагина, и перезапустите плагин o.reload RustAppLite",
          "Setup discord_webhook in plugin config, then reload plugin o.reload RustAppLite"
        );
        return;
      }

      _Settings.report_ui_commands.ForEach(v =>
      {
        cmd.AddChatCommand(v, this, nameof(ChatCmdReport));
      });

      Log(
        "\nВы пользуетесь упрощённой версией плагина RustApp!\nВ полной версии есть:\n — вызов на проверку\n — проверка на АФК\n — история чата / команд\n — и многое другое на сайте: https://rustapp.io",
        "\nYou are using a simplified version of the RustApp plugin!\nThe full version contains:\n - call for verification\n - check for AFK\n - chat / command history\n - and much more on the website: https://rustapp.io"
      );

      RegisterMessages();
      WriteLiteMarker();
    }

    private void WriteLiteMarker()
    {
      if (Interface.Oxide.DataFileSystem.ExistsDatafile(Name))
      {
        return;
      }

      // Is using in main plugin to detect users, who switched from Lite to main branch
      Interface.Oxide.DataFileSystem.WriteObject(Name, CurrentTime());
    }

    private void RegisterMessages()
    {
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
      }, this, "en");

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
      }, this, "ru");
    }


    private void Unload()
    {
      foreach (var player in BasePlayer.activePlayerList)
      {
        CuiHelper.DestroyUi(player, ReportLayer);
      }
    }

    #endregion

    #region Hooks

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

    #region Commands

    [ConsoleCommand("RAL_CommandHandler")]
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

            for (var i = 0; i < _Settings.report_ui_reasons.Count; i++)
            {
              var offXMin = (20 + (i * 5)) + i * 80;
              var offXMax = 20 + (i * 5) + (i + 1) * 80;

              container.Add(new CuiButton()
              {
                RectTransform = { AnchorMin = $"{(leftAlign ? 0 : 1)} 0", AnchorMax = $"{(leftAlign ? 0 : 1)} 0", OffsetMin = $"{(leftAlign ? -offXMax : offXMin)} 15", OffsetMax = $"{(leftAlign ? -offXMin : offXMax)} 45" },
                Button = { FadeIn = 0.4f + i * 0.2f, Color = HexToRustFormat("#D0C6BD4D"), Command = $"RAL_CommandHandler report {target.UserIDString} {_Settings.report_ui_reasons[i].Replace(" ", "0")}" },
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

    #endregion

    #region User Manipulation 

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

      player.Command("gametip.showtoast", type, text);
    }

    #endregion

    #region Discord

    private void RA_ReportSend(string initiator_steam_id, string target_steam_id, string reason, string message = "")
    {
      var author = permission.GetUserData(initiator_steam_id);
      var target = permission.GetUserData(target_steam_id);

      var list = new DiscordField[4] {
        new DiscordField(Msg("Никнейм", "Nickname"), $"```{target.LastSeenNickname}```", false),
        new DiscordField($"SteamID", $"```{target_steam_id}```", true),
        new DiscordField(Msg("Причина", "Reason"), @$"```ansi
[2;31m{reason}[0m
```", true),
        null
      };

      if (message != null && message.Length > 0)
      {
        list[3] = new DiscordField(Msg("Комментарий", "Comment"), $"```{message}```", false);
      }

      DiscordEmbed embed = new DiscordEmbed("", $" ", null, list.Where(v => v != null).ToArray(), new DiscordFooter($"{Msg("Отправил жалобу", "Report sent")}: {author.LastSeenNickname} [{initiator_steam_id}]", "", ""));
      DiscordMessage req = new DiscordMessage(null, new DiscordEmbed[1] { embed });

      req.Send(_Settings.discord_webhook);
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


    private long getUnixTime()
    {
      return ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
    }

    #endregion

    #region Messages

    public string Msg(string ru, string en)
    {
      if (_Settings.language == "ru")
      {
        return ru;
      }
      else
      {
        return en;
      }
    }

    public void Log(string ru, string en)
    {
      if (_Settings.language == "ru")
      {
        Puts(ru);
      }
      else
      {
        Puts(en);
      }
    }

    public void Warning(string ru, string en)
    {
      if (_Settings.language == "ru")
      {
        PrintWarning(ru);
      }
      else
      {
        PrintWarning(en);
      }
    }

    public void Error(string ru, string en)
    {
      if (_Settings.language == "ru")
      {
        PrintError(ru);
      }
      else
      {
        PrintError(en);
      }
    }

    #endregion
  }
}