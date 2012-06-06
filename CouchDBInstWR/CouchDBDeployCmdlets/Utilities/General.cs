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
using System.IO;
using System.Xml.Serialization;
using CouchDBDeployCmdlets.Properties;
using Microsoft.Win32;
using AzureDeploymentCmdlets.Utilities;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;

namespace CouchDBDeployCmdlets.Utilities
{
    public class General
    {
        private static General instance;

        public string AzureSDKBinFolder()
        {
            string installPath = null;
            RegistryKey serviceHost = Registry.LocalMachine.OpenSubKey("software\\microsoft\\microsoft sdks\\servicehosting");
            if (serviceHost!=null)
            {
                string[] versions = serviceHost.GetSubKeyNames();
                if (versions!=null && versions.Length>0)
                {
                    Array.Sort<string>(versions);
                    string currentVersion = versions[versions.Length-1];
                    installPath = serviceHost.OpenSubKey(currentVersion).GetValue("InstallPath").ToString();
                    installPath += "bin";
                }
            }
            else
            {
              installPath = string.Format("{0}/{1}", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Resources.AzureSdkBinFolder);
            }

            return installPath;
        }

        public string GetAzureCouchDBInstallFolder()
        {
            string installFolder;

            try
            {
                installFolder = (string)Registry.CurrentUser.
                   CreateSubKey(Resources.AzureCouchDBRegistrySectionName).
                   GetValue(Resources.AzureCouchDBRegistryInstallLocationKey);
            }
            catch (Exception)
            {
                installFolder = null;
            }

            if (string.IsNullOrEmpty(installFolder))
                installFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            return installFolder;
        }

        public T ParseFile<T>(string fileName)
        {
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(T));
            Stream s = new FileStream(fileName, FileMode.Open);
            T item = (T)xmlSerializer.Deserialize(s);
            s.Close();

            return item;
        }

        public string LocalSvcConfigFilePath(string parentDirectory)
        {
            return string.Format("{0}/{1}", parentDirectory, Resources.ServiceConfigurationLocalFileName);
        }

        public string CloudSvcConfigFilePath(string parentDirectory)
        {
            return string.Format("{0}/{1}", parentDirectory, Resources.ServiceConfigurationCloudFileName);
        }

        public string SvcDefinitionFilePath(string parentDirectory)
        {
            return string.Format("{0}/{1}", parentDirectory, Resources.ServiceDefinitionFileName);
        }

        public static int GetRandomFromTwo(int first, int second)
        {
            return (new Random(DateTime.Now.Millisecond).Next(2) == 0) ? first : second;
        }


        public static General Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new General();
                }

                return instance;
            }
        }

        public static T DeserializeXmlFile<T>(string fileName, string exceptionMessage = null)
        {
            Validate.ValidateFileFull(fileName, string.Format(Resources.InvalidPath, string.Empty, fileName));

            T item = default(T);

            XmlSerializer xmlSerializer = new XmlSerializer(typeof(T));
            using (Stream s = new FileStream(fileName, FileMode.Open))
            {
                try { item = (T)xmlSerializer.Deserialize(s); }
                catch
                {
                    if (!string.IsNullOrEmpty(exceptionMessage))
                    {
                        throw new InvalidOperationException(exceptionMessage);
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            return item;
        }

        public static void SerializeXmlFile<T>(T obj, string fileName)
        {
            Validate.ValidatePathName(fileName, string.Format(Resources.InvalidPath, string.Empty, fileName));
            Validate.ValidateStringIsNullOrEmpty(fileName, string.Empty);

            XmlSerializer xmlSerializer = new XmlSerializer(typeof(T));
            using (Stream stream = new FileStream(fileName, FileMode.Create))
            {
                xmlSerializer.Serialize(stream, obj);
            }
        }

        public static X509Certificate2 GetCertificateFromStore(string thumbprint)
        {
            Validate.ValidateStringIsNullOrEmpty(thumbprint, "certificate thumbprint");

            X509Store store = new X509Store(StoreName.My, System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            X509Certificate2Collection certificates = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);

            if (certificates.Count == 0)
            {
                throw new ArgumentException(string.Format(Resources.CertificateNotFoundInStore, thumbprint));
            }
            else
            {
                return certificates[0];
            }
        }

        public static void AddCertificateToStore(X509Certificate2 certificate)
        {
            Validate.ValidateNullArgument(certificate, Resources.InvalidCertificate);
            X509Store store = new X509Store(StoreName.My, System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);
        }

        public static void RemoveCertificateFromStore(X509Certificate2 certificate)
        {
            Validate.ValidateNullArgument(certificate, Resources.InvalidCertificate);
            X509Store store = new X509Store(StoreName.My, System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Remove(certificate);
        }
    }
}
