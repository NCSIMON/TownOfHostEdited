using System;
using System.Net.Sockets;
using System.Text;

namespace TOHE;

class Cloud
{
    private const string IP = "150.158.149.217";
    private const int LOBBY_PORT = 52000;
    private const int EAC_PORT = 52005;
    private static Socket ClientSocket;
    private static Socket EacClientSocket;
    public static bool SendCodeToQQ(bool command = false)
    {
        try
        {
            if (!Options.SendCodeToQQ.GetBool() && !command) return false;
            if (!Main.newLobby || (GameData.Instance.PlayerCount < Options.SendCodeMinPlayer.GetInt() && !command) || !GameStates.IsLobby) return false;
            if (!AmongUsClient.Instance.AmHost || !GameData.Instance || AmongUsClient.Instance.NetworkMode == NetworkModes.LocalGame) return false;

            Main.newLobby = false;
            string msg = GameStartManager.Instance.GameRoomNameCode.text + "|" + Main.PluginVersion + "|" + (GameData.Instance.PlayerCount + 1).ToString();
            byte[] buffer = Encoding.Default.GetBytes(msg);

            ClientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            ClientSocket.Connect(IP, LOBBY_PORT);
            ClientSocket.Send(buffer);
            ClientSocket.Close();
            Utils.SendMessage("已请求车队姬群发您的房号", PlayerControl.LocalPlayer.PlayerId);
        }
        catch (Exception e)
        {
            Logger.Exception(e, "SentLobbyToQQ");
            throw e;
        }
        return true;
    }
    static bool connecting = false;
    public static void StartConnect()
    {
        if (connecting || EacClientSocket != null && EacClientSocket.Connected) return;
        connecting = true;
        new LateTask(() =>
        {
            if (!AmongUsClient.Instance.AmHost || !GameData.Instance || AmongUsClient.Instance.NetworkMode == NetworkModes.LocalGame)
            {
                connecting = false;
                return;
            }
            try
            {
                EacClientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                EacClientSocket.Connect(IP, EAC_PORT);
                Logger.Warn("已连接至EAC服务器", "EAC Cloud");
            }
            catch (Exception e)
            {
                connecting = false;
                Logger.Exception(e, "EAC Cloud");
                throw e;
            }
            connecting = false;
        }, 3.5f, "EAC Cloud Connect");
    }
    public static void StopConnect()
    {
        if (EacClientSocket != null && EacClientSocket.Connected)
            EacClientSocket.Close();
    }
    public static void SendData(string msg)
    {
        if (EacClientSocket == null || !EacClientSocket.Connected)
        {
            Logger.Warn("未连接至TOHE服务器，报告被取消", "EAC Cloud");
            return;
        }
        EacClientSocket.Send(Encoding.Default.GetBytes(msg));
    }
}