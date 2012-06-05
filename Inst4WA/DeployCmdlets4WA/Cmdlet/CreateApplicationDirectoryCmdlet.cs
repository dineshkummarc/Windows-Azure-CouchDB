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
using System.IO;
using System.Threading;

namespace DeployCmdlets4WA.Cmdlet
{
    [Cmdlet(VerbsCommon.New, "ApplicationDirectory")]
    public class CreateApplicationDirectoryCmdlet : PSCmdlet
    {
        [Parameter(Mandatory = true)]
        public String AppFolder { get; set; }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            while (true)
            {
                try
                {
                    // cleanup any previous stuff
                    if (Directory.Exists(this.AppFolder))
                    {
                        Console.WriteLine("Cleaning up temp application folder: " + this.AppFolder);

                        // Remove read only atttributes from all the files.
                        String[] allFiles = Directory.GetFiles(this.AppFolder, "*.*", SearchOption.AllDirectories);
                        foreach (string eachFile in allFiles)
                        {
                            File.SetAttributes(eachFile, FileAttributes.Normal);
                        }

                        Directory.Delete(this.AppFolder, true);
                    }

                    Directory.CreateDirectory(this.AppFolder);
                    Console.WriteLine("Creating new temp application folder: " + this.AppFolder);

                    break;
                }
                catch
                {
                    Console.WriteLine("Unable to create temp application folder: " + this.AppFolder);
                    Console.WriteLine("Please close any programs that may have that folder or any subfolders or files within those folders open. You may also try to delete the folder yourself.");
                    Thread.Sleep(10000);
                }
            }
        }
    }
}
