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
using System.Security.Permissions;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Management.Automation;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DeployCmdlets4WA.Properties;
using System.Reflection;
using System.Management.Automation.Runspaces;

namespace DeployCmdlets4WA
{
    [Cmdlet(VerbsCommon.New, "DeployOnAzure")]
    public class DeployOnAzure : PSCmdlet, IDynamicParameters
    {
        private class StepType
        {
            public const string CmdLet = "Cmdlet";
            public const string ChangeWorkingDir = "ChangeWorkingDir";
            public const string PowershellScript = "Powershell";
            public const string PS1File = "PS1";
        }

        private const string publishSettingExtn = ".publishsettings";
        private string _publishSettingsPath;
        private RuntimeDefinedParameterDictionary _runtimeParamsCollection;
        private AutoResetEvent _threadBlocker;
        private AutoResetEvent _executePS1Blocker;
        private DeploymentModelHelper _controller;

        // method to get Downloads folder path
        private static readonly Guid DownloadsFolderGUID = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out string pszPath);

        [Parameter(Mandatory = true)]
        public String XmlConfigPath { get; set; }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        protected override void ProcessRecord()
        {
            if (!ValidateParameters())
                return;

            if (IsEmulator() == false)
            {
                if (!DownloadPublishSettings())
                    return;
            }

            for (int i = 0; ; i++)
            {
                DeploymentModelStepsStep step = _controller.GetStepAtIndex(i);
                Console.WriteLine("===============================");

                if (step == null)
                {
                    break;
                }

                if (!ProcessStep(step))
                    break;
            }
        }

        private bool ProcessStep(DeploymentModelStepsStep step)
        {
            bool bRet = true;

            switch (step.Type)
            {
                case StepType.CmdLet:
                    String command = GetCommandForStep(step);
                    bRet = ExecutePsCmdlet(step.Message, command);
                    break;

                case StepType.ChangeWorkingDir:
                    bRet = ChangeWorkingDir(step);
                    break;

                case StepType.PowershellScript:
                    bRet = ExecuteCommand(step.Command);
                    break;

                case StepType.PS1File:
                    bRet = ExecutePS1File(step);
                    break;

                default:
                    Console.WriteLine("Unrecognized step type inside deployment model xml: " + step.Type);
                    bRet = false;
                    break;
            }

            return bRet;
        }

        private bool ChangeWorkingDir(DeploymentModelStepsStep step)
        {
            try
            {
                String location = GetParamValue(step.CommandParam[0].ParameterName);
                SessionState.Path.SetLocation(location);
            }
            catch (Exception exc)
            {
                Console.WriteLine("Exception while changing working directory: " + exc.Message);
                return false;
            }

            return true;
        }

        private string GetCommandForStep(DeploymentModelStepsStep step)
        {
            StringBuilder command = new StringBuilder(step.Command + "  ");

            if (step.CommandParam != null)
            {
                for (int i = 0; i < step.CommandParam.Length; i++)
                {
                    String paramValue = GetParamValue(step.CommandParam[i].ParameterName);

                    //Handle the cases where user need to just add switch to command..
                    paramValue = String.IsNullOrEmpty(paramValue) == true ? String.Empty : paramValue;
                    command.AppendFormat(" -{0} \"{1}\" ", step.CommandParam[i].Name, paramValue);
                }
            }

            return command.ToString();
        }

        private bool ExecuteCommand(String command)
        {
            bool bRet = true;
            Console.WriteLine("Executing command:");
            Console.WriteLine(command);

            try
            {
                PowerShell ps = PowerShell.Create();
                ps.AddScript(command);

                // Create the output buffer for the results.
                PSDataCollection<PSObject> output = new PSDataCollection<PSObject>();
                Collection<PSObject> result = ps.Invoke();
                foreach (PSObject eachResult in result)
                {
                    Console.WriteLine(eachResult.ToString());
                }
            }
            catch (Exception exc)
            {
                Console.WriteLine("Exception while executing command: " + exc.Message);
                bRet = false;
            }

            return bRet;
        }

        private bool ExecutePS1File(DeploymentModelStepsStep step)
        {
            bool bRet = true;
            Console.WriteLine(step.Message);
            Console.WriteLine("File: " + step.Command);

            try
            {
                using (Runspace space = RunspaceFactory.CreateRunspace(this.Host))
                {
                    space.Open();
                    using (PowerShell scriptExecuter = PowerShell.Create())
                    {
                        scriptExecuter.Runspace = space;
                        // if relative, path is relative to dll
                        string filePath = step.Command;
                        if (!Path.IsPathRooted(filePath))
                            filePath = Path.Combine(System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), step.Command);

                        scriptExecuter.AddCommand(filePath);
                        if (step.CommandParam != null)
                        {
                            for (int i = 0; i < step.CommandParam.Length; i++)
                            {
                                DeploymentModelStepsStepCommandParam param = step.CommandParam[i];
                                String paramValue = GetParamValue(param.ParameterName);
                                if (String.IsNullOrEmpty(param.Name) == true)
                                {
                                    scriptExecuter.AddArgument(paramValue);
                                }
                                else
                                {
                                    scriptExecuter.AddParameter(param.Name, paramValue);
                                }
                            }
                        }

                        _executePS1Blocker = new AutoResetEvent(false);
                        PSDataCollection<PSObject> output = new PSDataCollection<PSObject>();

                        output.DataAdded += new EventHandler<DataAddedEventArgs>(output_DataAdded);
                        scriptExecuter.InvocationStateChanged += new EventHandler<PSInvocationStateChangedEventArgs>(scriptExecuter_InvocationStateChanged);

                        IAsyncResult asyncResult = scriptExecuter.BeginInvoke<PSObject, PSObject>(null, output);
                        _executePS1Blocker.WaitOne();

                        PSDataStreams errorStream = scriptExecuter.Streams;
                        foreach (ErrorRecord error in errorStream.Error)
                        {
                            Console.WriteLine(error.Exception.Message);
                            bRet = false;
                        }
                    }
                }
            }
            catch (Exception exc)
            {
                Console.WriteLine("Exception while executing PS1 file: " + exc.Message);
                bRet = false;
            }

            return bRet;
        }

        void scriptExecuter_InvocationStateChanged(object sender, PSInvocationStateChangedEventArgs e)
        {
            if (e.InvocationStateInfo.State == PSInvocationState.Completed)
            {
                _executePS1Blocker.Set();
            }
        }

        void output_DataAdded(object sender, DataAddedEventArgs e)
        {
            PSDataCollection<PSObject> myp = (PSDataCollection<PSObject>)sender;

            Collection<PSObject> results = myp.ReadAll();
            foreach (PSObject result in results)
            {
                Console.WriteLine(result.ToString());
            }
        }

        private bool ExecutePsCmdlet(String beginMessage, String command)
        {
            Console.WriteLine(beginMessage);
            Console.WriteLine(command);

            try
            {
                Collection<PSObject> results = this.InvokeCommand.InvokeScript(command);
                foreach (PSObject result in results)
                {
                    Console.WriteLine(result.ToString());
                }
            }
            catch (Exception exc)
            {
                Console.WriteLine("Exception while executing cmdlet: " + exc.Message);
                return false;
            }

            return true;
        }

        private bool DownloadPublishSettings()
        {
            bool bRet = true;

            // determine paas or iaas
            bool isIaaS = false;
            string serviceModel = GetParamValue("ServiceModel");
            if (!string.IsNullOrEmpty(serviceModel))
                isIaaS = (serviceModel.ToLower() == "iaas");

            Process.Start(isIaaS ? Resources.AzureIaaSPublishSettingsURL : Resources.AzurePaaSPublishSettingsURL);
            Console.WriteLine("Waiting for publish settings file.");

            _publishSettingsPath = GetParamValue("PublishSettingsPath");

            try
            {
                // if no path is specified, we need to watch the default downloads folder as well as the folder where the current assembly is running from
                if (string.IsNullOrEmpty(_publishSettingsPath))
                {
                    string downloadsFolderPath, currentFolderPath;

                    SHGetKnownFolderPath(DownloadsFolderGUID, 0, IntPtr.Zero, out downloadsFolderPath);
                    currentFolderPath = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                    // set up watchers for both paths and wait until one fires
                    using (_threadBlocker = new AutoResetEvent(false))
                    using (FileSystemWatcher downloadsFolderWatcher = SetupFolderWatcher(downloadsFolderPath))
                    using (FileSystemWatcher currentFolderWatcher = SetupFolderWatcher(currentFolderPath))
                    {
                        downloadsFolderWatcher.Changed += new FileSystemEventHandler(folderWatcher_EventHandler);
                        downloadsFolderWatcher.Created += new FileSystemEventHandler(folderWatcher_EventHandler);
                        downloadsFolderWatcher.Renamed += new RenamedEventHandler(folderWatcher_EventHandler);

                        currentFolderWatcher.Changed += new FileSystemEventHandler(folderWatcher_EventHandler);
                        currentFolderWatcher.Created += new FileSystemEventHandler(folderWatcher_EventHandler);
                        currentFolderWatcher.Renamed += new RenamedEventHandler(folderWatcher_EventHandler);

                        if (!_threadBlocker.WaitOne(1200000))
                        {
                            Console.WriteLine("Timed out waiting for publishsettings file.");
                            bRet = false;
                        }
                    }
                }
                else
                {
                    using (_threadBlocker = new AutoResetEvent(false))
                    using (FileSystemWatcher publishSettingsFolderWatcher = SetupFolderWatcher(_publishSettingsPath))
                    {
                        publishSettingsFolderWatcher.Changed += new FileSystemEventHandler(folderWatcher_EventHandler);
                        publishSettingsFolderWatcher.Created += new FileSystemEventHandler(folderWatcher_EventHandler);
                        publishSettingsFolderWatcher.Renamed += new RenamedEventHandler(folderWatcher_EventHandler);

                        if (!_threadBlocker.WaitOne(1200000))
                        {
                            Console.WriteLine("Timed out waiting for publishsettings file.");
                            bRet = false;
                        }
                    }
                }
            }
            catch (Exception exc)
            {
                Console.WriteLine("Exception while downloading publishsettings file: " + exc.Message);
                bRet = false;
            }

            return bRet;
        }

        private FileSystemWatcher SetupFolderWatcher(string folderPath)
        {
            Console.WriteLine("Watching publish settings folder: " + folderPath);

            FileSystemWatcher publishSettingsLocationWatcher = new FileSystemWatcher(folderPath);
            publishSettingsLocationWatcher.EnableRaisingEvents = true;
            publishSettingsLocationWatcher.IncludeSubdirectories = true;
            publishSettingsLocationWatcher.Filter = String.Concat("*", publishSettingExtn);
            return publishSettingsLocationWatcher;
        }

        private void folderWatcher_EventHandler(object sender, FileSystemEventArgs e)
        {
            if (Path.GetExtension(e.Name) == publishSettingExtn)
            {
                string publishSettingsFilePath = e.FullPath;
                Console.WriteLine("Publish settings file: " + publishSettingsFilePath);
                _controller.SetParameterByName("PublishSettingsFilePath", publishSettingsFilePath);
                _threadBlocker.Set();
            }
        }

        public object GetDynamicParameters()
        {
            _controller = new DeploymentModelHelper(XmlConfigPath);
            _controller.Init();

            IEnumerable<string> paramsForModel = _controller.GetAllParameters();
            _runtimeParamsCollection = new RuntimeDefinedParameterDictionary();

            foreach (string paramForModel in paramsForModel)
            {
                RuntimeDefinedParameter dynamicParam = new RuntimeDefinedParameter()
                {
                    Name = paramForModel,
                    ParameterType = typeof(string),
                };
                dynamicParam.Attributes.Add(new ParameterAttribute() { Mandatory = false });
                _runtimeParamsCollection.Add(paramForModel, dynamicParam);
            }

            return _runtimeParamsCollection;
        }

        // check for required parameters / invalid values etc.
        private bool ValidateParameters()
        {
            bool bRet = true;
            IEnumerable<string> paramsForModel = _controller.GetAllParameters();

            foreach (string paramForModel in paramsForModel)
            {
                //Console.WriteLine("param: " + paramForModel + ", reqd: " + _controller.IsParamValueRequired(paramForModel) +
                //    ", XML value: " + _controller.GetParameterByName(paramForModel) + ", cmdline value: " + GetDynamicParamValue(paramForModel));
                if (_controller.IsParamValueRequired(paramForModel))
                {
                    string value = GetParamValue(paramForModel);
                    if (string.IsNullOrEmpty(value))
                    {
                        Console.WriteLine("Missing required value for parameter: " + paramForModel);
                        bRet = false;
                    }
                }
            }

            return bRet;
        }

        // This is the main method using with parameter values should be obtained. It takes care of overrides etc.
        private string GetParamValue(string paramName)
        {
            String paramValueFromXml = _controller.GetParameterByName(paramName);
            String paramValueFromCmdline = GetDynamicParamValue(paramName);

            // Value of parameter set inside cmdline has higher precedence than the one inside xml.
            string value = String.IsNullOrEmpty(paramValueFromCmdline) ? paramValueFromXml : paramValueFromCmdline;

            // if no value given, see if ValuePrefixRef and ValueSuffix are available, and combine them to get the value
            if (string.IsNullOrEmpty(value))
            {
                string valuePrefixRef = _controller.GetParamValuePrefixRef(paramName);
                if (string.IsNullOrEmpty(valuePrefixRef))
                    return null;

                string valuePrefix = GetParamValue(valuePrefixRef);
                if (string.IsNullOrEmpty(valuePrefix))
                    return null;

                string valueSuffix = _controller.GetParamValueSuffix(paramName);

                value = valuePrefix + valueSuffix;
            }

            return value;
        }

        private string GetDynamicParamValue(string paramName)
        {
            RuntimeDefinedParameter paramDef;
            _runtimeParamsCollection.TryGetValue(paramName, out paramDef);

            return (paramDef == null || paramDef.Value == null) ? String.Empty : paramDef.Value.ToString();
        }

        private bool IsEmulator()
        {
            string paramValue = GetParamValue("Emulator");
            if (string.IsNullOrEmpty(paramValue) == true)
            {
                return false;
            }
            return bool.Parse(paramValue);
        }
    }
}