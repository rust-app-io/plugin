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


namespace Oxide.Plugins
{
  [Info("ExtStopDamageMan", "RustApp.Io", "1.0.0")]
  public class ExtStopDamageMan : RustPlugin
  {
    [PluginReference]
    private Plugin StopDamageMan;

    private void OnServerInitialized()
    {
      if (StopDamageMan == null || !StopDamageMan.IsLoaded)
      {
        PrintError("Плагин StopDamageMan не обнаружен или не загружен");
        return;
      }

      Puts("Загружено расширение для интеграции StopDamageMan в RustApp");
      Puts("Игроки которые видят уведомление о вызове на проверку не будут получать урон");
      Puts("");
      PrintWarning("Из-за конкретной реализации этого плагина есть вероятность, что игрок не включит урон обратно после завершения проверки! Мы не несём ответственности за данную ситуацию");
    }

    [HookMethod("RustApp_OnCheckNoticeShowed")]
    private void RustApp_OnCheckNoticeShowed(BasePlayer player)
    {
      if (StopDamageMan == null || !StopDamageMan.IsLoaded)
      {
        return;
      }

      StopDamageMan.Call("AddPlayerSDM", player);
    }

    [HookMethod("RustApp_OnCheckNoticeHidden")]
    private void RustApp_OnCheckNoticeHidden(BasePlayer player)
    {
      if (StopDamageMan == null || !StopDamageMan.IsLoaded)
      {
        return;
      }

      StopDamageMan.Call("RemovePlayerSDM", player);
    }
  }
}