using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.AddOns.Crewmate;
using TownOfHostY.Roles.Crewmate;
using System.Linq;
using TownOfHostY.Roles.Neutral;

namespace TownOfHostY
{
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.AddTasksFromList))]
    class AddTasksFromListPatch
    {
        public static void Prefix(ShipStatus __instance,
            [HarmonyArgument(4)] Il2CppSystem.Collections.Generic.List<NormalPlayerTask> unusedTasks)
        {
            if (!AmongUsClient.Instance.AmHost) return;

            if (!Options.DisableTasks.GetBool()) return;
            List<NormalPlayerTask> disabledTasks = new();
            for (var i = 0; i < unusedTasks.Count; i++)
            {
                var task = unusedTasks[i];
                if (task.TaskType == TaskTypes.SwipeCard && Options.DisableSwipeCard.GetBool()) disabledTasks.Add(task);//カードタスク
                if (task.TaskType == TaskTypes.SubmitScan && Options.DisableSubmitScan.GetBool()) disabledTasks.Add(task);//スキャンタスク
                if (task.TaskType == TaskTypes.UnlockSafe && Options.DisableUnlockSafe.GetBool()) disabledTasks.Add(task);//金庫タスク
                if (task.TaskType == TaskTypes.UploadData && Options.DisableUploadData.GetBool()) disabledTasks.Add(task);//アップロードタスク
                if (task.TaskType == TaskTypes.StartReactor && Options.DisableStartReactor.GetBool()) disabledTasks.Add(task);//リアクターの3x3タスク
                if (task.TaskType == TaskTypes.ResetBreakers && Options.DisableResetBreaker.GetBool()) disabledTasks.Add(task);//ブレーカータスク

                if (task.TaskType == TaskTypes.RewindTapes && Options.DisableRewindTapes.GetBool()) disabledTasks.Add(task);//テープタスク
                if (task.TaskType == TaskTypes.VentCleaning && Options.DisableVentCleaning.GetBool()) disabledTasks.Add(task);//ベントタスク
                if (task.TaskType == TaskTypes.BuildSandcastle && Options.DisableBuildSandcastle.GetBool()) disabledTasks.Add(task);//砂の城タスク
                if (task.TaskType == TaskTypes.TestFrisbee && Options.DisableTestFrisbee.GetBool()) disabledTasks.Add(task);//フリスビータスク
                if (task.TaskType == TaskTypes.WaterPlants && Options.DisableWaterPlants.GetBool()) disabledTasks.Add(task);//水やりタスク
                if (task.TaskType == TaskTypes.CatchFish && Options.DisableCatchFish.GetBool()) disabledTasks.Add(task);//魚釣りタスク
                if (task.TaskType == TaskTypes.HelpCritter && Options.DisableHelpCritter.GetBool()) disabledTasks.Add(task);//卵孵化タスク
                if (task.TaskType == TaskTypes.TuneRadio && Options.DisableTuneRadio.GetBool()) disabledTasks.Add(task);//通信修復タスク
                if (task.TaskType == TaskTypes.AssembleArtifact && Options.DisableAssembleArtifact.GetBool()) disabledTasks.Add(task);//宝石組み立てタスク
            }
            foreach (var task in disabledTasks)
            {
                Logger.Msg("削除: " + task.TaskType.ToString(), "AddTask");
                unusedTasks.Remove(task);
            }
        }
    }

    [HarmonyPatch(typeof(NetworkedPlayerInfo), nameof(NetworkedPlayerInfo.RpcSetTasks))]
    class RpcSetTasksPatch
    {
        //タスクを割り当ててRPCを送る処理が行われる直前にタスクを上書きするPatch
        //バニラのタスク割り当て処理自体には干渉しない
        public static void Prefix(NetworkedPlayerInfo __instance,
        [HarmonyArgument(0)] ref Il2CppStructArray<byte> taskTypeIds)
        {
            //null対策
            if (Main.RealOptionsData == null)
            {
                Logger.Warn("警告:RealOptionsDataがnullです。", "RpcSetTasksPatch");
                return;
            }

            var pc = Utils.GetPlayerById(__instance.PlayerId);
            CustomRoles? RoleNullable = pc?.GetCustomRole();
            if (RoleNullable == null) return;
            CustomRoles role = RoleNullable.Value;

            //デフォルトのタスク数
            bool hasCommonTasks = true;
            int NumLongTasks = Main.NormalOptions.NumLongTasks;
            int NumShortTasks = Main.NormalOptions.NumShortTasks;

            if (Options.OverrideTasksData.AllData.TryGetValue(role, out var data) && data.doOverride.GetBool())
            {
                hasCommonTasks = data.assignCommonTasks.GetBool(); // コモンタスク(通常タスク)を割り当てるかどうか
                                                                   // 割り当てる場合でも再割り当てはされず、他のクルーと同じコモンタスクが割り当てられる。
                NumLongTasks = data.numLongTasks.GetInt(); // 割り当てるロングタスクの数
                NumShortTasks = data.numShortTasks.GetInt(); // 割り当てるショートタスクの数
                                                             // ロングとショートは常時再割り当てが行われる。
            }

            if (pc.Is(CustomRoles.VentManager))
                (hasCommonTasks, NumLongTasks, NumShortTasks) = VentManager.TaskData;
            if (pc.Is(CustomRoles.FoxSpirit))
                (hasCommonTasks, NumLongTasks, NumShortTasks) = FoxSpirit.TaskData;
            if (pc.Is(CustomRoles.Workhorse))
                (hasCommonTasks, NumLongTasks, NumShortTasks) = Workhorse.TaskData;
            if (pc.Is(CustomRoles.Rabbit) && Rabbit.IsFinish(pc))
                (hasCommonTasks, NumLongTasks, NumShortTasks) = Rabbit.TaskData;

            if (taskTypeIds.Count == 0) hasCommonTasks = false; //タスク再配布時はコモンを0に
            if (!hasCommonTasks && NumLongTasks == 0 && NumShortTasks == 0) NumShortTasks = 1; //タスク0対策
            if (!pc.Is(CustomRoles.VentManager) && !pc.Is(CustomRoles.FoxSpirit)
                && hasCommonTasks && NumLongTasks == Main.NormalOptions.NumLongTasks && NumShortTasks == Main.NormalOptions.NumShortTasks) return; //変更点がない場合

            //割り当て可能なタスクのIDが入ったリスト
            //本来のRpcSetTasksの第二引数のクローン
            Il2CppSystem.Collections.Generic.List<byte> TasksList = new();
            foreach (var num in taskTypeIds)
                TasksList.Add(num);

            //参考:ShipStatus.Begin
            //不要な割り当て済みのタスクを削除する処理
            //コモンタスクを割り当てる設定ならコモンタスク以外を削除
            //コモンタスクを割り当てない設定ならリストを空にする
            int defaultCommonTasksNum = Main.RealOptionsData.GetInt(Int32OptionNames.NumCommonTasks);
            if (hasCommonTasks) TasksList.RemoveRange(defaultCommonTasksNum, TasksList.Count - defaultCommonTasksNum);
            else TasksList.Clear();

            //割り当て済みのタスクが入れられるHashSet
            //同じタスクが複数割り当てられるのを防ぐ
            Il2CppSystem.Collections.Generic.HashSet<TaskTypes> usedTaskTypes = new();
            int start2 = 0;
            int start3 = 0;

            //割り当て可能なロングタスクのリスト
            Il2CppSystem.Collections.Generic.List<NormalPlayerTask> LongTasks = new();
            foreach (var task in ShipStatus.Instance.LongTasks)
                LongTasks.Add(task);
            Shuffle<NormalPlayerTask>(LongTasks);

            //割り当て可能なショートタスクのリスト
            Il2CppSystem.Collections.Generic.List<NormalPlayerTask> ShortTasks = new();
            foreach (var task in ShipStatus.Instance.ShortTasks)
                ShortTasks.Add(task);
            Shuffle<NormalPlayerTask>(ShortTasks);

            if (pc.Is(CustomRoles.VentManager) || pc.Is(CustomRoles.FoxSpirit))
            {
                TasksList.Clear();
                ShortTasks.Clear();

                var task = ShipStatus.Instance.ShortTasks.FirstOrDefault(task => task.TaskType == TaskTypes.VentCleaning);
                ShortTasks.Add(task);
            }

            //実際にAmong Us側で使われているタスクを割り当てる関数を使う。
            ShipStatus.Instance.AddTasksFromList(
                ref start2,
                NumLongTasks,
                TasksList,
                usedTaskTypes,
                LongTasks
            );
            ShipStatus.Instance.AddTasksFromList(
                ref start3,
                NumShortTasks,
                TasksList,
                usedTaskTypes,
                ShortTasks
            );

            //タスクのリストを配列(Il2CppStructArray)に変換する
            taskTypeIds = new Il2CppStructArray<byte>(TasksList.Count);
            for (int i = 0; i < TasksList.Count; i++)
            {
                taskTypeIds[i] = TasksList[i];
            }
        }
        public static void Shuffle<T>(Il2CppSystem.Collections.Generic.List<T> list)
        {
            for (int i = 0; i < list.Count - 1; i++)
            {
                T obj = list[i];
                int rand = IRandom.Instance.Next(i, list.Count);
                list[i] = list[rand];
                list[rand] = obj;
            }
        }
    }
}