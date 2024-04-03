using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ExtDonateSalary", "RustApp.Io (by EricovvDev)", "1.0.0")]
    class ExtDonateSalary : RustPlugin
    {
        #region Configuration

        private class Configuration
        {
            [JsonProperty("[GS] Secret key")]
            public string gs_secret_key;

            [JsonProperty("[GS] Shop ID")]
            public string gs_shop_id;

            [JsonProperty("[GS] Use reserve domain")]
            public bool gs_use_reserve;

            public static Configuration Generate()
            {
                return new Configuration
                {
                    gs_secret_key = "",
                    gs_shop_id = ""
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

        #region Variables

        private static Configuration _Settings;

        #endregion

        #region Initialization

        private void OnServerInitialized()
        {
            if (_Settings.gs_secret_key.Length == 0 || _Settings.gs_shop_id.Length == 0)
            {
                PrintWarning("Ваш плагин не настроен, необходимо вписать данные от магазина в конфигурацию");
                return;
            }
        }

        #endregion

        #region Commands

        [ConsoleCommand("ra.gs_salary")]
        private void GiveRub_ConsoleCommand(ConsoleSystem.Arg args)
        {
            if (args.Player() != null && !args.Player().IsAdmin)
            {
                return;
            }

            if (!args.HasArgs(2))
            {
                args.ReplyWithObject("Формат: ra.gs_salary {steam_id} {amount}");
                return;
            }

            string steamId = args.Args[0];
            int amount = 0;

            if (!int.TryParse(args.Args[1], out amount) || amount <= 0)
            {
                args.ReplyWithObject("Неверная сумма пополнения!");
                return;
            }

            GameStoreRequest(steamId, amount, success =>
            {
                if (!success)
                {
                    args.ReplyWithObject("Ошибка пополнения, подробности в консоли");
                    return;
                }

                args.ReplyWithObject("Баланс модератора пополнен");
            });
        }

        #endregion

        private void GameStoreRequest(string steamId, int amount, Action<bool> callback)
        {
            if (!CheckGameStoreConditions())
            {
                PrintWarning("Пополнение невозможно, плагин не настроен");
                callback(false);
                return;
            }

            webrequest.Enqueue(GetGameStoreUrl(steamId, amount), null, (i, s) =>
            {
                if (i != 200)
                {
                    PrintError($"Не удалось пополнить баланс {steamId} на {amount}. Код: {amount}");
                    callback(false);
                    return;
                }

                if (!s.Contains("success"))
                {
                    PrintError($"Не удалось пополнить баланс {steamId} на {amount} руб.. Ошибка: {s}");
                    callback(false);
                    return;
                }

                Puts($"Баланс модератора {steamId} пополнен на {amount} руб.");
                callback(true);
            }, this);
        }

        #region Utils

        private string GetGameStoreUrl(string steamId, int amount)
        {
            // Please, do not change this mess. It can cause ban in game-store
            var mess = "Зарплата модератору";

            var base_url = $"https://gamestores.ru/api";
            var reserve_url = "https://gamestores.app/api";

            var query = $"?shop_id={_Settings.gs_shop_id}&secret={_Settings.gs_secret_key}&action=moneys&type=plus&steam_id={steamId}&amount={amount}&mess={mess}";

            if (_Settings.gs_use_reserve)
            {
                return reserve_url + query;
            }

            return base_url + query;
        }

        private bool CheckGameStoreConditions()
        {
            return _Settings.gs_secret_key.Length != 0 && _Settings.gs_shop_id.Length != 0;
        }

        #endregion
    }
}
