// ****************************************************************************
///*!	\file API.cs
// *	\brief Core FlexLib source
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2012-03-05
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;

using Flex.Util;
using System.Linq;
using System.Timers;

namespace Flex.Smoothlake.FlexLib
{
    public class API
    {
        const uint FLEX_OUI = 0x1C2D;

        private static List<Radio> radio_list;
        /// <summary>
        /// Contains a list of discovered Radios on the network
        /// </summary>
        public static List<Radio> RadioList
        {
            get
            {
                lock(radio_list)
                    return radio_list; 
            }
        }

        private static ConcurrentDictionary<string, Stopwatch> _radio_list_timed;
        public const double RADIOLIST_TIMEOUT_SECONDS = 17.0;

        private static ConcurrentDictionary<string, Radio> _radioDictionaryBySerial;

        private static List<string> filter_serial;

        private static string program_name;
        /// <summary>
        /// Sets the name of the program that is using this API
        /// </summary>
        public static string ProgramName
        {
            get { return program_name; }
            set { program_name = value; }
        }

        private static bool is_gui = false;
        /// <summary>
        /// Sets whether the program using this API is a GUI
        /// </summary>
        public static bool IsGUI
        {
            get { return is_gui; }
            set { is_gui = value; }
        }

        private static bool _logDiscovery = false;
        private static bool _logDisconnect = false;

        private static bool initialized = false;
        private static object init_obj = new Object();
        
        private static System.Timers.Timer _cleanupTimer = new System.Timers.Timer(1000);

        /// <summary>
        /// Creates a UDP socket, listens for new radios on the network, and adds them to the RadioList
        /// </summary>
        public static void Init()
        {
            // ensure that the initialized variable is atomically set here (i.e. only let one instance through here)
            lock (init_obj)
            {
                if (!initialized)
                {
                    initialized = true;

                    string log_enable_file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlexRadio Systems", "log_discovery.txt");
                    _logDiscovery = File.Exists(log_enable_file);

                    log_enable_file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) , "FlexRadio Systems","log_disconnect.txt");
                    _logDisconnect = File.Exists(log_enable_file);

                    LogDiscovery("API::Init()");

                    radio_list = new List<Radio>();
                    _radio_list_timed = new ConcurrentDictionary<string, Stopwatch>();

                    _radioDictionaryBySerial = new ConcurrentDictionary<string, Radio>();

                    filter_serial = new List<string>();
                    ProcessFilterFile();

                    Discovery.RadioDiscovered += new RadioDiscoveredEventHandler(Discovery_RadioDiscovered);
                    Discovery.Start();

                    WanServer.WanRadioRadioListRecieved += WanServer_WanRadioRadioListRecieved;

                    _cleanupTimer.AutoReset = true;
                    _cleanupTimer.Elapsed += RadioListMaid;
                    _cleanupTimer.Enabled = true;
                }
            }
        }

        public static void CloseSession()
        {
            Discovery.Stop();

            while(radio_list.Count > 0)
            {
                Radio r = radio_list[0];

                // since we are shutting down, ensure that we don't hang because we are in the middle of an update
                if (r.Updating) r.Updating = false;

                RemoveRadio(r);
                LogDisconnect("API::CloseSession(" + r.ToString() + ")--Application is closing");
            }

            initialized = false;
        }

        private static void RadioListMaid(object source, ElapsedEventArgs args)
        {
            // create a list to use to store radios to be removed
            var removeList = new List<Radio>();

            removeList.Clear();
            
            lock (radio_list)
            {
                removeList.AddRange(from r in radio_list
                    where r != null
                    where !r.Updating
                    where !r.Connected
                    where _radio_list_timed.ContainsKey(r.Serial) &&
                          _radio_list_timed[r.Serial].Elapsed.TotalSeconds > RADIOLIST_TIMEOUT_SECONDS
                    select r);
            }

            // now loop through the remove list and take action
            foreach (var r in removeList)
            {
                RemoveRadio(r);
                LogDisconnect($"API::CleanupRadioList_ThreadFunction({r})--Timeout waiting on Discovery");
            }
        }

        private static void ProcessFilterFile()
        {
            string dev_file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) , "FlexRadio Systems", "filter.txt");
            if (!File.Exists(dev_file)) return;

            TextReader reader = File.OpenText(dev_file);

            string buffer = reader.ReadToEnd();
            reader.Close();

            string[] lines = buffer.Split('\n');

            foreach (string s in lines)
            {
                string temp = s.Trim();
                if (temp.Length > 0)
                {
                    //Console.WriteLine("Adding " + s + " to filter list");
                    filter_serial.Add(temp);
                }
            }
        }

        private static void Discovery_RadioDiscovered(Radio discovered_radio)
        {
            //Log("1 API::Discovery_RadioDiscovered("+discovered_radio.ToString()+")");
            if (filter_serial.Count > 0)
            {
                bool found = false;
                foreach (string s in filter_serial)
                {
                    if (discovered_radio.Serial.Contains(s))
                    {
                        found = true;
                        //Debug.WriteLine("Found radio that matches filter: " + radio.Serial);
                        break;
                    }
                }

                if (!found) return;
            }

            // keep the radio alive in the list if it exists
            if (_radio_list_timed.ContainsKey(discovered_radio.Serial))
            {
                _radio_list_timed[discovered_radio.Serial].Restart();
            }

            
            Radio r = null;
            lock (radio_list)
            {
                if (_radioDictionaryBySerial.ContainsKey(discovered_radio.Serial))
                    r = _radioDictionaryBySerial[discovered_radio.Serial];

                if(r != null)
                {
                    if (r.Model == discovered_radio.Model && r.Serial == discovered_radio.Serial)
                    {
                        LogDiscovery("2 API::Discovery_RadioDiscovered(" + discovered_radio.ToString() + ") - IP/Model/Serial match found in list");
                        ulong ver_1_0 = FlexVersion.Parse("1.0.0.0");
                        if (r.DiscoveryProtocolVersion <= ver_1_0 &&
                            discovered_radio.DiscoveryProtocolVersion > ver_1_0)
                        {
                            LogDiscovery("3 API::Discovery_RadioDiscovered(" + discovered_radio.ToString() + ") - newer protocol, updating radio info");
                            r.DiscoveryProtocolVersion = discovered_radio.DiscoveryProtocolVersion;
                            r.Callsign = discovered_radio.Callsign;
                            r.Nickname = discovered_radio.Nickname;
                            r.Serial = discovered_radio.Serial;
                        }

                        if (discovered_radio.Version != r.Version)
                        {
                            LogDiscovery("4 API::Discovery_RadioDiscovered(" + discovered_radio.ToString() + ") - updating radio version");
                            Debug.WriteLine("Version Updated-" + r.ToString());
                            r.Version = discovered_radio.Version;
                            r.Updating = false;
                        }

                        // update the status if this is a newer discovery version
                        if (discovered_radio.DiscoveryProtocolVersion > ver_1_0)
                        {
                            if (discovered_radio.Status == "Available" && r.Status == "Updating")
                            {
                                LogDiscovery("5 API::Discovery_RadioDiscovered(" + discovered_radio.ToString() + ") - Radio coming out of update");
                                r.Updating = false;
                            }

                            if (r.Status != discovered_radio.Status)
                            {
                                LogDiscovery("5 API::Discovery_RadioDiscovered(" + discovered_radio.ToString() + ") - update radio status - " + discovered_radio.Status);
                                r.Status = discovered_radio.Status;
                            }

                            if (r.GuiClientIPs != discovered_radio.GuiClientIPs)
                                r.GuiClientIPs = discovered_radio.GuiClientIPs;

                            if (r.GuiClientHosts != discovered_radio.GuiClientHosts)
                                r.GuiClientHosts = discovered_radio.GuiClientHosts;

                            if (r.GuiClientStations != discovered_radio.GuiClientStations)
                                r.GuiClientStations = discovered_radio.GuiClientStations;
                        }

                        if (r.IsInternetConnected != discovered_radio.IsInternetConnected)
                            r.IsInternetConnected = discovered_radio.IsInternetConnected;

                        if (r.MaxLicensedVersion != discovered_radio.MaxLicensedVersion)
                            r.MaxLicensedVersion = discovered_radio.MaxLicensedVersion;

                        if (r.RequiresAdditionalLicense != discovered_radio.RequiresAdditionalLicense)
                            r.RequiresAdditionalLicense = discovered_radio.RequiresAdditionalLicense;

                        if (r.FrontPanelMacAddress != discovered_radio.FrontPanelMacAddress)
                            r.FrontPanelMacAddress = discovered_radio.FrontPanelMacAddress;

                        if (r.RadioLicenseId != discovered_radio.RadioLicenseId)
                            r.RadioLicenseId = discovered_radio.RadioLicenseId;

                        if (r.Callsign != discovered_radio.Callsign)
                            r.Callsign = discovered_radio.Callsign;

                        if (r.Nickname != discovered_radio.Nickname)
                            r.Nickname = discovered_radio.Nickname;

                        if (!r.IP.Equals(discovered_radio.IP))
                            r.IP = discovered_radio.IP;

                        if (r.LicensedClients != discovered_radio.LicensedClients)
                            r.LicensedClients = discovered_radio.LicensedClients;

                        if (r.AvailableClients != discovered_radio.AvailableClients)
                            r.AvailableClients = discovered_radio.AvailableClients;

                        if (r.MaxPanadapters != discovered_radio.MaxPanadapters)
                            r.MaxPanadapters = discovered_radio.MaxPanadapters;

                        if (r.AvailablePanadapters != discovered_radio.AvailablePanadapters)
                            r.AvailablePanadapters = discovered_radio.AvailablePanadapters;

                        if (r.MaxSlices != discovered_radio.MaxSlices)
                            r.MaxSlices = discovered_radio.MaxSlices;

                        if (r.AvailableSlices != discovered_radio.AvailableSlices)
                            r.AvailableSlices = discovered_radio.AvailableSlices;

                        r.UpdateGuiClientsList(newGuiClients: discovered_radio.GuiClients);

                        //Debug.WriteLine("Skipping Radio -- already in list: "+radio.ToString());
                        return;
                    }
                }

                Debug.WriteLine("Discovered " + discovered_radio.ToString());
                LogDiscovery("6 API::Discovery_RadioDiscovered(" + discovered_radio.ToString() + ") - Add radio to list");

                radio_list.Add(discovered_radio);
                bool b = _radioDictionaryBySerial.TryAdd(discovered_radio.Serial, discovered_radio);
            }

            if (!_radio_list_timed.ContainsKey(discovered_radio.Serial))
                _radio_list_timed.TryAdd(discovered_radio.Serial, Stopwatch.StartNew());
            
            OnRadioAddedEventHandler(discovered_radio);
            //Debug.WriteLine("Adding Radio: " + radio.ToString());
        }

        private static void WanServer_WanRadioRadioListRecieved(List<Radio> radios)
        {
            OnWanListReceivedEventHandler(radios);
        }

        public delegate void WanListReceivedEventHandler(List<Radio> radios);
        /// <summary>
        /// This event fires when a new radio on the network has been detected
        /// </summary>
        public static event WanListReceivedEventHandler WanListReceived;

        public static void OnWanListReceivedEventHandler(List<Radio> radios)
        {
            LogDiscovery("8 API::OnWanListReceivedEventHandler(" + radios.ToString() + ")");

            // filter out WAN radios with filter.txt
            for (int i = 0; i < radios.Count; i++)
            {
                Radio radio = radios[i];

                if (filter_serial.Count > 0)
                {
                    bool found = false;
                    foreach (string s in filter_serial)
                    {
                        if (radio.Serial.Contains(s))
                        {
                            found = true;
                            //Debug.WriteLine("Found radio that matches filter: " + radio.Serial);
                            break;
                        }
                    }

                    if (!found)
                    {
                        radios.RemoveAt(i);
                        i--;
                    }
                }
            }

            if (WanListReceived != null)
                WanListReceived(radios);
        }

        public delegate void RadioAddedEventHandler(Radio radio);
        /// <summary>
        /// This event fires when a new radio on the network has been detected
        /// </summary>
        public static event RadioAddedEventHandler RadioAdded;

        public static void OnRadioAddedEventHandler(Radio radio)
        {
            LogDiscovery("7 API::OnRadioAddedEventHandler("+radio.ToString()+ ")");
            if (RadioAdded != null)
                RadioAdded(radio);
        }

        public delegate void RadioRemovedEventHandler(Radio radio);
        public static event RadioRemovedEventHandler RadioRemoved;

        public static void OnRadioRemovedEventHandler(Radio radio)
        {
            LogDiscovery("8 API::OnRadioRemovedEventHandler(" + radio.ToString() + ")");
            if(RadioRemoved != null)
                RadioRemoved(radio);
        }

        internal static bool RemoveRadio(Radio radio)
        {
            LogDiscovery("9 API::RemoveRadio(" + radio.ToString() + ")");
            if (radio.Updating) return false; // don't remove the radio if we're just updating
                        
            lock (radio_list)
            {
                // if the radio isn't the list, we're done here
                if (!radio_list.Contains(radio)) return false;

                radio_list.Remove(radio);
                Radio removed_radio;
                bool b = _radioDictionaryBySerial.TryRemove(radio.Serial, out removed_radio);
            }

            if (_radio_list_timed.ContainsKey(radio.Serial))
            {
                _radio_list_timed.TryRemove(radio.Serial, out var watch);
                watch.Stop();
            }

            OnRadioRemovedEventHandler(radio);

            // disconnect the radio object
            if (radio.Connected)
                radio.Disconnect();

            return true;
        }

        private static void LogDiscovery(string msg)
        {
            if(!_logDiscovery) return;

            string log_data_path_name = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FlexRadio Systems", "LogFiles", "SSDR_Discovery.log");

            try
            {
                TextWriter writer = new StreamWriter(log_data_path_name, true);
                string app_name = System.AppDomain.CurrentDomain.FriendlyName;
                string timestamp = DateTime.Now.ToShortDateString() + "  " + DateTime.Now.ToString("HH:mm:ss");
                writer.WriteLine(timestamp + " " + app_name + ": "+ msg);
                writer.Close();
            }
            catch (Exception)
            {

            }
        }

        internal static void LogDisconnect(string msg)
        {
            if (!_logDisconnect) return;

            string log_data_path_name = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FlexRadio Systems", "LogFiles", "SSDR_Disconnect.log");

            try
            {
                TextWriter writer = new StreamWriter(log_data_path_name, true);
                string app_name = System.AppDomain.CurrentDomain.FriendlyName;
                string timestamp = DateTime.Now.ToShortDateString() + "  " + DateTime.Now.ToString("HH:mm:ss");
                writer.WriteLine(timestamp + " " + app_name + ": " + msg);
                writer.Close();
            }
            catch (Exception)
            {

            }
        }
    }
}
