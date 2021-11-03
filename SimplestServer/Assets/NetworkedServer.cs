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

    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        account_list_ = new LinkedList<PlayerAccount>();
    }

    // Update is called once per frame
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
        GlobalEnum.ClientToServerSignifier signifier = (GlobalEnum.ClientToServerSignifier)System.Enum.Parse(typeof(GlobalEnum.ClientToServerSignifier), csv[0]);

        switch (signifier)
        {
            case GlobalEnum.ClientToServerSignifier.CreateAccount:
                Debug.Log("GlobalEnum.ClientToServerSignifier.CreateAccount");
                string n = csv[1];
                string p = csv[2];
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
                    SendMessageToClient(GlobalEnum.ServerToClientSignifier.AccountCreationFailed + "", id);
                }
                else
                {
                    PlayerAccount new_account = new PlayerAccount(n, p);
                    account_list_.AddLast(new_account);
                    SendMessageToClient(GlobalEnum.ServerToClientSignifier.AccountCreationComplete + "", id);
                }
                break;
            case GlobalEnum.ClientToServerSignifier.Login:
                Debug.Log("GlobalEnum.ClientToServerSignifier.Login");
                break;
            default:
                break;
        }
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

public static class GlobalEnum //copied from NetworkedClient
{
    public enum ClientToServerSignifier
    {
        CreateAccount = 1,
        Login
    }

    public enum ServerToClientSignifier
    {
        LoginComplete = 1,
        LoginFailed,
        AccountCreationComplete,
        AccountCreationFailed
    }
}