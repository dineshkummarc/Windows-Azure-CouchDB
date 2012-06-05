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
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Xml;
using System.Threading;
using System.Security.Principal;

namespace Inst4WA
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static bool IsElevated
        {
            get
            {
                return new WindowsPrincipal
                    (WindowsIdentity.GetCurrent()).IsInRole
                    (WindowsBuiltInRole.Administrator);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (!IsElevated || (e.Args == null) || (e.Args.Count() == 0) || !e.Args.Contains("-XmlConfigPath"))
            {
                ShowUsage();
            }
            else
            {
                RunInstaller(e.Args);
                Thread.Sleep(5000);
            }

            Shutdown();
        }

        private void ShowUsage()
        {
            string msg = "Inst4WA:\r\n\r\n" +
                         "Installs open source packages to Windows Azure using settings specified in a configuration file.\r\n\r\n" +
                         "Usage:\r\n\r\n" +
                         "Start a command window in Administrator mode and then type:\r\n\r\n" +
                         "Inst4WA -XmlConfigPath <config file path> -Subscription <subscription name> -DomainName <domain name>\r\n\r\n" +
                         "Where:\r\n\r\n" +
                         "<config file path>: path to an XML config file containing settings for the service to be deployed\r\n" +
                         "<subscription name>: Windows Azure subscription name\r\n" +
                         "<domain name>: Unique name to be used to create the service to be deployed. The domain name will be used to generate other unique names such as storage account name etc.\r\n\r\n" +
                         "For example:\r\n\r\n" +
                         "Inst4WA.exe -XmlConfigPath \"DeploymentModelSolr.xml\" -DomainName \"foo\" -Subscription \"bar\"\r\n\r\n" +
                         "Please also refer to the sample config XML files included along with this tool.";

            MessageBox.Show(msg, "Inst4WA Usage", MessageBoxButton.OK);
        }

        private void RunInstaller(string[] args)
        {
            //Launch Powershell Window with Cmdlets loaded.
            String argsForCmdlet = GetArgsStringForCmdlet(args);

            string fmt = Inst4WA.Properties.Resources.CommandArgs;
            EnsureCorrectPowershellConfig();

            ProcessStartInfo psi = new ProcessStartInfo("Powershell.exe",
                String.Format(fmt, System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + @"\", argsForCmdlet));
            psi.WorkingDirectory = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            psi.Verb = "runas";
            Process.Start(psi);
        }

        private void EnsureCorrectPowershellConfig()
        {
            List<string> locationOfPowershellExe = GetPowershellLocation();
            if (locationOfPowershellExe == null || locationOfPowershellExe.Count == 0)
            {
                throw new Exception("Unable to locate powershell exe.");
            }

            //Create config file if not exists.
            foreach (string eachLocation in locationOfPowershellExe)
            {
                string configFileLoc = System.IO.Path.Combine(eachLocation, "powershell.exe.config");
                if (File.Exists(configFileLoc) == false)
                {
                    FileStream configStream = File.Create(configFileLoc);
                    using (XmlTextWriter textWriter = new XmlTextWriter(configStream, null))
                    {
                        textWriter.WriteStartElement("configuration"); //Append Root node otherwise xmldocument cannot load the config file.
                        textWriter.WriteEndElement();
                    }
                    configStream.Close();
                    configStream.Dispose();
                }

                //Prepate config file.
                PrepareConfigFile(configFileLoc);

            }
        }

        private static void PrepareConfigFile(string configFileLoc)
        {
            //Load config file.
            XmlDocument configFile = new XmlDocument();

            string configFileContext = File.ReadAllText(configFileLoc);
            configFile.LoadXml(configFileContext);

            XmlNode configurationNode = configFile.SelectSingleNode("configuration");

            //Check if start up node is present.
            XmlNode startupNode = configurationNode.SelectSingleNode("startup");
            if (startupNode == null)
            {
                startupNode = configFile.CreateNode(XmlNodeType.Element, "startup", string.Empty);
                configurationNode.AppendChild(startupNode);
            }

            //Check runtime policy attribute if it does not exist.
            XmlAttribute runtimePolicyAttr = startupNode.Attributes["useLegacyV2RuntimeActivationPolicy"];
            if (runtimePolicyAttr == null)
            {
                runtimePolicyAttr = configFile.CreateAttribute("useLegacyV2RuntimeActivationPolicy");
                runtimePolicyAttr.Value = "true";
                startupNode.Attributes.Append(runtimePolicyAttr);
            }

            //Add supported runtime version node.
            string[] requiredVersions = new string[4] { "v3.5", "v3.0", "v2.0.50727", "v4.0.30319" };

            //Add supported runtime version node.
            foreach (string version in requiredVersions)
            {
                XmlNode supportedRunTimeVersionNode = startupNode.SelectSingleNode(string.Format("supportedRuntime[@version='{0}']", version));
                if (supportedRunTimeVersionNode == null)
                {
                    supportedRunTimeVersionNode = configFile.CreateElement("supportedRuntime");

                    XmlAttribute supportedRuntimeVer = configFile.CreateAttribute("version");
                    supportedRuntimeVer.Value = version;
                    supportedRunTimeVersionNode.Attributes.Append(supportedRuntimeVer);

                    startupNode.AppendChild(supportedRunTimeVersionNode);
                }
            }

            configFile.Save(configFileLoc);
        }

        private List<string> GetPowershellLocation()
        {
            List<String> locationOfPowersehllExe = new List<String>();
            String windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            String locationOf64BitPowersehllExe = System.IO.Path.Combine(windowsDir, @"SysWOW64\WindowsPowerShell\v1.0");
            if (Directory.Exists(locationOf64BitPowersehllExe) == true)
            {
                locationOfPowersehllExe.Add(locationOf64BitPowersehllExe);
            }

            String locationOf32BitPowershellExe = System.IO.Path.Combine(windowsDir, @"System32\WindowsPowerShell\v1.0");
            if (Directory.Exists(locationOf32BitPowershellExe) == true)
            {
                locationOfPowersehllExe.Add(locationOf32BitPowershellExe);
            }

            return locationOfPowersehllExe;
        }

        private static string GetArgsStringForCmdlet(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return string.Empty;
            }
            //Wrap value of args inside double quote.
            string[] argsWithValsInsideDoubleQuote = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg.StartsWith("-"))
                    argsWithValsInsideDoubleQuote[i] = arg;
                else if (arg.Contains(' '))
                    argsWithValsInsideDoubleQuote[i] = String.Concat("\\\"", arg, "\\\"");
                else
                    argsWithValsInsideDoubleQuote[i] = String.Concat("\"", arg, "\"");
            }
            return string.Join(" ", argsWithValsInsideDoubleQuote);
        }

    }
}
