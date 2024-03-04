using AmongUs.GameOptions;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Class;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Roles.Impostor;
public sealed class EvilGuesser : VoteGuesser, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(EvilGuesser),
            player => new EvilGuesser(player),
            CustomRoles.EvilGuesser,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            //(int)Options.offsetId.ImpY + 1400,
            (int)Options.offsetId.ImpSpecial + 0,
            SetupOptionItem,
            "イビルゲッサー"
        );
    public EvilGuesser(PlayerControl player)
    : base(
        RoleInfo,
        player)
    {
        NumOfGuess = OptionNumOfGuess.GetInt();
        MultipleInMeeting = OptionMultipleInMeeting.GetBool();
        HideMisfire = OptionHideMisfire.GetBool();
    }
    private static OptionItem OptionNumOfGuess;
    private static OptionItem OptionMultipleInMeeting;
    private static OptionItem OptionHideMisfire;
    enum OptionName
    {
        GuesserNumOfGuess,
        GuesserMultipleInMeeting,
        GuesserHideMisfire,
    }
    public static void SetupOptionItem()
    {
        OptionNumOfGuess = IntegerOptionItem.Create(RoleInfo, 10, OptionName.GuesserNumOfGuess, new(1, 15, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
        OptionMultipleInMeeting = BooleanOptionItem.Create(RoleInfo, 11, OptionName.GuesserMultipleInMeeting, false, false);
        OptionHideMisfire = BooleanOptionItem.Create(RoleInfo, 12, OptionName.GuesserHideMisfire, false, false);
    }
}