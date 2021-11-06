using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;

    LinkedList<PlayerAccount> account_list_;
    const int kPlayerAccountNameAndPassword = 1;
    string accounts_file_path_;
    int player_id_waiting_for_match_ = -1;
    LinkedList<GameRoom> room_list_;

    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        account_list_ = new LinkedList<PlayerAccount>();
        accounts_file_path_ = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccounts.txt";
        LoadPlayerAccounts();

        //foreach (PlayerAccount item in account_list_)
        //{

        //}
        room_list_ = new LinkedList<GameRoom>();
    }

    void Update()
    {
        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessReceivedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }
    }
  
    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }
    
    private void ProcessReceivedMsg(string msg, int id)
    {
        Debug.Log("msg received = " + msg + ".  connection id = " + id);
        string[] csv = msg.Split(',');
        NetworkEnum.ClientToServerSignifier signifier = (NetworkEnum.ClientToServerSignifier)System.Enum.Parse(typeof(NetworkEnum.ClientToServerSignifier), csv[0]);
        string n = csv[1];
        string p = csv[2];

        switch (signifier)
        {
            case NetworkEnum.ClientToServerSignifier.CreateAccount:
                Debug.Log(">>> Creating Account...");
                bool does_name_exist = false;
                foreach (PlayerAccount item in account_list_)
                {
                    if (item.name == n)
                    {
                        does_name_exist = true;
                        break;
                    }
                }
                if (does_name_exist)
                {
                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.AccountCreationFailed + "", id);
                    Debug.Log(">>> Creating Account FAILED!");
                }
                else
                {
                    PlayerAccount new_account = new PlayerAccount(n, p);
                    account_list_.AddLast(new_account);
                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.AccountCreationComplete + "", id);
                    SavePlayerAccounts();
                    Debug.Log(">>> Creating Account done!");
                }
                break;
            case NetworkEnum.ClientToServerSignifier.Login:
                Debug.Log(">>> Logging in...");
                bool does_account_exist = false;
                foreach (PlayerAccount item in account_list_)
                {
                    if (item.name == n && item.password == p)
                    {
                        does_account_exist = true;
                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.LoginComplete + "", id);
                        Debug.Log(">>> Login done!");
                        break;
                    }
                }
                if (!does_account_exist)
                {
                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.LoginFailed + "", id);
                    Debug.Log(">>> Login FAILED!");
                }
                break;
            case NetworkEnum.ClientToServerSignifier.JoinQueueForGameRoom:
                Debug.Log(">>> Getting player to queue...");
                if (player_id_waiting_for_match_ == -1) //assign 1st player to player_id_waiting_for_match_
                {
                    player_id_waiting_for_match_ = id;
                }
                else //create room when 2nd player joins
                {
                    GameRoom gr = new GameRoom(player_id_waiting_for_match_, id);
                    room_list_.AddLast(gr);
                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameStart + "", gr.player_id_1);
                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameStart + "", gr.player_id_2);
                    player_id_waiting_for_match_ = -1;
                }
                break;
            case NetworkEnum.ClientToServerSignifier.TTTPlay:
                GameRoom gr = GetGameRoomWithClientId(id);
                if (gr != null)
                {
                    if (gr.player_id_1 == id)
                    {
                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.OpponentPlay + "", gr.player_id_2);
                    } 
                    else
                    {
                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.OpponentPlay + "", gr.player_id_1);
                    }
                }
                break;
            default:
                break;
        }
    }

    private void SavePlayerAccounts()
    {
        StreamWriter sw = new StreamWriter(accounts_file_path_);
        foreach (PlayerAccount item in account_list_)
        {
            sw.WriteLine(kPlayerAccountNameAndPassword + "," + item.name + "," + item.password);
        }
        sw.Close();
    }

    private void LoadPlayerAccounts()
    {
        if (File.Exists(accounts_file_path_))
        {
            StreamReader sr = new StreamReader(accounts_file_path_);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');
                int signifier = int.Parse(csv[0]);
                if (signifier == kPlayerAccountNameAndPassword)
                {
                    account_list_.AddLast(new PlayerAccount(csv[1], csv[2]));
                }
            }
            sr.Close();
        }
    }

    private GameRoom GetGameRoomWithClientId(int id)
    {
        foreach (GameRoom item in room_list_)
        {
            if (item.player_id_1 == id || item.player_id_2 == id)
            {
                return item;
            }
        }
        return null;
    }
}

public class PlayerAccount
{
    public string name, password;

    public PlayerAccount(string name, string password)
    {
        this.name = name;
        this.password = password;
    }
}

public class GameRoom
{
    public int player_id_1, player_id_2;

    public GameRoom(int id_1, int id_2)
    {
        player_id_1 = id_1;
        player_id_2 = id_2;
    }
}

public static class NetworkEnum //copied from NetworkedClient
{
    public enum ClientToServerSignifier
    {
        CreateAccount = 1,
        Login,
        JoinQueueForGameRoom,
        TTTPlay
    }

    public enum ServerToClientSignifier
    {
        LoginComplete = 1,
        LoginFailed,
        AccountCreationComplete,
        AccountCreationFailed,
        OpponentPlay,
        GameStart
    }
}