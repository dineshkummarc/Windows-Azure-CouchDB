#region Copyright Notice
/*
Copyright © Microsoft Open Technologies, Inc.
All Rights Reserved
Apache 2.0 License

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

     http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.

See the Apache Version 2.0 License for specific language governing permissions and limitations under the License.
*/
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.WindowsAzure.ServiceRuntime;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Collections.Specialized;
using System.IO;
using System.ComponentModel;

namespace HelperLib
{
    public class Util
    {
        public static void AddRoleInfoEntry(string roleId, string ipString, int port)
        {
            RoleInfoDataSource rids = new RoleInfoDataSource();
            rids.AddRoleInfoEntity(new RoleInfoEntity(roleId, ipString, port));
        }

        public static void RemoveRoleInfoEntry(string roleId, string ipString, int port)
        {
            RoleInfoDataSource rids = new RoleInfoDataSource();
            rids.RemoveRoleInfoEntity(roleId, ipString, port);
        }

        // get internal endpoint addreses of all other couchdb instances
        public static IEnumerable<string> GetReplicationSrcAddresses()
        {
            RoleInfoDataSource rids = new RoleInfoDataSource();
            List<string> addresses = new List<string>();
            string myEndpointAddress = RoleEnvironment.CurrentRoleInstance.InstanceEndpoints["CouchEndpoint"].IPEndpoint.ToString();

            foreach (IPEndPoint endpoint in rids.GetAllEndpoints())
            {
                if (endpoint.ToString() == myEndpointAddress) // skip our own endpoint
                    continue;

                if (CheckEndpoint(endpoint))
                    addresses.Add(endpoint.ToString());
            }

            return addresses;
        }

        // returns null if the url could not be obtained for any reason (such as the role was not available)
        public static string GetCouchUrl(int iInstance = -1)
        {
            string url = null;

            try
            {
                // Worker role access:
                IPEndPoint endpoint = GetCouchEndpoint(iInstance);
                if (endpoint == null)
                    return null;

                url = string.Format("http://{0}/", endpoint);
            }
            catch { }

            return url;
        }

        /// <summary>
        /// Get the url associated with the worker instance. If running with a warm standby instance
        /// it returns the address on which CouchDB is actually listening.
        /// Specify iInstance = -1 to get the endpoint of any instance of that type that may be actively listening.
        /// </summary>
        private static IPEndPoint GetCouchEndpoint(int iInstance)
        {
            var roleInstances = RoleEnvironment.Roles["CouchHostWorkerRole"].Instances;
            IPEndPoint couchEndpoint = null;

            if (iInstance >= 0)
                couchEndpoint = GetEndpoint(roleInstances[iInstance]);
            else
            {
                foreach (var instance in roleInstances)
                {
                    couchEndpoint = GetEndpoint(instance);
                    if (couchEndpoint == null)
                        continue;
                    
                    break;
                }
            }

            if (couchEndpoint == null)
                return null;

            return couchEndpoint;
        }

        private static IPEndPoint GetEndpoint(RoleInstance instance)
        {
            IPEndPoint endpoint;
            RoleInfoDataSource rids = new RoleInfoDataSource();

            endpoint = rids.GetInstanceEndpoint(instance.Id);

            if (!CheckEndpoint(endpoint))
                return null;

            return endpoint;
        }

        public static int GetNumInstances(bool bMaster)
        {
            var roleInstances = RoleEnvironment.Roles["CouchHostWorkerRole"].Instances;
            return roleInstances.Count();
        }

        private static bool CheckEndpoint(IPEndPoint couchEndpoint)
        {
            var valid = false;
            using (var s = new Socket(couchEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
            {
                try
                {
                    s.Connect(couchEndpoint);
                    if (s.Connected)
                    {
                        valid = true;
                        s.Disconnect(true);
                    }
                    else
                    {
                        valid = false;
                    }
                }
                catch
                {
                    valid = false;
                }
            }

            return valid;
        }
    }

    public class IniFile
    {
        Dictionary<string, NameValueCollection> data = 
            new Dictionary<string,NameValueCollection>();

        static readonly Regex regRemoveEmptyLines =
            new Regex
            (
                @"(\s*;[\d\D]*?\r?\n)+|\r?\n(\s*\r?\n)*", 
                RegexOptions.Multiline | RegexOptions.Compiled
            );

        static readonly Regex regParseIniData =
            new Regex
            (
                @"
                (?<IsSection>
                    ^\s*\[(?<SectionName>[^\]]+)?\]\s*$
                )
                |
                (?<IsKeyValue>
                    ^\s*(?<Key>[^(\s*\=\s*)]+)?\s*\=\s*(?<Value>[\d\D]*)$
                )",
                RegexOptions.Compiled | 
                RegexOptions.IgnoreCase | 
                RegexOptions.IgnorePatternWhitespace
            );

        public IniFile()
        {
            readIniData(null, null);
        }

        public IniFile(string fileName)
            : this(fileName, Encoding.UTF8)
        { }

        public IniFile(string fileName, Encoding encoding)
        {
            using (FileStream fs = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                readIniData(fs, encoding);
            }
        }

        public IniFile(Stream stream)
            : this(stream, Encoding.UTF8)
        {
        }

        public IniFile(Stream stream, Encoding encoding)
        {
            if (stream == null || stream == Stream.Null)
                throw new ArgumentNullException("stream");
            if (encoding == null)
                throw new ArgumentNullException("encoding");

            readIniData(stream, encoding);
        }

        private void readIniData(Stream stream, Encoding encoding)
        {
            string lastSection = string.Empty;
            data.Add(lastSection, new NameValueCollection());
            if (stream != null && encoding != null)
            {
                string iniData;
                using 
                (
                    StreamReader reader = 
                        new StreamReader(stream, encoding)
                )
                    iniData = reader.ReadToEnd();

                iniData = regRemoveEmptyLines.Replace(iniData, "\n");
                
                string[] lines = 
                    iniData.Split
                    (
                        new char[] { '\n' }, 
                        StringSplitOptions.RemoveEmptyEntries
                    );

                foreach (string s in lines)
                {
                    Match m = regParseIniData.Match(s);
                    if (m.Success)
                    {
                        if (m.Groups["IsSection"].Length > 0)
                        {
                            string sName = 
                                m.Groups["SectionName"].Value.
                                ToLowerInvariant();

                            if (lastSection != sName)
                            {
                                lastSection = sName;
                                if (!data.ContainsKey(sName))
                                {
                                    data.Add
                                    (
                                        sName, 
                                        new NameValueCollection()
                                    );
                                }
                            }
                        }
                        else if (m.Groups["IsKeyValue"].Length > 0)
                        {
                            data[lastSection].Add
                            (
                                m.Groups["Key"].Value, 
                                m.Groups["Value"].Value
                            );
                        }
                    }
                }
            }
        }

        public NameValueCollection this[string section]
        {
            get
            {
                section = section.ToLowerInvariant();
                if (!data.ContainsKey(section))
                    data.Add(section, new NameValueCollection());
                return data[section];
            }
        }

        public string this[string section, string key]
        {
            get
            {
                return this[section][key];
            }
            set
            {
                this[section][key] = value;
            }
        }

        public object this[string section, string key, Type t]
        {
            get
            {
                if(t == null || t == Type.Missing)
                    return this[section][key];
                return GetValue(section, key, null, t);
            }
            set
            {
                if (t == null || t == Type.Missing)
                    this[section][key] = String.Empty;
                else
                    SetValue(section, key, value);
            }
        }

        public string[] SectionNames
        {
            get
            {
                string[] all = new string[data.Count];
                data.Keys.CopyTo(all, 0);
                return all;
            }
        }

        public string[] KeyNames(string section)
        {
            return this[section].AllKeys;
        }

        public string[] SectionValues(string section)
        {
            return this[section].GetValues(0);
        }

        public object GetValue(string section, string key, object defaultValue, Type t)
        {
            if (!data.ContainsKey(section))
                return defaultValue;
            string v = data[section][key];
            if (string.IsNullOrEmpty(v))
                return defaultValue;
            TypeConverter conv = TypeDescriptor.GetConverter(t);
            if (conv == null)
                return defaultValue;
            if (!conv.CanConvertFrom(typeof(string)))
                return defaultValue;
            try
            {
                return conv.ConvertFrom(v);
            }
            catch
            {
                return defaultValue;
            }
        }

        public T GetValue<T>(string section, string key, T defaultValue)
        {
            return (T)GetValue(section, key, defaultValue, typeof(T));
        }
        
        public T GetValue<T>(string section, string key)
        {
            return GetValue<T>(section, key, default(T));
        }

        public Boolean GetBoolean(string section, string key, Boolean defaultValue)
        {
            return GetValue<Boolean>(section, key);
        }
       
        public Boolean GetBoolean(string section, string key)
        {
            return GetBoolean(section, key, default(Boolean));
        }
        
        public Byte GetByte(string section, string key, Byte defaultValue)
        {
            return GetValue<Byte>(section, key);
        }
        
        public Byte GetByte(string section, string key)
        {
            return GetByte(section, key, default(Byte));
        }
        
        public SByte GetSByte(string section, string key, SByte defaultValue)
        {
            return GetValue<SByte>(section, key);
        }
        
        public SByte GetSByte(string section, string key)
        {
            return GetSByte(section, key, default(SByte));
        }
        
        public Int16 GetInt16(string section, string key, Int16 defaultValue)
        {
            return GetValue<Int16>(section, key);
        }
        
        public Int16 GetInt16(string section, string key)
        {
            return GetInt16(section, key, default(Int16));
        }
        
        public UInt16 GetUInt16(string section, string key, UInt16 defaultValue)
        {
            return GetValue<UInt16>(section, key);
        }
        
        public UInt16 GetUInt16(string section, string key)
        {
            return GetUInt16(section, key, default(UInt16));
        }
        
        public Int32 GetInt32(string section, string key, Int32 defaultValue)
        {
            return GetValue<Int32>(section, key);
        }
        
        public Int32 GetInt32(string section, string key)
        {
            return GetInt32(section, key, default(Int32));
        }
        
        public UInt32 GetUInt32(string section, string key, UInt32 defaultValue)
        {
            return GetValue<UInt32>(section, key);
        }
        
        public UInt32 GetUInt32(string section, string key)
        {
            return GetUInt32(section, key, default(UInt32));
        }
        
        public Int64 GetInt64(string section, string key, Int64 defaultValue)
        {
            return GetValue<Int64>(section, key);
        }
        
        public Int64 GetInt64(string section, string key)
        {
            return GetInt64(section, key, default(Int64));
        }
        
        public UInt64 GetUInt64(string section, string key, UInt64 defaultValue)
        {
            return GetValue<UInt64>(section, key);
        }
        
        public UInt64 GetUInt64(string section, string key)
        {
            return GetUInt64(section, key, default(UInt64));
        }
        
        public Single GetSingle(string section, string key, Single defaultValue)
        {
            return GetValue<Single>(section, key);
        }
        
        public Single GetSingle(string section, string key)
        {
            return GetSingle(section, key, default(Single));
        }
        
        public Double GetDouble(string section, string key, Double defaultValue)
        {
            return GetValue<Double>(section, key);
        }
        
        public Double GetDouble(string section, string key)
        {
            return GetDouble(section, key, default(Double));
        }

        public Decimal GetDecimal(string section, string key, Decimal defaultValue)
        {
            return GetValue<Decimal>(section, key);
        }

        public Decimal GetDecimal(string section, string key)
        {
            return GetDecimal(section, key, default(Decimal));
        }

        public DateTime GetDateTime(string section, string key, DateTime defaultValue)
        {
            return GetValue<DateTime>(section, key);
        }

        public DateTime GetDateTime(string section, string key)
        {
            return GetDateTime(section, key, default(DateTime));
        }

        public void SetValue(string section, string key, object value)
        {
            if (value == null)
            {
                this[section][key] = String.Empty;
            }
            else
            {
                TypeConverter conv = TypeDescriptor.GetConverter(value);
                if (conv == null || !conv.CanConvertTo(typeof(string)))
                {
                    this[section][key] = value.ToString();
                }
                else
                {
                    this[section][key] = (string)conv.ConvertTo(value, typeof(string));
                }
            }
        }

        public void SetValue(string section, string key, Boolean value)
        {
            SetValueToString(section, key, value);
        }

        public void SetValue(string section, string key, Byte value)
        {
            SetValueToString(section, key, value);
        }

        public void SetValue(string section, string key, SByte value)
        {
            SetValueToString(section, key, value);
        }

        public void SetValue(string section, string key, Int16 value)
        {
            SetValueToString(section, key, value);
        }

        public void SetValue(string section, string key, Int32 value)
        {
            SetValueToString(section, key, value);
        }

        public void SetValue(string section, string key, Int64 value)
        {
            SetValueToString(section, key, value);
        }

        public void SetValue(string section, string key, UInt16 value)
        {
            SetValueToString(section, key, value);
        }

        public void SetValue(string section, string key, UInt32 value)
        {
            SetValueToString(section, key, value);
        }

        public void SetValue(string section, string key, UInt64 value)
        {
            SetValueToString(section, key, value);
        }

        public void SetValue(string section, string key, Single value)
        {
            SetValueToString(section, key, value);
        }

        public void SetValue(string section, string key, Double value)
        {
            SetValueToString(section, key, value);
        }

        public void SetValue(string section, string key, Decimal value)
        {
            SetValueToString(section, key, value);
        }

        public void SetValue(string section, string key, DateTime value)
        {
            SetValueToString(section, key, value);
        }

        public bool HasSection(string section)
        {
            return data.ContainsKey(section.ToLowerInvariant());
        }

        public bool HasKey(string section, string key)
        {
            return
                data.ContainsKey(section) &&
                !string.IsNullOrEmpty(data[section][key]);
        }

        public void Save(string fileName)
        {
            Save(fileName, Encoding.UTF8);
        }

        public void Save(string fileName, Encoding encoding)
        {
            using (FileStream fs = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                Save(fs, encoding);
            }
        }

        public void Save(Stream stream)
        {
            Save(stream, Encoding.UTF8);
        }

        public void Save(Stream stream, Encoding encoding)
        {
            if (stream == null || stream == Stream.Null)
                throw new ArgumentNullException("stream");
            if (encoding == null)
                throw new ArgumentNullException("encoding");
            using (StreamWriter sw = new StreamWriter(stream, encoding))
            {
                Dictionary<string, NameValueCollection>.Enumerator en =
                    data.GetEnumerator();
                while (en.MoveNext())
                {
                    KeyValuePair<string, NameValueCollection> cur =
                        en.Current;
                    if (!string.IsNullOrEmpty(cur.Key))
                    {
                        sw.WriteLine("[{0}]", cur.Key);
                    }
                    NameValueCollection col = cur.Value;
                    foreach (string key in col.Keys)
                    {
                        if (!string.IsNullOrEmpty(key))
                        {
                            string value = col[key];
                            if (!string.IsNullOrEmpty(value))
                                sw.WriteLine("{0}={1}", key, value);
                        }
                    }
                }
                sw.Flush();
            }
        }

        void SetValueToString(string section, string key, object value)
        {
            this[section][key] = value.ToString();
        }

    }
}
