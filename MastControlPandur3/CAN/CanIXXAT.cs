// #define DEBUG_IXXAT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ixxat.Vci4;
using Ixxat.Vci4.Bal;
using Ixxat.Vci4.Bal.Can;
using System.Threading;
using System.Collections.Concurrent;
using System.ComponentModel;
using Infralution.Localization.Wpf;

namespace MastControlPandur3.CAN
{
    class CanIXXAT : ICAN
    {
        #region Member variables

        /// <summary>
        ///   Reference to the used VCI device.
        /// </summary>
        private IVciDevice _mDevice;

        /// <summary>
        ///   Reference to the CAN controller.
        /// </summary>
        private ICanControl _mCanCtl;

        /// <summary>
        ///   Reference to the CAN message communication channel.
        /// </summary>
        private ICanChannel _mCanChn;

        /// <summary>
        ///   Reference to the message writer of the CAN message channel.
        /// </summary>
        private ICanMessageWriter _mWriter;

        /// <summary>
        ///   Reference to the message reader of the CAN message channel.
        /// </summary>
        private ICanMessageReader _mReader;

        private string _hwid;
        private BitRate _bitRate;

        private ConcurrentQueue<string> _debugMsgQueue;
        private ConcurrentQueue<string> _errorMsgQueue;
        private BackgroundWorker _canWorkerThread = new BackgroundWorker();
        private AutoResetEvent _connectNow = new AutoResetEvent(false);
        private AutoResetEvent _disconnectNow = new AutoResetEvent(false);
        private ConcurrentQueue<ICanMessage> _receiveQueue = new ConcurrentQueue<ICanMessage>();

        private CanHandlerStatus _handlerStatus;
       
        public event EventHandler Initialized;
        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler Closed;
        public event EventHandler ConnectionFailed;

        #endregion

        public void Initialize(string hwid, BitRate bitRate, ConcurrentQueue<string> errorMsgQueue, ConcurrentQueue<string> debugMsgQueue)
        {
            _hwid = hwid;
            _errorMsgQueue = errorMsgQueue;
            _debugMsgQueue = debugMsgQueue;
            _bitRate = bitRate;

            _handlerStatus = CanHandlerStatus.Idle;
            
            _canWorkerThread.DoWork += CanWorkerThread_DoWork;
            _canWorkerThread.RunWorkerAsync();
            LastHeartbeatSlave = DateTime.MinValue;
        }

        public CanHandlerStatus Status { get { return _handlerStatus; } }
        public DateTime LastHeartbeatSlave { get; set; }

        public bool Connect()
        {
            if (_handlerStatus != CanHandlerStatus.Initialized)
            {
                return false;
            }
            _connectNow.Set();

            return true;
        }

        public bool Disconnect()
        {
            if (_handlerStatus != CanHandlerStatus.Connected)
            {
                return false;
            }
            _disconnectNow.Set();

            return true;
        }

        public bool Reset()
        {
            if (_canWorkerThread.IsBusy)
            {
                return false;
            }
            _handlerStatus = CanHandlerStatus.Idle;
            _canWorkerThread.RunWorkerAsync();

            return true;
        }

        public bool SendMsg(int canId, byte[] data)
        {
            if (_handlerStatus != CanHandlerStatus.Connected)
            {
                return false;
            }
            IMessageFactory factory = VciServer.Instance().MsgFactory;
            ICanMessage canMsg = (ICanMessage)factory.CreateMsg(typeof(ICanMessage));

            canMsg.ExtendedFrameFormat = true;
            canMsg.TimeStamp = 0;
            canMsg.Identifier = (uint)canId;
            canMsg.FrameType = CanMsgFrameType.Data;
            canMsg.DataLength = (byte)Math.Min(8, data.Length);
            canMsg.SelfReceptionRequest = false;  // show this message in the console window

            var dbgMsg = $"CanIXXAT.SendMsg {DateTime.Now:HH:mm:ss.ffff} S {canId:X3}";
            for (Byte i = 0; i < canMsg.DataLength; i++)
            {
                dbgMsg += $" {data[i]:X2}";
                canMsg[i] = data[i];
            }
            _mWriter.SendMessage(canMsg);
#if DEBUG_IXXAT
            System.Diagnostics.Debug.Print(dbgMsg);
#endif
            return true;
        }

        public bool HasMsg()
        {
            return !_receiveQueue.IsEmpty;
        }

        public bool GetNextMsg(ref int canId, ref int len, ref byte[] data)
        {
            if (_handlerStatus != CanHandlerStatus.Connected)
            {
                return false;
            }
            ICanMessage canMsg = null;
            if (!_receiveQueue.TryDequeue(out canMsg))
            {
                return false;
            }
            bool rval = false;
            var dbgMsg = $"CanIXXAT.GetNextMsg {DateTime.Now:mm:ss.fff} R {canId:X3}";
            for (int index = 0; index < canMsg.DataLength; index++)
            {
                dbgMsg += $" {canMsg[index]:X2}";
            }
#if DEBUG_IXXAT
            System.Diagnostics.Debug.Print(dbgMsg);
#endif
            switch (canMsg.FrameType)
            {
                //
                // show data frames
                //
                case CanMsgFrameType.Data:
                    {
                        if (!canMsg.RemoteTransmissionRequest)
                        {
                            canId = (int)canMsg.Identifier;
                            len = canMsg.DataLength;
                            for (int index = 0; index < canMsg.DataLength; index++)
                            {
                                data[index] = canMsg[index];
                            }
                            rval = true;
                        }
                        else
                        {
                            _debugMsgQueue.Enqueue(Resources.AppStrings.CANRemoteFrame);
                        }
                        break;
                    }

                //
                // show informational frames
                //
                case CanMsgFrameType.Info:
                    {
                        switch ((CanMsgInfoValue)canMsg[0])
                        {
                            case CanMsgInfoValue.Start:
                                _debugMsgQueue.Enqueue(Resources.AppStrings.CANStart);
                                break;
                            case CanMsgInfoValue.Stop:
                                _debugMsgQueue.Enqueue(Resources.AppStrings.CANStop);
                                break;
                            case CanMsgInfoValue.Reset:
                                _debugMsgQueue.Enqueue(Resources.AppStrings.CANReset);
                                break;
                        }
                        break;
                    }

                //
                // show error frames
                //
                case CanMsgFrameType.Error:
                    {
                        switch ((CanMsgError)canMsg[0])
                        {
                            case CanMsgError.Stuff:
                                _debugMsgQueue.Enqueue(Resources.AppStrings.CANErrorStuff);
                                break;
                            case CanMsgError.Form:
                                _debugMsgQueue.Enqueue(Resources.AppStrings.CANErrorForm);
                                break;
                            case CanMsgError.Acknowledge:
                                _debugMsgQueue.Enqueue(Resources.AppStrings.CANErrorAcknowledge);
                                break;
                            case CanMsgError.Bit:
                                _debugMsgQueue.Enqueue(Resources.AppStrings.CANErrorBit);
                                break;
                            case CanMsgError.Crc:
                                _debugMsgQueue.Enqueue(Resources.AppStrings.CANErrorCRC);
                                break;
                            case CanMsgError.Other:
                                _debugMsgQueue.Enqueue(Resources.AppStrings.CANErrorOther);
                                break;
                        }
                        break;
                    }
            }
            return rval;
        }

        private CanBitrate DecodedBitrate()
        {
            switch (_bitRate)
            {
                case BitRate.R125:
                    return CanBitrate.Cia125KBit;
                case BitRate.R250:
                    return CanBitrate.Cia250KBit;
                case BitRate.R500:
                    return CanBitrate.Cia500KBit;
                case BitRate.R800:
                    return CanBitrate.Cia800KBit;
                case BitRate.R1000:
                    return CanBitrate.Cia1000KBit;
            }
            throw new Exception("CanIXXAT: Bitrate not supported!");
        }

        private void CanWorkerThread_DoWork(object sender, DoWorkEventArgs e)
        {            
            Thread.CurrentThread.CurrentUICulture = CultureManager.UICulture;
            Byte canNo;
            if (_hwid == "")
            {
                return;
            }
            string errMsg = SelectDevice(_hwid, out canNo);
            if (errMsg != null)
            {
                _errorMsgQueue.Enqueue(errMsg);
                return;
            }
            _handlerStatus = CanHandlerStatus.Initialized;
            _debugMsgQueue.Enqueue(Resources.AppStrings.CANInitialized);
            if (Initialized != null)
            {
                Initialized(this, new EventArgs());
            }
            _connectNow.WaitOne();
            _debugMsgQueue.Enqueue(Resources.AppStrings.CANConnecting);
            errMsg = InitSocket(0, DecodedBitrate());
            if (errMsg != null)
            {
                _handlerStatus = CanHandlerStatus.Idle;
                _errorMsgQueue.Enqueue(errMsg);
                if (ConnectionFailed != null)
                {
                    ConnectionFailed(this, new EventArgs());
                }
                if (Closed != null)
                {
                    Closed(this, new EventArgs());
                }
                return;
            }
            _handlerStatus = CanHandlerStatus.Connected;
            _debugMsgQueue.Enqueue(Resources.AppStrings.CANConnected);
            if (Connected != null)
            {
                Connected(this, new EventArgs());
            }
            try
            {
                var secondsPerTick = _mCanChn.TimeStampCounterDivisor * 1.0 / _mCanChn.ClockFrequency;
                while (true)
                {
                    if (_disconnectNow.WaitOne(1))
                    {
                        break;
                    }
                    if (!_mCanChn.ChannelStatus.IsActivated)
                    {
                        break;
                    }
                    ICanMessage canMsg = null;
                    // read a CAN message from the receive FIFO
                    while (_mReader.ReadMessage(out canMsg))
                    {
                        bool ext = canMsg.ExtendedFrameFormat;
                        if (canMsg.FrameType == CanMsgFrameType.Data) {
                            _receiveQueue.Enqueue(canMsg);
#if DEBUG_IXXAT
                            System.Diagnostics.Debug.Print($"CanIXXAT.RecvMsg {DateTime.Now:HH:mm:ss.ffff} {canMsg.TimeStamp * secondsPerTick:F6} {canMsg.FrameType} {canMsg.Identifier:X8}");
#endif
                        } else {
                            System.Diagnostics.Debug.Print($"Receive Other {canMsg.TimeStamp * secondsPerTick:F6} {canMsg.FrameType} {canMsg.Identifier:X8}");
                        }
                    }
                }
                _mCanCtl.StopLine();
                _mCanChn.Deactivate();
            }
            catch (Exception ex)
            {
                _debugMsgQueue.Enqueue(ex.Message);
            }
            _handlerStatus = CanHandlerStatus.Disconnected;
            _debugMsgQueue.Enqueue(Resources.AppStrings.CANDisconnecting);
            if (Disconnected != null)
            {
                Disconnected(this, new EventArgs());
            }
            FinalizeApp();
            _handlerStatus = CanHandlerStatus.Idle;
            _debugMsgQueue.Enqueue(Resources.AppStrings.CANDisconnected);
            if (Closed != null)
            {
                Closed(this, new EventArgs());
            }
        }

        #region Device selection

        //************************************************************************
        /// <summary>
        ///   Selects the first CAN adapter.
        /// </summary>
        //************************************************************************
        private string SelectDevice(string hardwareId, out Byte canNo)
        {
            IVciDeviceManager deviceManager = null;
            IVciDeviceList deviceList = null;
            IEnumerator deviceEnum = null;
            string msg = null;

            canNo = 255;
            try
            {
                //
                // Get device manager from VCI server
                //
                deviceManager = VciServer.Instance().DeviceManager;

                //
                // Get the list of installed VCI devices
                //
                deviceList = deviceManager.GetDeviceList();

                //
                // Get enumerator for the list of devices
                //
                deviceEnum = deviceList.GetEnumerator();

                //
                // Go through the list of devices
                //
                string ids = "";
                bool found = false;
                Byte idx = 0;
                while (deviceEnum.MoveNext())
                {
                    _mDevice = deviceEnum.Current as IVciDevice;

                    object serialNumberGuid = _mDevice.UniqueHardwareId;
                    string serialNumberText = GetSerialNumberText(ref serialNumberGuid);
                    if (serialNumberText == hardwareId || hardwareId == "any")
                    {
                        canNo = idx;
                        found = true;
                        break;
                    }

                    var bla1 = _mDevice.Description;
                    var bla2 = _mDevice.DeviceClass;
                    var bla3 = _mDevice.DriverVersion;
                    var bla4 = _mDevice.Equipment;
                    var bla5 = _mDevice.HardwareVersion;
                    var bla6 = _mDevice.Manufacturer;
                    var bla7 = _mDevice.UniqueHardwareId;
                    var bla8 = _mDevice.VciObjectId;

                    ids += " " + serialNumberText;
                    idx++;
                }
                if (!found)
                {
                    if (ids == "")
                    {
                        ids = Resources.AppStrings.CANEmptyDeviceList;
                    }
                    throw new Exception(
                        string.Format(Resources.AppStrings.CANDeviceNotFound, _hwid)
                        + " " + string.Format(Resources.AppStrings.CANAvailableHWIDs, ids));
                }
            }
            catch (Exception ex)
            {
                msg = ex.Message;
            }
            finally
            {
                //
                // Dispose device manager ; it's no longer needed.
                //
                DisposeVciObject(deviceManager);

                //
                // Dispose device list ; it's no longer needed.
                //
                DisposeVciObject(deviceList);

                //
                // Dispose device list ; it's no longer needed.
                //
                DisposeVciObject(deviceEnum);
            }
            return msg;
        }

        #endregion

        #region Opening socket

        //************************************************************************
        /// <summary>
        ///   Opens the specified socket, creates a message channel, initializes
        ///   and starts the CAN controller.
        /// </summary>
        /// <param name="canNo">
        ///   Number of the CAN controller to open.
        /// </param>
        /// <returns>
        ///   A value indicating if the socket initialization succeeded or failed.
        /// </returns>
        //************************************************************************
        private string InitSocket(Byte canNo, CanBitrate bitRate)
        {
            IBalObject bal = null;
            string msg = null;

            try
            {
                //
                // Open bus access layer
                //
                bal = _mDevice.OpenBusAccessLayer();

                //
                // Open a message channel for the CAN controller
                //
                _mCanChn = bal.OpenSocket(canNo, typeof(ICanChannel)) as ICanChannel;

                // USB-to-CAN: Kein Support für zyklisches Senden?!?
                //var bla = bal.OpenSocket(canNo, typeof(ICanScheduler)) as ICanScheduler;
                //var cyclicMsg = bla.AddMessage();
                //if (cyclicMsg != null) {
                //    cyclicMsg.CycleTicks = (ushort)(1.0 / 128.0 * bla.ClockFrequency / bla.CyclicMessageTimerDivisor);
                //    cyclicMsg.AutoIncrementMode = CanCyclicTXIncMode.Inc16;
                //    cyclicMsg.AutoIncrementIndex = 0;
                //    cyclicMsg.DataLength = 2;
                //    cyclicMsg.Identifier = 0x02008001;
                //    cyclicMsg.Start(0);
                //}

                // Initialize the message channel
                _mCanChn.Initialize(1024, 128, false);

                // Get a message reader object
                _mReader = _mCanChn.GetMessageReader();

                // Initialize message reader
                _mReader.Threshold = 1;

                // Get a message wrtier object
                _mWriter = _mCanChn.GetMessageWriter();

                // Initialize message writer
                _mWriter.Threshold = 1;

                // Activate the message channel
                _mCanChn.Activate();

                //
                // Open the CAN controller
                //
                _mCanCtl = bal.OpenSocket(canNo, typeof(ICanControl)) as ICanControl;

                // Initialize the CAN controller
                _mCanCtl.InitLine(CanOperatingModes.Extended | CanOperatingModes.ErrFrame, bitRate);

                // Set the acceptance filter
                _mCanCtl.SetAccFilter(CanFilter.Ext, (uint)CanAccCode.All, (uint)CanAccMask.All);

                // Start the CAN controller
                _mCanCtl.StartLine();
            }
            catch (Exception ex)
            {
                msg = ex.Message;
            }
            finally
            {
                //
                // Dispose bus access layer
                //
                DisposeVciObject(bal);
            }

            return msg;
        }

        #endregion

        #region Utility methods

        /// <summary>
        /// Returns the UniqueHardwareID GUID number as string which
        /// shows the serial number.
        /// Note: This function will be obsolete in later version of the VCI.
        /// Until VCI Version 3.1.4.1784 there is a bug in the .NET API which
        /// returns always the GUID of the interface. In later versions there
        /// the serial number itself will be returned by the UniqueHardwareID property.
        /// </summary>
        /// <param name="serialNumberGuid">Data read from the VCI.</param>
        /// <returns>The GUID as string or if possible the  serial number as string.</returns>
        static string GetSerialNumberText(ref object serialNumberGuid)
        {
            string resultText;

            // check if the object is really a GUID type
            if (serialNumberGuid.GetType() == typeof(System.Guid))
            {
                // convert the object type to a GUID
                System.Guid tempGuid = (System.Guid)serialNumberGuid;

                // copy the data into a byte array
                byte[] byteArray = tempGuid.ToByteArray();

                // serial numbers starts always with "HW"
                if (((char)byteArray[0] == 'H') && ((char)byteArray[1] == 'W'))
                {
                    // run a loop and add the byte data as char to the result string
                    resultText = "";
                    int i = 0;
                    while (true)
                    {
                        // the string stops with a zero
                        if (byteArray[i] != 0)
                            resultText += (char)byteArray[i];
                        else
                            break;
                        i++;

                        // stop also when all bytes are converted to the string
                        // but this should never happen
                        if (i == byteArray.Length)
                            break;
                    }
                }
                else
                {
                    // if the data did not start with "HW" convert only the GUID to a string
                    resultText = serialNumberGuid.ToString();
                }
            }
            else
            {
                // if the data is not a GUID convert it to a string
                string tempString = (string)(string)serialNumberGuid;
                resultText = "";
                for (int i = 0; i < tempString.Length; i++)
                {
                    if (tempString[i] != 0)
                        resultText += tempString[i];
                    else
                        break;
                }
            }

            return resultText;
        }


        //************************************************************************
        /// <summary>
        ///   Finalizes the application 
        /// </summary>
        //************************************************************************
        private void FinalizeApp()
        {
            //
            // Dispose all hold VCI objects.
            //
#if false
            if (_mCanCtl != null)
            {
                _mCanCtl.StopLine();
            }
            if (_mCanChn != null)
            {
                _mCanChn.Deactivate();
            }
#endif
            // Dispose CAN controller
            DisposeVciObject(_mCanCtl);

            // Dispose message reader
            DisposeVciObject(_mReader);

            // Dispose message writer 
            DisposeVciObject(_mWriter);

            // Dispose CAN channel
            DisposeVciObject(_mCanChn);
            
            // Dispose VCI device
            DisposeVciObject(_mDevice);
        }


        //************************************************************************
        /// <summary>
        ///   This method tries to dispose the specified object.
        /// </summary>
        /// <param name="obj">
        ///   Reference to the object to be disposed.
        /// </param>
        /// <remarks>
        ///   The VCI interfaces provide access to native driver resources. 
        ///   Because the .NET garbage collector is only designed to manage memory, 
        ///   but not native OS and driver resources the application itself is 
        ///   responsible to release these resources via calling 
        ///   IDisposable.Dispose() for the obects obtained from the VCI API 
        ///   when these are no longer needed. 
        ///   Otherwise native memory and resource leaks may occure.  
        /// </remarks>
        //************************************************************************
        private static void DisposeVciObject(object obj)
        {
            if (null != obj)
            {
                IDisposable dispose = obj as IDisposable;
                if (null != dispose)
                {
                    dispose.Dispose();
                    obj = null;
                }
            }
        }

        #endregion

    }
}
