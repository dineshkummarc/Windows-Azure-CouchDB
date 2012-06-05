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
using System.Security.Permissions;
using System.Collections.ObjectModel;

namespace DeployCmdlets4WA
{
    [Cmdlet(VerbsCommon.Add, "LoadAssembly")]
    public class LoadAssemblyCmdlet : PSCmdlet
    {
        [Parameter(Mandatory = true)]
        public String CmdletsAssemblyPath { get; set; }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        protected override void ProcessRecord()
        {
            String functionBody = @"
                                    function loadAssembly()
                                    {{
                                        $assemblyloaded = [System.Reflection.Assembly]::LoadFrom(""{0}"");
                                        $assemblyCollection = @($assemblyloaded); 
                                        Import-Module -Assembly $assemblyCollection;
                                    }}
                                    loadAssembly";
            String functionWithAssemblyLoc = String.Format(functionBody, CmdletsAssemblyPath);
            ExecutePsCmdlet("Loading the assembly inside Powershell session.", functionWithAssemblyLoc);
        }
        
        private void ExecutePsCmdlet(String beginMessage, String command)
        {
            Console.WriteLine(beginMessage);
            Collection<PSObject> results = this.InvokeCommand.InvokeScript(command);
            foreach (PSObject result in results)
            {
                Console.WriteLine(result.ToString());
            }
        }
    }
}


