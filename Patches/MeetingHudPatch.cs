using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

using TownOfHostY.Modules;
using TownOfHostY.Roles;
using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.AddOns.Common;
using static TownOfHostY.Translator;
using TownOfHostY.Roles.Impostor;

namespace TownOfHostY;

[HarmonyPatch]
public static class MeetingHudPatch
{
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CheckForEndVoting))]
    class CheckForEndVotingPatch
    {
        public static bool Prefix()
        {
            if (!AmongUsClient.Instance.AmHost) return true;
            MeetingVoteManager.Instance?.CheckAndEndMeeting();
            return false;
        }
    }
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CastVote))]
    public static class CastVotePatch
    {
        public static bool Prefix(MeetingHud __instance, [HarmonyArgument(0)] byte srcPlayerId /* 投票した人 */ , [HarmonyArgument(1)] byte suspectPlayerId /* 投票された人 */ )
        {
            var voter = Utils.GetPlayerById(srcPlayerId);
            var voted = Utils.GetPlayerById(suspectPlayerId);
            if (voter.GetRoleClass()?.CheckVoteAsVoter(voted) == false)
            {
                __instance.RpcClearVote(voter.GetClientId());
                Logger.Info($"{voter.GetNameWithRole()} は投票しない", nameof(CastVotePatch));
                return false;
            }

            MeetingVoteManager.Instance?.SetVote(srcPlayerId, suspectPlayerId);
            return true;
        }
    }
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    class StartPatch
    {
        public static void Prefix()
        {
            Logger.Info("------------会議開始------------", "Phase");
            VentilationSystemPatch.ClearVent();
            ChatUpdatePatch.DoBlockChat = true;
            GameStates.AlreadyDied |= !Utils.IsAllAlive;
            Main.AllPlayerControls.Do(x => ReportDeadBodyPatch.WaitReport[x.PlayerId].Clear());
            Sending.OnStartMeeting();
            foreach (var tm in Main.AllAlivePlayerControls.Where(p=>p.Is(CustomRoles.TaskManager) || p.Is(CustomRoles.Management)))
                Utils.NotifyRoles(true, tm);
            TargetDeadArrow.OnStartMeeting();
            MeetingStates.MeetingCalled = true;
        }
        public static void Postfix(MeetingHud __instance)
        {
            MeetingVoteManager.Start();

            SoundManager.Instance.ChangeAmbienceVolume(0f);
            if (!GameStates.IsModHost) return;
            var myRole = PlayerControl.LocalPlayer.GetRoleClass();
            foreach (var pva in __instance.playerStates)
            {
                var pc = Utils.GetPlayerById(pva.TargetPlayerId);
                if (pc == null) continue;
                var roleTextMeeting = UnityEngine.Object.Instantiate(pva.NameText);
                roleTextMeeting.transform.SetParent(pva.NameText.transform);
                roleTextMeeting.transform.localPosition = new Vector3(0f, -0.18f, 0f);
                roleTextMeeting.fontSize = 1.5f;
                (roleTextMeeting.enabled, roleTextMeeting.text)
                    = Utils.GetRoleNameAndProgressTextData(true, PlayerControl.LocalPlayer, pc);
                roleTextMeeting.gameObject.name = "RoleTextMeeting";
                roleTextMeeting.enableWordWrapping = false;
                // 役職とサフィックスを同時に表示する必要が出たら要改修
                var suffixBuilder = new StringBuilder(32);
                if (myRole != null)
                {
                    suffixBuilder.Append(myRole.GetSuffix(PlayerControl.LocalPlayer, pc, isForMeeting: true));
                }
                suffixBuilder.Append(CustomRoleManager.GetSuffixOthers(PlayerControl.LocalPlayer, pc, isForMeeting: true));
                if (suffixBuilder.Length > 0)
                {
                    roleTextMeeting.text = suffixBuilder.ToString();
                    roleTextMeeting.enabled = true;
                }
            }
            CustomRoleManager.AllActiveRoles.Values.Do(role => role.OnStartMeeting());
            if (Options.SyncButtonMode.GetBool())
            {
                Utils.SendMessage(string.Format(GetString("Message.SyncButtonLeft"), Options.SyncedButtonCount.GetFloat() - Options.UsedButtonCount));
                Logger.Info("緊急会議ボタンはあと" + (Options.SyncedButtonCount.GetFloat() - Options.UsedButtonCount) + "回使用可能です。", "SyncButtonMode");
            }
            if (Options.ShowReportReason.GetBool())
            {
                if (ReportDeadBodyPatch.ReportTarget == null)
                    Utils.SendMessage(GetString("Message.isButton"));
                else if (!ReportDeadBodyPatch.SpecialMeeting)
                    Utils.SendMessage(string.Format(GetString("Message.isReport"),
                        $"{ReportDeadBodyPatch.ReportTarget.PlayerName}{ReportDeadBodyPatch.ReportTarget.ColorName.Color(ReportDeadBodyPatch.ReportTarget.Color)}"));
            }
            if (Options.ShowRevengeTarget.GetBool())
            {
                foreach (var Exiled_Target in RevengeTargetPlayer)
                {
                    Utils.SendMessage(string.Format(GetString("Message.RevengeText"),
                        $"{Exiled_Target.exiled.PlayerName}{Exiled_Target.exiled.ColorName.Color(Exiled_Target.exiled.Color)}", $"{Exiled_Target.revengeTarget.PlayerName}{Exiled_Target.revengeTarget.ColorName.Color(Exiled_Target.revengeTarget.Color)}"));
                }
            }

            if (AntiBlackout.OverrideExiledPlayer && !Options.IsCCMode)
            {
                Utils.SendMessage(GetString("Warning.OverrideExiledPlayer"));
            }
            if (Options.IsCCMode)
            {
                CatchCat.Infomation.ShowMeeting();
            }

            if (MeetingStates.FirstMeeting) TemplateManager.SendTemplate("OnFirstMeeting", noErr: true);
            TemplateManager.SendTemplate("OnMeeting", noErr: true);

            EvilHacker.FirstMeetingText();

            if (AmongUsClient.Instance.AmHost)
            {
                _ = new LateTask(() =>
                {
                    foreach (var seen in Main.AllPlayerControls)
                    {
                        var seenName = seen.GetRealName(isMeeting: true);
                        var coloredName = Utils.ColorString(seen.GetRoleColor(), seenName);
                        foreach (var seer in Main.AllPlayerControls)
                        {
                            seen.RpcSetNamePrivate(
                                seer == seen ? coloredName : seenName,
                                true,
                                seer);
                        }
                    }
                    ChatUpdatePatch.DoBlockChat = false;
                }, 3f, "SetName To Chat");
            }

            // MeetingDisplayText
            int Showi = 0;

            foreach (var pva in __instance.playerStates)
            {
                if (pva == null) continue;
                var seer = PlayerControl.LocalPlayer;
                var seerRole = seer.GetRoleClass();

                var target = Utils.GetPlayerById(pva.TargetPlayerId);
                if (target == null) continue;

                // 初手会議での役職説明表示
                if (Options.ShowRoleInfoAtFirstMeeting.GetBool() && MeetingStates.FirstMeeting)
                {
                    string RoleInfoTitleString = $"{GetString("RoleInfoTitle")}";
                    string RoleInfoTitle = $"{Utils.ColorString(Utils.GetRoleColor(target.GetCustomRole()), RoleInfoTitleString)}";
                    Utils.SendMessage(Utils.GetMyRoleInfo(target), pva.TargetPlayerId, RoleInfoTitle);
                }

                var sb = new StringBuilder();

                //会議画面での名前変更
                //自分自身の名前の色を変更
                //NameColorManager準拠の処理
                if (target.AmOwner && AmongUsClient.Instance.IsGameStarted) //変更先が自分自身
                {
                    //if (Options.IsONMode && (Main.DefaultRole[pva.TargetPlayerId] != CustomRoles.ONPhantomThief))
                    //    pva.NameText.color = Utils.GetRoleColor(Main.DefaultRole[pva.TargetPlayerId]);
                    //else if (Options.IsONMode && (Main.DefaultRole[pva.TargetPlayerId] == CustomRoles.ONPhantomThief))
                    //    pva.NameText.color = Utils.GetRoleColor(seer.GetCustomRole());
                    //else
                        pva.NameText.text = pva.NameText.text.ApplyNameColorData(seer, target, true);

                    (Color c,string t) = (pva.NameText.color, "");
                    //trueRoleNameでColor上書きあればそれになる
                    target.GetRoleClass()?.OverrideTrueRoleName(ref c, ref t);
                    pva.NameText.color = c;
                }
                else
                {
                    //if (Options.IsONMode && Main.DefaultRole[seer.PlayerId].IsONImpostor() && Main.DefaultRole[target.PlayerId].IsONImpostor())
                    //    pva.NameText.color = Utils.GetRoleColor(CustomRoles.ONWerewolf);
                    //else if (Options.IsONMode && Main.DefaultRole[seer.PlayerId] == CustomRoles.ONPhantomThief && Main.DefaultRole[target.PlayerId].IsONImpostor())
                    //{ }
                    //else if (Options.IsONMode && (Main.DefaultRole[target.PlayerId] == CustomRoles.ONPhantomThief))
                    //{ }
                    //else
                        pva.NameText.text = pva.NameText.text.ApplyNameColorData(seer, target, true);
                }

                //とりあえずSnitchは会議中にもインポスターを確認することができる仕様にしていますが、変更する可能性があります。

                if (seer.KnowDeathReason(target))
                    sb.Append($"({Utils.ColorString(Utils.GetRoleColor(CustomRoles.Doctor), Utils.GetVitalText(target.PlayerId))})");

                sb.Append(seerRole?.GetMark(seer, target, true));
                sb.Append(CustomRoleManager.GetMarkOthers(seer, target, true));
                //Lovers
                sb.Append(Lovers.GetMark(seer, target));

                foreach (var subRole in target.GetCustomSubRoles())
                {
                    switch (subRole)
                    {
                    }
                }

                //会議画面ではインポスター自身の名前にSnitchマークはつけません。

                pva.NameText.text += sb.ToString();

                if (!pva.AmDead && Showi < 3)
                {
                    pva.NameText.text = MeetingDisplayText.AddTextForClient(pva.NameText.text, Showi);
                    Showi++;
                }
            }
            // 道連れ記載情報破棄
            RevengeTargetPlayer.Clear();
        }
    }
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Update))]
    class UpdatePatch
    {
        public static void Postfix(MeetingHud __instance)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (Input.GetMouseButtonUp(1) && Input.GetKey(KeyCode.LeftControl))
            {
                __instance.playerStates.DoIf(x => x.HighlightedFX.enabled, x =>
                {
                    var player = Utils.GetPlayerById(x.TargetPlayerId);
                    player.RpcExileV2();
                    var state = PlayerState.GetByPlayerId(player.PlayerId);
                    state.DeathReason = CustomDeathReason.Execution;
                    state.SetDead();
                    Utils.SendMessage(string.Format(GetString("Message.Executed"), player.Data.PlayerName));
                    Logger.Info($"{player.GetNameWithRole()}を処刑しました", "Execution");
                    __instance.CheckForEndVoting();
                });
            }
        }
    }
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.OnDestroy))]
    class OnDestroyPatch
    {
        public static void Postfix()
        {
            MeetingStates.FirstMeeting = false;
            Logger.Info("------------会議終了------------", "Phase");
            if (AmongUsClient.Instance.AmHost)
            {
                AntiBlackout.SetIsDead();

                Main.AllPlayerControls.Where(pc => !pc.Is(CustomRoles.GM)).Do(pc => RandomSpawn.CustomNetworkTransformPatch.FirstTP[pc.PlayerId] = true);
            }
            // MeetingVoteManagerを通さずに会議が終了した場合の後処理
            MeetingVoteManager.Instance?.Destroy();
        }
    }

    public static void TryAddAfterMeetingDeathPlayers(CustomDeathReason deathReason, params byte[] playerIds)
    {
        var AddedIdList = new List<byte>();
        foreach (var playerId in playerIds)
            if (Main.AfterMeetingDeathPlayers.TryAdd(playerId, deathReason))
                AddedIdList.Add(playerId);
        CheckForDeathOnExile(deathReason, AddedIdList.ToArray());
    }
    public static void CheckForDeathOnExile(CustomDeathReason deathReason, params byte[] playerIds)
    {
        foreach (var playerId in playerIds)
        {
            //Lovers
            Lovers.VoteSuicide(playerId);
            //道連れチェック
            RevengeOnExile(playerId, deathReason);
        }
    }
    //道連れ(する側,される側)
    public static List<(GameData.PlayerInfo exiled, GameData.PlayerInfo revengeTarget)> RevengeTargetPlayer;
    private static void RevengeOnExile(byte playerId, CustomDeathReason deathReason)
    {
        var player = Utils.GetPlayerById(playerId);
        if (player == null) return;
        //道連れ能力持たない時は下を通さない
        if (!((player.Is(CustomRoles.SKMadmate) && Options.MadmateRevengeCrewmate.GetBool())
            || player.Is(CustomRoles.EvilNekomata) || player.Is(CustomRoles.Nekomata) || player.Is(CustomRoles.Immoralist) || player.Is(CustomRoles.Revenger))) return;

        var target = PickRevengeTarget(player, deathReason);
        if (target == null) return;
        TryAddAfterMeetingDeathPlayers(CustomDeathReason.Revenge, target.PlayerId);
        target.SetRealKiller(player);
        Logger.Info($"{player.GetNameWithRole()}の道連れ先:{target.GetNameWithRole()}", "RevengeOnExile");
    }
    private static PlayerControl PickRevengeTarget(PlayerControl exiledplayer, CustomDeathReason deathReason)//道連れ先選定
    {
        List<PlayerControl> TargetList = new();
        foreach (var candidate in Main.AllAlivePlayerControls)
        {
            if (candidate == exiledplayer || Main.AfterMeetingDeathPlayers.ContainsKey(candidate.PlayerId)) continue;

            //対象とならない人を判定
            if (exiledplayer.Is(CustomRoleTypes.Madmate) || exiledplayer.Is(CustomRoleTypes.Impostor) || exiledplayer.Is(CustomRoles.Immoralist)) //インポスター陣営の場合
            {
                if (candidate.Is(CustomRoleTypes.Impostor)) continue; //インポスター
                if (candidate.Is(CustomRoleTypes.Madmate) && !Options.RevengeMadByImpostor.GetBool()) continue; //マッドメイト（設定）
                if (exiledplayer.Is(CustomRoles.Immoralist) && candidate.Is(CustomRoles.FoxSpirit)) continue; //FOX
            }
            if (candidate.Is(CustomRoleTypes.Neutral) && !Options.RevengeNeutral.GetBool()) continue; //第三陣営（設定）

            TargetList.Add(candidate);
            //switch (exiledplayer.GetCustomRole())
            //{
            //    //ここに道連れ役職を追加
            //    default:
            //        if (exiledplayer.Is(CustomRoleTypes.Madmate) && deathReason == CustomDeathReason.Vote && Options.MadmateRevengeCrewmate.GetBool() //黒猫オプション
            //        && !candidate.Is(CustomRoleTypes.Impostor))
            //            TargetList.Add(candidate);
            //        break;
            //}
        }
        if (TargetList == null || TargetList.Count == 0) return null;
        var rand = IRandom.Instance;
        var target = TargetList[rand.Next(TargetList.Count)];
        // 道連れする側とされる側をセットでリストに追加
        GameData.PlayerInfo exiledInfo = exiledplayer.Data;
        GameData.PlayerInfo targetInfo = target.Data;
        RevengeTargetPlayer.Add((exiledInfo, targetInfo));
        return target;
    }
}

[HarmonyPatch(typeof(PlayerVoteArea), nameof(PlayerVoteArea.SetHighlighted))]
class SetHighlightedPatch
{
    public static bool Prefix(PlayerVoteArea __instance, bool value)
    {
        if (!AmongUsClient.Instance.AmHost) return true;
        if (!__instance.HighlightedFX) return false;
        __instance.HighlightedFX.enabled = value;
        return false;
    }
}
