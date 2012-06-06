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
using AzureDeploymentCmdlets.Model;
using System.Management.Automation;
using System.Security.Permissions;
using CouchDBDeployCmdlets.Properties;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using CouchDBDeployCmdlets.Utilities;

namespace CouchDBDeployCmdlets.Couch.Cmdlet
{
    [Cmdlet(VerbsCommon.Get, "NodeJSExecutables")]
    public class GetNodeJSExecutables : CmdletBase
    {
        [Parameter(Mandatory = true)]
        public string AzureNodeJSSdkBinPath { get; set; }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        protected override void ProcessRecord()
        {
            try
            {
                base.ProcessRecord();
                string result = this.CopyNodeJSExe();
                WriteObject(result);
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, string.Empty, ErrorCategory.CloseError, null));
            }
        }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        public string CopyNodeJSExe()
        {
            string azureCouchDBInstallFolder;

            azureCouchDBInstallFolder = General.Instance.GetAzureCouchDBInstallFolder();

            try
            {
                if (Directory.Exists(AzureNodeJSSdkBinPath) == false)
                {
                    throw new Exception("Node js not installed.");
                }

                string destination = Path.Combine(azureCouchDBInstallFolder, @"WorkerRole\Node\bin");
                if (Directory.Exists(destination) == false)
                {
                    Directory.CreateDirectory(destination);
                }
                
                string nodeExeLocation = Path.Combine(AzureNodeJSSdkBinPath, "node.exe");

                using (PowerShell ps = PowerShell.Create())
                {
                    ps.AddScript(String.Format("COPY-ITEM \"{0}\" \"{1}\" ", nodeExeLocation, destination));
                    PSDataCollection<PSObject> output = new PSDataCollection<PSObject>();
                    IAsyncResult async = ps.BeginInvoke();
                    foreach (PSObject result in ps.EndInvoke(async))
                    {
                        Console.WriteLine(result.ToString());
                    }
                }
            }
            catch
            {
                return "Error copying Node executable.";
            }
            return "Node Executable copied Successfully.";
        }
    }
}
