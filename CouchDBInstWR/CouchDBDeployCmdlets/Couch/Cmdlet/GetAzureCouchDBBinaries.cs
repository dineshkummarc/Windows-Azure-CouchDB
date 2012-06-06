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
using System.Text;
using AzureDeploymentCmdlets.WAPPSCmdlet;
using AzureDeploymentCmdlets.Model;

namespace CouchDBDeployCmdlets.Couch.Cmdlet
{
    using System;
    using System.Management.Automation;
    using System.Security.Permissions;
    using System.ServiceModel;
    using AzureDeploymentCmdlets.Model;
    using CouchDBDeployCmdlets.Properties;
    using CouchDBDeployCmdlets.Utilities;
    using AzureDeploymentCmdlets.WAPPSCmdlet;
    using CouchDBDeployCmdlets.ServiceDefinitionSchema;
    using CouchDBDeployCmdlets.ServiceConfigurationSchema;
    using System.Xml.Serialization;
    using System.IO;
    using CouchDBDeployCmdlets.Model;
    using AzureDeploymentCmdlets.Utilities;
    using System.Reflection;
    using AzureDeploymentCmdlets.Scaffolding;
    using Microsoft.Win32;
    using System.Net;
    using System.Threading;
    using System.Collections.ObjectModel;

    [Cmdlet(VerbsCommon.Get, "AzureCouchDBBinaries")]
    public class GetAzureCouchDBBinaries : CmdletBase
    {
        private string azureCouchDBInstallFolder;
        private AutoResetEvent threadBlocker;
        private int downloadProgress = 0;
        private Exception downloadException;

        [Parameter(Mandatory = true)]
        public string CouchDBSetupPath { get; set; }

        [Parameter(Mandatory = true)]
        public string CouchDBInstallFolder { get; set; }

        public GetAzureCouchDBBinaries() { }

        public GetAzureCouchDBBinaries(IServiceManagement channel) { }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        protected override void ProcessRecord()
        {
            try
            {
                // Get Azure Service root path
                string serviceRootPath = base.GetServiceRootPath();

                base.ProcessRecord();
                string result = this.DownloadCouchDBBinaries(serviceRootPath);
                WriteObject(result);
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, string.Empty, ErrorCategory.CloseError, null));
                throw;
            }
        }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        public string DownloadCouchDBBinaries(string rootPath)
        {
            azureCouchDBInstallFolder = General.Instance.GetAzureCouchDBInstallFolder();

            InstallCouchDb();

            if (!Directory.Exists(CouchDBInstallFolder))
            {
                throw new Exception(Resources.CouchDBBinariesDownloadScriptNotAvailable);
            }

            string scriptArguments = Path.Combine(azureCouchDBInstallFolder, Resources.CouchDBBinariesDownloadScriptArguments);

            if (Directory.Exists(scriptArguments) == true)
            {
                Directory.Delete(scriptArguments, true);
            }

            String copyCommand = String.Format("COPY-ITEM \"{0}\" \"{1}\" -recurse", CouchDBInstallFolder, scriptArguments);
            ExecuteCommand("Copying CouchDB binaries to CouchDbOnAzure folder.", copyCommand);
            return Resources.CouchDBBinariesDownloadSuccessMessage;
        }

        private void InstallCouchDb()
        {
            if (IsAlreadyInstalled() == true)
            {
                Console.WriteLine("CouchDB already installed on the machine.");
                return;
            }

            //Download the setup.
            String setupPath = DownloadCouchDbSetup();

            //Execute the setup.
            String command = String.Format("Start-Process -File \"{0}\" -ArgumentList \"{1}\" -Wait", setupPath, "/sp- /verysilent /norestart /SUPPRESSMSGBOXES");
            ExecuteCommand("Installing CouchDB.", command);

            //Delete the setup file.
            File.Delete(setupPath);
        }

        private String DownloadCouchDbSetup()
        {
            try
            {
                String tempLocationToSave = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".exe");
                using (WebClient setupDownloader = new WebClient())
                {
                    setupDownloader.DownloadProgressChanged += new DownloadProgressChangedEventHandler(setupDownloader_DownloadProgressChanged);
                    setupDownloader.DownloadFileCompleted += new System.ComponentModel.AsyncCompletedEventHandler(setupDownloader_DownloadFileCompleted);
                    setupDownloader.DownloadFileAsync(new Uri(CouchDBSetupPath), tempLocationToSave);

                    Console.Write("Downloading CouchDB setup - ");
                    threadBlocker = new AutoResetEvent(false);
                    threadBlocker.WaitOne();
                    if (downloadException != null)
                    {
                        throw downloadException;
                    }
                }
                return tempLocationToSave;
            }
            finally
            {
                if (threadBlocker != null) { threadBlocker.Close(); }
            }
        }

        private void setupDownloader_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            if ((e.ProgressPercentage % 10 == 0) && (e.ProgressPercentage > downloadProgress))
            {
                downloadProgress = e.ProgressPercentage;
                Console.Write(String.Concat(" ", e.ProgressPercentage, "%"));
            }
        }

        private void setupDownloader_DownloadFileCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            if (e.Error != null)
            {
               downloadException = e.Error;
            }
            Console.WriteLine(String.Empty);
            threadBlocker.Set();
        }

        private bool IsAlreadyInstalled()
        {
            string registryKey = @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\";
            bool foundRegistryKeyForCouch = false;
            using (Microsoft.Win32.RegistryKey key = Registry.LocalMachine.OpenSubKey(registryKey))
            {
                foreach (string subkeyname in key.GetSubKeyNames())
                {
                    using (RegistryKey subkey = key.OpenSubKey(subkeyname))
                    {
                        if (subkey.Name.Contains("ApacheCouchDB") == true)
                        {
                            foundRegistryKeyForCouch = true;
                            break;
                        }
                    }
                }
            }
            return foundRegistryKeyForCouch;
        }

        private void ExecuteCommand(String message, String command)
        {
            Console.WriteLine(message);
            Console.WriteLine(command);

            PowerShell powershell = PowerShell.Create();
            powershell.AddScript(command);

            PSDataCollection<PSObject> output = new PSDataCollection<PSObject>();
            Collection<PSObject> result = powershell.Invoke();
            foreach (PSObject eachResult in result)
            {
                Console.WriteLine(eachResult.ToString());
            }
        }
    }
}
