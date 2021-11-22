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

    // OBSERVER VARS
    LinkedList<int> observer_list_;

    void Awake()
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
        observer_list_ = new LinkedList<int>();
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
        
        switch (signifier)
        {
            case NetworkEnum.ClientToServerSignifier.CreateAccount:
                {
                    Debug.Log(">>> Creating Account...");
                    string n = csv[1]; //name to CreateAccount
                    string p = csv[2]; //password to CreateAccount
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
                }
            case NetworkEnum.ClientToServerSignifier.Login:
                { 
                    Debug.Log(">>> Logging in...");
                    string n = csv[1]; //name to Login
                    string p = csv[2]; //password to Login
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
                }
            case NetworkEnum.ClientToServerSignifier.JoinQueueForGameRoom:
                {
                    Debug.Log(">>> Getting player to queue...");
                    if (player_id_waiting_for_match_ == -1) //assign 1st player to player_id_waiting_for_match_
                    {
                        player_id_waiting_for_match_ = id;
                    }
                    else //create room when 2nd player joins
                    {
                        GameRoom gr = GetGameRoomWithClientId(id);
                        if (gr == null)
                        {
                            gr = new GameRoom(player_id_waiting_for_match_, id);
                            room_list_.AddLast(gr);
                        }
                        gr.CleanRoom();
                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameStart + "," + gr.player_1_token + "," + gr.player_2_token, gr.player_id_1);
                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameStart + "," + gr.player_2_token + "," + gr.player_1_token, gr.player_id_2);
                        player_id_waiting_for_match_ = -1;
                        Debug.Log(">>> Created game room with player_id_1: " + gr.player_id_1 + ", player_id_2: " + gr.player_id_2);
                        foreach (var item in observer_list_)
                        {
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameStartForObserver + "," + gr.player_1_token + "," + gr.player_2_token, item);
                        }
                        int first_turn = Random.Range(0, 2);
                        if (first_turn == 0)
                        {
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameDoTurn + "", gr.player_id_1);
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameWaitForTurn + "", gr.player_id_2);
                        }
                        else
                        {
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameWaitForTurn + "", gr.player_id_1);
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameDoTurn + "", gr.player_id_2);
                        }
                    }
                    break;
                }
            case NetworkEnum.ClientToServerSignifier.JoinQueueForGameRoomAsObserver:
                {
                    observer_list_.AddLast(id);
                    break;
                }
            case NetworkEnum.ClientToServerSignifier.TTTPlay:
                {
                    GameRoom gr = GetGameRoomWithClientId(id);
                    if (gr != null)
                    {
                        string x = csv[1];
                        string y = csv[2];
                        int id_cast = (int)GameEnum.TicTacToeButtonState.kBlank;
                        if (gr.player_id_1 == id)
                        {
                            id_cast = (int)GameEnum.TicTacToeButtonState.kPlayer1;
                            gr.grid[int.Parse(x), int.Parse(y)] = (int)GameEnum.TicTacToeButtonState.kPlayer1;
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameMarkSpace + "," + x + "," + y + "," + gr.player_1_token, gr.player_id_1);
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameMarkSpace + "," + x + "," + y + "," + gr.player_1_token, gr.player_id_2);
                            gr.replay_log_.Add(x + "," + y + "," + gr.player_1_token);
                            foreach (var item in observer_list_)
                            {
                                SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameMarkSpace + "," + x + "," + y + "," + gr.player_1_token, item);
                            }
                        }
                        else
                        {
                            id_cast = (int)GameEnum.TicTacToeButtonState.kPlayer2;
                            gr.grid[int.Parse(x), int.Parse(y)] = (int)GameEnum.TicTacToeButtonState.kPlayer2;
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameMarkSpace + "," + x + "," + y + "," + gr.player_2_token, gr.player_id_1);
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameMarkSpace + "," + x + "," + y + "," + gr.player_2_token, gr.player_id_2);
                            gr.replay_log_.Add(x + "," + y + "," + gr.player_2_token);
                            foreach (var item in observer_list_)
                            {
                                SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameMarkSpace + "," + x + "," + y + "," + gr.player_2_token, item);
                            }
                        }
                        switch (gr.CheckGridCoord(id_cast, new Vector2Int(int.Parse(x), int.Parse(y))))
                        {
                            case GameEnum.State.TicTacToeWin:
                                Debug.Log(">>> TicTacToeWin");
                                if (gr.player_id_1 == id)
                                {
                                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameCurrPlayerWin + "", gr.player_id_1);
                                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameOtherPlayerWin + "", gr.player_id_2);
                                    foreach (var item in observer_list_)
                                    {
                                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameCurrPlayerWin + "", item);
                                    }
                                }
                                else
                                {
                                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameOtherPlayerWin + "", gr.player_id_1);
                                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameCurrPlayerWin + "", gr.player_id_2);
                                    foreach (var item in observer_list_)
                                    {
                                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameOtherPlayerWin + "", item);
                                    }
                                }
                                break;
                            case GameEnum.State.TicTacToeDraw:
                                Debug.Log(">>> TicTacToeDraw");
                                SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameDraw + "", gr.player_id_1);
                                SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameDraw + "", gr.player_id_2);
                                foreach (var item in observer_list_)
                                {
                                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameDraw + "", item);
                                }
                                break;
                            case GameEnum.State.TicTacToeNextPlayer:
                                if (gr.player_id_1 == id)
                                {
                                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameWaitForTurn + "", gr.player_id_1);
                                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameDoTurn + "", gr.player_id_2);
                                }
                                else
                                {
                                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameDoTurn + "", gr.player_id_1);
                                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameWaitForTurn + "", gr.player_id_2);
                                }
                                break;
                            default:
                                break;
                        }
                    }
                    break;
                }
            case NetworkEnum.ClientToServerSignifier.ChatSend:
                {
                    Debug.Log(">>> ChatSend");
                    GameRoom gr = GetGameRoomWithClientId(id);
                    if (gr != null)
                    {
                        string str = csv[1];
                        if (csv.Length >1)
                        {
                            for (int i = 2; i < csv.Length; i++)
                            {
                                str = str + "," + csv[i];
                            }
                        }
                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.ChatRelay + ",> [" + id.ToString() + "]:" + str, gr.player_id_1);
                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.ChatRelay + ",> [" + id.ToString() + "]:" + str, gr.player_id_2);
                        Debug.Log(">>> Relayed: " + (int)NetworkEnum.ServerToClientSignifier.ChatRelay + ",> [" + id.ToString() + "]:" + str + " >>> " + gr.player_id_1);
                        Debug.Log(">>> Relayed: " + (int)NetworkEnum.ServerToClientSignifier.ChatRelay + ",> [" + id.ToString() + "]:" + str + " >>> " + gr.player_id_2);
                        foreach (var item in observer_list_)
                        {
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.ChatRelay + ",> [" + id.ToString() + "]:" + str, item);
                        }
                    }
                    break;
                }
            case NetworkEnum.ClientToServerSignifier.DoReplay:
                {
                    Debug.Log(">>> DoReplay");
                    GameRoom gr = GetGameRoomWithClientId(id);
                    if (gr != null)
                    {
                        if (gr.player_id_1 == id)
                        {
                            gr.p1_replay_step_ = 0;
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayRelay + "," + gr.replay_log_[gr.p1_replay_step_], gr.player_id_1);
                            foreach (var item in observer_list_)
                            {
                                SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayRelay + "," + gr.replay_log_[gr.p1_replay_step_], item);
                            }
                            gr.p1_replay_step_++;
                        }
                        else
                        {
                            gr.p2_replay_step_ = 0;
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayRelay + "," + gr.replay_log_[gr.p2_replay_step_], gr.player_id_2);
                            foreach (var item in observer_list_)
                            {
                                SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayRelay + "," + gr.replay_log_[gr.p2_replay_step_], item);
                            }
                            gr.p2_replay_step_++;
                        }
                    }
                    break;
                }
            case NetworkEnum.ClientToServerSignifier.NextReplayMove:
                {
                    Debug.Log(">>> DoReplay");
                    GameRoom gr = GetGameRoomWithClientId(id);
                    if (gr != null)
                    {
                        if (gr.player_id_1 == id)
                        {
                            if (gr.p1_replay_step_ < gr.replay_log_.Count)
                            {
                                SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayRelay + "," + gr.replay_log_[gr.p1_replay_step_], gr.player_id_1);
                                foreach (var item in observer_list_)
                                {
                                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayRelay + "," + gr.replay_log_[gr.p1_replay_step_], item);
                                }
                                gr.p1_replay_step_++;
                            }
                            else
                            {
                                SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayEnd + "", gr.player_id_1);
                                foreach (var item in observer_list_)
                                {
                                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayEnd + "", item);
                                }
                            }
                        }
                        else
                        {
                            if (gr.p2_replay_step_ < gr.replay_log_.Count)
                            {
                                SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayRelay + "," + gr.replay_log_[gr.p2_replay_step_], gr.player_id_2);
                                foreach (var item in observer_list_)
                                {
                                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayRelay + "," + gr.replay_log_[gr.p2_replay_step_], item);
                                }
                                gr.p2_replay_step_++;
                            }
                            else
                            {
                                SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayEnd + "", gr.player_id_2);
                                foreach (var item in observer_list_)
                                {
                                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayEnd + "", item);
                                }
                            }
                        }
                    }
                    break;
                }
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
    
    // GAME VARS
    public int grid_size_x = 3, grid_size_y = 3;
    public int[,] grid;
    public string player_1_token = "X";
    public string player_2_token = "O";
    public int move_count_ = 0;

    // REPLAY VARS
    public List<string> replay_log_;
    public int p1_replay_step_ = 0;
    public int p2_replay_step_ = 0;

    public GameRoom(int id_1, int id_2)
    {
        player_id_1 = id_1;
        player_id_2 = id_2;

        grid = new int[grid_size_x, grid_size_y];
        ResetGrid();

        replay_log_ = new List<string>();
    }

    public void ResetGrid()
    {
        for (int j = 0; j < grid_size_y; j++)
        {
            for (int i = 0; i < grid_size_x; i++)
            {
                grid[i, j] = (int)GameEnum.TicTacToeButtonState.kBlank; //blank state
            }
        }
    }

    public void CleanRoom()
    {
        move_count_ = 0;
        ResetGrid();
        replay_log_.Clear();
    }

    public GameEnum.State CheckGridCoord(int player_id, Vector2Int coord)
    {
        move_count_++;
        // CHECK WITH OTHER COLS
        for (int i = 0; i < grid_size_y; i++)
        {
            if (grid[coord.x, i] != player_id)
                break;
            if (i == grid_size_y - 1)
            {
                //report win for player_id_
                return (GameEnum.State.TicTacToeWin);
            }
        }
        // CHECK WITH OTHER ROWS
        for (int i = 0; i < grid_size_x; i++)
        {
            if (grid[i, coord.y] != player_id)
                break;
            if (i == grid_size_x - 1)
            {
                //report win for player_id_
                return (GameEnum.State.TicTacToeWin);
            }
        }
        // CHECK DIAGONALLY
        if (coord.x == coord.y)
        {
            for (int i = 0; i < grid_size_x; i++)
            {
                if (grid[i, i] != player_id)
                    break;
                if (i == grid_size_x - 1)
                {
                    //report win for player_id_
                    return (GameEnum.State.TicTacToeWin);
                }
            }
        }
        // CHECK REVERSE DIAGONALLY
        if (coord.x + coord.y == grid_size_x - 1)
        {
            for (int i = 0; i < grid_size_x; i++)
            {
                if (grid[i, (grid_size_x - 1) - i] != player_id)
                    break;
                if (i == grid_size_x - 1)
                {
                    //report win for player_id_
                    return (GameEnum.State.TicTacToeWin);
                }
            }
        }
        // CHECK IF IS TIE
        if (move_count_ == (Mathf.Pow(grid_size_x, 2)))
        {
            return (GameEnum.State.TicTacToeDraw);
        }

        return GameEnum.State.TicTacToeNextPlayer;
    }
}

public static class NetworkEnum //copied from NetworkedClient
{
    public enum ClientToServerSignifier
    {
        CreateAccount = 1,
        Login,
        JoinQueueForGameRoom,
        JoinQueueForGameRoomAsObserver,
        TTTPlay,
        ChatSend,
        DoReplay,
        NextReplayMove
    }

    public enum ServerToClientSignifier
    {
        LoginComplete = 1,
        LoginFailed,
        AccountCreationComplete,
        AccountCreationFailed,
        GameStart,
        GameStartForObserver,
        GameDoTurn,
        GameWaitForTurn,
        GameMarkSpace,
        GameDraw,
        GameCurrPlayerWin,
        GameOtherPlayerWin,
        ChatRelay,
        ReplayRelay,
        ReplayEnd
    }
}

public static class GameEnum
{
    public enum State
    {
        LoginMenu = 1,
        MainMenu,
        WaitingInQueueForOtherPlayer,
        TicTacToe,
        TicTacToeNextPlayer,
        TicTacToeWin,
        TicTacToeDraw,
    }

    public enum TicTacToeButtonState
    {
        kBlank = -1,
        kPlayer1,
        kPlayer2
    }
}