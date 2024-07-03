using AmongUs.GameOptions;

using TownOfHostY.Modules;
using TownOfHostY.Roles.Core;

namespace TownOfHostY.Roles.Madmate;

public sealed class MadDictator : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(MadDictator),
            player => new MadDictator(player),
            CustomRoles.MadDictator,
            () => OptionCanVent.GetBool() ? RoleTypes.Engineer : RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            (int)Options.offsetId.MadY + 0,
            SetupOptionItem,
            "マッドディクテーター",
            introSound: () => GetIntroSound(RoleTypes.Impostor)
        );
    public MadDictator(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        canVent = OptionCanVent.GetBool();
    }

    private static OptionItem OptionCanVent;
    private static bool canVent;

    private static void SetupOptionItem()
    {
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, 10, GeneralOption.CanVent, false, false);
        Options.SetUpAddOnOptions(RoleInfo.ConfigId + 20, RoleInfo.RoleName, RoleInfo.Tab);
    }
    public override (byte? votedForId, int? numVotes, bool doVote) ModifyVote(byte voterId, byte sourceVotedForId, bool isIntentional)
    {
        // 既定値
        var (votedForId, numVotes, doVote) = base.ModifyVote(voterId, sourceVotedForId, isIntentional);
        var baseVote = (votedForId, numVotes, doVote);
        //死んでいないディクテーターが投票済み
        if (voterId != Player.PlayerId || sourceVotedForId == Player.PlayerId || sourceVotedForId >= 253 || !Player.IsAlive())
        {
            return baseVote;
        }
        MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.Suicide, Player.PlayerId);
        Utils.GetPlayerById(sourceVotedForId).SetRealKiller(Player);
        MeetingVoteManager.Instance.ClearAndExile(Player.PlayerId, sourceVotedForId);
        return (votedForId, numVotes, false);
    }

    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        if (!isForMeeting || !Player.IsAlive()) return string.Empty;

        //seenが省略の場合seer
        seen ??= seer;
        //seeおよびseenが自分である場合以外は関係なし
        if (!Is(seer) || !Is(seen)) return "";

        return Translator.GetString("DictatorVote").Color(RoleInfo.RoleColor);
    }
}