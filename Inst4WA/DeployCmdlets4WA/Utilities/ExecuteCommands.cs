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
using System.Collections.ObjectModel;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;

namespace DeployCmdlets4WA.Utilities
{
    public static class ExecuteCommands
    {
        public static void ExecuteCommand(String command, PSHost host)
        {
            Console.WriteLine("Executing command:");

            string outputLine = command;
            int ichNewline = command.IndexOfAny("\r\n".ToCharArray());
            if (ichNewline > 0)
                outputLine = command.Substring(0, ichNewline);
            
            Console.WriteLine(outputLine);

            using (Runspace space = RunspaceFactory.CreateRunspace(host))
            {
                space.Open();
                using (PowerShell ps = PowerShell.Create())
                {
                    ps.Runspace = space;
                    ps.AddScript(command);

                    // Create the output buffer for the results.
                    PSDataCollection<PSObject> output = new PSDataCollection<PSObject>();
                    IAsyncResult async = ps.BeginInvoke();
                    foreach (PSObject result in ps.EndInvoke(async))
                    {
                        Console.WriteLine(result.ToString());
                    }

                    PSDataStreams streams = ps.Streams;

                    if (streams.Error != null)
                    {
                        foreach (ErrorRecord record in streams.Error)
                        {
                            Console.WriteLine(GetMessageFromErrorRecord(record));
                            throw record.Exception;
                        }
                    }
                }
            }
        }

        private static string GetMessageFromErrorRecord(ErrorRecord record)
        {
            if(record.Exception != null)
            {
                return record.Exception.Message;
            }
            if(record.ErrorDetails != null)
            {
                return String.Format("Erro - {0} & Recommended action - {1}", record.ErrorDetails.Message, record.ErrorDetails.RecommendedAction);
            }
            return record.ToString();
        }
    }
}
