using System.Globalization;
using HarmonyLib;
using InnerNet;
using Hazel;
using UnityEngine;
using TownOfHostY.Modules;
using static TownOfHostY.Translator;

namespace TownOfHostY;

class CannotUsePublicRoom
{
    public static string ShowMessage()
    {
        var message = "";
        if (!Main.AllowPublicRoom) message = GetString("DisabledByProgram");
        else if (!Main.IsPublicAvailableOnThisVersion) message = GetString("PublicNotAvailableOnThisVersion");
        else if (ModUpdater.hasUpdate) message = GetString("CanNotJoinPublicRoomNoLatest");
        else if (!VersionChecker.IsSupported) message = GetString("UnsupportedVersion");
        return message;
    }
}

[HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.MakePublic))]
class MakePublicPatch
{
    public static bool Prefix(GameStartManager __instance)
    {
        // 定数設定による公開ルームブロック
        if (!Main.AllowPublicRoom || ModUpdater.hasUpdate || !VersionChecker.IsSupported || !Main.IsPublicAvailableOnThisVersion)
        {
            var message = CannotUsePublicRoom.ShowMessage();
            Logger.Info(message, "MakePublicPatch");
            Logger.SendInGame(message);
            return false;
        }

        return true;
    }
}
[HarmonyPatch(typeof(MMOnlineManager), nameof(MMOnlineManager.Start))]
class MMOnlineManagerStartPatch
{
    public static void Postfix(MMOnlineManager __instance)
    {
        if (VersionChecker.IsSupported && Main.AllowPublicRoom && Main.IsPublicAvailableOnThisVersion) return;

        var objF = GameObject.Find("Buttons/FindGameButton");
        if (objF)
        {
            objF?.SetActive(false);
        }
        var objJ = GameObject.Find("Buttons/JoinGameButton");
        if (objJ)
        {
            objJ?.SetActive(false);
        }

        var objmenu = GameObject.Find("NormalMenu");
        if (objmenu)
        {
            var objW = new GameObject("ModWarning");
            objW.transform.SetParent(objmenu.transform);
            objW.transform.localPosition = new Vector3(0f, -0.7f, 0f);
            objW.transform.localScale = new Vector3(2f, 2f, 2f);
            var renderer = objW.AddComponent<SpriteRenderer>();
            renderer.sprite = Utils.LoadSprite($"TownOfHost_Y.Resources.warning_online.png", 400f);
        }
    }
}
[HarmonyPatch(typeof(SplashManager), nameof(SplashManager.Update))]
class SplashLogoAnimatorPatch
{
    public static void Prefix(SplashManager __instance)
    {
        if (DebugModeManager.AmDebugger)
        {
            __instance.sceneChanger.AllowFinishLoadingScene();
            __instance.startedSceneLoad = true;
        }
    }
}
[HarmonyPatch(typeof(EOSManager), nameof(EOSManager.IsAllowedOnline))]
class RunLoginPatch
{
    public static void Prefix(ref bool canOnline)
    {
#if DEBUG
        if (CultureInfo.CurrentCulture.Name != "ja-JP") canOnline = false;
#endif
    }
}
[HarmonyPatch(typeof(BanMenu), nameof(BanMenu.SetVisible))]
class BanMenuSetVisiblePatch
{
    public static bool Prefix(BanMenu __instance, bool show)
    {
        if (!AmongUsClient.Instance.AmHost) return true;
        show &= PlayerControl.LocalPlayer && PlayerControl.LocalPlayer.Data != null;
        __instance.BanButton.gameObject.SetActive(AmongUsClient.Instance.CanBan());
        __instance.KickButton.gameObject.SetActive(AmongUsClient.Instance.CanKick());
        __instance.MenuButton.gameObject.SetActive(show);
        return false;
    }
}
[HarmonyPatch(typeof(InnerNet.InnerNetClient), nameof(InnerNet.InnerNetClient.CanBan))]
class InnerNetClientCanBanPatch
{
    public static bool Prefix(InnerNet.InnerNetClient __instance, ref bool __result)
    {
        __result = __instance.AmHost;
        return false;
    }
}
[HarmonyPatch(typeof(InnerNet.InnerNetClient), nameof(InnerNet.InnerNetClient.KickPlayer))]
class KickPlayerPatch
{
    public static void Prefix(InnerNet.InnerNetClient __instance, int clientId, bool ban)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (ban) BanManager.AddBanPlayer(AmongUsClient.Instance.GetRecentClient(clientId));
    }
}
[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.SendAllStreamedObjects))]
class InnerNetObjectSerializePatch
{
    public static bool Prefix(InnerNetClient __instance, ref bool __result)
    {
        if (AmongUsClient.Instance.AmHost)
            GameOptionsSender.SendAllGameOptions();

        //9人以上部屋で落ちる現象の対策コード
        __result = false;
        var obj = __instance.allObjects;
        lock (obj)
        {
            for (int i = 0; i < __instance.allObjects.Count; i++)
            {
                InnerNetObject innerNetObject = __instance.allObjects[i];
                if (innerNetObject && innerNetObject.IsDirty && (innerNetObject.AmOwner || (innerNetObject.OwnerId == -2 && __instance.AmHost)))
                {
                    var messageWriter = MessageWriter.Get(SendOption.Reliable);
                    messageWriter.StartMessage(5);
                    messageWriter.Write(__instance.GameId);
                    messageWriter.StartMessage(1);
                    messageWriter.WritePacked(innerNetObject.NetId);
                    try
                    {
                        if (innerNetObject.Serialize(messageWriter, false))
                        {
                            messageWriter.EndMessage();
                        }
                        else
                        {
                            messageWriter.CancelMessage();
                        }
                        if (innerNetObject.Chunked && innerNetObject.IsDirty)
                        {
                            __result = true;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Logger.Info($"Exception:{ex.Message}", "InnerNetClient");
                        messageWriter.CancelMessage();
                    }
                    if (messageWriter.HasBytes(7))
                    {
                        messageWriter.EndMessage();
                        if (DebugModeManager.IsDebugMode)
                        {
                            Logger.Info($"SendAllStreamedObjects", "InnerNetClient");
                        }
                        __instance.SendOrDisconnect(messageWriter);
                    }
                    messageWriter.Recycle();
                }
            }
        }
        return false;
    }
    [HarmonyPatch]
    class InnerNetClientPatch
    {
        [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.SendOrDisconnect)), HarmonyPrefix]
        public static void SendOrDisconnectPatch(InnerNetClient __instance, MessageWriter msg)
        {
            if (msg.Length > 1000)
                Logger.Info($"SendOrDisconnectPatch:Large Packet({msg.Length})", "InnerNetClient");
        }
        [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.SendInitialData)), HarmonyPrefix]
        public static bool SendInitialDataPatch(InnerNetClient __instance, int clientId)
        {
            Logger.Info($"SendInitialDataPatch:", "InnerNetClient");
            var obj = __instance.allObjects;

            lock (obj)
            {
                var hashSet = new System.Collections.Generic.HashSet<GameObject>();
                //SendGameManagerの代替。初めに発行する必要があるためここへ。
                WriteSpawnMessageEx(__instance, GameManager.Instance, GameManager.Instance.OwnerId, GameManager.Instance.SpawnFlags, clientId);
                hashSet.Add(GameManager.Instance.gameObject);

                for (int i = 0; i < __instance.allObjects.Count; i++)
                {
                    var innerNetObject = __instance.allObjects[i];
                    if (innerNetObject && (innerNetObject.OwnerId != -4 || __instance.AmModdedHost) && hashSet.Add(innerNetObject.gameObject))
                    {
                        WriteSpawnMessageEx(__instance, innerNetObject, innerNetObject.OwnerId, innerNetObject.SpawnFlags, clientId);
                    }
                }
            }
            return false;
        }
        [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.Spawn)), HarmonyPrefix]
        public static bool SpawnPatch(InnerNetClient __instance, InnerNetObject netObjParent, int ownerId, SpawnFlags flags)
        {
            Logger.Info($"SpawnPatch", "InnerNetClient");
            if (__instance.AmHost)
            {
                ownerId = (ownerId == -3) ? __instance.ClientId : ownerId;
                WriteSpawnMessageEx(__instance, netObjParent, ownerId, flags);
            }
            return false;
        }
        /// <summary>
        /// WriteSpawnMessageを単一パケットにまとめて発行する。
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="netObjParent"></param>
        /// <param name="ownerId"></param>
        /// <param name="flags"></param>
        /// <param name="clientId"></param>
        public static void WriteSpawnMessageEx(InnerNetClient __instance, InnerNetObject netObjParent, int ownerId, SpawnFlags flags, int clientId = -1)
        {
            Logger.Info($"WriteSpawnMessageEx", "InnerNetClient");

            InnerNetObject[] componentsInChildren = netObjParent.GetComponentsInChildren<InnerNetObject>();
            var msg = MessageWriter.Get(SendOption.Reliable);
            if (clientId == -1)
            {
                msg.StartMessage(5);
                msg.Write(__instance.GameId);
            }
            else
            {
                msg.StartMessage(6);
                msg.Write(__instance.GameId);
                msg.Write(clientId);
            }
            {
                msg.StartMessage(4);
                {
                    msg.WritePacked(netObjParent.SpawnId);
                    msg.WritePacked(ownerId);
                    msg.Write((byte)flags);
                    msg.WritePacked(componentsInChildren.Length);
                    foreach (InnerNetObject innerNetObject in componentsInChildren)
                    {
                        innerNetObject.OwnerId = ownerId;
                        innerNetObject.SpawnFlags = flags;
                        if (innerNetObject.NetId == 0U)
                        {
                            InnerNetObject innerNetObject2 = innerNetObject;
                            uint netIdCnt = __instance.NetIdCnt;
                            __instance.NetIdCnt = netIdCnt + 1U;
                            innerNetObject2.NetId = netIdCnt;
                            __instance.allObjects.Add(innerNetObject);
                            __instance.allObjectsFast.Add(innerNetObject.NetId, innerNetObject);
                        }
                        msg.WritePacked(innerNetObject.NetId);
                        msg.StartMessage(1);
                        {
                            innerNetObject.Serialize(msg, true);
                        }
                        msg.EndMessage();
                    }
                    msg.EndMessage();
                }
                msg.EndMessage();
            }
            __instance.SendOrDisconnect(msg);
            msg.Recycle();
        }
    }
}