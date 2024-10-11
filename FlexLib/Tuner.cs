// ****************************************************************************
///*!	\file Tuner.cs
// *	\brief Represents a single hardware tuner
// *
// *	\copyright	Copyright 2024 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2024-04-01
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

using Flex.UiWpfFramework.Mvvm;
using System.Diagnostics;


namespace Flex.Smoothlake.FlexLib
{
    public enum TunerState
    {
        PowerUp,
        SelfCheck,
        Standby,
        Operate,
        Bypass,
        Fault,
        Unknown
    }

    public class Tuner : ObservableObject
    {
        // Variable Declaration
        private Radio _radio;

        private string _handle;
        public string Handle
        {
            get => _handle;
        }

        private string _serialNumber;
        public string SerialNumber
        {
            get => _serialNumber;
            private set
            {
                if (value != _serialNumber)
                {
                    _serialNumber = value;
                    RaisePropertyChanged(nameof(SerialNumber));
                }
            }
        }

        private string _version;
        public string Version
        {
            get => _version;
            private set
            {
                if (value != _version)
                {
                    _version = value;
                    RaisePropertyChanged(nameof(Version));
                }
            }
        }

        private string _nickname;
        public string Nickname
        {
            get => _nickname;
            private set
            {
                if (_nickname != value)
                {
                    _nickname = value;
                    RaisePropertyChanged(nameof(Nickname));
                }
            }
        }

        public string Model { get; private set; }

        private bool _one_by_three = false;
        public bool OneByThree
        {
            get => _one_by_three;
            private set
            {
                if (_one_by_three != value)
                {
                    _one_by_three = value;
                    RaisePropertyChanged(nameof(OneByThree));
                }
            }
        }

        private IPAddress _ip;
        public IPAddress IP 
        {
            get => _ip;
            private set
            {
                if (_ip != value)
                {
                    _ip = value;
                    RaisePropertyChanged(nameof(IP));
                }
            }
        }

        private IPAddress _netmask;
        public IPAddress Netmask
        {
            get => _netmask;
            private set
            {
                if (_netmask != value)
                {
                    _netmask = value;
                    RaisePropertyChanged(nameof(Netmask));
                }
            }
        }

        private IPAddress _gateway;
        public IPAddress Gateway
        {
            get => _gateway;
            private set
            {
                if (_gateway != value)
                {
                    _gateway = value;
                    RaisePropertyChanged(nameof(Gateway));
                }
            }
        }

        public int Port { get; private set; }

        private string _ant;
        public string Ant
        {
            get => _ant;
            set
            {
                if (_ant == value) return;

                _ant = value;
                ParseAntennaSettings(_ant);
                RaisePropertyChanged(nameof(Ant));
            }
        }

        private Dictionary<string, string> _antennaSettingsDict = new Dictionary<string, string>();
        private void ParseAntennaSettings(string s)
        {
            Dictionary<string, string> new_ant_settings_dict = new Dictionary<string, string>();

            string[] ant_setting_pairs = s.Split(',');
            foreach (string ant_setting_pair in ant_setting_pairs)
            {
                if (!ant_setting_pair.Contains(":")) continue;

                string[] settings = ant_setting_pair.Split(':');

                if (settings.Length != 2) continue;

                new_ant_settings_dict.Add(settings[0], settings[1]);
            }

            _antennaSettingsDict = new_ant_settings_dict;
        }

        /// <summary>
        /// Returns the name of the output associated with the ant given the current configuration of the amplifier
        /// </summary>
        /// <param name="ant">The radio antenna port name</param>
        /// <returns>The name of the output associated with the radio antenna port, or null if not configured for that port</returns>
        public string OutputConfiguredForAntenna(string ant)
        {
            if (_antennaSettingsDict == null || !_antennaSettingsDict.Keys.Contains(ant)) return null;

            return _antennaSettingsDict[ant];
        }

        private List<Meter> _meters = new List<Meter>();

        private void UpdateState()
        {
            if (!_isOperate)
            {
                State = TunerState.Standby;
            }
            else
            {
                if (!_isBypass) State = TunerState.Operate;
                else State = TunerState.Bypass;
            }
        }

        private TunerState _state = TunerState.Unknown;
        public TunerState State
        {
            get => _state;
            internal set
            {
                if (_state == value) return;

                _state = value;
                RaisePropertyChanged(nameof(State));
            }
        }

        private bool _isOperate = false; // also known as "not standby"
        public bool IsOperate
        {
            get => _isOperate;
            set
            {
                if (_isOperate != value)
                {
                    _isOperate = value;
                    _radio.SendCommand("tgxl set handle=" + _handle + " mode=" + Convert.ToByte(_isOperate));
                    RaisePropertyChanged(nameof(IsOperate));

                    UpdateState();
                }
            }
        }

        private bool _isBypass = false;
        public bool IsBypass
        {
            get => _isBypass;
            set
            {
                if (_isBypass != value)
                {
                    _isBypass = value;
                    _radio.SendCommand("tgxl set handle=" + _handle + " bypass=" + Convert.ToByte(_isBypass));
                    RaisePropertyChanged(nameof(IsBypass));

                    UpdateState();
                }
            }
        }

        int _relayC1 = 0;
        public int RelayC1
        {
            get => _relayC1;
            internal set
            {
                if (_relayC1 == value) return;

                _relayC1 = value;
                RaisePropertyChanged(nameof(RelayC1));
            }
        }

        int _relayC2 = 0;
        public int RelayC2
        {
            get => _relayC2;
            internal set
            {
                if (_relayC2 == value) return;

                _relayC2 = value;
                RaisePropertyChanged(nameof(RelayC2));
            }
        }
        int _relayL = 0;
        public int RelayL
        {
            get => _relayL;
            internal set
            {
                if (_relayL == value) return;

                _relayL = value;
                RaisePropertyChanged(nameof(RelayL));
            }
        }

        public void AutoTune()
        {
            if (_ant != _radio.TransmitSlice.TXAnt)
            {
                Debug.WriteLine("AutoTune skipped as configured Antenna doesn't match TX Slice TX Ant");
                return;
            }

            if (_radio.InterlockState != InterlockState.Ready)
            {
                Debug.WriteLine("AutoTune skipped as interlock is not ready (" + _radio.InterlockState.ToString() + ")");
                return;
            }

            if (!IsOperate) IsOperate = true;
            if (IsBypass) IsBypass = false;
            
            _radio.SendCommand("tgxl autotune handle=" + _handle);
        }

        // Constructor
        public Tuner(Radio radio, string handle)
        {
            _radio = radio;
            _handle = handle;

            foreach (Meter m in _radio.FindMetersByTuner(this))
                AddMeter(m);
        }

        #region Meter Routines

        internal void AddMeter(Meter m)
        {
            lock (_meters)
            {
                if (!_meters.Contains(m))
                {
                    _meters.Add(m);
                    OnMeterAdded(m);
                }
            }
        }

        internal void RemoveMeter(Meter m)
        {
            lock (_meters)
            {
                if (_meters.Contains(m))
                {
                    _meters.Remove(m);
                    OnMeterRemoved(m);
                }
            }
        }

        public delegate void MeterAddedEventHandler(Tuner tuner, Meter m);
        public event MeterAddedEventHandler MeterAdded;
        private void OnMeterAdded(Meter m)
        {
            if (MeterAdded != null)
                MeterAdded(this, m);
        }

        public delegate void MeterRemovedEventHandler(Tuner tuner, Meter m);
        public event MeterRemovedEventHandler MeterRemoved;
        private void OnMeterRemoved(Meter m)
        {
            if (MeterRemoved != null)
                MeterRemoved(this, m);
        }

        public Meter FindMeterByIndex(int index)
        {
            lock (_meters)
                return _meters.FirstOrDefault(m => m.Index == index);
        }

        public Meter FindMeterByName(string s)
        {
            lock (_meters)
                return _meters.FirstOrDefault(m => m.Name == s);
        }

        #endregion

        public void StatusUpdate(string s)
        {
            string[] words = s.Split(' ');
            //Debug.WriteLine("Tuner Status: " + s);

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Tuner::StatusUpdate: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "serial_num": SerialNumber = value; break;
                    case "version": Version = value; break;
                    case "nickname": Nickname = value; break;
                    case "model": Model = value; break;
                    case "one_by_three":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            OneByThree = Convert.ToBoolean(temp);
                        }
                        break;
                    case "ip":
                        {
                            IPAddress temp;
                            bool b = IPAddress.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            IP = temp;
                        }
                        break;
                    case "netmask":
                        {
                            IPAddress temp;
                            bool b = IPAddress.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            Netmask = temp;
                        }
                        break;
                    case "gateway":
                        {
                            IPAddress temp;
                            bool b = IPAddress.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            Gateway = temp;
                        }
                        break;
                    case "ant": Ant = value; break;
                    case "operate":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_isOperate == Convert.ToBoolean(temp))
                                continue;

                            _isOperate = Convert.ToBoolean(temp);
                            RaisePropertyChanged(nameof(IsOperate));
                            UpdateState();
                        }
                        break;
                    case "bypass":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_isOperate == Convert.ToBoolean(temp))
                                continue;

                            _isOperate = Convert.ToBoolean(temp);
                            RaisePropertyChanged(nameof(IsOperate));
                            UpdateState();
                        }
                        break;
                    case "relayC1":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b || temp > 0xFF)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_relayC1 == temp) continue;

                            _relayC1 = temp;
                            RaisePropertyChanged(nameof(RelayC1));
                        }
                        break;
                    case "relayC2":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b || temp > 0xFF)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_relayC2 == temp) continue;

                            _relayC2 = temp;
                            RaisePropertyChanged(nameof(RelayC2));
                        }
                        break;
                    case "relayL":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b || temp > 0xFF)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_relayL == temp) continue;

                            _relayL = temp;
                            RaisePropertyChanged(nameof(RelayL));
                        }
                        break;
                    default:
                        Debug.WriteLine("Tuner::StatusUpdate: Unknown Key (" + kv + ")"); 
                        break;
                }
            }
        }
    }
}
