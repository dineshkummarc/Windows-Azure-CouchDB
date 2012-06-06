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
using AzureDeploymentCmdlets.Model;
using CouchDBDeployCmdlets.Properties;
using Microsoft.Win32;
using System.IO;
using System.Reflection;
using System.Collections.ObjectModel;
using System.Security.Permissions;
using CouchDBDeployCmdlets.Utilities;

namespace CouchDBDeployCmdlets.Couch.Cmdlet
{
    [Cmdlet(VerbsCommon.Add, "NodeJSModules")]
    public class AddNodeJSModules : CmdletBase
    {
        private string azureCouchDBInstallFolder;

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        protected override void ProcessRecord()
        {
            base.ProcessRecord();
            InstallNodeJsModules();
        }

        private void InstallNodeJsModules()
        {
            azureCouchDBInstallFolder = General.Instance.GetAzureCouchDBInstallFolder();

            string installNodeJSModulesFileName = Path.Combine(azureCouchDBInstallFolder, Resources.InstallNodeJsModuleScriptName);

            if (!File.Exists(installNodeJSModulesFileName))
            {
                throw new Exception(Resources.InstallNodeJSModuleScriptNotAvailable);
            }
            
            using (PowerShell scriptExecuter = PowerShell.Create())
            {
                scriptExecuter.AddScript(File.ReadAllText(installNodeJSModulesFileName));
                scriptExecuter.AddArgument(this.SessionState.Path.CurrentLocation.Path);
                scriptExecuter.AddParameter("Verb", "runas");
                Collection<PSObject> result = scriptExecuter.Invoke();
                foreach (PSObject eachResult in result)
                {
                    Console.WriteLine(eachResult.ToString());
                }
            }
        }
    }
}
