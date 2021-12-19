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

    // RECORD VARS
    int last_idx_used_ = 0;
    LinkedList<NameAndIndex> name_idx_list_;
    string idx_file_path_;

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

        name_idx_list_ = new LinkedList<NameAndIndex>();
        idx_file_path_ = Application.dataPath + Path.DirectorySeparatorChar + "GameRecordingIndices.txt";
        LoadIndexManagementFile();

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

                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameStart + "," + GameRoom.player_1_token + "," + GameRoom.player_2_token, gr.player_id_1);
                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameStart + "," + GameRoom.player_2_token + "," + GameRoom.player_1_token, gr.player_id_2);
                        player_id_waiting_for_match_ = -1;
                        Debug.Log(">>> Created game room with player_id_1: " + gr.player_id_1 + ", player_id_2: " + gr.player_id_2);
                        foreach (var item in observer_list_)
                        {
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameStartForObserver + "," + GameRoom.player_1_token + "," + GameRoom.player_2_token, item);
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
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameMarkSpace + "," + x + "," + y + "," + GameRoom.player_1_token, gr.player_id_1);
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameMarkSpace + "," + x + "," + y + "," + GameRoom.player_1_token, gr.player_id_2);
                            //gr.replay_log_.Add(x + "," + y + "," + gr.player_1_token);
                            gr.replay_log_.AddGameMoveWithCurrTime(GameEnum.PlayerTurn.kPlayer1, int.Parse(x), int.Parse(y));
                            foreach (var item in observer_list_)
                            {
                                SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameMarkSpace + "," + x + "," + y + "," + GameRoom.player_1_token, item);
                            }
                        }
                        else
                        {
                            id_cast = (int)GameEnum.TicTacToeButtonState.kPlayer2;
                            gr.grid[int.Parse(x), int.Parse(y)] = (int)GameEnum.TicTacToeButtonState.kPlayer2;
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameMarkSpace + "," + x + "," + y + "," + GameRoom.player_2_token, gr.player_id_1);
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameMarkSpace + "," + x + "," + y + "," + GameRoom.player_2_token, gr.player_id_2);
                            //gr.replay_log_.Add(x + "," + y + "," + gr.player_2_token);
                            gr.replay_log_.AddGameMoveWithCurrTime(GameEnum.PlayerTurn.kPlayer2, int.Parse(x), int.Parse(y));
                            foreach (var item in observer_list_)
                            {
                                SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameMarkSpace + "," + x + "," + y + "," + GameRoom.player_2_token, item);
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
                                SaveGameRecordingToFile(gr);
                                break;
                            case GameEnum.State.TicTacToeDraw:
                                Debug.Log(">>> TicTacToeDraw");
                                SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameDraw + "", gr.player_id_1);
                                SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameDraw + "", gr.player_id_2);
                                foreach (var item in observer_list_)
                                {
                                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.GameDraw + "", item);
                                }
                                SaveGameRecordingToFile(gr);
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
                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.RecordingTransferDataStart + "", id);
                        foreach (string line in LoadGameRecordingFileByIndex(last_idx_used_))
                        {
                            SendMessageToClient(NetworkEnum.ServerToClientSignifier.RecordingTransferData + "," + line, id);
                        }
                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.RecordingTransferDataEnd + "", id);
                    }
                    break;
                }
                //case NetworkEnum.ClientToServerSignifier.NextReplayMove:
                //    {
                //        Debug.Log(">>> DoReplay");
                //        GameRoom gr = GetGameRoomWithClientId(id);
                //        if (gr != null)
                //        {
                //            if (gr.player_id_1 == id)
                //            {
                //                if (gr.p1_replay_step_ < gr.replay_log_.Count)
                //                {
                //                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayRelay + "," + gr.replay_log_[gr.p1_replay_step_], gr.player_id_1);
                //                    foreach (var item in observer_list_)
                //                    {
                //                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayRelay + "," + gr.replay_log_[gr.p1_replay_step_], item);
                //                    }
                //                    gr.p1_replay_step_++;
                //                }
                //                else
                //                {
                //                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayEnd + "", gr.player_id_1);
                //                    foreach (var item in observer_list_)
                //                    {
                //                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayEnd + "", item);
                //                    }
                //                }
                //            }
                //            else
                //            {
                //                if (gr.p2_replay_step_ < gr.replay_log_.Count)
                //                {
                //                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayRelay + "," + gr.replay_log_[gr.p2_replay_step_], gr.player_id_2);
                //                    foreach (var item in observer_list_)
                //                    {
                //                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayRelay + "," + gr.replay_log_[gr.p2_replay_step_], item);
                //                    }
                //                    gr.p2_replay_step_++;
                //                }
                //                else
                //                {
                //                    SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayEnd + "", gr.player_id_2);
                //                    foreach (var item in observer_list_)
                //                    {
                //                        SendMessageToClient(NetworkEnum.ServerToClientSignifier.ReplayEnd + "", item);
                //                    }
                //                }
                //            }
                //        }
                //        break;
                //    }
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

    private void SaveIndexManagementFile()
    {
        StreamWriter sw = new StreamWriter(idx_file_path_);
        sw.WriteLine((int)NameAndIndex.SignifierId.LastUsedIndex + "," + last_idx_used_);
        foreach (NameAndIndex item in name_idx_list_)
        {
            sw.WriteLine((int)NameAndIndex.SignifierId.IndexAndName + "," + item.index + "," + item.name);
        }
        sw.Close();
    }

    private void LoadIndexManagementFile()
    {
        if (File.Exists(idx_file_path_))
        {
            StreamReader sr = new StreamReader(idx_file_path_);

            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');
                int signifier = int.Parse(csv[0]);

                if (signifier == (int)NameAndIndex.SignifierId.LastUsedIndex)
                {
                    last_idx_used_ = int.Parse(csv[1]);
                }
                else if (signifier == (int)NameAndIndex.SignifierId.IndexAndName)
                {
                    name_idx_list_.AddLast(new NameAndIndex(int.Parse(csv[1]), csv[2]));
                }
            }
            sr.Close();
        }
    }

    private void SaveGameRecordingToFile(GameRoom gr)
    {
        last_idx_used_++;
        StreamWriter sw = new StreamWriter(Application.dataPath + Path.DirectorySeparatorChar + last_idx_used_ + ".txt");
        foreach (string data in gr.replay_log_.Serialize())
        {
            sw.WriteLine(data);
        }
        sw.Close();

        name_idx_list_.AddLast(new NameAndIndex(last_idx_used_, gr.player_id_1 + "," + gr.player_id_2));

        Debug.Log(">>> Saving to " + Application.dataPath + Path.DirectorySeparatorChar + last_idx_used_ + ".txt");
        SaveIndexManagementFile();
    }

    private Queue<string> LoadGameRecordingFileByIndex(int file_idx)
    {
        int index_to_load = -1;
        foreach (NameAndIndex item in name_idx_list_)
        {
            if (item.index == file_idx)
            {
                index_to_load = item.index;
                break;
            }
        }

        StreamReader sr = new StreamReader(Application.dataPath + Path.DirectorySeparatorChar + index_to_load + ".txt");
        string line;
        Queue<string> data = new Queue<string>();
        while ((line = sr.ReadLine()) != null)
        {
            data.Enqueue(line);
        }
        sr.Close();
        return data;
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

public class GameRecording
{
    public int player_id_1, player_id_2;
    public int grid_size_x, grid_size_y;
    public System.DateTime start_datetime;
    public Queue<GameMove> game_move_queue;

    public struct GameMove
    {
        public GameEnum.PlayerTurn turn;
        public int grid_coord_x;
        public int grid_coord_y;
        public System.DateTime datetime;

        public GameMove(GameEnum.PlayerTurn turn, int grid_coord_x, int grid_coord_y, System.DateTime datetime)
        {
            this.turn = turn;
            this.grid_coord_x = grid_coord_x;
            this.grid_coord_y = grid_coord_y;
            this.datetime = datetime;
        }
    }

    public GameRecording(int id_1, int id_2, int grid_size_x, int grid_size_y)
    {
        player_id_1 = id_1;
        player_id_2 = id_2;
        this.grid_size_x = grid_size_x;
        this.grid_size_y = grid_size_y;
        start_datetime = System.DateTime.Now;
        game_move_queue = new Queue<GameMove>();
    }

    public void AddGameMoveWithCurrTime(GameEnum.PlayerTurn turn, int grid_coord_x, int grid_coord_y)
    {
        Debug.Log(">>> AddGameMoveWithCurrTime: " + turn + ", " + grid_coord_x + ", " + grid_coord_y);
        Debug.Log(System.DateTime.Now);
        game_move_queue.Enqueue(new GameMove(turn, grid_coord_x, grid_coord_y, System.DateTime.Now));
    }

    public Queue<string> Serialize()
    {
        Queue<string> data = new Queue<string>();
        data.Enqueue((int)GameEnum.RecordDataId.kRoomSettingId + "," + 
            player_id_1 + "," + player_id_2 + "," +
            grid_size_x + "," + grid_size_y + "," +
            start_datetime.Year.ToString() + "," + start_datetime.Month.ToString() + "," + start_datetime.Day.ToString() + "," +
            start_datetime.Hour.ToString() + "," + start_datetime.Minute.ToString() + "," + start_datetime.Second.ToString());
        foreach (GameMove item in game_move_queue)
        {
            data.Enqueue((int)GameEnum.RecordDataId.kMoveDataId + "," +
                (int)item.turn + "," + item.grid_coord_x + "," + item.grid_coord_y + "," +
            item.datetime.Year.ToString() + "," + item.datetime.Month.ToString() + "," + item.datetime.Day.ToString() + "," +
            item.datetime.Hour.ToString() + "," + item.datetime.Minute.ToString() + "," + item.datetime.Second.ToString());
        }
        return data;
    }

    public void Deserialize(Queue<string> data)
    {
        foreach (string line in data)
        {
            string[] csv = line.Split(',');
            GameEnum.RecordDataId record_data_id = (GameEnum.RecordDataId)int.Parse(csv[0]);
            switch (record_data_id)
            {
                case GameEnum.RecordDataId.kRoomSettingId:
                    player_id_1 = int.Parse(csv[1]);
                    player_id_2 = int.Parse(csv[2]);
                    grid_size_x = int.Parse(csv[3]);
                    grid_size_y = int.Parse(csv[4]);
                    start_datetime = new System.DateTime(int.Parse(csv[5]), int.Parse(csv[6]) , int.Parse(csv[7]) , int.Parse(csv[8]) , int.Parse(csv[9]), int.Parse(csv[10]));
                    break;
                case GameEnum.RecordDataId.kMoveDataId:
                    game_move_queue.Enqueue(new GameMove((GameEnum.PlayerTurn)int.Parse(csv[1]), int.Parse(csv[2]), int.Parse(csv[3]),
                        new System.DateTime(int.Parse(csv[4]), int.Parse(csv[5]), int.Parse(csv[6]), int.Parse(csv[7]), int.Parse(csv[8]), int.Parse(csv[9]))));
                    break;
            }
        }
    }
}

public class GameRoom
{
    public int player_id_1, player_id_2;
    
    // GAME VARS
    public int grid_size_x = 3, grid_size_y = 3;
    public int[,] grid;
    public const string player_1_token = "X";
    public const string player_2_token = "O";
    public int move_count_ = 0;

    // REPLAY VARS
    public GameRecording replay_log_;
    //public List<string> replay_log_;
    //public int p1_replay_step_ = 0;
    //public int p2_replay_step_ = 0;

    public GameRoom(int id_1, int id_2)
    {
        player_id_1 = id_1;
        player_id_2 = id_2;

        grid = new int[grid_size_x, grid_size_y];
        ResetGrid();

        //replay_log_ = new List<string>();
        replay_log_ = new GameRecording(player_id_1, player_id_2, grid_size_x, grid_size_y);
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
        //replay_log_.Clear();
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
        NextReplayMove,
        RecordingTransferDataStart = 100,
        RecordingTransferData = 101,
        RecordingTransferDataEnd = 102
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
        ReplayEnd,
        RecordingTransferDataStart = 100,
        RecordingTransferData = 101,
        RecordingTransferDataEnd = 102
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

    public enum PlayerTurn
    {
        kPlayer1,
        kPlayer2
    }

    public enum RecordDataId
    {
        kRoomSettingId,
        kMoveDataId
    }
}

public class NameAndIndex
{
    public string name;
    public int index;

    public NameAndIndex(int index, string name)
    {
        this.name = name;
        this.index = index;
    }

    public enum SignifierId
    {
        LastUsedIndex = 1,
        IndexAndName
    }
}