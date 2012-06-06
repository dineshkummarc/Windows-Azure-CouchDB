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

namespace CouchDBDeployCmdlets.Couch.Cmdlet
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Management.Automation;
    using System.Reflection;
    using System.ServiceModel;
    using System.Xml.Serialization;
    using CouchDBDeployCmdlets.Properties;
    using CouchDBDeployCmdlets.ServiceConfigurationSchema;
    using CouchDBDeployCmdlets.ServiceDefinitionSchema;
    using CouchDBDeployCmdlets.Utilities;
    using Microsoft.PowerShell.Commands;
    using Microsoft.WindowsAzure;
    using Microsoft.WindowsAzure.StorageClient;
    using AzureDeploymentCmdlets.WAPPSCmdlet;
    using AzureDeploymentCmdlets.Model;
    using AzureDeploymentCmdlets.Scaffolding;
    using Microsoft.Win32;

    [Cmdlet(VerbsCommon.Add, "AzureCouchWorkerRole")]
    public class AddAzureCouchWorkerRoleCommand : AddRole
    {
        private int numOfInstances;
        private string azureCouchDBInstallFolder;
        private Session session;

        [Parameter(Mandatory = false, HelpMessage = "Set the VMSize for CouchDb Worker role.")]
        public RoleSize? Size { get; set; }

        public AddAzureCouchWorkerRoleCommand()
        {
            numOfInstances = 0;
        }

        public string AddAzureCouchRoleProcess()
        {
            azureCouchDBInstallFolder = General.Instance.GetAzureCouchDBInstallFolder();

            string couchDBFileName = Path.Combine(azureCouchDBInstallFolder, Resources.CouchDBBinaryFileName);
            if (!File.Exists(couchDBFileName))
            {
                throw new Exception(Resources.CouchDBBinariesNotAvailableMessage);
            }

            // Number of instances should be greater than 0
            numOfInstances = Instances;
            if (numOfInstances == 0)
            {
                throw new Exception(Resources.InvalidRoleInstancesMessage);
            }

            string serviceRootPath = base.GetServiceRootPath();
            string localSvcConfigFileName = General.Instance.LocalSvcConfigFilePath(serviceRootPath);
            string cloudSvcConfigFileName = General.Instance.CloudSvcConfigFilePath(serviceRootPath);
            string serviceDefinitionFileName = General.Instance.SvcDefinitionFilePath(serviceRootPath);
            string roleName;
            int webRoleOccurrence;
            int workerRoleOccurrence;
            ServiceConfiguration localSvcConfig;
            ServiceConfiguration cloudSvcConfig;
            ServiceDefinition svcDef;

            RoleSettings roleSettings;
            WorkerRole workerRole;

            // Loading *.cscfg and *csdef files
            localSvcConfig = General.Instance.ParseFile<ServiceConfiguration>(localSvcConfigFileName);
            cloudSvcConfig = General.Instance.ParseFile<ServiceConfiguration>(cloudSvcConfigFileName);
            svcDef = General.Instance.ParseFile<ServiceDefinition>(serviceDefinitionFileName);

            GetRoleOccurrence(svcDef, out webRoleOccurrence, out workerRoleOccurrence);

            bool updated = false;

            // Set role name
            roleName = this.Name;
            if (string.IsNullOrEmpty(this.Name))
            {
                roleName = Resources.WorkerRole;
            }

            string message = string.Format(Resources.AddAzureCouchWorkerRoleSuccessMessage,
                (this.Name == null ? Resources.WorkerRole : this.Name), this.Instances, serviceRootPath);

            if (workerRoleOccurrence > 0)
            {
                foreach (RoleSettings role in localSvcConfig.Role)
                {
                    if (role.name == roleName)
                    {
                        role.Instances.count = numOfInstances;
                        updated = true;
                        break;
                    }
                }
                foreach (RoleSettings role in cloudSvcConfig.Role)
                {
                    if (role.name == roleName)
                    {
                        role.Instances.count = numOfInstances;
                        updated = true;
                    }
                }
            }

            if (!updated)
            {
                // Get default RoleSettings template
                roleSettings = GetRoleTemplate(roleName);

                // Set instance count
                roleSettings.Instances.count = numOfInstances;


                // Add role to local and cloud *.cscfg
                AddNewRole(localSvcConfig, roleSettings);
                AddNewRole(cloudSvcConfig, roleSettings);

                // Get default WorkerRole template
                GetWorkerRoleTemplate(roleName, ref workerRoleOccurrence, out workerRole);
                workerRole.vmsize = Size == null ? RoleSize.Small : Size.Value;

                // Add WorkerRole to *.csdef file
                AddNewWorkerRole(svcDef, workerRole);

                // Add WorkerRole scaffolding
                CreateScaffolding(roleName, false);
            }
            else
            {
                message = string.Format(Resources.UpdateAzureCouchWorkerRoleSuccessMessage,
                    (this.Name == null ? Resources.WorkerRole : this.Name), this.Instances, serviceRootPath);
            }

            General.SerializeXmlFile<ServiceConfiguration>(localSvcConfig, localSvcConfigFileName);
            General.SerializeXmlFile<ServiceConfiguration>(cloudSvcConfig, cloudSvcConfigFileName);
            General.SerializeXmlFile<ServiceDefinition>(svcDef, serviceDefinitionFileName);

            return message;
        }

        private void CreateScaffolding(string roleFolderName, bool isWebRole)
        {
            string sourceDir = Path.Combine(azureCouchDBInstallFolder, Resources.CouchScaffoldFolder);
            string destinationDir = Path.Combine(base.GetServiceRootPath(), roleFolderName);
            Scaffold.GenerateScaffolding(sourceDir, destinationDir, new Dictionary<string, object>());
        }

        private void GetRoleOccurrence(ServiceDefinition serviceDefinition, out int webRoleOccurrence, out int workerRoleOccurrence)
        {
            webRoleOccurrence = (serviceDefinition.WebRole == null) ? 0 : serviceDefinition.WebRole.Length;
            workerRoleOccurrence = (serviceDefinition.WorkerRole == null) ? 0 : serviceDefinition.WorkerRole.Length;
        }

        private RoleSettings GetRoleTemplate(string roleName)
        {
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(ServiceConfiguration));
            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream s = assembly.GetManifestResourceStream(ResourceName.RoleSettingsTemplate);
            RoleSettings roleSettings = ((ServiceConfiguration)xmlSerializer.Deserialize(s)).Role[0];
            roleSettings.name = roleName;
            s.Close();

            return roleSettings;
        }

        private void AddNewRole(ServiceConfiguration sc, RoleSettings newRole)
        {
            int count = (sc.Role == null) ? 0 : sc.Role.Length;
            RoleSettings[] roleSettings = new RoleSettings[count + 1];

            if (count > 0)
            {
                sc.Role.CopyTo(roleSettings, 0);
            }
            roleSettings[count] = newRole;
            sc.Role = roleSettings;
        }

        private void GetWorkerRoleTemplate(string workerRoleName, ref int workerRoleOccurrence, out WorkerRole workerRole)
        {
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(ServiceDefinition));
            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream stream = assembly.GetManifestResourceStream(ResourceName.WorkerRoleTemplate);
            workerRole = ((ServiceDefinition)xmlSerializer.Deserialize(stream)).WorkerRole[0];
            stream.Close();
            workerRole.name = workerRoleName;
            workerRoleOccurrence++;
        }

        private void AddNewWorkerRole(ServiceDefinition sd, WorkerRole newWorkerRole)
        {
            int count = (sd.WorkerRole == null) ? 0 : sd.WorkerRole.Length;
            WorkerRole[] workerRoles = new WorkerRole[count + 1];

            if (count > 0)
            {
                sd.WorkerRole.CopyTo(workerRoles, 0);
            }
            workerRoles[count] = newWorkerRole;
            sd.WorkerRole = workerRoles;
        }

        protected override void ProcessRecord()
        {
            try
            {
                base.ProcessRecord();

                string result = this.AddAzureCouchRoleProcess();

                WriteObject(result);
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, string.Empty, ErrorCategory.CloseError, null));
            }
        }

        public Session Session
        {
            get { return (session == null ? session = Session.GetSession(base.GetServiceRootPath(), true) : session); }
        }
    }
}
