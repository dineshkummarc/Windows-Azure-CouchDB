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

using System.Management.Automation;

namespace CouchDBDeployCmdlets.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.IO;
    using System.Reflection;
	using System.Runtime.Serialization;

    /// <summary>
    /// Persists the data which has to be shared across different powershell commands.
    /// </summary>
    public class Session
    {
        private const string PersistantFileName = "_couchSettings.xml";
        private static Session session = null;
        private static object gate = new object();

        /// <summary>
        /// File path where the data is persisted.
        /// </summary>
        private string path = null;
        private Dictionary<string, string> data = null;
        
        private Session(string path, bool isNew)
        {
            this.path = path;
            this.data = new Dictionary<string, string>();
            if (isNew)
            {
                this.ClearSession();
            }
            else
            {
                LoadData();
            }
        }

        /// <summary>
        /// Loads data from Persisted file.
        /// </summary>
        private void LoadData()
        {
            if (File.Exists(this.path))
            {
                string serializedContent = File.ReadAllText(path);
                this.data = XmlDeserialize<Dictionary<string,string>>(serializedContent);
            }
        }

        /// <summary>
        /// Persists data to a file.
        /// </summary>
        private void SaveData()
        {
            string serializedContent = XmlSerialize<Dictionary<string, string>>(this.data);
            File.WriteAllText(this.path, serializedContent);
        }

        /// <summary>
        /// Serializes object using DataContractSerializer
        /// </summary>
        /// <returns>Serialized content</returns>
        public static string XmlSerialize<T>(T obj)
        {
            var serializer = new DataContractSerializer(obj.GetType());
            using (MemoryStream ms = new MemoryStream())
            {
                serializer.WriteObject(ms, obj);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>
        /// Serializes string using DataContractSerializer
        /// </summary>
        /// <returns>deserialized object</returns>
        public static T XmlDeserialize<T>(string xml)
        {
            T obj = Activator.CreateInstance<T>();
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            {
                var serializer = new DataContractSerializer(obj.GetType());
                return (T)serializer.ReadObject(ms);
            }
        }
        
        /// <summary>
        /// Gets the current session.
        /// </summary>
        /// <returns></returns>
        public static Session GetSession(string location, bool isNew=false)
        {
            if (session == null)
            {
                lock (gate)
                {
                    if (session == null)
                    {
                        session = new Session(Path.Combine(location, PersistantFileName), isNew);
                    }
                }
            }

            return session;
        }

        /// <summary>
        /// Gets the data from session using key.
        /// </summary>
        public string this[string key]
        {
            get
            {
                string result;
                this.data.TryGetValue(key, out result);
                return result;
            }
            set
            {
                this.data[key] = value;
                // Not sure whether this line is required. If the caller can call Persist() then this line is not required.
                this.SaveData();
            }
        }

        /// <summary>
        /// Adds new data to session.
        /// </summary>
        public void Add(string key, string value)
        {
            this[key] = value;
        }

        /// <summary>
        /// Clears data in session.
        /// </summary>
        public void ClearSession()
        {
            this.data.Clear();
            this.SaveData();
        }

        /// <summary>
        /// Persists data in session.
        /// </summary>
        public void Persist()
        {
            this.SaveData();
        }
    }
}
