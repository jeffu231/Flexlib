// ****************************************************************************
///*!	\file ALE.cs
// *	\brief Contains ALE interface
// *
// *	\copyright	Copyright 2020 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2020-11-16
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using Flex.UiWpfFramework.Mvvm;


namespace Flex.Smoothlake.FlexLib
{
    public class ALE2G : ObservableObject
    {
        private Radio _radio;

        public ALE2G(Radio radio)
        {
            _radio = radio;
        }

        public void SendConfigCommand(string cmd)
        {
            _radio.SendCommand(cmd);
        }

        private bool _enable = false;
        public bool Enable
        {
            get { return _enable; }
            set
            {
                if (_enable != value)
                {
                    _enable = value;

                    string cmd;
                    if (value) cmd = "ale enable 2g=scan";
                    else cmd = "ale disable";
                    _radio.SendCommand(cmd);

                    RaisePropertyChanged("Enable");
                }
            }
        }

        private bool _link = false;
        public bool Link
        {
            get { return _link; }
            private set
            {
                if (_link != value)
                {
                    _link = value;
                    RaisePropertyChanged("Link");
                }
            }
        }

        private string _linkedStation = null;
        public string LinkedStation
        {
            get { return _linkedStation; }
            set
            {
                if (_linkedStation != value)
                {
                    _linkedStation = value;
                    RaisePropertyChanged("LinkedStation");
                }
            }
        }

        public void SetLink(ALE2GStation station)
        {
            // check parameter
            if (station == null) return;

            // are we already linked?
            if (_link == true)
            {
                // yes -- we're done here
                return;
            }

            _radio.SendCommand("ale link station=" + station.Name + " data");
        }

        public void Unlink()
        {
            if (_link == false) return;

            _radio.SendCommand("ale unlink");
        }

        private bool _sound = false;
        public bool Sound
        {
            get { return _sound; }
            set
            {
                if (_sound != value)
                {
                    _sound = value;
                    _radio.SendCommand("ale sound=" + Convert.ToByte(_sound));
                    RaisePropertyChanged("Sound");
                }
            }
        }

        public void SendAmd(ALE2GStation station, string msg, bool link)
        {
            if (link)
                _radio.SendCommand("ale link station=" + station.Name + " data \"text=" + msg +"\"");
            else
                _radio.SendCommand("ale msg station=" + station.Name + " \"text=" + msg + "\"");
        }

        public delegate void ALE2GAmdEventHandler(string from_station, string to_station, string msg);
        public event ALE2GAmdEventHandler ALE2GAmd;

        private void OnALE2GAmd(string from_station, string to_station, string msg)
        {
            if (ALE2GAmd != null)
                ALE2GAmd(from_station, to_station, msg);
        }


        private ALE2GStatus _status = null;
        public ALE2GStatus Status
        {
            get { return _status; }
        }

        private ALE2GConfig _config = null;
        public ALE2GConfig Config
        {
            get { return _config; }
        }

        private ALE2GMessage _message = null;
        public ALE2GMessage Message
        {
            get { return _message; }
        }

        private List<ALE2GStation> _stationList = new List<ALE2GStation>();
        public List<ALE2GStation> StationList
        {
            get
            {
                if (_stationList == null) return null;
                lock (_stationList)
                    return _stationList;
            }
        }

        public bool GetSelfStationName(out string self_name)
        {
            self_name = null;

            lock (_stationList)
            {
                foreach (ALE2GStation station in _stationList)
                {
                    if (station.Self)
                    {
                        self_name = station.Name;
                        return true;
                    }
                }
            }

            return false;
        }

        public delegate void ALE2GStationAddedEventHandler(ALE2GStation station);
        public event ALE2GStationAddedEventHandler ALE2GStationAdded;

        private void OnALE2GStationAdded(ALE2GStation station)
        {
            if (ALE2GStationAdded != null)
                ALE2GStationAdded(station);
        }

        public delegate void ALE2GStationRemovedEventHandler(ALE2GStation station);
        public event ALE2GStationRemovedEventHandler ALE2GStationRemoved;

        private void OnALE2GStationRemoved(ALE2GStation station)
        {
            if (ALE2GStationRemoved != null)
                ALE2GStationRemoved(station);
        }

        private void RemoveStation(string name)
        {
            ALE2GStation station_to_be_removed = null;
            lock (_stationList)
            {
                foreach (ALE2GStation station in _stationList)
                {
                    if (station.Name == name)
                    {
                        station_to_be_removed = station;
                        break;
                    }
                }

                if (station_to_be_removed != null)
                    _stationList.Remove(station_to_be_removed);
            }

            if(station_to_be_removed != null)
                OnALE2GStationRemoved(station_to_be_removed);
        }

        internal void ParseStatus(string s)
        {
            string[] words = s.Split(' ');

            switch (words[0])
            {
                case "status":
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine("ALE::ParseStatus - status: Too few words -- min 2 (" + words + ")");
                            return;
                        }

                        ALE2GStatus status = new ALE2GStatus();

                        string[] status_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "status"
                        foreach (string kv in status_words)
                        {
                            string[] tokens = kv.Split('=');
                            if (tokens.Length != 2)
                            {
                                Debug.WriteLine("ALE::ParseStatus - status: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            string key = tokens[0];
                            string value = tokens[1];

                            switch (key.ToLower())
                            {
                                case "mode": status.Mode = value; break;
                                case "state":
                                    {
                                        status.State = value;
                                        if (status.State.ToLower() == "linking" || status.State.ToLower() == "linked")
                                            Link = true;
                                        else
                                            Link = false;
                                        RaisePropertyChanged("Link");
                                        break;
                                    }

                                case "caller":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b)
                                        {
                                            Debug.WriteLine("ALE::ParseStatus - status - caller: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        status.Caller = Convert.ToBoolean(temp);
                                    }
                                    break;
                                case "other":
                                    {
                                        status.Other = value;
                                        LinkedStation = value;
                                    }
                                    break;
                                case "type": status.Type = value; break;
                                case "to_sinad": status.ToSinad = value; break;
                                case "from_sinad": status.FromSinad = value; break;
                                case "self": status.Self = value; break;
                            }
                        }

                        _status = status;
                        RaisePropertyChanged("Status");
                    }
                    break;
                case "config":
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine("ALE::ParseStatus - config: Too few words -- min 2 (" + words + ")");
                            return;
                        }

                        ALE2GConfig config = new ALE2GConfig();

                        string[] config_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "config"
                        foreach (string kv in config_words)
                        {
                            string[] tokens = kv.Split('=');
                            if (tokens.Length != 2)
                            {
                                Debug.WriteLine("ALE::ParseStatus - config: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            string key = tokens[0];
                            string value = tokens[1];

                            switch (key.ToLower())
                            {
                                case "name": config.Name = value; break;
                                case "desc": config.Description = value; break;
                                case "rate": config.Rate = value; break;
                                case "timeout": config.Timeout = value; break;
                                case "lbt": config.ListenBeforeTalk = value; break;
                                case "scan_retries": config.ScanRetries = value; break;
                                case "call_retries": config.CallRetries = value; break;
                                case "all":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b)
                                        {
                                            Debug.WriteLine("ALE::ParseStatus - config - all: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        config.All = Convert.ToBoolean(temp);
                                    }
                                    break;
                                case "any":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b)
                                        {
                                            Debug.WriteLine("ALE::ParseStatus - config - any: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        config.Any = Convert.ToBoolean(temp);
                                    }
                                    break;
                                case "wild":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b)
                                        {
                                            Debug.WriteLine("ALE::ParseStatus - config - wild: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        config.Wild = Convert.ToBoolean(temp);
                                    }
                                    break;
                                case "amd":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b)
                                        {
                                            Debug.WriteLine("ALE::ParseStatus - config - amd: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        config.Amd = Convert.ToBoolean(temp);
                                    }
                                    break;
                                case "dtm":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b)
                                        {
                                            Debug.WriteLine("ALE::ParseStatus - config - dtm: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        config.Dtm = Convert.ToBoolean(temp);
                                    }
                                    break;
                                case "sound":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b || temp > 1)
                                        {
                                            Debug.WriteLine("ALE::ParseStatus - config - sound: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        config.Sound = Convert.ToBoolean(temp);
                                    }
                                    break;
                            }
                        }

                        _config = config;
                        RaisePropertyChanged("Config");
                    }
                    break;
                case "station":
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine("ALE::ParseStatus - station: Too few words -- min 2 (" + words + ")");
                            return;
                        }

                        ALE2GStation station = new ALE2GStation();

                        string[] station_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "station"
                        foreach (string kv in station_words)
                        {
                            string[] tokens = kv.Split('=');
                            if (tokens.Length != 2)
                            {
                                Debug.WriteLine("ALE::ParseStatus - station: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            string key = tokens[0];
                            string value = tokens[1];

                            switch (key.ToLower())
                            {
                                case "name": station.Name = value; break;
                                case "self":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b || temp > 1)
                                        {
                                            Debug.WriteLine("ALE::ParseStatus - config - self: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        station.Self = Convert.ToBoolean(temp);
                                    }
                                    break;
                                case "addr": station.Address = value; break;
                                case "reply": station.Reply = value; break;
                                case "timeout": station.Timeout = value; break;
                                case "tune": station.Tune = value; break;
                                case "slot": station.Slot = value; break;
                            }
                        }

                        // is this a remove status?
                        if (words.Length == 3 && //"station name=<name> removed"
                            words[2] == "removed" &&
                                words[1].StartsWith("name="))
                        {
                            // yes -- remove the station
                            RemoveStation(station.Name);
                            RaisePropertyChanged("StationList");
                        }
                        else
                        {
                            // no -- add the station
                            lock (_stationList)
                            {
                                //if station  already exists, delete the old one to replace with new station object
                                ALE2GStation oldStation = _stationList.Find(stn => stn.Name == station.Name);
                                if (oldStation != null)
                                {
                                    RemoveStation(oldStation.Name);
                                }
                                //add the new station
                                _stationList.Add(station);
                            }
                            RaisePropertyChanged("StationList");

                            OnALE2GStationAdded(station);
                        }
                    }
                    break;
                case "amd":
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine("ALE::ParseStatus - amd: Too few words -- min 2 (" + words + ")");
                            return;
                        }

                        ALE2GMessage amd = new ALE2GMessage();

                        string[] amd_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "amd"
                        foreach (string kv in amd_words)
                        {
                            string[] tokens = kv.Split('=');
                            if (tokens.Length != 2)
                            {
                                // if we are receiving a message with multiple words, we have already seperated the words into their own tokens
                                // here we are appending the rest of the message to the first word
                                if (s.Contains("text") && amd.Message != null)
                                {
                                    amd.Message += " " + tokens[0];
                                }
                                Debug.WriteLine("ALE::ParseStatus - amd: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            string key = tokens[0];
                            string value = tokens[1];

                            switch (key.ToLower())
                            {
                                case "other": amd.Sender = value; break;
                                case "text": amd.Message = value; break;
                            }
                        }

                        if (!string.IsNullOrEmpty(amd.Message))
                        {
                            string self;
                            // Do we have a good name for the local 'self' station?
                            if (!GetSelfStationName(out self))
                            {
                                // no -- we don't have the info to move forward then.  Write out a debug and drop out.
                                Debug.WriteLine("ALE2G::ParseStatus Error: No 'self' station (self=true) found.");
                            }
                            else
                            {

                                //remove quotes around received messages if needed
                                if (amd.Message.StartsWith("\""))
                                {
                                    OnALE2GAmd(amd.Sender, self, amd.Message.Substring(1, amd.Message.Length - 2));
                                }
                                else
                                {
                                   OnALE2GAmd(amd.Sender, self, amd.Message);
                                }
                            }
                        }

                        _message = amd;
                        RaisePropertyChanged("Message");
                    }
                    break;
            }
        }
    }

    public class ALE2GStatus
    {
        public string Mode { get; set; }
        public string State { get; set; }
        public bool Caller { get; set; }
        public string Other { get; set; }
        public string Type { get; set; }
        public string ToSinad { get; set; }
        public string FromSinad { get; set; }
        public string Amd { get; set; }
        public string Self { get; set; }
    }

    public class ALE2GConfig
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Rate { get; set; }
        public string Timeout { get; set; }
        public string ListenBeforeTalk { get; set; }
        public string ScanRetries { get; set; }
        public string CallRetries { get; set; }
        public bool All { get; set; }
        public bool Any { get; set; }
        public bool Wild { get; set; }
        public bool Amd { get; set; }
        public bool Dtm { get; set; }
        public bool Sound { get; set; }
    }

    public class ALE2GStation
    {
        public string Name { get; set; }
        public bool Self { get; set; }
        public string Address { get; set; }
        public string Reply { get; set; }
        public string Timeout { get; set; }
        public string Tune { get; set; }
        public string Slot { get; set; }
    }
    public class ALE2GMessage
    {
        public string Message { get; set; }
        public string Sender { get; set; }
    }
}
