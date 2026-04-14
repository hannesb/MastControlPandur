using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;

namespace MastControlPandur3.CAN
{
    public enum CanHandlerStatus
    {
        Idle, Initialized, Connected, Disconnected
    }

    public enum BitRate
    {
        R125 = 125, R250 = 250, R1000 = 1000, R500 = 500, R800 = 800
    }

    public interface ICAN
    {
        void Initialize(string hwid, BitRate bitRate, ConcurrentQueue<string> errorMsgQueue, ConcurrentQueue<string> debugMsgQueue);
        bool Connect();
        bool Disconnect();
        bool Reset();
        bool SendMsg(int canId, byte[] data);
        bool HasMsg();
        bool GetNextMsg(ref int canId, ref int len, ref byte[] data);
        CanHandlerStatus Status { get; }
        event EventHandler Initialized;
        event EventHandler Connected;
        event EventHandler Disconnected;
        event EventHandler Closed;
        event EventHandler ConnectionFailed;
        DateTime LastHeartbeatSlave { get; set; }
    }
}
