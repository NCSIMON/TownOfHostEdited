using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TOHE.Modules;
using TOHE.Roles.AddOns.Crewmate;
using TOHE.Roles.Crewmate;
using TOHE.Roles.Impostor;
using TOHE.Roles.Neutral;
using UnityEngine;
using static TOHE.Translator;
namespace TOHE;

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckProtect))]
class CheckProtectPatch
{
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        if (!AmongUsClient.Instance.AmHost) return false;
        Logger.Info("CheckProtect発生: " + __instance.GetNameWithRole() + "=>" + target.GetNameWithRole(), "CheckProtect");
        if (__instance.Is(CustomRoles.Sheriff))
        {
            if (__instance.Data.IsDead)
            {
                Logger.Info("守護をブロックしました。", "CheckProtect");
                return false;
            }
        }
        return true;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckMurder))]
class CheckMurderPatch
{
    public static Dictionary<byte, float> TimeSinceLastKill = new();
    public static void Update()
    {
        for (byte i = 0; i < 15; i++)
        {
            if (TimeSinceLastKill.ContainsKey(i))
            {
                TimeSinceLastKill[i] += Time.deltaTime;
                if (15f < TimeSinceLastKill[i]) TimeSinceLastKill.Remove(i);
            }
        }
    }
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        if (!AmongUsClient.Instance.AmHost) return false;

        var killer = __instance; //読み替え変数

        Logger.Info($"{killer.GetNameWithRole()} => {target.GetNameWithRole()}", "CheckMurder");

        //死人はキルできない
        if (killer.Data.IsDead)
        {
            Logger.Info($"{killer.GetNameWithRole()}は死亡しているためキャンセルされました。", "CheckMurder");
            return false;
        }

        //不正キル防止処理
        if (target.Data == null || //PlayerDataがnullじゃないか確認
            target.inVent || target.inMovingPlat //targetの状態をチェック
        )
        {
            Logger.Info("targetは現在キルできない状態です。", "CheckMurder");
            return false;
        }
        if (target.Data.IsDead) //同じtargetへの同時キルをブロック
        {
            Logger.Info("targetは既に死んでいたため、キルをキャンセルしました。", "CheckMurder");
            return false;
        }
        if (MeetingHud.Instance != null) //会議中でないかの判定
        {
            Logger.Info("会議が始まっていたため、キルをキャンセルしました。", "CheckMurder");
            return false;
        }

        float minTime = Mathf.Max(0.02f, AmongUsClient.Instance.Ping / 1000f * 6f); //※AmongUsClient.Instance.Pingの値はミリ秒(ms)なので÷1000
                                                                                    //TimeSinceLastKillに値が保存されていない || 保存されている時間がminTime以上 => キルを許可
                                                                                    //↓許可されない場合
        if (TimeSinceLastKill.TryGetValue(killer.PlayerId, out var time) && time < minTime)
        {
            Logger.Info("前回のキルからの時間が早すぎるため、キルをブロックしました。", "CheckMurder");
            return false;
        }
        TimeSinceLastKill[killer.PlayerId] = 0f;

        killer.ResetKillCooldown();

        //キル可能判定
        if (killer.PlayerId != target.PlayerId && !killer.CanUseKillButton())
        {
            Logger.Info(killer.GetNameWithRole() + "はKillできないので、キルはキャンセルされました。", "CheckMurder");
            return false;
        }
        //実際のキラーとkillerが違う場合の入れ替え処理
        if (Sniper.IsEnable) Sniper.TryGetSniper(target.PlayerId, ref killer);
        if (killer != __instance) Logger.Info($"Real Killer={killer.GetNameWithRole()}", "CheckMurder");

        //キル時の特殊判定
        if (killer.PlayerId != target.PlayerId)
        {
            //自殺でない場合のみ役職チェック
            switch (killer.GetCustomRole())
            {
                //==========インポスター役職==========//
                case CustomRoles.BountyHunter: //キルが発生する前にここの処理をしないとバグる
                    BountyHunter.OnCheckMurder(killer, target);
                    break;
                case CustomRoles.SerialKiller:
                    SerialKiller.OnCheckMurder(killer);
                    break;
                case CustomRoles.Vampire:
                    if (!Vampire.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Warlock:
                    if (!Main.CheckShapeshift[killer.PlayerId] && !Main.isCurseAndKill[killer.PlayerId])
                    { //Warlockが変身時以外にキルしたら、呪われる処理
                        if (target.Is(CustomRoles.Needy)) return false;
                        Main.isCursed = true;
                        killer.SetKillCooldown();
                        Main.CursedPlayers[killer.PlayerId] = target;
                        Main.WarlockTimer.Add(killer.PlayerId, 0f);
                        Main.isCurseAndKill[killer.PlayerId] = true;
                        return false;
                    }
                    if (Main.CheckShapeshift[killer.PlayerId])
                    {//呪われてる人がいないくて変身してるときに通常キルになる
                        killer.RpcMurderPlayer(target);
                        killer.RpcGuardAndKill(target);
                        return false;
                    }
                    if (Main.isCurseAndKill[killer.PlayerId]) killer.RpcGuardAndKill(target);
                    return false;
                case CustomRoles.Assassin:
                    if (!Main.CheckShapeshift[killer.PlayerId] && !Main.isMarkAndKill[killer.PlayerId])
                    { //Assassinが変身時以外にキルしたら
                        Main.isMarked = true;
                        killer.SetKillCooldown();
                        Main.MarkedPlayers[killer.PlayerId] = target;
                        Main.AssassinTimer.Add(killer.PlayerId, 0f);
                        Main.isMarkAndKill[killer.PlayerId] = true;
                        return false;
                    }
                    if (Main.CheckShapeshift[killer.PlayerId])
                    {//呪われてる人がいないくて変身してるときに通常キルになる
                        killer.RpcMurderPlayer(target);
                        killer.RpcGuardAndKill(target);
                        return false;
                    }
                    if (Main.isCurseAndKill[killer.PlayerId]) killer.RpcGuardAndKill(target);
                    return false;
                case CustomRoles.Witch:
                    if (!Witch.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Puppeteer:
                    if (target.Is(CustomRoles.Needy)) return false;
                    Main.PuppeteerList[target.PlayerId] = killer.PlayerId;
                    killer.SetKillCooldown();
                    Utils.NotifyRoles(SpecifySeer: killer);
                    return false;
                case CustomRoles.Capitalism:
                    if (!Main.CapitalismAddTask.ContainsKey(target.PlayerId))
                        Main.CapitalismAddTask.Add(target.PlayerId, 0);
                    Main.CapitalismAddTask[target.PlayerId]++;
                    if (!Main.CapitalismAssignTask.ContainsKey(target.PlayerId))
                        Main.CapitalismAssignTask.Add(target.PlayerId, 0);
                    Main.CapitalismAssignTask[target.PlayerId]++;
                    Logger.Info($"资本主义 {killer.GetRealName()} 又开始祸害人了：{target.GetRealName()}", "Capitalism Add Task");
                    killer.RpcGuardAndKill(killer);
                    killer.SetKillCooldown();
                    return false;
                case CustomRoles.Counterfeiter:
                    if (Counterfeiter.CanBeClient(target) && Counterfeiter.CanSeel(killer.PlayerId))
                        Counterfeiter.SeelToClient(killer, target);
                    return false;
                case CustomRoles.Bomber:
                    return false;
                case CustomRoles.Gangster:
                    if (Gangster.OnCheckMurder(killer, target))
                        return false;
                    break;

                //==========第三陣営役職==========//
                case CustomRoles.Arsonist:
                    killer.SetKillCooldown(Options.ArsonistDouseTime.GetFloat());
                    if (!Main.isDoused[(killer.PlayerId, target.PlayerId)] && !Main.ArsonistTimer.ContainsKey(killer.PlayerId))
                    {
                        Main.ArsonistTimer.Add(killer.PlayerId, (target, 0f));
                        Utils.NotifyRoles(SpecifySeer: __instance);
                        RPC.SetCurrentDousingTarget(killer.PlayerId, target.PlayerId);
                    }
                    return false;
                case CustomRoles.Revolutionist:
                    killer.SetKillCooldown(Options.RevolutionistDrawTime.GetFloat());
                    if (!Main.isDraw[(killer.PlayerId, target.PlayerId)] && !Main.RevolutionistTimer.ContainsKey(killer.PlayerId))
                    {
                        Main.RevolutionistTimer.Add(killer.PlayerId, (target, 0f));
                        Utils.NotifyRoles(SpecifySeer: __instance);
                        RPC.SetCurrentDrawTarget(killer.PlayerId, target.PlayerId);
                    }
                    return false;
                case CustomRoles.Innocent:
                    target.RpcMurderPlayer(killer);
                    return false;
                case CustomRoles.Pelican:
                    if (Pelican.CanEat(killer, target.PlayerId))
                    {
                        Pelican.EatPlayer(killer, target);
                        killer.RpcGuardAndKill(killer);
                        killer.SetKillCooldown();
                    }
                    return false;
                case CustomRoles.FFF:
                    if (!target.Is(CustomRoles.Lovers) && !target.Is(CustomRoles.Ntr))
                    {
                        killer.Data.IsDead = true;
                        Main.PlayerStates[killer.PlayerId].deathReason = PlayerState.DeathReason.Sacrifice;
                        killer.RpcMurderPlayer(killer);
                        Main.PlayerStates[killer.PlayerId].SetDead();
                        Logger.Info($"{killer.GetRealName()} 击杀了非目标玩家，壮烈牺牲了", "FFF");
                        return false;
                    }
                    return true;

                //==========クルー役職==========//
                case CustomRoles.Sheriff:
                    if (!Sheriff.OnCheckMurder(killer, target))
                        return false;
                    break;
                case CustomRoles.SwordsMan:
                    if (!SwordsMan.OnCheckMurder(killer))
                        return false;
                    break;
            }
        }

        switch (target.GetCustomRole())
        {
            //击杀幸运儿
            case CustomRoles.Luckey:
                var rd = IRandom.Instance;
                if (rd.Next(0, 100) < Options.LuckeyProbability.GetInt())
                {
                    killer.RpcGuardAndKill(target);
                    return false;
                }
                break;
            //击杀老兵
            case CustomRoles.Veteran:
                if (Main.VeteranInProtect.ContainsKey(target.PlayerId))
                    if (Main.VeteranInProtect[target.PlayerId] + Options.VeteranSkillDuration.GetInt() >= Utils.GetTimeStamp(DateTime.Now))
                    {
                        killer.SetRealKiller(target);
                        target.RpcMurderPlayer(killer);
                        Logger.Info($"{target.GetRealName()} 老兵反弹击杀：{killer.GetRealName()}", "Veteran Kill");
                        return false;
                    }
                break;
        }

        //赝品检查
        if (Counterfeiter.OnClientMurder(killer)) return false;

        //保镖保护
        foreach (var pc in Main.AllAlivePlayerControls.Where(x => x.PlayerId != target.PlayerId))
        {
            var pos = target.transform.position;
            var dis = Vector2.Distance(pos, pc.transform.position);
            if (dis > Options.BodyguardProtectRadius.GetFloat()) continue;
            if (pc.Is(CustomRoles.Bodyguard))
            {
                if (pc.Is(CustomRoles.Madmate) && killer.GetCustomRole().IsImpostorTeam())
                    Logger.Info($"{pc.GetRealName()} 是个叛徒，所以他选择无视杀人现场", "Bodyguard");
                else
                {
                    Main.PlayerStates[pc.PlayerId].deathReason = PlayerState.DeathReason.Sacrifice;
                    pc.RpcMurderPlayer(killer);
                    pc.SetRealKiller(killer);
                    pc.RpcMurderPlayer(pc);
                    Logger.Info($"{pc.GetRealName()} 挺身而出与歹徒 {killer.GetRealName()} 同归于尽", "Bodyguard");
                    return false;
                }
            }
        }

        //==キル処理==
        __instance.RpcMurderPlayer(target);
        //============

        return false;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
class MurderPlayerPatch
{
    public static void Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        Logger.Info($"{__instance.GetNameWithRole()} => {target.GetNameWithRole()}{(target.protectedByGuardian ? "(Protected)" : "")}", "MurderPlayer");

        if (RandomSpawn.CustomNetworkTransformPatch.NumOfTP.TryGetValue(__instance.PlayerId, out var num) && num > 2) RandomSpawn.CustomNetworkTransformPatch.NumOfTP[__instance.PlayerId] = 3;
        if (!target.protectedByGuardian)
            Camouflage.RpcSetSkin(target, ForceRevert: true);
    }
    public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {

        if (target.AmOwner) RemoveDisableDevicesPatch.UpdateDisableDevices();
        if (!target.Data.IsDead || !AmongUsClient.Instance.AmHost) return;

        PlayerControl killer = __instance; //読み替え変数

        //実際のキラーとkillerが違う場合の入れ替え処理
        if (Sniper.IsEnable)
        {
            if (Sniper.TryGetSniper(target.PlayerId, ref killer))
            {
                Main.PlayerStates[target.PlayerId].deathReason = PlayerState.DeathReason.Sniped;
            }
        }
        if (killer != __instance)
        {
            Logger.Info($"Real Killer={killer.GetNameWithRole()}", "MurderPlayer");

        }
        if (Main.PlayerStates[target.PlayerId].deathReason == PlayerState.DeathReason.etc)
        {
            //死因が設定されていない場合は死亡判定
            Main.PlayerStates[target.PlayerId].deathReason = PlayerState.DeathReason.Kill;
        }

        //看看UP是不是被首刀了
        if (Main.FirstDied == 255 && target.Is(CustomRoles.Youtuber))
        {
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Youtuber); //UP主被首刀了，哈哈哈哈哈
            CustomWinnerHolder.WinnerIds.Add(target.PlayerId);
        }

        //记录首刀
        if (Main.FirstDied == 255)
            Main.FirstDied = target.PlayerId;

        //骇客击杀
        bool hackKilled = false;
        if (killer.GetCustomRole() == CustomRoles.Hacker && Main.HackerUsedCount.TryGetValue(killer.PlayerId, out var count) && count < Options.HackUsedMaxTime.GetInt())
        {
            hackKilled = true;
            Main.HackerUsedCount[killer.PlayerId] += 1;
            List<PlayerControl> playerList = new();
            foreach (PlayerControl pc in PlayerControl.AllPlayerControls)
                if (pc.IsAlive() && !Pelican.IsEaten(pc.PlayerId) && !(pc.GetCustomRole() == CustomRoles.Hacker) && !(pc.GetCustomRole() is CustomRoles.Needy or CustomRoles.GM)) playerList.Add(pc);
            if (playerList.Count < 1)
            {
                Logger.Info(target?.Data?.PlayerName + "被骇客击杀，但无法找到骇入目标", "MurderPlayer");
                new LateTask(() => killer.CmdReportDeadBody(target.Data), 0.15f, "Hacker Self Report");
            }
            else
            {
                var rd = IRandom.Instance;
                int hackinPlayer = rd.Next(0, playerList.Count);
                if (playerList[hackinPlayer] == null) hackinPlayer = 0;
                Logger.Info(target?.Data?.PlayerName + "被骇客击杀，随机报告者：" + playerList[hackinPlayer]?.Data?.PlayerName, "MurderPlayer");
                new LateTask(() => playerList[hackinPlayer].CmdReportDeadBody(target.Data), 0.15f, "Hacker Hackin Report");
            }
        }

        //When Bait is killed
        if (target.Is(CustomRoles.Bait) && killer.PlayerId != target.PlayerId && !hackKilled)
        {
            if (target.Is(CustomRoles.Madmate)) //背叛诱饵
            {
                List<PlayerControl> playerList = new();
                foreach (PlayerControl pc in PlayerControl.AllPlayerControls)
                    if (pc.IsAlive() && !Pelican.IsEaten(pc.PlayerId) && !(pc.GetCustomRole() is CustomRoles.Needy or CustomRoles.GM) && pc.PlayerId != target.PlayerId) playerList.Add(pc);
                if (playerList.Count < 1)
                {
                    Logger.Info(target?.Data?.PlayerName + "是背叛诱饵，但找不到替罪羊", "MurderPlayer");
                    new LateTask(() => killer.CmdReportDeadBody(target.Data), 0.15f, "Bait Self Report");
                }
                else
                {
                    var rd = IRandom.Instance;
                    int hackinPlayer = rd.Next(0, playerList.Count);
                    if (playerList[hackinPlayer] == null) hackinPlayer = 0;
                    Logger.Info(target?.Data?.PlayerName + "是背叛诱饵，随机报告者：" + playerList[hackinPlayer]?.Data?.PlayerName, "MurderPlayer");
                    new LateTask(() => playerList[hackinPlayer].CmdReportDeadBody(target.Data), 0.15f, "Bait of MadmateJ Random Report");
                }
            }
            else //船员诱饵
            {
                Logger.Info(target?.Data?.PlayerName + "はBaitだった", "MurderPlayer");
                new LateTask(() => killer.CmdReportDeadBody(target.Data), 0.15f, "Bait Self Report");
            }
        }
        else
        //Terrorist
        if (target.Is(CustomRoles.Terrorist))
        {
            Logger.Info(target?.Data?.PlayerName + "はTerroristだった", "MurderPlayer");
            Utils.CheckTerroristWin(target.Data);
        }
        if (target.Is(CustomRoles.Trapper) && !killer.Is(CustomRoles.Trapper))
            killer.TrapperKilled(target);
        if (Executioner.Target.ContainsValue(target.PlayerId))
            Executioner.ChangeRoleByTarget(target);
        if (target.Is(CustomRoles.Executioner) && Executioner.Target.ContainsKey(target.PlayerId))
        {
            Executioner.Target.Remove(target.PlayerId);
            Executioner.SendRPC(target.PlayerId);
        }
        if (target.Is(CustomRoles.CyberStar) && Main.CyberStarDead.Contains(target.PlayerId))
            Main.CyberStarDead.Add(target.PlayerId);
        if (killer.Is(CustomRoles.Sans))
        {
            if (!Main.SansKillCooldown.ContainsKey(killer.PlayerId))
                Main.SansKillCooldown.Add(killer.PlayerId, Options.SansDefaultKillCooldown.GetFloat());
            Main.SansKillCooldown[killer.PlayerId] -= Options.SansReduceKillCooldown.GetFloat();
            if (Main.SansKillCooldown[killer.PlayerId] < Options.SansMinKillCooldown.GetFloat())
                Main.SansKillCooldown[killer.PlayerId] = Options.SansMinKillCooldown.GetFloat();
        }
        if (killer.Is(CustomRoles.BoobyTrap) && killer != target)
        {
            if (!Main.BoobyTrapBody.Contains(target.PlayerId)) Main.BoobyTrapBody.Add(target.PlayerId);
            if (!Main.KillerOfBoobyTrapBody.ContainsKey(target.PlayerId)) Main.KillerOfBoobyTrapBody.Add(target.PlayerId, killer.PlayerId);
            Main.PlayerStates[killer.PlayerId].deathReason = PlayerState.DeathReason.Misfire;
            killer.RpcMurderPlayer(killer);
        }
        if (killer.Is(CustomRoles.SwordsMan) && killer != target)
        {
            SwordsMan.OnMurder(killer);
        }
        if (target.Is(CustomRoles.Avanger))
        {
            var pcList = Main.AllAlivePlayerControls.Where(x => x.IsAlive() && !Pelican.IsEaten(x.PlayerId) && x.PlayerId != target.PlayerId).ToList();
            var rp = pcList[IRandom.Instance.Next(0, pcList.Count)];
            Main.PlayerStates[rp.PlayerId].deathReason = PlayerState.DeathReason.Revenge;
            rp.SetRealKiller(target);
            rp.RpcMurderPlayer(rp);
        }
        if (target.Is(CustomRoles.Pelican))
        {
            Pelican.OnPelicanDied(target.PlayerId);
        }

        FixedUpdatePatch.LoversSuicide(target.PlayerId);

        Main.PlayerStates[target.PlayerId].SetDead();
        target.SetRealKiller(killer, true); //既に追加されてたらスキップ
        Utils.CountAlivePlayers(true);
        Utils.SyncAllSettings();
        Utils.NotifyRoles();
        Utils.TargetDies(__instance, target);
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Shapeshift))]
class ShapeshiftPatch
{
    public static void Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        Logger.Info($"{__instance?.GetNameWithRole()} => {target?.GetNameWithRole()}", "Shapeshift");
        if (!AmongUsClient.Instance.AmHost) return;

        var shapeshifter = __instance;
        var shapeshifting = shapeshifter.PlayerId != target.PlayerId;

        if (Main.CheckShapeshift.TryGetValue(shapeshifter.PlayerId, out var last) && last == shapeshifting)
        {
            Logger.Info($"{__instance?.GetNameWithRole()}:Cancel Shapeshift.Prefix", "Shapeshift");
            return;
        }

        Main.CheckShapeshift[shapeshifter.PlayerId] = shapeshifting;
        Main.ShapeshiftTarget[shapeshifter.PlayerId] = target.PlayerId;

        Sniper.OnShapeshift(shapeshifter, shapeshifting);

        if (!AmongUsClient.Instance.AmHost) return;
        if (!shapeshifting) Camouflage.RpcSetSkin(__instance);

        //逃逸者的变形
        if (shapeshifter.Is(CustomRoles.Escapee))
        {
            if (shapeshifting)
            {
                if (Main.EscapeeLocation.ContainsKey(shapeshifter.PlayerId))
                {
                    var position = Main.EscapeeLocation[shapeshifter.PlayerId];
                    Main.EscapeeLocation.Remove(shapeshifter.PlayerId);
                    Logger.Msg($"{shapeshifter.GetNameWithRole()}:{position}", "EscapeeTeleport");
                    Utils.TP(shapeshifter.NetTransform, new Vector2(position.x, position.y));
                }
                else
                {
                    Main.EscapeeLocation.Add(shapeshifter.PlayerId, shapeshifter.GetTruePosition());
                }
            }
        }

        //矿工传送
        if (shapeshifter.Is(CustomRoles.Miner))
        {
            if (Main.LastEnteredVent.ContainsKey(shapeshifter.PlayerId))
            {
                int ventId = Main.LastEnteredVent[shapeshifter.PlayerId].Id;
                var vent = Main.LastEnteredVent[shapeshifter.PlayerId];
                var position = Main.LastEnteredVentLocation[shapeshifter.PlayerId];
                Logger.Msg($"{shapeshifter.GetNameWithRole()}:{position}", "MinerTeleport");
                Utils.TP(shapeshifter.NetTransform, new Vector2(position.x, position.y));
            }
        }

        //自爆兵自爆了
        if (shapeshifter.Is(CustomRoles.Bomber) && shapeshifting)
        {
            Logger.Info("炸弹爆炸了", "Boom");
            foreach (var tg in Main.AllPlayerControls)
            {
                tg.KillFlash();
                var pos = shapeshifter.transform.position;
                var dis = Vector2.Distance(pos, tg.transform.position);

                if (!tg.IsAlive() || Pelican.IsEaten(tg.PlayerId)) continue;
                if (dis > Options.BomberRadius.GetFloat()) continue;
                if (tg.PlayerId == shapeshifter.PlayerId) continue;

                Main.PlayerStates[tg.PlayerId].deathReason = PlayerState.DeathReason.Bombed;
                tg.SetRealKiller(shapeshifter);
                tg.RpcMurderPlayer(tg);
            }
            new LateTask(() =>
            {
                var totalAlive = Main.AllAlivePlayerControls.Count();
                //自分が最後の生き残りの場合は勝利のために死なない
                if (totalAlive != 1 && !GameStates.IsEnded)
                {
                    Main.PlayerStates[shapeshifter.PlayerId].deathReason = PlayerState.DeathReason.Misfire;
                    shapeshifter.RpcMurderPlayer(shapeshifter);
                }
                Utils.NotifyRoles();
            }, 1.5f, "Bomber Suiscide");
        }

        //术士操控击杀
        if (shapeshifter.Is(CustomRoles.Warlock))
        {
            if (Main.CursedPlayers[shapeshifter.PlayerId] != null)//呪われた人がいるか確認
            {
                if (shapeshifting && !Main.CursedPlayers[shapeshifter.PlayerId].Data.IsDead)//変身解除の時に反応しない
                {
                    var cp = Main.CursedPlayers[shapeshifter.PlayerId];
                    Vector2 cppos = cp.transform.position;//呪われた人の位置
                    Dictionary<PlayerControl, float> cpdistance = new();
                    float dis;
                    foreach (PlayerControl p in Main.AllAlivePlayerControls)
                    {
                        if (!Options.WarlockCanKillSelf.GetBool() && cp == p) continue;
                        if (!Options.WarlockCanKillAllies.GetBool() && p.Is(CustomRoles.Impostor)) continue;
                        dis = Vector2.Distance(cppos, p.transform.position);
                        cpdistance.Add(p, dis);
                        Logger.Info($"{p?.Data?.PlayerName}の位置{dis}", "Warlock");
                    }
                    var min = cpdistance.OrderBy(c => c.Value).FirstOrDefault();//一番小さい値を取り出す
                    PlayerControl targetw = min.Key;
                    targetw.SetRealKiller(shapeshifter);
                    Logger.Info($"{targetw.GetNameWithRole()} was killed", "Warlock");
                    cp.RpcMurderPlayerV2(targetw);//殺す
                    shapeshifter.RpcGuardAndKill(shapeshifter);
                    Main.isCurseAndKill[shapeshifter.PlayerId] = false;
                }
                Main.CursedPlayers[shapeshifter.PlayerId] = null;
            }
        }

        //刺客刺杀
        if (shapeshifter.Is(CustomRoles.Assassin))
        {
            if (Main.MarkedPlayers[shapeshifter.PlayerId] != null)//确认被标记的人
            {
                if (shapeshifting && Main.MarkedPlayers[shapeshifter.PlayerId].IsAlive() && !Pelican.IsEaten(Main.MarkedPlayers[shapeshifter.PlayerId].PlayerId))//解除变形时不执行操作
                {
                    PlayerControl targetw = Main.MarkedPlayers[shapeshifter.PlayerId];
                    new LateTask(() =>
                    {
                        if (!GameStates.IsMeeting && !GameStates.IsEnded && GameStates.IsInTask && GameStates.IsInGame && Main.MarkedPlayers[shapeshifter.PlayerId].IsAlive() && !Pelican.IsEaten(Main.MarkedPlayers[shapeshifter.PlayerId].PlayerId))
                        {
                            Logger.Info($"{targetw.GetNameWithRole()} was killed", "Assassin");
                            targetw.SetRealKiller(shapeshifter);
                            shapeshifter.RpcMurderPlayer(targetw);//殺す
                        }
                        else Logger.Info($"{targetw.GetNameWithRole()} 本该被刺客击杀但状态不对劲了", "Assassin");
                        if (GameStates.IsMeeting && shapeshifter.shapeshifting) shapeshifter.RpcRevertShapeshift(false);
                    }, 1.5f, "Assassin Kill");
                    Main.isMarkAndKill[shapeshifter.PlayerId] = false;
                }
                Main.MarkedPlayers[shapeshifter.PlayerId] = null;
            }
        }

        if (shapeshifter.Is(CustomRoles.EvilTracker)) EvilTracker.OnShapeshift(shapeshifter, target, shapeshifting);
        if (shapeshifter.Is(CustomRoles.FireWorks)) FireWorks.ShapeShiftState(shapeshifter, shapeshifting);

        //変身解除のタイミングがずれて名前が直せなかった時のために強制書き換え
        if (!shapeshifting)
        {
            new LateTask(() =>
            {
                Utils.NotifyRoles(NoCache: true);
            },
            1.2f, "ShapeShiftNotify");
        }
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ReportDeadBody))]
class ReportDeadBodyPatch
{
    public static Dictionary<byte, bool> CanReport;
    public static Dictionary<byte, List<GameData.PlayerInfo>> WaitReport = new();
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] GameData.PlayerInfo target)
    {
        if (GameStates.IsMeeting) return false;
        Logger.Info($"{__instance.GetNameWithRole()} => {target?.Object?.GetNameWithRole() ?? "null"}", "ReportDeadBody");
        if (!CanReport[__instance.PlayerId])
        {
            WaitReport[__instance.PlayerId].Add(target);
            Logger.Warn($"{__instance.GetNameWithRole()}:通報禁止中のため可能になるまで待機します", "ReportDeadBody");
            return false;
        }

        //杀戮机器无法报告或拍灯
        if (__instance.Is(CustomRoles.Minimalism)) return false;
        //禁止小黑人报告
        if (Utils.IsActive(SystemTypes.Comms) && Options.CommsCamouflage.GetBool() && Options.DisableReportWhenCC.GetBool()) return false;

        if (target == null) //拍灯事件
        {
            if (__instance.Is(CustomRoles.Mayor)) Main.MayorUsedButtonCount[__instance.PlayerId] += 1;
            if (__instance.Is(CustomRoles.Jester) && !Options.JesterCanUseButton.GetBool()) return false;
        }
        else //报告尸体事件
        {
            var tpc = Utils.GetPlayerById(target.PlayerId);

            // 清洁工来扫大街咯
            if (__instance.Is(CustomRoles.Cleaner))
            {
                Main.CleanerBodies.Remove(tpc.PlayerId);
                Main.CleanerBodies.Add(tpc.PlayerId);
                __instance.RpcGuardAndKill(__instance);
                __instance.ResetKillCooldown();
                Logger.Info($"{__instance.GetRealName()} 清理了 {tpc.GetRealName()} 的尸体", "Cleaner");
                return false;
            }

            // 被赌杀的尸体无法被报告
            if (Main.PlayerStates[target.PlayerId].deathReason == PlayerState.DeathReason.Gambled) return false;

            // 清道夫的尸体无法被报告
            if (tpc.GetRealKiller().Is(CustomRoles.Scavenger)) return false;

            // 被清理的尸体无法报告
            if (Main.CleanerBodies.Contains(tpc.PlayerId)) return false;

            // 胆小鬼不敢报告
            if (__instance.Is(CustomRoles.Oblivious))
            {
                if ((!tpc.GetRealKiller().Is(CustomRoles.Hacker)) && !tpc.Is(CustomRoles.Bait))
                    return false;
            }

            // 报告了诡雷尸体
            if (Main.BoobyTrapBody.Contains(target.PlayerId) && __instance.IsAlive())
            {
                var killerID = Main.KillerOfBoobyTrapBody[target.PlayerId];
                Main.PlayerStates[__instance.PlayerId].deathReason = PlayerState.DeathReason.Bombed;
                __instance.SetRealKiller(Utils.GetPlayerById(killerID));

                __instance.RpcMurderPlayer(__instance);
                RPC.PlaySoundRPC(killerID, Sounds.KillSound);

                if (!Main.BoobyTrapBody.Contains(__instance.PlayerId)) Main.BoobyTrapBody.Add(__instance.PlayerId);
                if (!Main.KillerOfBoobyTrapBody.ContainsKey(__instance.PlayerId)) Main.KillerOfBoobyTrapBody.Add(__instance.PlayerId, killerID);
                return false;
            }

            // 侦探报告
            if (__instance.Is(CustomRoles.Detective))
            {
                string msg;
                msg = string.Format(GetString("DetectiveNoticeVictim"), tpc.GetRealName(), tpc.GetDisplayRoleName());
                if (Options.DetectiveCanknowKiller.GetBool()) msg += "；" + string.Format(GetString("DetectiveNoticeKiller"), tpc.GetRealKiller().GetDisplayRoleName());
                Main.DetectiveNotify.Add(__instance.PlayerId, msg);
            }
        }
        foreach (var kvp in Main.PlayerStates)
        {
            var pc = Utils.GetPlayerById(kvp.Key);
            kvp.Value.LastRoom = pc.GetPlainShipRoom();
        }
        if (!AmongUsClient.Instance.AmHost) return true;
        BountyHunter.OnReportDeadBody();
        SerialKiller.OnReportDeadBody();
        Main.ArsonistTimer.Clear();
        Main.RevolutionistTimer.Clear();

        if (Options.SyncButtonMode.GetBool() && target == null)
        {
            Logger.Info("最大:" + Options.SyncedButtonCount.GetInt() + ", 現在:" + Options.UsedButtonCount, "ReportDeadBody");
            if (Options.SyncedButtonCount.GetFloat() <= Options.UsedButtonCount)
            {
                Logger.Info("使用可能ボタン回数が最大数を超えているため、ボタンはキャンセルされました。", "ReportDeadBody");
                return false;
            }
            else Options.UsedButtonCount++;
            if (Options.SyncedButtonCount.GetFloat() == Options.UsedButtonCount)
            {
                Logger.Info("使用可能ボタン回数が最大数に達しました。", "ReportDeadBody");
            }
        }

        Main.PuppeteerList.Clear();
        Sniper.OnReportDeadBody();
        Vampire.OnStartMeeting();
        Pelican.OnReport();
        foreach (var x in Main.RevolutionistStart)
        {
            var tar = Utils.GetPlayerById(x.Key);
            if (tar == null) continue;
            tar.Data.IsDead = true;
            Main.PlayerStates[tar.PlayerId].deathReason = PlayerState.DeathReason.Sacrifice;
            tar.RpcExileV2();
            Main.PlayerStates[tar.PlayerId].SetDead();
            Logger.Info($"{tar.GetRealName()} 因会议革命失败", "Revolutionist");
        }
        Main.RevolutionistStart.Clear();
        Main.RevolutionistLastTime.Clear();

        if (__instance.Data.IsDead) return true;
        //=============================================
        //以下、ボタンが押されることが確定したものとする。
        //=============================================

        Main.AllPlayerControls
            .Where(pc => Main.CheckShapeshift.ContainsKey(pc.PlayerId))
            .Do(pc => Camouflage.RpcSetSkin(pc, RevertToDefault: true));
        MeetingTimeManager.OnReportDeadBody();

        Utils.SyncAllSettings();
        return true;
    }
    public static async void ChangeLocalNameAndRevert(string name, int time)
    {
        //async Taskじゃ警告出るから仕方ないよね。
        var revertName = PlayerControl.LocalPlayer.name;
        PlayerControl.LocalPlayer.RpcSetNameEx(name);
        await Task.Delay(time);
        PlayerControl.LocalPlayer.RpcSetNameEx(revertName);
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
class FixedUpdatePatch
{
    private static StringBuilder Mark = new(20);
    private static StringBuilder Suffix = new(120);
    private static int BufferTime = 10;
    public static void Postfix(PlayerControl __instance)
    {
        var player = __instance;

        if (!GameStates.IsModHost) return;

        TargetArrow.OnFixedUpdate(player);
        Sniper.OnFixedUpdate(player);

        if (AmongUsClient.Instance.AmHost)
        {//実行クライアントがホストの場合のみ実行
            if (GameStates.IsLobby && ((ModUpdater.hasUpdate && ModUpdater.forceUpdate) || ModUpdater.isBroken || !Main.AllowPublicRoom) && AmongUsClient.Instance.IsGamePublic)
                AmongUsClient.Instance.ChangeGamePublic(false);

            if (GameStates.IsInTask && ReportDeadBodyPatch.CanReport[__instance.PlayerId] && ReportDeadBodyPatch.WaitReport[__instance.PlayerId].Count > 0)
            {
                var info = ReportDeadBodyPatch.WaitReport[__instance.PlayerId][0];
                ReportDeadBodyPatch.WaitReport[__instance.PlayerId].Clear();
                Logger.Info($"{__instance.GetNameWithRole()}:通報可能になったため通報処理を行います", "ReportDeadbody");
                __instance.ReportDeadBody(info);
            }

            //检查老兵技能是否失效
            if (GameStates.IsInTask && CustomRoles.Veteran.IsEnable())
            {
                var vpList = Main.VeteranInProtect.Where(x => x.Value + Options.VeteranSkillDuration.GetInt() < Utils.GetTimeStamp(DateTime.Now));
                foreach (var vp in vpList)
                {
                    Main.VeteranInProtect.Remove(vp.Key);
                    var pc = Utils.GetPlayerById(vp.Key);
                    if (pc != null && GameStates.IsInTask && !GameStates.IsMeeting) pc.RpcGuardAndKill(pc);
                    break;
                }
            }

            //检查掷雷兵技能是否生效
            if (GameStates.IsInTask && CustomRoles.Grenadier.IsEnable())
            {
                var gbList = Main.GrenadierBlinding.Where(x => x.Value + Options.GrenadierSkillDuration.GetInt() < Utils.GetTimeStamp(DateTime.Now));
                foreach (var gb in gbList)
                {
                    Main.GrenadierBlinding.Remove(gb.Key);
                    var pc = Utils.GetPlayerById(gb.Key);
                    if (pc != null && GameStates.IsInTask && !GameStates.IsMeeting) pc.RpcGuardAndKill(pc);
                    Utils.MarkEveryoneDirtySettings();
                    break;
                }
                var mgbList = Main.MadGrenadierBlinding.Where(x => x.Value + Options.GrenadierSkillDuration.GetInt() < Utils.GetTimeStamp(DateTime.Now));
                foreach (var mgb in mgbList)
                {
                    Main.MadGrenadierBlinding.Remove(mgb.Key);
                    var pc = Utils.GetPlayerById(mgb.Key);
                    if (pc != null && GameStates.IsInTask && !GameStates.IsMeeting) pc.RpcGuardAndKill(pc);
                    Utils.MarkEveryoneDirtySettings();
                    break;
                }
            }

            //吹笛者的加速
            if (GameStates.IsInTask && CustomRoles.Piper.IsEnable())
            {
                BufferTime--;
                if (BufferTime <= 0)
                {
                    BufferTime = 50;
                    foreach (var pc in Main.AllAlivePlayerControls.Where(x => x.Is(CustomRoles.Piper)))
                    {
                        foreach (var apc in Main.AllAlivePlayerControls.Where(x => x.PlayerId != pc.PlayerId))
                        {
                            var pos = pc.transform.position;
                            var dis = Vector2.Distance(pos, apc.transform.position);
                            bool acc = true;

                            if (!apc.IsAlive() || Pelican.IsEaten(apc.PlayerId)) acc = false;
                            if (dis > Options.PiperAccelerationRadius.GetFloat()) acc = false;
                            if (acc && Main.AllPlayerSpeed[apc.PlayerId] == Options.PiperAccelerationSpeed.GetFloat()) break;
                            if (acc) Main.AllPlayerSpeed[apc.PlayerId] = Options.PiperAccelerationSpeed.GetFloat();
                            if (acc || (!acc && Main.AllPlayerSpeed[apc.PlayerId] == Options.PiperAccelerationSpeed.GetFloat()))
                            {
                                ExtendedPlayerControl.MarkDirtySettings(apc);
                                Logger.Info($"{apc.GetRealName()} 因靠近吹笛者 {pc.GetRealName()} 速度被改变", "Piper Speed Boost");
                            }
                        }
                    }
                }
            }

            //检查马里奥是否完成
            if (GameStates.IsInTask && CustomRoles.Mario.IsEnable())
            {
                foreach (var pc in PlayerControl.AllPlayerControls.ToArray().Where(x => x.Is(CustomRoles.Mario)))
                {
                    if (Main.MarioVentCount[pc.PlayerId] > Options.MarioVentNumWin.GetInt())
                    {

                        Main.MarioVentCount[pc.PlayerId] = Options.MarioVentNumWin.GetInt();
                        CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Mario); //马里奥这个多动症赢了
                        CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
                    }
                }
            }

            Pelican.FixedUpdate();
            DoubleTrigger.OnFixedUpdate(player);
            Vampire.OnFixedUpdate(player);
            if (GameStates.IsInTask && CustomRoles.SerialKiller.IsEnable()) SerialKiller.FixedUpdate(player);
            if (GameStates.IsInTask && Main.WarlockTimer.ContainsKey(player.PlayerId))//処理を1秒遅らせる
            {
                if (player.IsAlive())
                {
                    if (Main.WarlockTimer[player.PlayerId] >= 1f)
                    {
                        player.RpcResetAbilityCooldown();
                        Main.isCursed = false;//変身クールを１秒に変更
                        player.SyncSettings();
                        Main.WarlockTimer.Remove(player.PlayerId);
                    }
                    else Main.WarlockTimer[player.PlayerId] = Main.WarlockTimer[player.PlayerId] + Time.fixedDeltaTime;//時間をカウント
                }
                else
                {
                    Main.WarlockTimer.Remove(player.PlayerId);
                }
            }
            if (GameStates.IsInTask && Main.AssassinTimer.ContainsKey(player.PlayerId))//処理を1秒遅らせる
            {
                if (player.IsAlive() && !Pelican.IsEaten(player.PlayerId))
                {
                    if (Main.AssassinTimer[player.PlayerId] >= 1f)
                    {
                        player.RpcResetAbilityCooldown();
                        Main.isMarked = false;//変身クールを１秒に変更
                        player.SyncSettings();
                        Main.AssassinTimer.Remove(player.PlayerId);
                    }
                    else Main.AssassinTimer[player.PlayerId] = Main.AssassinTimer[player.PlayerId] + Time.fixedDeltaTime;//時間をカウント
                }
                else
                {
                    Main.AssassinTimer.Remove(player.PlayerId);
                }
            }
            //ターゲットのリセット
            BountyHunter.FixedUpdate(player);
            if (GameStates.IsInTask && player.IsAlive() && Options.LadderDeath.GetBool())
            {
                FallFromLadder.FixedUpdate(player);
            }
            /*if (GameStates.isInGame && main.AirshipMeetingTimer.ContainsKey(__instance.PlayerId)) //会議後すぐにここの処理をするため不要になったコードです。今後#465で変更した仕様がバグって、ここの処理が必要になった時のために残してコメントアウトしています
            {
                if (main.AirshipMeetingTimer[__instance.PlayerId] >= 9f && !main.AirshipMeetingCheck)
                {
                    main.AirshipMeetingCheck = true;
                    Utils.CustomSyncAllSettings();
                }
                if (main.AirshipMeetingTimer[__instance.PlayerId] >= 10f)
                {
                    Utils.AfterMeetingTasks();
                    main.AirshipMeetingTimer.Remove(__instance.PlayerId);
                }
                else
                    main.AirshipMeetingTimer[__instance.PlayerId] = (main.AirshipMeetingTimer[__instance.PlayerId] + Time.fixedDeltaTime);
                }
            }*/

            if (GameStates.IsInGame) LoversSuicide();

            if (GameStates.IsInTask && Main.ArsonistTimer.ContainsKey(player.PlayerId))//アーソニストが誰かを塗っているとき
            {
                if (!player.IsAlive() || Pelican.IsEaten(player.PlayerId))
                {
                    Main.ArsonistTimer.Remove(player.PlayerId);
                    Utils.NotifyRoles(SpecifySeer: __instance);
                    RPC.ResetCurrentDousingTarget(player.PlayerId);
                }
                else
                {
                    var ar_target = Main.ArsonistTimer[player.PlayerId].Item1;//塗られる人
                    var ar_time = Main.ArsonistTimer[player.PlayerId].Item2;//塗った時間
                    if (!ar_target.IsAlive())
                    {
                        Main.ArsonistTimer.Remove(player.PlayerId);
                    }
                    else if (ar_time >= Options.ArsonistDouseTime.GetFloat())//時間以上一緒にいて塗れた時
                    {
                        player.SetKillCooldown();
                        Main.ArsonistTimer.Remove(player.PlayerId);//塗が完了したのでDictionaryから削除
                        Main.isDoused[(player.PlayerId, ar_target.PlayerId)] = true;//塗り完了
                        player.RpcSetDousedPlayer(ar_target, true);
                        Utils.NotifyRoles();//名前変更
                        RPC.ResetCurrentDousingTarget(player.PlayerId);
                    }
                    else
                    {
                        float dis;
                        dis = Vector2.Distance(player.transform.position, ar_target.transform.position);//距離を出す
                        if (dis <= 1.75f)//一定の距離にターゲットがいるならば時間をカウント
                        {
                            Main.ArsonistTimer[player.PlayerId] = (ar_target, ar_time + Time.fixedDeltaTime);
                        }
                        else//それ以外は削除
                        {
                            Main.ArsonistTimer.Remove(player.PlayerId);
                            Utils.NotifyRoles(SpecifySeer: __instance);
                            RPC.ResetCurrentDousingTarget(player.PlayerId);

                            Logger.Info($"Canceled: {__instance.GetNameWithRole()}", "Arsonist");
                        }
                    }
                }
            }

            if (GameStates.IsInTask && Main.RevolutionistTimer.ContainsKey(player.PlayerId))//当革命家拉拢一个玩家时
            {
                if (!player.IsAlive() || Pelican.IsEaten(player.PlayerId))
                {
                    Main.RevolutionistTimer.Remove(player.PlayerId);
                    Utils.NotifyRoles(SpecifySeer: __instance);
                    RPC.ResetCurrentDrawTarget(player.PlayerId);
                }
                else
                {
                    var ar_target = Main.RevolutionistTimer[player.PlayerId].Item1;//拉拢的人
                    var ar_time = Main.RevolutionistTimer[player.PlayerId].Item2;//拉拢时间
                    if (!ar_target.IsAlive())
                    {
                        Main.RevolutionistTimer.Remove(player.PlayerId);
                    }
                    else if (ar_time >= Options.RevolutionistDrawTime.GetFloat())//在一起时间超过多久
                    {
                        player.SetKillCooldown();
                        Main.RevolutionistTimer.Remove(player.PlayerId);//拉拢完成从字典中删除
                        Main.isDraw[(player.PlayerId, ar_target.PlayerId)] = true;//完成拉拢
                        player.RpcSetDrawPlayer(ar_target, true);
                        Utils.NotifyRoles(SpecifySeer: __instance);
                        RPC.ResetCurrentDrawTarget(player.PlayerId);
                        if (IRandom.Instance.Next(1, 100) <= Options.RevolutionistKillProbability.GetInt())
                        {
                            ar_target.SetRealKiller(player);
                            player.RpcMurderPlayer(ar_target);
                            Main.PlayerStates[ar_target.PlayerId].deathReason = PlayerState.DeathReason.Sacrifice;
                            Main.PlayerStates[ar_target.PlayerId].SetDead();
                            Logger.Info($"Revolutionist: {__instance.GetNameWithRole()} killed {ar_target.GetNameWithRole()}", "Revolutionist");
                        }
                    }
                    else
                    {
                        float dis;
                        dis = Vector2.Distance(player.transform.position, ar_target.transform.position);//超出距离
                        if (dis <= 1.75f)//在一定距离内则计算时间
                        {
                            Main.RevolutionistTimer[player.PlayerId] = (ar_target, ar_time + Time.fixedDeltaTime);
                        }
                        else//否则删除
                        {
                            Main.RevolutionistTimer.Remove(player.PlayerId);
                            Utils.NotifyRoles(SpecifySeer: __instance);
                            RPC.ResetCurrentDrawTarget(player.PlayerId);

                            Logger.Info($"Canceled: {__instance.GetNameWithRole()}", "Revolutionist");
                        }
                    }
                }
            }
            if (GameStates.IsInTask && player.IsDrawDone() && player.IsAlive())
            {
                if (Main.RevolutionistStart.ContainsKey(player.PlayerId)) //如果存在字典
                {
                    if (Main.RevolutionistLastTime.ContainsKey(player.PlayerId))
                    {
                        long nowtime = Utils.GetTimeStamp(DateTime.Now);
                        if (Main.RevolutionistLastTime[player.PlayerId] != nowtime) Main.RevolutionistLastTime[player.PlayerId] = nowtime;
                        int time = (int)(Main.RevolutionistLastTime[player.PlayerId] - Main.RevolutionistStart[player.PlayerId]);
                        int countdown = Options.RevolutionistVentCountDown.GetInt() - time;
                        Main.RevolutionistCountdown.Clear();
                        Main.RevolutionistCountdown.Add(player.PlayerId, countdown);
                        Utils.NotifyRoles(player);
                        if (countdown <= 0)//倒计时结束
                        {
                            Utils.GetDrawPlayerCount(player.PlayerId, out var y);
                            foreach (var pc in y.Where(x => x != null && x.IsAlive()))
                            {
                                pc.Data.IsDead = true;
                                pc.RpcMurderPlayer(pc);
                                Main.PlayerStates[pc.PlayerId].deathReason = PlayerState.DeathReason.Sacrifice;
                                Main.PlayerStates[pc.PlayerId].SetDead();
                            }
                            player.Data.IsDead = true;
                            player.RpcMurderPlayer(player);
                            Main.PlayerStates[player.PlayerId].deathReason = PlayerState.DeathReason.Sacrifice;
                            Main.PlayerStates[player.PlayerId].SetDead();
                        }
                    }
                    else
                    {
                        Main.RevolutionistLastTime.Add(player.PlayerId, Main.RevolutionistStart[player.PlayerId]);
                    }
                }
                else //如果不存在字典
                {
                    Main.RevolutionistStart.Add(player.PlayerId, Utils.GetTimeStamp(DateTime.Now));
                }
            }

            if (GameStates.IsInTask && Main.PuppeteerList.ContainsKey(player.PlayerId))
            {
                if (!player.IsAlive() || Pelican.IsEaten(player.PlayerId))
                {
                    Main.PuppeteerList.Remove(player.PlayerId);
                }
                else
                {
                    Vector2 puppeteerPos = player.transform.position;//PuppeteerListのKeyの位置
                    Dictionary<byte, float> targetDistance = new();
                    float dis;
                    foreach (var target in Main.AllAlivePlayerControls)
                    {
                        if (target.PlayerId != player.PlayerId && !target.Is(CountTypes.Impostor))
                        {
                            dis = Vector2.Distance(puppeteerPos, target.transform.position);
                            targetDistance.Add(target.PlayerId, dis);
                        }
                    }
                    if (targetDistance.Count() != 0)
                    {
                        var min = targetDistance.OrderBy(c => c.Value).FirstOrDefault();//一番値が小さい
                        PlayerControl target = Utils.GetPlayerById(min.Key);
                        var KillRange = NormalGameOptionsV07.KillDistances[Mathf.Clamp(Main.NormalOptions.KillDistance, 0, 2)];
                        if (min.Value <= KillRange && player.CanMove && target.CanMove)
                        {
                            var puppeteerId = Main.PuppeteerList[player.PlayerId];
                            RPC.PlaySoundRPC(puppeteerId, Sounds.KillSound);
                            target.SetRealKiller(Utils.GetPlayerById(puppeteerId));
                            player.RpcMurderPlayer(target);
                            Utils.MarkEveryoneDirtySettings();
                            Main.PuppeteerList.Remove(player.PlayerId);
                            Utils.NotifyRoles();
                        }
                    }
                }
            }
            if (GameStates.IsInTask && player == PlayerControl.LocalPlayer)
                DisableDevice.FixedUpdate();
            if (GameStates.IsInTask && player == PlayerControl.LocalPlayer)
                AntiAdminer.FixedUpdate();

            if (GameStates.IsInGame && Main.RefixCooldownDelay <= 0)
                foreach (var pc in Main.AllPlayerControls)
                {
                    if (pc.Is(CustomRoles.Vampire) || pc.Is(CustomRoles.Warlock) || pc.Is(CustomRoles.Assassin))
                        Main.AllPlayerKillCooldown[pc.PlayerId] = Options.DefaultKillCooldown * 2;
                }

            if (!Main.DoBlockNameChange && AmongUsClient.Instance.AmHost)
            {
                if (__instance.AmOwner) Utils.ApplySuffix();
                Utils.ApplyDevSuffix(__instance);
            }
        }
        //LocalPlayer専用
        if (__instance.AmOwner)
        {
            //キルターゲットの上書き処理
            if (GameStates.IsInTask && !__instance.Is(CustomRoleTypes.Impostor) && __instance.CanUseKillButton() && !__instance.Data.IsDead)
            {
                var players = __instance.GetPlayersInAbilityRangeSorted(false);
                PlayerControl closest = players.Count <= 0 ? null : players[0];
                HudManager.Instance.KillButton.SetTarget(closest);
            }
        }

        //役職テキストの表示
        var RoleTextTransform = __instance.cosmetics.nameText.transform.Find("RoleText");
        var RoleText = RoleTextTransform.GetComponent<TMPro.TextMeshPro>();
        if (RoleText != null && __instance != null)
        {
            if (GameStates.IsLobby)
            {
                if (Main.playerVersion.TryGetValue(__instance.PlayerId, out var ver))
                {
                    if (Main.ForkId != ver.forkId) // フォークIDが違う場合
                        __instance.cosmetics.nameText.text = $"<color=#ff0000><size=1.2>{ver.forkId}</size>\n{__instance?.name}</color>";
                    else if (Main.version.CompareTo(ver.version) == 0)
                        __instance.cosmetics.nameText.text = ver.tag == $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})" ? $"<color=#87cefa>{__instance.name}</color>" : $"<color=#ffff00><size=1.2>{ver.tag}</size>\n{__instance?.name}</color>";
                    else __instance.cosmetics.nameText.text = $"<color=#ff0000><size=1.2>v{ver.version}</size>\n{__instance?.name}</color>";
                }
                else __instance.cosmetics.nameText.text = __instance?.Data?.PlayerName;
            }
            if (GameStates.IsInGame)
            {
                var RoleTextData = Utils.GetRoleText(__instance.PlayerId);
                //if (Options.CurrentGameMode == CustomGameMode.HideAndSeek)
                //{
                //    var hasRole = main.AllPlayerCustomRoles.TryGetValue(__instance.PlayerId, out var role);
                //    if (hasRole) RoleTextData = Utils.GetRoleTextHideAndSeek(__instance.Data.Role.Role, role);
                //}
                RoleText.text = RoleTextData.Item1;
                RoleText.color = RoleTextData.Item2;
                if (__instance.AmOwner) RoleText.enabled = true; //自分ならロールを表示
                else if (Main.VisibleTasksCount && PlayerControl.LocalPlayer.Data.IsDead && Options.GhostCanSeeOtherRoles.GetBool()) RoleText.enabled = true; //他プレイヤーでVisibleTasksCountが有効なおかつ自分が死んでいるならロールを表示
                else if (__instance.GetCustomRole().IsImpostor() && PlayerControl.LocalPlayer.GetCustomRole().IsImpostor() && !PlayerControl.LocalPlayer.Data.IsDead && Options.ImpKnowAlliesRole.GetBool()) RoleText.enabled = true;
                else if (__instance.GetCustomRole().IsImpostor() && PlayerControl.LocalPlayer.Is(CustomRoles.Madmate) && !PlayerControl.LocalPlayer.Data.IsDead) RoleText.enabled = true;
                else if (PlayerControl.LocalPlayer.Is(CustomRoles.God) && !PlayerControl.LocalPlayer.Data.IsDead) RoleText.enabled = true;
                else RoleText.enabled = false; //そうでなければロールを非表示
                if (!AmongUsClient.Instance.IsGameStarted && AmongUsClient.Instance.NetworkMode != NetworkModes.FreePlay)
                {
                    RoleText.enabled = false; //ゲームが始まっておらずフリープレイでなければロールを非表示
                    if (!__instance.AmOwner) __instance.cosmetics.nameText.text = __instance?.Data?.PlayerName;
                }
                if (Main.VisibleTasksCount) //他プレイヤーでVisibleTasksCountは有効なら
                    RoleText.text += Utils.GetProgressText(__instance); //ロールの横にタスクなど進行状況表示


                //変数定義
                var seer = PlayerControl.LocalPlayer;
                var target = __instance;

                string RealName;
                Mark.Clear();
                Suffix.Clear();

                //名前変更
                RealName = target.GetRealName();

                //名前色変更処理
                //自分自身の名前の色を変更
                if (target.AmOwner && AmongUsClient.Instance.IsGameStarted)
                { //targetが自分自身
                    if (target.Is(CustomRoles.Arsonist) && target.IsDouseDone())
                        RealName = Utils.ColorString(Utils.GetRoleColor(CustomRoles.Arsonist), GetString("EnterVentToWin"));
                    if (target.Is(CustomRoles.Revolutionist) && target.IsDrawDone())
                        RealName = Utils.ColorString(Utils.GetRoleColor(CustomRoles.Revolutionist), string.Format(GetString("EnterVentWinCountDown"), Main.RevolutionistCountdown.TryGetValue(seer.PlayerId, out var x) ? x : 10));
                    if (Pelican.IsEaten(seer.PlayerId))
                        RealName = Utils.ColorString(Utils.GetRoleColor(CustomRoles.Pelican), GetString("EatenByPelican"));
                }

                //NameColorManager準拠の処理
                RealName = RealName.ApplyNameColorData(seer, target, false);

                //インポスター/キル可能なニュートラルがタスクが終わりそうなSnitchを確認できる
                Mark.Append(Snitch.GetWarningMark(seer, target));

                if (seer.Is(CustomRoles.Arsonist))
                {
                    if (seer.IsDousedPlayer(target))
                    {
                        Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Arsonist)}>▲</color>");
                    }
                    else if (
                        Main.currentDousingTarget != 255 &&
                        Main.currentDousingTarget == target.PlayerId
                    )
                    {
                        Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Arsonist)}>△</color>");
                    }
                }

                if (seer.Is(CustomRoles.Revolutionist))
                {
                    if (seer.IsDrawPlayer(target))
                    {
                        Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Revolutionist)}>●</color>");
                    }
                    else if (
                        Main.currentDrawTarget != 255 &&
                        Main.currentDrawTarget == target.PlayerId
                    )
                    {
                        Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Revolutionist)}>○</color>");
                    }
                }

                Mark.Append(Executioner.TargetMark(seer, target));
                if (seer.Is(CustomRoles.Puppeteer))
                {
                    if (seer.Is(CustomRoles.Puppeteer) &&
                    Main.PuppeteerList.ContainsValue(seer.PlayerId) &&
                    Main.PuppeteerList.ContainsKey(target.PlayerId))
                        Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Impostor)}>◆</color>");
                }
                if (Sniper.IsEnable && target.AmOwner)
                {
                    //銃声が聞こえるかチェック
                    Mark.Append(Sniper.GetShotNotify(target.PlayerId));

                }
                if (seer.Is(CustomRoles.EvilTracker)) Mark.Append(EvilTracker.GetTargetMark(seer, target));
                //タスクが終わりそうなSnitchがいるとき、インポスター/キル可能な第三陣営に警告が表示される
                Mark.Append(Snitch.GetWarningArrow(seer, target));

                if (target.Is(CustomRoles.SuperStar) && Options.EveryOneKnowSuperStar.GetBool())
                    Mark.Append(Utils.ColorString(Utils.GetRoleColor(CustomRoles.SuperStar), "★"));

                //ハートマークを付ける(会議中MOD視点)
                if (__instance.Is(CustomRoles.Lovers) && PlayerControl.LocalPlayer.Is(CustomRoles.Lovers))
                {
                    Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Lovers)}>♡</color>");
                }
                else if (__instance.Is(CustomRoles.Lovers) && PlayerControl.LocalPlayer.Data.IsDead)
                {
                    Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Lovers)}>♡</color>");
                }
                else if (__instance.Is(CustomRoles.Ntr) || PlayerControl.LocalPlayer.Is(CustomRoles.Ntr))
                {
                    Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Lovers)}>♡</color>");
                }
                else if (__instance == PlayerControl.LocalPlayer && CustomRolesHelper.RoleExist(CustomRoles.Ntr))
                {
                    Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Lovers)}>♡</color>");
                }

                //矢印オプションありならタスクが終わったスニッチはインポスター/キル可能な第三陣営の方角がわかる
                Suffix.Append(Snitch.GetSnitchArrow(seer, target));

                Suffix.Append(BountyHunter.GetTargetArrow(seer, target));

                Suffix.Append(EvilTracker.GetTargetArrow(seer, target));

                if (GameStates.IsInTask && seer.Is(CustomRoles.AntiAdminer))
                {
                    AntiAdminer.FixedUpdate();
                    if (target.AmOwner)
                    {
                        if (AntiAdminer.IsAdminWatch) Suffix.Append("★" + GetString("AntiAdminerAD"));
                        if (AntiAdminer.IsVitalWatch) Suffix.Append("★" + GetString("AntiAdminerVI"));
                        if (AntiAdminer.IsDoorLogWatch) Suffix.Append("★" + GetString("AntiAdminerDL"));
                        if (AntiAdminer.IsCameraWatch) Suffix.Append("★" + GetString("AntiAdminerCA"));
                    }
                }

                /*if(main.AmDebugger.Value && main.BlockKilling.TryGetValue(target.PlayerId, out var isBlocked)) {
                    Mark = isBlocked ? "(true)" : "(false)";
                }*/
                if (Utils.IsActive(SystemTypes.Comms) && Options.CommsCamouflage.GetBool())
                    RealName = $"<size=0>{RealName}</size> ";

                string DeathReason = seer.Data.IsDead && seer.KnowDeathReason(target) ? $"({Utils.ColorString(Utils.GetRoleColor(CustomRoles.Doctor), Utils.GetVitalText(target.PlayerId))})" : "";
                //Mark・Suffixの適用
                target.cosmetics.nameText.text = $"{RealName}{DeathReason}{Mark}";

                if (Suffix.ToString() != "")
                {
                    //名前が2行になると役職テキストを上にずらす必要がある
                    RoleText.transform.SetLocalY(0.35f);
                    target.cosmetics.nameText.text += "\r\n" + Suffix.ToString();

                }
                else
                {
                    //役職テキストの座標を初期値に戻す
                    RoleText.transform.SetLocalY(0.2f);
                }
            }
            else
            {
                //役職テキストの座標を初期値に戻す
                RoleText.transform.SetLocalY(0.2f);
            }
        }
    }
    //FIXME: 役職クラス化のタイミングで、このメソッドは移動予定
    public static void LoversSuicide(byte deathId = 0x7f, bool isExiled = false)
    {
        if (Options.LoverSuicide.GetBool() && CustomRoles.Lovers.IsEnable() && Main.isLoversDead == false)
        {
            foreach (var loversPlayer in Main.LoversPlayers)
            {
                //生きていて死ぬ予定でなければスキップ
                if (!loversPlayer.Data.IsDead && loversPlayer.PlayerId != deathId) continue;

                Main.isLoversDead = true;
                foreach (var partnerPlayer in Main.LoversPlayers)
                {
                    //本人ならスキップ
                    if (loversPlayer.PlayerId == partnerPlayer.PlayerId) continue;

                    //残った恋人を全て殺す(2人以上可)
                    //生きていて死ぬ予定もない場合は心中
                    if (partnerPlayer.PlayerId != deathId && !partnerPlayer.Data.IsDead)
                    {
                        Main.PlayerStates[partnerPlayer.PlayerId].deathReason = PlayerState.DeathReason.FollowingSuicide;
                        if (isExiled)
                            CheckForEndVotingPatch.TryAddAfterMeetingDeathPlayers(PlayerState.DeathReason.FollowingSuicide, partnerPlayer.PlayerId);
                        else
                            partnerPlayer.RpcMurderPlayer(partnerPlayer);
                    }
                }
            }
        }
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Start))]
class PlayerStartPatch
{
    public static void Postfix(PlayerControl __instance)
    {
        var roleText = UnityEngine.Object.Instantiate(__instance.cosmetics.nameText);
        roleText.transform.SetParent(__instance.cosmetics.nameText.transform);
        roleText.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        roleText.fontSize -= 1.2f;
        roleText.text = "RoleText";
        roleText.gameObject.name = "RoleText";
        roleText.enabled = false;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.SetColor))]
class SetColorPatch
{
    public static bool IsAntiGlitchDisabled = false;
    public static bool Prefix(PlayerControl __instance, int bodyColor)
    {
        //色変更バグ対策
        if (!AmongUsClient.Instance.AmHost || __instance.CurrentOutfit.ColorId == bodyColor || IsAntiGlitchDisabled) return true;
        return true;
    }
}

[HarmonyPatch(typeof(Vent), nameof(Vent.EnterVent))]
class EnterVentPatch
{
    public static void Postfix(Vent __instance, [HarmonyArgument(0)] PlayerControl pc)
    {
        if (AmongUsClient.Instance.AmHost)
        {
            if (Main.LastEnteredVent.ContainsKey(pc.PlayerId))
                Main.LastEnteredVent.Remove(pc.PlayerId);
            Main.LastEnteredVent.Add(pc.PlayerId, __instance);
            if (Main.LastEnteredVentLocation.ContainsKey(pc.PlayerId))
                Main.LastEnteredVentLocation.Remove(pc.PlayerId);
            Main.LastEnteredVentLocation.Add(pc.PlayerId, pc.GetTruePosition());
        }

        Witch.OnEnterVent(pc);

        if (pc.Is(CustomRoles.Paranoia))
        {
            if (Main.ParaUsedButtonCount.TryGetValue(pc.PlayerId, out var count) && count < Options.ParanoiaNumOfUseButton.GetInt())
            {
                Main.ParaUsedButtonCount[pc.PlayerId] += 1;
                new LateTask(() =>
                {
                    Utils.SendMessage(GetString("SkillUsedLeft") + (Options.ParanoiaNumOfUseButton.GetInt() - Main.ParaUsedButtonCount[pc.PlayerId]).ToString(), pc.PlayerId);
                }, 4.0f, "Skill Remain Message");
                pc?.MyPhysics?.RpcBootFromVent(__instance.Id);
                pc?.NoCheckStartMeeting(pc?.Data);
            }
        }
        if (pc.Is(CustomRoles.Mayor))
        {
            if (Main.MayorUsedButtonCount.TryGetValue(pc.PlayerId, out var count) && count < Options.MayorNumOfUseButton.GetInt())
            {
                pc?.MyPhysics?.RpcBootFromVent(__instance.Id);
                pc?.ReportDeadBody(null);
            }
        }
        if (pc.Is(CustomRoles.Mario))
        {
            if (!Main.MarioVentCount.ContainsKey(pc.PlayerId)) Main.MarioVentCount.Add(pc.PlayerId, 0);
            Main.MarioVentCount[pc.PlayerId]++;
            Utils.NotifyRoles(SpecifySeer: pc);
            if (Main.MarioVentCount[pc.PlayerId] >= Options.MarioVentNumWin.GetInt())
            {
                CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Mario); //马里奥这个多动症赢了
                CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
            }
        }
        if (pc.Is(CustomRoles.Veteran))
        {
            Main.VeteranInProtect.Remove(pc.PlayerId);
            Main.VeteranInProtect.Add(pc.PlayerId, Utils.GetTimeStamp(DateTime.Now));
            new LateTask(() =>
            {
                if (GameStates.IsInTask && !GameStates.IsMeeting) pc.RpcGuardAndKill(pc);
            }, 1.5f, "Veteran Skill Notify");
        }
        if (pc.Is(CustomRoles.Grenadier))
        {
            if (pc.Is(CustomRoles.Madmate))
            {
                Main.MadGrenadierBlinding.Remove(pc.PlayerId);
                Main.MadGrenadierBlinding.Add(pc.PlayerId, Utils.GetTimeStamp(DateTime.Now));
            }
            else
            {
                Main.GrenadierBlinding.Remove(pc.PlayerId);
                Main.GrenadierBlinding.Add(pc.PlayerId, Utils.GetTimeStamp(DateTime.Now));
            }
            new LateTask(() =>
            {
                if (GameStates.IsInTask && !GameStates.IsMeeting) pc.RpcGuardAndKill(pc);
                Utils.MarkEveryoneDirtySettings();
            }, 1.5f, "Grenadier Skill Notify");
        }
    }
}

[HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.CoEnterVent))]
class CoEnterVentPatch
{
    public static bool Prefix(PlayerPhysics __instance, [HarmonyArgument(0)] int id)
    {

        if (AmongUsClient.Instance.AmHost)
        {
            if (AmongUsClient.Instance.IsGameStarted && __instance.myPlayer.IsDouseDone())
            {
                foreach (var pc in Main.AllAlivePlayerControls)
                {
                    if (pc != __instance.myPlayer)
                    {
                        //生存者は焼殺
                        pc.SetRealKiller(__instance.myPlayer);
                        pc.RpcMurderPlayer(pc);
                        Main.PlayerStates[pc.PlayerId].deathReason = PlayerState.DeathReason.Torched;
                        Main.PlayerStates[pc.PlayerId].SetDead();
                    }
                    else
                        RPC.PlaySoundRPC(pc.PlayerId, Sounds.KillSound);
                }
                foreach (var pc in PlayerControl.AllPlayerControls) pc.KillFlash();
                CustomWinnerHolder.ShiftWinnerAndSetWinner(CustomWinner.Arsonist); //焼殺で勝利した人も勝利させる
                CustomWinnerHolder.WinnerIds.Add(__instance.myPlayer.PlayerId);
                return true;
            }

            if (AmongUsClient.Instance.IsGameStarted && __instance.myPlayer.IsDrawDone())//完成拉拢任务的玩家跳管后
            {
                CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Revolutionist);//革命者胜利
                Utils.GetDrawPlayerCount(__instance.myPlayer.PlayerId, out var x);
                CustomWinnerHolder.WinnerIds.Add(__instance.myPlayer.PlayerId);
                foreach (var apc in x) CustomWinnerHolder.WinnerIds.Add(apc.PlayerId);//胜利玩家
                return true;
            }

            //让玩家滚出通风管
            if ((__instance.myPlayer.Data.Role.Role != RoleTypes.Engineer && //不是工程师
            !__instance.myPlayer.CanUseImpostorVentButton()) || //不能使用内鬼的跳管按钮
            (__instance.myPlayer.Is(CustomRoles.Mayor) && Main.MayorUsedButtonCount.TryGetValue(__instance.myPlayer.PlayerId, out var count) && count >= Options.MayorNumOfUseButton.GetInt()) ||
            (__instance.myPlayer.Is(CustomRoles.Paranoia) && Main.ParaUsedButtonCount.TryGetValue(__instance.myPlayer.PlayerId, out var count2) && count2 >= Options.ParanoiaNumOfUseButton.GetInt())
            )
            {
                MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(__instance.NetId, (byte)RpcCalls.BootFromVent, SendOption.Reliable, -1);
                writer.WritePacked(127);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
                new LateTask(() =>
                {
                    int clientId = __instance.myPlayer.GetClientId();
                    MessageWriter writer2 = AmongUsClient.Instance.StartRpcImmediately(__instance.NetId, (byte)RpcCalls.BootFromVent, SendOption.Reliable, clientId);
                    writer2.Write(id);
                    AmongUsClient.Instance.FinishRpcImmediately(writer2);
                }, 0.5f, "Fix DesyncImpostor Stuck");
                return false;
            }
        }
        return true;
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.SetName))]
class SetNamePatch
{
    public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] string name)
    {
    }
}
[HarmonyPatch(typeof(GameData), nameof(GameData.CompleteTask))]
class GameDataCompleteTaskPatch
{
    public static void Postfix(PlayerControl pc)
    {
        Logger.Info($"TaskComplete:{pc.GetNameWithRole()}", "CompleteTask");
        Main.PlayerStates[pc.PlayerId].UpdateTask(pc);
        Utils.NotifyRoles();
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CompleteTask))]
class PlayerControlCompleteTaskPatch
{
    public static bool Prefix(PlayerControl __instance)
    {
        var player = __instance;

        //加班狂的任务
        if (Workhorse.OnCompleteTask(player)) //タスク勝利をキャンセル
            return false;

        //来自资本主义的任务
        if (Main.CapitalismAddTask.ContainsKey(player.PlayerId))
        {
            var taskState = player.GetPlayerTaskState();
            taskState.AllTasksCount += Main.CapitalismAddTask[player.PlayerId];
            Main.CapitalismAddTask.Remove(player.PlayerId);
            taskState.CompletedTasksCount++;
            GameData.Instance.RpcSetTasks(player.PlayerId, new byte[0]); //タスクを再配布
            player.SyncSettings();
            Utils.NotifyRoles(player);
            return false;
        }

        return true;
    }
    public static void Postfix(PlayerControl __instance)
    {
        var pc = __instance;
        Snitch.OnCompleteTask(pc);

        var isTaskFinish = pc.GetPlayerTaskState().IsTaskFinished;
        if ((isTaskFinish &&
            pc.GetCustomRole() is CustomRoles.Lighter or CustomRoles.Doctor) ||
            pc.GetCustomRole() is CustomRoles.SpeedBooster)
        {
            //ライターもしくはスピードブースターもしくはドクターがいる試合のみタスク終了時にCustomSyncAllSettingsを実行する
            Utils.MarkEveryoneDirtySettings();
        }
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ProtectPlayer))]
class PlayerControlProtectPlayerPatch
{
    public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        Logger.Info($"{__instance.GetNameWithRole()} => {target.GetNameWithRole()}", "ProtectPlayer");
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RemoveProtection))]
class PlayerControlRemoveProtectionPatch
{
    public static void Postfix(PlayerControl __instance)
    {
        Logger.Info($"{__instance.GetNameWithRole()}", "RemoveProtection");
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSetRole))]
class PlayerControlSetRolePatch
{
    public static bool Prefix(PlayerControl __instance, ref RoleTypes roleType)
    {
        var target = __instance;
        Logger.Info($"{__instance.GetNameWithRole()} =>{roleType}", "PlayerControl.RpcSetRole");
        if (!ShipStatus.Instance.enabled) return true;
        if (roleType is RoleTypes.CrewmateGhost or RoleTypes.ImpostorGhost)
        {
            foreach (var seer in Main.AllPlayerControls)
            {
                var self = seer.PlayerId == target.PlayerId;
                var seerIsKiller = seer.Is(CustomRoleTypes.Impostor) || Main.ResetCamPlayerList.Contains(seer.PlayerId);
                var targetIsKiller = target.Is(CustomRoleTypes.Impostor) || Main.ResetCamPlayerList.Contains(target.PlayerId);
                if ((self && targetIsKiller) || (!seerIsKiller && target.Is(CustomRoleTypes.Impostor)))
                {
                    Logger.Info($"Desync {target.GetNameWithRole()} =>ImpostorGhost for{seer.GetNameWithRole()}", "PlayerControl.RpcSetRole");
                    target.RpcSetRoleDesync(RoleTypes.ImpostorGhost, seer.GetClientId());
                }
                else
                {
                    Logger.Info($"Desync {target.GetNameWithRole()} =>CrewmateGhost for{seer.GetNameWithRole()}", "PlayerControl.RpcSetRole");
                    target.RpcSetRoleDesync(RoleTypes.CrewmateGhost, seer.GetClientId());
                }
            }
            return false;
        }
        return true;
    }
}