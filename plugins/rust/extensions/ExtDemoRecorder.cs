using Oxide.Core;
using Oxide.Plugins;
using System;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ExtDemoRecorder", "RustApp", "1.0.0")]
    [Description("Allows you to start recording a demo for a specified player for a specified amount of time.")]
    public class ExtDemoRecorder : RustPlugin
    {
        [ConsoleCommand("ra.demo.start")]
        private void startRecordCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null) return;

            if (arg.Args == null || arg.Args.Length < 2)
            {
                arg.ReplyWith("Error: No target ID or duration provided.");
                return;
            }

            if (!ulong.TryParse(arg.Args[0], out ulong targetId))
            {
                arg.ReplyWith("Error: Invalid target ID format.");
                return;
            }

            if (!int.TryParse(arg.Args[1], out int duration))
            {
                arg.ReplyWith("Error: Invalid duration format.");
                return;
            }

            if (duration <= 0)
            {
                arg.ReplyWith("Error: Duration must be a positive number.");
                return;
            }

            BasePlayer targetPlayer = BasePlayer.FindByID(targetId);

            if (targetPlayer == null)
            {
                arg.ReplyWith("Error: Player not found.");
                return;
            }

            StartRecording(targetPlayer, duration);
        }

        void StartRecording(BasePlayer player, int time)
        {
            if (player == null) return;

            player.StartDemoRecording();

            timer.Once(time * 60, () => 
            { 
                player.StopDemoRecording(); 
            });
        }
    }
}