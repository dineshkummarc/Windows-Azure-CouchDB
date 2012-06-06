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
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.StorageClient;
using System.IO;
using System.Xml.Linq;
using System.Xml;
using System.Security.AccessControl;
using System.Security.Principal;

namespace CouchHostWorkerRole
{
    public class WorkerRole : RoleEntryPoint
    {
        private static CloudDrive _couchStorageDrive = null;
        private static Process _couchProcess = null;
        private static Process _nodeProcess = null;
        private static string _host = null;
        private static string _couchPort = null;
        private static string _nodePort = null;
        private static string _myCouchUrl = null;
        private static string _myNodeUrl = null;
        private static string _username = "admin";
        private static string _password = "password";
        private HashSet<string> replicationSrcAddresses = new HashSet<string>();

#if DEBUG
        private static String _logFileLocation;
#endif

        // TODO: couchdb currently has a bug that prevents us from mapping the database_dir to the Azure drive. For now, we are working around it by mapping the Azure drive to a folder in local storage.
        // Remove this define when the couchdb bug is fixed.
        private bool _couchdb_database_dir_bug = true;

        public override void Run()
        {
            Log("CouchHostWorkerRole Run() called", "Information");

            while (true)
            {
                Thread.Sleep(10000);
                Log("Working", "Information");

                if ((_couchProcess != null) && (_couchProcess.HasExited == true))
                {
                    Log("Couch Process Exited. Hence recycling instance.", "Information");
                    RoleEnvironment.RequestRecycle();
                    return;
                }

                if ((_nodeProcess != null) && (_nodeProcess.HasExited == true))
                {
                    Log("Node Process Exited. Hence recycling instance.", "Information");
                    RoleEnvironment.RequestRecycle();
                    return;
                }

                UpdateReplication();
            }
        }

        public override bool OnStart()
        {
            Log("CouchHostWorkerRole Start() called", "Information");

            // Set the maximum number of concurrent connections 
            ServicePointManager.DefaultConnectionLimit = 12;

            RoleEnvironment.Changing += (sender, arg) =>
            {
                RoleEnvironment.RequestRecycle();
            };

            InitDiagnostics();
            InitRoleInfo();
            StartCouch();
            StartNode();

            return base.OnStart();
        }

        public override void OnStop()
        {
            Log("CouchHostWorkerRole OnStop() called", "Information");

            // Remove our endpoint from endpoints table
            try
            {
                IPEndPoint couchEndpoint = RoleEnvironment.CurrentRoleInstance.InstanceEndpoints["CouchEndpoint"].IPEndpoint;
                HelperLib.Util.RemoveRoleInfoEntry(RoleEnvironment.CurrentRoleInstance.Id, couchEndpoint.Address.ToString(), couchEndpoint.Port);
            }
            catch (Exception ex)
            {
                Log("Exception occured in OnStop while removing endpoint: " + ex.ToString(), "Error");
            }

            if (_nodeProcess != null)
            {
                try
                {
                    _nodeProcess.Kill();
                    _nodeProcess.WaitForExit(2000);
                }
                catch { }
            }

            if (_couchProcess != null)
            {
                try
                {
                    _couchProcess.Kill();
                    _couchProcess.WaitForExit(2000);
                }
                catch { }
            }

            if (_couchStorageDrive != null)
            {
                try
                {
                    _couchStorageDrive.Unmount();
                }
                catch { }
            }

            base.OnStop();
        }

        private void StartNode()
        {
            try
            {
                // install node into local storage
                String nodeInstallPath = RoleEnvironment.GetLocalResource("NodeInstall").RootPath;
                string nodePath = Path.Combine(Environment.GetEnvironmentVariable("RoleRoot") + @"\", @"approot\Node");
                string nodeWebAppPath = Path.Combine(Environment.GetEnvironmentVariable("RoleRoot") + @"\", @"approot\WebApp");

                CopyNodeFiles(nodePath, nodeWebAppPath, nodeInstallPath);

                // start Node
                string cmd = Path.Combine(nodeInstallPath, @"bin\node.exe");
                string args = string.Format("{0} {1} {2} {3} {4}",
                    Path.Combine(nodeInstallPath, @"server.js"), _host, _nodePort, _myCouchUrl, RoleEnvironment.GetConfigurationSettingValue("DatabaseName"));
                Log("Node start command line: " + cmd + " " + args, "Information");

                _nodeProcess = ExecuteShellCommand(cmd, args, false, nodeInstallPath);
                _nodeProcess.Exited += new EventHandler(_NodeProcess_Exited);

                // wait for Node to be up
                int n = 0;
                while (true)
                {
                    if (n++ > 10) break;
                    try
                    {
                        new WebClient().DownloadString(_myNodeUrl);
                        break;
                    }
                    catch
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(3));
                    }
                }

                Log("Done - Starting Node", "Information");
            }
            catch (Exception ex)
            {
                Log("Exception occured in StartNode: " + ex.ToString(), "Error");
            }
        }

        private void _NodeProcess_Exited(object sender, EventArgs e)
        {
            Log("Node Exited", "Information");
            RoleEnvironment.RequestRecycle();
        }

        private void CopyNodeFiles(string nodePath, string nodeWebAppPath, string nodeInstallPath)
        {
            string nodeInstallPathT = nodeInstallPath;

            if (nodeInstallPath.EndsWith(@"\") || nodeInstallPath.EndsWith("/"))
                nodeInstallPathT = nodeInstallPath.Substring(0, nodeInstallPath.Length - 1); // drop last slash

            ExecuteShellCommand("cmd.exe", String.Format("/C XCOPY \"{0}\" \"{1}\"  /E /Y /Q", nodePath, nodeInstallPathT), true);
            Log(string.Format("Copied Node files from {0} to {1}", nodePath, nodeInstallPathT), "Information");

            ExecuteShellCommand("cmd.exe", String.Format("/C XCOPY \"{0}\" \"{1}\"  /E /Y /Q", nodeWebAppPath, nodeInstallPathT), true);
            Log(string.Format("Copied WebApp files from {0} to {1}", nodeWebAppPath, nodeInstallPathT), "Information");
        }

        private void StartCouch()
        {
            try
            {
                // install couch db into local storage (because we need to modify ini files)
                String couchInstallPath = RoleEnvironment.GetLocalResource("CouchInstall").RootPath;

                // we use an Azure drive to store the Couch data and logs for persistence
                String vhdRootPath = CreateCouchStorageVhd();

                // log file for all our own logging: save it to Azure drive so it will persist even if role crashes
                InitializeLogFile(vhdRootPath);

                // copy couchdb executables and related files to local storage
                CopyCouchFiles(couchInstallPath);

                // create data and log directories for couchdb on Azure drive
                string couchDataPath, couchLogPath;
                CreateCouchDataDirs(vhdRootPath, couchInstallPath, out couchDataPath, out couchLogPath);

                // set everything up with approp paths / ip address / port etc.
                SetupCouchEnvironment(vhdRootPath, couchInstallPath, couchDataPath, couchLogPath);

                // start CouchDB
                string cmdLine = Path.Combine(couchInstallPath, @"bin\couchdb.bat");
                Log("Couch start command line: " + cmdLine, "Information");

                string workingDir = Path.Combine(couchInstallPath, @"bin");
                _couchProcess = ExecuteShellCommand(cmdLine, null, false, workingDir);
                _couchProcess.Exited += new EventHandler(_CouchProcess_Exited);

                // wait for CouchDB to be up
                int n = 0;
                while (true)
                {
                    if (n++ > 10) break;
                    try
                    {
                        new WebClient().DownloadString(_myCouchUrl);
                        break;
                    }
                    catch
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(3));
                    }
                }

                Log("Done - Starting Couch", "Information");

                try
                {
                    // create default db
                    var req = WebRequest.Create(string.Format("{0}{1}", _myCouchUrl, RoleEnvironment.GetConfigurationSettingValue("DatabaseName")));
                    req.Credentials = new NetworkCredential(_username, _password);
                    req.PreAuthenticate = true;
                    req.Method = "PUT";
                    req.GetResponse();
                }
                catch (Exception exc)
                {
                    Log("Exception while creating database: " + exc.ToString(), "Error");
                }
            }
            catch (Exception ex)
            {
                Log("Exception occured in StartCouch: " + ex.ToString(), "Error");
            }
        }

        private void _CouchProcess_Exited(object sender, EventArgs e)
        {
            Log("Couch Exited", "Information");
            RoleEnvironment.RequestRecycle();
        }

        private void SetupCouchEnvironment(string vhdRootPath, string couchInstallPath, string couchDataPath, string couchLogPath)
        {
            // update CouchDb local.ini file
            string iniFilePath = Path.Combine(couchInstallPath, @"etc\couchdb\local.ini");
            var ini = new HelperLib.IniFile(iniFilePath);
            Log("Ini file at: " + iniFilePath, "Information");

            IPEndPoint endpoint = RoleEnvironment.CurrentRoleInstance.InstanceEndpoints["CouchEndpoint"].IPEndpoint;
            ini.SetValue("httpd", "port", endpoint.Port.ToString());
            Log("local.ini port set to: " + ini.GetInt32("httpd", "port"), "Information");

            ini.SetValue("httpd", "bind_address", endpoint.Address.ToString());
            Log("local.ini bind_address set to: " + ini.GetValue<string>("httpd", "bind_address"), "Information");

            string varPath = Path.Combine(couchInstallPath, "var");
            if (Directory.Exists(varPath))
                Directory.Delete(varPath, true);

            Directory.CreateDirectory(varPath);

            // TODO: temporary workaround for couchdb bug that prevents us from saving the db to Azure drive
            if (_couchdb_database_dir_bug && !RoleEnvironment.IsEmulated)
            {
                // map vhd to couchdb "var" directory so local.ini paths will not need to be updated
                string volume = vhdRootPath.Substring(0, vhdRootPath.IndexOf(Path.VolumeSeparatorChar));

                string diskPartFilePath = System.IO.Path.GetTempFileName();
                using (StreamWriter sw = File.CreateText(diskPartFilePath))
                {
                    string cmd1 = string.Format("select volume {0}", volume);
                    sw.WriteLine(cmd1);
                    Log("diskpart cmd1: " + cmd1, "Information");

                    string cmd2 = string.Format("assign mount={0}", varPath);
                    sw.WriteLine(cmd2);
                    Log("diskpart cmd2: " + cmd2, "Information");
                }

                string cmdDiskPart = String.Format("/s \"{0}\"", diskPartFilePath);
                ExecuteShellCommand("diskpart.exe", cmdDiskPart, true);
                Log("diskpart.exe " + cmdDiskPart, "Information");
            }
            else
            {
                ini.SetValue("couchdb", "database_dir", couchDataPath.Replace('\\', '/'));
                Log("local.ini database_dir set to: " + ini.GetValue<string>("couchdb", "database_dir"), "Information");

                ini.SetValue("couchdb", "view_index_dir", couchDataPath.Replace('\\', '/'));
                Log("local.ini view_index_dir set to: " + ini.GetValue<string>("couchdb", "view_index_dir"), "Information");
            
                ini.SetValue("log", "file", Path.Combine(couchLogPath, @"couch.log").Replace('\\', '/'));
                Log("local.ini log file set to: " + ini.GetValue<string>("log", "file"), "Information");
            }

            ini.Save(iniFilePath);

            // need to create directory for run\couchdb otherwise you get a couchdb exception that the path does not exist
            Directory.CreateDirectory(Path.Combine(varPath, "run", "couchdb"));

            // update ERLANG ini file
            iniFilePath = Path.Combine(couchInstallPath, @"bin\erl.ini");
            ini = new HelperLib.IniFile(iniFilePath);
            Log("Ini file at: " + iniFilePath, "Information");

            string erlBinPath = Path.Combine(couchInstallPath, @"erts-5.9\bin");
            if (!Directory.Exists(erlBinPath))
                erlBinPath = Path.Combine(couchInstallPath, @"erts-5.8.4\bin");

            ini.SetValue("erlang", "Bindir", erlBinPath.Replace(@"\", @"\\"));
            Log("erl.ini Bindir set to: " + ini.GetValue<string>("erlang", "Bindir"), "Information");

            ini.SetValue("erlang", "Rootdir", couchInstallPath.Substring(0, couchInstallPath.Length - 1).Replace(@"\", @"\\"));
            Log("erl.ini Rootdir set to: " + ini.GetValue<string>("erlang", "Rootdir"), "Information");

            ini.Save(iniFilePath);

            // set up ERLANG path
            string erlPath = Path.Combine(couchInstallPath, @"bin\erl.exe");
            Environment.SetEnvironmentVariable("ERL", erlPath);
            Log("ERL Environment variable set to: " + erlPath, "Information");
        }

        private String CreateCouchStorageVhd()
        {
            CloudStorageAccount storageAccount;
            LocalResource localCache;
            CloudBlobClient client;
            CloudBlobContainer drives;

            localCache = RoleEnvironment.GetLocalResource("AzureDriveCache");
            Log(String.Format("AzureDriveCache {0} {1} MB", localCache.RootPath, localCache.MaximumSizeInMegabytes - 50), "Information");
            CloudDrive.InitializeCache(localCache.RootPath.TrimEnd('\\'), localCache.MaximumSizeInMegabytes - 50);

            storageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue("DataConnectionString"));
            client = storageAccount.CreateCloudBlobClient();

            string roleId = RoleEnvironment.CurrentRoleInstance.Id;
            string containerAddress = ContainerNameFromRoleId(roleId);
            drives = client.GetContainerReference(containerAddress);

            try { drives.CreateIfNotExist(); }
            catch { };

            var vhdUrl = client.GetContainerReference(containerAddress).GetBlobReference("CouchStorage.vhd").Uri.ToString();
            Log(String.Format("CouchStorage.vhd {0}", vhdUrl), "Information");
            _couchStorageDrive = storageAccount.CreateCloudDrive(vhdUrl);

            int cloudDriveSizeInMB = int.Parse(RoleEnvironment.GetConfigurationSettingValue("CloudDriveSize"));
            try { _couchStorageDrive.Create(cloudDriveSizeInMB); }
            catch (CloudDriveException) { }

            Log(String.Format("CloudDriveSize {0} MB", cloudDriveSizeInMB), "Information");

            var vhdRootPath = _couchStorageDrive.Mount(localCache.MaximumSizeInMegabytes - 50, DriveMountOptions.Force);
            Log(String.Format("Mounted as {0}", vhdRootPath), "Information");

            return vhdRootPath;
        }

        // follow container naming conventions to generate a unique container name
        private static string ContainerNameFromRoleId(string roleId)
        {
            return roleId.Replace('(', '-').Replace(").", "-").Replace('.', '-').Replace('_', '-').ToLower();
        }

        private void CreateCouchDataDirs(String vhdRootPath, string couchInstallPath, out string couchDataPath, out string couchLogPath)
        {
            string destPath = vhdRootPath;
            String sourcePath = Path.Combine(couchInstallPath, @"var");

            if (Directory.Exists(sourcePath))
            {
                ExecuteShellCommand("cmd.exe", String.Format("/C XCOPY \"{0}\" \"{1}\"  /E /Y /Q", sourcePath, destPath), true);
                Log(string.Format("Copied var folder structure from {0} to {1}", sourcePath, destPath), "Information");
            }

            // obtain data and log dirs
            couchDataPath = Path.Combine(destPath, @"lib\couchdb");
            couchLogPath = Path.Combine(destPath, @"log\couchdb");

            // ensure they exist
            if (!Directory.Exists(couchDataPath))
                Directory.CreateDirectory(couchDataPath);

            if (!Directory.Exists(couchLogPath))
                Directory.CreateDirectory(couchLogPath);
        }

        private void InitializeLogFile(string vhdRootPath)
        {
#if DEBUG
            String logFileName;
            String logFileDirectoryLocation;

            logFileDirectoryLocation = Path.Combine(vhdRootPath, "LogFiles");
            if (Directory.Exists(logFileDirectoryLocation) == false)
            {
                Directory.CreateDirectory(logFileDirectoryLocation);
            }

            logFileName = String.Format("Log_{0}.txt", DateTime.Now.ToString("MM_dd_yyyy_HH_mm_ss"));
            using (FileStream logFileStream = File.Create(Path.Combine(logFileDirectoryLocation, logFileName)))
            {
                _logFileLocation = Path.Combine(logFileDirectoryLocation, logFileName);
            }

            Log("Log file at: " + _logFileLocation, "Information");
#endif
        }

        private void CopyCouchFiles(String couchInstallPath)
        {
            string couchInstallPathT = couchInstallPath;
            
            if (couchInstallPath.EndsWith(@"\") || couchInstallPath.EndsWith("/"))
                couchInstallPathT = couchInstallPath.Substring(0, couchInstallPath.Length - 1); // drop last slash

            String sourcePath = Path.Combine(Environment.GetEnvironmentVariable("RoleRoot") + @"\", @"approot\CouchDB");
            ExecuteShellCommand("cmd.exe", String.Format("/C XCOPY \"{0}\" \"{1}\"  /E /Y /Q", sourcePath, couchInstallPathT), true);
            Log(string.Format("Copied Couch files from {0} to {1}", sourcePath, couchInstallPathT), "Information");
        }

        // figure out and set port etc.
        private void InitRoleInfo()
        {
            // node endpoint and url
            IPEndPoint nodeEndpoint = RoleEnvironment.CurrentRoleInstance.InstanceEndpoints["NodeEndpoint"].IPEndpoint;
            _host = nodeEndpoint.Address.ToString();
            _nodePort = nodeEndpoint.Port.ToString();
            _myNodeUrl = string.Format("http://{0}/", nodeEndpoint);
            
            // couch endpoint and url
            IPEndPoint couchEndpoint = RoleEnvironment.CurrentRoleInstance.InstanceEndpoints["CouchEndpoint"].IPEndpoint;
            _couchPort = couchEndpoint.Port.ToString();
            //_myCouchUrl = string.Format("http://{0}:{1}@{2}/", "admin", "password", couchEndpoint);
            _myCouchUrl = string.Format("http://{0}/", couchEndpoint);

            // add couch endpoint to endpoint discovery table
            HelperLib.Util.AddRoleInfoEntry(RoleEnvironment.CurrentRoleInstance.Id, couchEndpoint.Address.ToString(), couchEndpoint.Port);

            Log("My Node / Couch URLs: " + _myNodeUrl + ", " + _myCouchUrl, "Information");
        }

        private Process ExecuteShellCommand(String cmd, String args, bool waitForExit, String workingDir = null)
        {
            Process processToExecuteCommand = new Process();

            processToExecuteCommand.StartInfo.FileName = cmd;
            processToExecuteCommand.StartInfo.Arguments = args;

            if (workingDir != null)
                processToExecuteCommand.StartInfo.WorkingDirectory = workingDir;

            processToExecuteCommand.StartInfo.RedirectStandardInput = true;
            processToExecuteCommand.StartInfo.RedirectStandardError = true;
            processToExecuteCommand.StartInfo.RedirectStandardOutput = true;
            processToExecuteCommand.StartInfo.UseShellExecute = false;
            processToExecuteCommand.StartInfo.CreateNoWindow = true;
            processToExecuteCommand.EnableRaisingEvents = false;
            processToExecuteCommand.Start();

            processToExecuteCommand.OutputDataReceived += new DataReceivedEventHandler(processToExecuteCommand_OutputDataReceived);
            processToExecuteCommand.ErrorDataReceived += new DataReceivedEventHandler(processToExecuteCommand_ErrorDataReceived);
            processToExecuteCommand.BeginOutputReadLine();
            processToExecuteCommand.BeginErrorReadLine();

            if (waitForExit == true)
            {
                processToExecuteCommand.WaitForExit();
                processToExecuteCommand.Close();
                processToExecuteCommand.Dispose();
                processToExecuteCommand = null;
            }

            return processToExecuteCommand;
        }

        private void processToExecuteCommand_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            Log(e.Data, "Message");
        }

        private void processToExecuteCommand_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            Log(e.Data, "Message");
        }

        // update replication relationships based on which couchdb servers are currently alive
        private void UpdateReplication()
        {
            IEnumerable<string> newReplicationSrcAddresses;

            try
            {
                // get addreses of couchdb servers that are currently alive
                newReplicationSrcAddresses = HelperLib.Util.GetReplicationSrcAddresses();
            }
            catch (Exception exc)
            {
                Log("Exception during GetReplicationSrcAddresses: " + exc.ToString(), "Error");
                return;
            }

            Log("CouchEndpoints: " + ((newReplicationSrcAddresses.Count() > 0) ? newReplicationSrcAddresses.Aggregate<string>((a, b) => a + ", " + b) : "None"), "Information"); 

            foreach (var address in replicationSrcAddresses.Except(newReplicationSrcAddresses).ToList())
            {
                if (SetupReplication(address, false)) // cancel old replication relationship
                {
                    replicationSrcAddresses.Remove(address);
                }
            }

            foreach (var address in newReplicationSrcAddresses.Except(replicationSrcAddresses).ToList())
            {
                if (SetupReplication(address, true)) // start new replication relationship
                {
                    replicationSrcAddresses.Add(address);
                }
            }
        }

        // initiates or cancels replication relationship from current couchdb instance to instance specified by replicationSrcAddress, depending upon bInitiate flag
        private bool SetupReplication(string replicationSrcAddress, bool bInitiate)
        {
            try
            {
                var req = WebRequest.Create(string.Format("http://{0}/_replicate",
                    RoleEnvironment.CurrentRoleInstance.InstanceEndpoints["CouchEndpoint"].IPEndpoint));
                
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Credentials = new NetworkCredential(_username, _password);
                req.PreAuthenticate = true;
                
                using (var writer = new StreamWriter(req.GetRequestStream()))
                {
                    string data = string.Format(
                        bInitiate ? @"{{""source"": ""http://{0}/{1}"", ""target"": ""{1}"", ""continuous"": true}}"
                               : @"{{""source"": ""http://{0}/{1}"", ""target"": ""{1}"", ""continuous"": true, ""cancel"": true}}",
                        replicationSrcAddress,
                        RoleEnvironment.GetConfigurationSettingValue("DatabaseName"));

                    Log("Replication command: " + req.RequestUri.ToString() + ", data: " + data, "Information");

                    writer.Write(data);
                }

                req.GetResponse();
                return true;
            }
            catch (Exception exc)
            {
                Log("Exception during /_replicate: " + exc.ToString(), "Error");
            }

            return false;
        }

        private void InitDiagnostics()
        {
#if DEBUG
            // Get the default initial configuration for DiagnosticMonitor.
            DiagnosticMonitorConfiguration diagnosticConfiguration = DiagnosticMonitor.GetDefaultInitialConfiguration();

            // Filter the logs so that only error-level logs are transferred to persistent storage.
            diagnosticConfiguration.Logs.ScheduledTransferLogLevelFilter = LogLevel.Undefined;

            // Schedule a transfer period of 30 minutes.
            diagnosticConfiguration.Logs.ScheduledTransferPeriod = TimeSpan.FromMinutes(2.0);

            // Specify a buffer quota of 1GB.
            diagnosticConfiguration.Logs.BufferQuotaInMB = 1024;

            // Start the DiagnosticMonitor using the diagnosticConfig and our connection string.
            DiagnosticMonitor.Start("Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString", diagnosticConfiguration);
#endif
        }

        private void Log(string message, string category)
        {
#if DEBUG
            message = RoleEnvironment.CurrentRoleInstance.Id + "=> " + message;

            try
            {
                if (String.IsNullOrWhiteSpace(_logFileLocation) == false)
                {
                    File.AppendAllText(_logFileLocation, String.Concat(message, Environment.NewLine));
                }
            }
            catch
            { }

            Trace.WriteLine(message, category);
#endif
        }
    }
}
