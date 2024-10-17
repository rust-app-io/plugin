using System.Collections.Generic;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("ExtHardwareBan", "Bizlich", "1.0.0")]
    [Description("Hardware ban for RustApp (requires Tirify)")]
    public class ExtHardwareBan : RustPlugin
    {
        private static Configuration config = new();
        private class Configuration
        {
            [JsonProperty("Режим (true - whitelist | false - blacklist)")]
            public bool WorB = false;

            [JsonProperty("Причины, по которым будут работать баны по железу (Tirify) (Оставьте пустым, если нужны все)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Reasons = new List<string> { "5+", "СВОЯ ПРИЧИНА" };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) LoadDefaultConfig();
            }
            catch
            {
                PrintWarning("Ошибка чтения конфигурации, создаём новую!");
                LoadDefaultConfig();
            }
            NextTick(SaveConfig);
        }
        protected override void LoadDefaultConfig() => config = new();
        protected override void SaveConfig() => Config.WriteObject(config);

        private void RustApp_OnPaidAnnounceBan(string steam_id, List<string> initiators, string reason)
        {
            if (config.Reasons.IsNullOrEmpty() || (config.WorB ? config.Reasons.Contains(reason) : !config.Reasons.Contains(reason)))
            {
                timer.Once(5f, () => 
                {
                    plugins.Find("TirifyGamePluginRust")?.Call("SetTirifyBan", steam_id, reason);
                    Puts($"Установлен Tirify бан для {steam_id} по причине: {reason}");
                });
            }
        }
    }
}
