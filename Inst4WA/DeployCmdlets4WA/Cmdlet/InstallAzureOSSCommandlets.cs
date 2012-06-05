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
using System.Management.Automation;
using System.Threading;
using Microsoft.Win32;
using System.IO;
using System.Net;

namespace DeployCmdlets4WA.Cmdlet
{
    [Cmdlet("Install", "AzureOSSCommandlets")]
    public class InstallAzureOSSCommandlets : PSCmdlet
    {
        private AutoResetEvent threadBlocker;
        private int downloadProgress;

        [Parameter(Mandatory = true, HelpMessage = "ProductId of product being installed")]
        public string ProductId { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Location on web from where product setup could be downloaded")]
        public string DownloadLoc { get; set; }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if (IsAlreadyInstalled() == true)
            {
                Console.WriteLine("OSS Commandlets is already installed on the computer.");
                return;
            }

            //Download the setup.
            String downloadLocation = Download();

            //Execute the setup.
            Install(downloadLocation);
        }

        private string Download()
        {
            try
            {
                String tempLocationToSave = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".msi");
                using (WebClient setupDownloader = new WebClient())
                {
                    setupDownloader.DownloadProgressChanged += new DownloadProgressChangedEventHandler(setupDownloader_DownloadProgressChanged);
                    setupDownloader.DownloadFileCompleted += new System.ComponentModel.AsyncCompletedEventHandler(setupDownloader_DownloadFileCompleted);
                    setupDownloader.DownloadFileAsync(new Uri(DownloadLoc), tempLocationToSave);

                    Console.Write("Downloading OSS Deployment Cmdlets setup - ");
                    threadBlocker = new AutoResetEvent(false);
                    threadBlocker.WaitOne();
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
            Console.WriteLine(String.Empty);
            threadBlocker.Set();
        }

        private bool IsAlreadyInstalled()
        {
            string loweredProductId = this.ProductId.ToLowerInvariant();
            string registryKey = @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\";
            bool foundRegistryKeyForCouch = false;
            using (Microsoft.Win32.RegistryKey key = Registry.LocalMachine.OpenSubKey(registryKey))
            {
                foreach (string subkeyname in key.GetSubKeyNames())
                {
                    using (RegistryKey subkey = key.OpenSubKey(subkeyname))
                    {
                        if (subkey.Name.ToLower().Contains(loweredProductId) == true)
                        {
                            foundRegistryKeyForCouch = true;
                            break;
                        }
                    }
                }
            }
            return foundRegistryKeyForCouch;
        }

        private void Install(string downloadLocation)
        {
            //msiexec.exe /i foo.msi /qn
            //Silent minor upgrade: msiexec.exe /i foo.msi REINSTALL=ALL REINSTALLMODE=vomus /qn
            String installCmd = String.Format("Start-Process -File msiexec.exe -ArgumentList \"/qn /i {0}\" -Wait", downloadLocation);
            Utilities.ExecuteCommands.ExecuteCommand(installCmd, this.Host);
        }

    }
}
