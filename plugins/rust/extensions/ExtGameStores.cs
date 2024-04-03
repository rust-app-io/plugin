using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Oxide.Plugins
{
    [Info("ExtGameStores", "EricovvDev for RustApp", "1.0.0")]
    class ExtGameStores : RustPlugin
    {
        private ConfigData configuration;

        [ConsoleCommand("givezp")]
        private void GiveRub_ConsoleCommand(ConsoleSystem.Arg arg)
        {
            try
            {
                ulong steamID = Convert.ToUInt64(arg.Args[0]);
                int amount = Convert.ToInt32(arg.Args[1]);

                GiveBalance(steamID, amount);
            }
            catch (Exception ex)
            {
                PrintWarning($"Ошибка, вы ввели одно из значений неправильно! | Exception: {ex.Message}");
            }
        }
        private void GiveBalance(ulong steamID, int amount) //Если не работает меняем gamestores.ru на gamestores.app
        {
            string url = $"https://gamestores.ru/api?shop_id={configuration.ID}&secret={configuration.SecretKey}&action=moneys&type=plus&steam_id={steamID}&amount={amount}&mess={configuration.ActionName}";
            webrequest.Enqueue(url, null, (i, s) =>
            {
                if (i != 200) { }
                if (s.Contains("success"))
                {
                    PrintWarning($"Модератору [{steamID}] было успешно выдано [{amount} руб]");
                }
                else
                {
                    PrintWarning($"Модератор [{steamID}] не авторизован в магазине.");
                }
            }, this);
        }
        #region OXIDE_HOOK
        private void Loaded()
        {
            ReadConfig();
        }
        #endregion
        #region CONFIGURATION
        class ConfigData
        {
            [JsonProperty("Ид магазина")] public ulong ID;
            [JsonProperty("Секретный ключ магазина")] public string SecretKey;
            [JsonProperty("Зарплата модератору")] public string ActionName; //Не ИЗМЕНЯТЬ
        }
        protected override void LoadDefaultConfig()
        {
            var config = new ConfigData();
            SaveConfig(config);
        }
        void SaveConfig(object config)
        {
            Config.WriteObject(config, true);
        }
        void ReadConfig()
        {
            base.Config.Settings.ObjectCreationHandling = ObjectCreationHandling.Replace;
            configuration = Config.ReadObject<ConfigData>();
            SaveConfig(configuration);
        }
        #endregion
    }
}
