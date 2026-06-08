using MastControlPandur3.Resources;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MastControlPandur3
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ConcurrentQueue<string> _debugMsgQueue = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _errorMsgQueue = new ConcurrentQueue<string>();
        private readonly MMTimer _mmTimer = new MMTimer();
        private readonly DispatcherTimer _dispatcherTimer = new DispatcherTimer() { Interval = new TimeSpan(0, 0, 0, 0, 50) };
        private readonly CAN.ICAN _canHandler = null;
        private readonly MMTimer.UserTimeProc _timeProc = null;
        private Int16 _syncOffset = 0;
        private int _cntCANStarted = 0;
        private int _cntCANClosed = 0;
        private Dialogs.DialogStartup _dlgStartup = null;
        private Dialogs.DialogShutdown _dlgShutdown;
        private const int ZYKLUSZEITMS = 8;
        // private int _cnt1s = 1000 / ZYKLUSZEITMS;
        private bool sendSync = true;
        private bool optikPodStatus = false;
        private int readParamIdx = 0;
        private uint operatingHours = 0;
        private uint hK = 0;
        private uint mastVariant = 0;
        private bool setMastEncoder = false;
        private bool queryMastEncoderStatus = false;

        private readonly List<Parameter> parameters = new List<Parameter> {
            new Parameter("8m", false, 0, 0, "Top Position", "mm"),
            new Parameter("8m", false, 0, 1, "Bottom Position", "mm"),
            new Parameter("8m", false, 0, 2, "Bottom Position Offset", "mm"),
            new Parameter("8m", false, 0, 3, "Current Limit Bottom", "A"),
            new Parameter("8m", false, 0, 4, "Bottom Limit Switch Window", "mm"),
            new Parameter("8m", false, 0, 5, "Velocity UP", "rpm"),
            new Parameter("8m", false, 0, 6, "Velocity DOWN", "rpm"),
            new Parameter("8m", false, 0, 7, "Velocity SLOW", "rpm"),
            new Parameter("8m", false, 0, 8, "Velocity BOTTOM", "rpm"),
            new Parameter("8m", false, 0, 9, "Middle Position", "mm"),
            new Parameter("8m", false, 0, 10, "Middle Position Window", "mm"),
            new Parameter("8m", false, 0, 11, "OptikPod Position", "mm"),

            new Parameter("5m", false, 1, 0, "Top Position", "mm"),
            new Parameter("5m", false, 1, 1, "Bottom Position", "mm"),
            new Parameter("5m", false, 1, 2, "Bottom Position Offset", "mm"),
            new Parameter("5m", false, 1, 3, "Current Limit Bottom", "A"),
            new Parameter("5m", false, 1, 4, "Bottom Limit Switch Window", "mm"),
            new Parameter("5m", false, 1, 5, "Velocity UP", "rpm"),
            new Parameter("5m", false, 1, 6, "Velocity DOWN", "rpm"),
            new Parameter("5m", false, 1, 7, "Velocity SLOW", "rpm"),
            new Parameter("5m", false, 1, 8, "Velocity BOTTOM", "rpm"),
            new Parameter("5m", false, 1, 9, "Middle Position", "mm"),
            new Parameter("5m", false, 1, 10, "Middle Position Window", "mm"),
            new Parameter("5m", false, 1, 11, "OptikPod Position", "mm"),

            new Parameter("Aufkl", true, 2, 0, "Offset Fahrzeug", "mm"),
            new Parameter("Aufkl", true, 2, 1, "Länge Kopflast", "mm"),

            new Parameter("JFS", true, 3, 0, "Offset Fahrzeug", "mm"),
            new Parameter("JFS", true, 3, 1, "Länge Kopflast", "mm"),

            new Parameter("NeFuE", true, 4, 0, "Offset Fahrzeug", "mm"),
            new Parameter("NeFuE", true, 4, 1, "Länge Kopflast", "mm"),

            new Parameter("ERFOS", true, 5, 0, "Offset Fahrzeug", "mm"),
            new Parameter("ERFOS", true, 5, 1, "Länge Kopflast", "mm"),

            new Parameter("STÖRSYS", true, 6, 0, "Offset Fahrzeug", "mm"),
            new Parameter("STÖRSYS", true, 6, 1, "Länge Kopflast", "mm"),
        };

        public MainWindow()
        {
            InitializeComponent();

            _canHandler = new CAN.CanIXXAT();

            _canHandler.Initialized += CanHandler_Initialized; ;
            _canHandler.Connected += CanHandler_Connected; ;
            _canHandler.Disconnected += CanHandler_Disconnected; ;
            _canHandler.ConnectionFailed += CanHandler_ConnectionFailed; ;

            _canHandler.Initialize("any", CAN.BitRate.R500, _errorMsgQueue, _debugMsgQueue);
            //_canHandler.Initialize("any", CAN.BitRate.R125, _errorMsgQueue, _debugMsgQueue);

            _timeProc = new MMTimer.UserTimeProc(TimerCallback);
            _mmTimer.Start(_timeProc, ZYKLUSZEITMS, MMTimer.TimerMode.Periodic);

            _dispatcherTimer.Tick += DispatcherTimer_Tick1; ;
            _dispatcherTimer.Start();

            dataGridParameter.ItemsSource = parameters;
        }

        private void TimerCallback()
        {
            var mainWindow = this;

            if (mainWindow._canHandler.Status == CAN.CanHandlerStatus.Connected) {
                if (mainWindow.sendSync) {
                    mainWindow._canHandler.SendMsg(0x02008001, BitConverter.GetBytes(mainWindow._syncOffset));
                    if (readParamIdx < parameters.Count) {
                        var para = parameters[readParamIdx++];
                        var bytes = new byte[] {
                            0x22, 0xC0, 0x07, para.HPN,
                            0xFF, 0xFF, 0xFF, 0xFF
                        };
                        mainWindow._canHandler.SendMsg(0x1E650201, bytes);
                    }
                }
            }
            if (mainWindow._syncOffset == 1024) {
                mainWindow.DebugMsg("Reset Sync");
                mainWindow._syncOffset = 0;
            } else {
                mainWindow._syncOffset += 1;
            }
            if (queryMastEncoderStatus) {
                // HPN (HUMS Parameter Number) = 0x07C021
                // HTI (HUMS Type Index) = 194: various counts
                // Magic Word = 0x13E509B5 (333777333 dez.)
                var bytes = new byte[] {
                    0x21, 0xC0, 0x07, 194,
                    0xFF, 0xFF, 0xFF, 0xFF
                };
                var bytesSet = new byte[] {
                    0x21, 0xC0, 0x07, 194,
                    0xB5, 0x09, 0xE5, 0x13
                };
                mainWindow._canHandler.SendMsg(0x1E650201, setMastEncoder ? bytesSet : bytes);
                setMastEncoder = false;
            }
            //if (mainWindow._cnt1s == 0) {
            //    mainWindow._debugMsgQueue.Enqueue("1s");
            //    mainWindow._cnt1s = 1000 / ZYKLUSZEITMS;
            //} else {
            //    mainWindow._cnt1s -= 1;
            //}
            // mainWindow._debugMsgQueue.Enqueue($"{mainWindow._cnt1s}");
            // mainWindow._timer.Change(ZYKLUSZEITMS, Timeout.Infinite);
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _mmTimer.Stop();

            _cntCANClosed = 0;
            if (_canHandler != null && _canHandler.Status == CAN.CanHandlerStatus.Connected) {
                _canHandler.Disconnect();
                _canHandler.Closed += CanHandler_Closed; ;

                e.Cancel = true;
                _cntCANClosed += 1;
            }
            if (e.Cancel) {
                _dlgShutdown = new Dialogs.DialogShutdown {
                    Owner = this
                };
                _dlgShutdown.Show();
            }
        }

        private void CanHandler_Closed(object sender, EventArgs e)
        {
            _cntCANClosed -= 1;
            if (_cntCANClosed == 0) {
                Dispatcher.Invoke(new Action(() => {
                    _dlgShutdown.Close();
                    Close();
                }));
            }
        }

        private void CanHandler_Initialized(object sender, EventArgs e)
        {
            Dispatcher.Invoke(new Action(() => {
                CANConnect(sender as CAN.ICAN);
            }));
        }

        private void CanHandler_ConnectionFailed(object sender, EventArgs e)
        {
            CloseCANConnectDialog();
        }

        private void CanHandler_Disconnected(object sender, EventArgs e)
        {
            Dispatcher.Invoke(new Action(() =>
            {
            }));
        }

        private void CanHandler_Connected(object sender, EventArgs e)
        {
            CloseCANConnectDialog();
        }

        private void CANConnect(CAN.ICAN canHandler)
        {
            _cntCANStarted += 1;
            if (_dlgStartup == null) {
                _dlgStartup = new Dialogs.DialogStartup {
                    Owner = this
                };
                _dlgStartup.Show();
            }
            canHandler.Connect();
        }

        private void CloseCANConnectDialog()
        {
            _cntCANStarted -= 1;
            if (_cntCANStarted == 0 && _dlgStartup != null) {
                Dispatcher.Invoke(new Action(() => {
                    _dlgStartup.Close();
                    _dlgStartup = null;
                }));
            }
        }

        private string Data2Hex(byte[] data)
        {
            return string.Join(" ", data.Select(d => $"{d:X2}"));
        }

        private string Decode2Bits(int bits)
        {
            switch (bits & 0x03) {
                case 0:
                    return "OFF";
                case 1:
                    return "ON";
                case 2:
                    return "ERR";
                default:
                    return "N/A";
            }
        }

        private void ProcessCanMsg(int canId, int len, byte[] data)
        {
            var s = Data2Hex(data);
            switch (canId) {
                case 0x1E620303:
                    // Alive message
                    if (len == 8) {
                        DebugMsg($"0x1E620303 alive {s}");
                        Dispatcher.Invoke(new Action(() => {
                            textBoxAlive.Text = $"{Decode2Bits(data[0])}";
                        }));
                    }
                    break;
                case 0x1E350103:
                    // MastStatus1
                    if (len == 8) {
                        DebugMsg($"0x1E350103 status {s}");
                        Dispatcher.Invoke(new Action(() => {
                            textBoxMastStatus.Text = 
                                $"EmergencyDrive = {Decode2Bits(data[0])}" +
                                $"\nTopPosition = {Decode2Bits(data[0] >> 2)}" +
                                $"\nLimitBottomPosition = {Decode2Bits(data[0] >> 4)}" +
                                $"\nMovingUp = {Decode2Bits(data[0] >> 6)}" +
                                $"\nMovingDown = {Decode2Bits(data[1])}" +
                                $"\nLimitSwitchMiddlePos = {Decode2Bits(data[1] >> 2)}" +
                                $"\nParkPosition = {Decode2Bits(data[1] >> 4)}" +
                                $"\nErrorFU = {Decode2Bits((data[2] >> 0) & 0x01)}" +
                                $"\nCanTimeout = {Decode2Bits((data[2] >> 1) & 0x01)}" +
                                $"\nHeightOutOfRange = {Decode2Bits((data[2] >> 2) & 0x01)}" +
                                $"\nPositionDifference = {Decode2Bits((data[2] >> 3) & 0x01)}" +
                                $"\nStateMachine = {Decode2Bits((data[2] >> 4) & 0x01)}" +
                                $"\nEncoderTimeOut = {Decode2Bits((data[2] >> 5) & 0x01)}" +
                                $"\nEmergDriveInUse = {Decode2Bits((data[2] >> 6) & 0x01)}" +
                                $"\nMastState = {data[3]}" +
                                $"\nhOffset = {data[4] + (data[5] << 8)}" +
                                $"\nMastEncoderHeight = {data[6] + (data[7] << 8)}" +
                                $"";
                        }));
                    }
                    break;
                case 0x1E660003:
                    // HUMS value
                    if (len == 8) {
                        DebugMsg($"0x1E660003 hums {s}");
                        Dispatcher.Invoke(new Action(() => {
                            var hpn = data[0] + (data[1] << 8) + ((data[2] & 0x07) << 16);
                            switch (hpn) {
                                case 0x07C020:
                                case 0x07C021:
                                    var tmp = data[4] + (data[5] << 8) + (data[6] << 16) + (data[7] << 24);
                                    textBoxHUMSValue.Text =
                                        $"HPN = {hpn:X}" +
                                        $"\nDevice = {data[2] >> 3}" +
                                        $"\nHTI = {data[3]}" +
                                        $"\nHUMS Value = {tmp}" +
                                        $"";
                                    if (hpn == 0x07C020 && data[3] == 196) {
                                        textBoxOperatingHours.Text = tmp.ToString();
                                    }
                                    if (hpn == 0x07C020 && data[3] == 234) {
                                        textBoxHMax.Text = tmp.ToString();
                                    }
                                    if (hpn == 0x07C020 && data[3] == 235) {
                                        textBoxMastEncoderHeight.Text = tmp.ToString();
                                    }
                                    if (hpn == 0x07C020 && data[3] == 194) {
                                        textBoxMastVariant.Text = tmp.ToString();
                                    }
                                    if (hpn == 0x07C021 && data[3] == 234) {
                                        textBoxHK.Text = tmp.ToString();
                                    }
                                    if (hpn == 0x07C021 && data[3] == 194) {
                                        textBoxSetMastEncoderStatus.Text = tmp.ToString();
                                        if (tmp == 0) {
                                            queryMastEncoderStatus = false;
                                        }
                                    }
                                    break;
                                case 0x07C022:
                                    var param = parameters.FirstOrDefault(r => r.HPN == data[3]);
                                    if (param != null) {
                                        param.Wert = (ushort)(data[4] + (data[5] << 8));
                                    }
                                    break;
                            }
                        }));
                    }
                    break;
                case 0x1E666003:
                    // Software version
                    if (len == 8) {
                        DebugMsg($"0x1E666003 software version {s}");
                        Dispatcher.Invoke(new Action(() => {
                            switch (data[0]) {
                                case 0:
                                    textBoxSWVersion.Text = $"Byte Count = {data[1] + (data[2] << 8) + (data[3] << 16)}";
                                    break;
                                case 1:
                                    textBoxSWVersion.Text += 
                                        $"\nVariant = {data[1]}" +
                                        $"\nMCU Version = {data[3]:X2}" +
                                        $"\nMCU Date Year = {data[4] + (data[5] << 8):X4}" +
                                        $"\nMCU Date Month = {data[6]:X2}" +
                                        $"\nMCU Date Day = {data[7]:X2}";
                                    break;
                                case 2:
                                    textBoxSWVersion.Text +=
                                        $"\nDisplay Version = {data[3]:X2}" +
                                        $"\nDisplay Date Year = {data[4] + (data[5] << 8):X4}" +
                                        $"\nDisplay Date Month = {data[6]:X2}" +
                                        $"\nDisplay Date Day = {data[7]:X2}";
                                    break;
                            }
                        }));
                    }
                    break;
                case 0x1E350303:
                    if (len == 8) {
                        DebugMsg($"0x1E350303 optik pod {s}");
                        Dispatcher.Invoke(new Action(() => {
                            textBoxOptikPod.Text = $"{Decode2Bits(data[0])}";
                        }));
                        byte[] sendData = new byte[] { optikPodStatus ? (byte)0xFD : (byte)0xFC, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
                        _canHandler.SendMsg(0x1E350402, sendData);
                    }
                    break;
                default:
                    DebugMsg($"other canId: 0x{canId:X}");
                    break;
            }
        }

        private void DispatcherTimer_Tick1(object sender, EventArgs e)
        {
            string msg;
            while (_debugMsgQueue.TryDequeue(out msg)) {
                DebugMsg(AppStrings.MsgListDebug + " " + msg);
            }
            while (_errorMsgQueue.TryDequeue(out msg)) {
                DebugMsg(AppStrings.MsgListError + " " + msg);
                var msg2 = "" + msg;
                Thread t = new Thread(() => MessageBox.Show(msg2, AppStrings.MainWindowTitle, MessageBoxButton.OK, MessageBoxImage.Error));
                t.Start();
            }
            if (_canHandler.Status == CAN.CanHandlerStatus.Connected) {
                int canId = 0;
                int len = 0;
                byte[] data = new byte[8];
                while (_canHandler.GetNextMsg(ref canId, ref len, ref data)) {
                    var str = "";
                    for (int i = 0; i < len; i++) {
                        str += $"{data[i]:X2}";
                        if (i == 3) {
                            str += " ";
                        }
                    }
                    ProcessCanMsg(canId, len, data);
                    // DebugMsg($"GetNextMsg {canId:X8} {len} {str}");
                }
                // _canHandler.SendMsg(0x02008001, BitConverter.GetBytes(mainWindow._syncOffset));
            }
        }

        private void DebugMsg(string msg)
        {
            Dispatcher.Invoke(new Action(() => {
                if (listBoxDebug.Items.Count > 30) {
                    listBoxDebug.Items.RemoveAt(0);
                }
                listBoxDebug.Items.Add($"{DateTime.Now:HH:mm:ss.fff} {msg}");
            }));
        }

        private void checkBoxSync_Click(object sender, RoutedEventArgs e)
        {
            sendSync = checkBoxSync.IsChecked.Value;
        }

        private void sendMastControl(byte data)
        {
            _canHandler.SendMsg(0x1E350201, new byte[] { data });
        }

        private void radioButtonBlackout100_Click(object sender, RoutedEventArgs e)
        {
            sendMastControl(0b11110011);
        }

        private void radioButtonBlackout50_Click(object sender, RoutedEventArgs e)
        {
            sendMastControl(0b11110111);
        }

        private void radioButtonBlackout10_Click(object sender, RoutedEventArgs e)
        {
            sendMastControl(0b11111011);
        }

        private void checkBoxOptikPod_Click(object sender, RoutedEventArgs e)
        {
            optikPodStatus = checkBoxOptikPod.IsChecked == true;
        }

        private void buttonSaveParam_Click(object sender, RoutedEventArgs e)
        {
            var para = ((Button)sender).CommandParameter as Parameter;
            if (para.NeuerWert != null) {
                var bytes = new byte[] {
                    0x22, 0xC0, 0x07, para.HPN,
                    (byte)(para.NeuerWert), (byte)(para.NeuerWert >> 8), 0x00, 0x00
                };
                _canHandler.SendMsg(0x1E650201, bytes);
                readParamIdx = 0;
            }
        }

        private void buttonRequestSWVersion_Click(object sender, RoutedEventArgs e)
        {
            _canHandler.SendMsg(0x1F656201, new byte[0]);
        }

        private void setInquireHUMS(int HPN, byte HTI, uint value = 0xFFFFFFFF)
        {
            var data = new byte[] {
                (byte)HPN,
                (byte)(HPN >> 8),
                (byte)(HPN >> 16),
                HTI,
                (byte)value,
                (byte)(value >> 8),
                (byte)(value >> 16),
                (byte)(value >> 24),
            };
            _canHandler.SendMsg(0x1E650201, data);
        }

        private void buttonInquire196_Click(object sender, RoutedEventArgs e)
        {
            setInquireHUMS(0x07C020, 196);
        }

        private void buttonInquire234_Click(object sender, RoutedEventArgs e)
        {
            setInquireHUMS(0x07C020, 234);
        }

        private void buttonInquire235_Click(object sender, RoutedEventArgs e)
        {
            setInquireHUMS(0x07C020, 235);
        }

        private void buttonInquire234a_Click(object sender, RoutedEventArgs e)
        {
            setInquireHUMS(0x07C021, 234);
        }

        private void buttonInquire194_Click(object sender, RoutedEventArgs e)
        {
            setInquireHUMS(0x07C020, 194);
        }

        private void buttonWrite196_Click(object sender, RoutedEventArgs e)
        {
            setInquireHUMS(0x07C020, 196, operatingHours);
        }

        private void buttonWrite234a_Click(object sender, RoutedEventArgs e)
        {
            setInquireHUMS(0x07C021, 234, hK);
        }

        private void buttonWrite194_Click(object sender, RoutedEventArgs e)
        {
            setInquireHUMS(0x07C020, 194, mastVariant);
        }

        private void textBoxOperatingHours_TextChanged(object sender, TextChangedEventArgs e)
        {
            uint.TryParse(textBoxOperatingHours.Text, out operatingHours);
        }

        private void textBoxHK_TextChanged(object sender, TextChangedEventArgs e)
        {
            uint.TryParse(textBoxHK.Text, out hK);
        }

        private void textBoxMastVariant_TextChanged(object sender, TextChangedEventArgs e)
        {
            uint.TryParse(textBoxMastVariant.Text, out mastVariant);
        }

        private void buttonSetMastEncoder_Click(object sender, RoutedEventArgs e)
        {
            setMastEncoder = true;
            queryMastEncoderStatus = true;
        }
    }
}
