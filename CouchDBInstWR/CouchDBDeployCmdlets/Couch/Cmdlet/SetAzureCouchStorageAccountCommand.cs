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
using AzureDeploymentCmdlets.WAPPSCmdlet;
using AzureDeploymentCmdlets.Model;

namespace CouchDBDeployCmdlets.Couch.Cmdlet
{

    using System;
    using System.Management.Automation;
    using System.Security.Permissions;
    using System.ServiceModel;
    using AzureDeploymentCmdlets.Model;
    using CouchDBDeployCmdlets.Properties;
    using CouchDBDeployCmdlets.Utilities;
    using AzureDeploymentCmdlets.WAPPSCmdlet;
    using CouchDBDeployCmdlets.ServiceDefinitionSchema;
    using CouchDBDeployCmdlets.ServiceConfigurationSchema;
    using System.Xml.Serialization;
    using System.IO;
    using CouchDBDeployCmdlets.Model;

    [Cmdlet(VerbsCommon.Set, "AzureCouchStorageAccount")]
    public class SetAzureCouchStorageAccountCommand : ServiceManagementCmdletBase
    {
        AzureService azureService;

        [Parameter(Position = 0, HelpMessage = "New Windows Azure Storage Account Name", Mandatory = true)]
        [Alias("st")]
        public String StorageAccountName { get; set; }

        [Parameter(Position = 1, HelpMessage = "CouchDB Role Name", Mandatory = false)]
        [Alias("cr")]
        public String CouchDBRoleName { get; set; }

        [Parameter(Position = 2, HelpMessage = "Windows Azure Storage Account Location", Mandatory = false)]
        [Alias("l")]
        public String StorageAccountLocation { get; set; }

        [Parameter(Position = 3, HelpMessage = "Windows Azure Subscription", Mandatory = false)]
        [Alias("sn")]
        public String Subscription { get; set; }

        [Parameter(Position = 4, HelpMessage = "Affinity group for storage account", Mandatory = false)]
        [Alias("ag")]
        public string AffinityGroup { get; set; }

        private Session session;
        private string cloudSvcConfigFileName;
        private string serviceDefinitionFileName;
        private ServiceConfiguration cloudSvcConfig;
        private ServiceDefinition svcDef;
        private int webRoleOccurrence;
        private int workerRoleOccurrence;

        public SetAzureCouchStorageAccountCommand() { }

        public SetAzureCouchStorageAccountCommand(IServiceManagement channel) { }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        protected override void ProcessRecord()
        {
            try
            {
                base.ProcessRecord();
                string result = this.SetAzureCouchStorageAccountProcess(base.GetServiceRootPath());
                WriteObject(result);
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, string.Empty, ErrorCategory.CloseError, null));
                throw;
            }
        }

        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust")]
        private string SetAzureCouchStorageAccountProcess(string rootPath)
        {
            InitializeArgs(rootPath);
            if (!StorageAccountExists(this.StorageAccountName))
            {
                CreateStorageAccount(this.StorageAccountName.ToLower(), this.StorageAccountName.ToLower(), azureService.Components.Settings.Location, this.AffinityGroup);
                WriteObject(string.Format(Resources.AzureStorageAccountCreatedMessage, this.StorageAccountName));
            }
            else
            {
                WriteObject(string.Format(Resources.AzureStorageAccountAlreadyExistsMessage, this.StorageAccountName));
            }

            session = Session.GetSession(SessionState.Path.ParseParent(rootPath, null), false);
            serviceDefinitionFileName = General.Instance.SvcDefinitionFilePath(rootPath);
            cloudSvcConfigFileName = General.Instance.CloudSvcConfigFilePath(rootPath);

            // Loading *.cscfg and *csdef files
            cloudSvcConfig = General.Instance.ParseFile<ServiceConfiguration>(cloudSvcConfigFileName);
            svcDef = General.Instance.ParseFile<ServiceDefinition>(serviceDefinitionFileName);

            GetRoleOccurrence(svcDef, out webRoleOccurrence, out workerRoleOccurrence);
            ConfigureRoleStorageAccountKeys();

            return string.Format(Resources.AzureStorageAccountConfiguredForCouchRoleMessage, this.StorageAccountName.ToLower(), this.CouchDBRoleName);
        }

        private bool ConfigureRoleStorageAccountKeys()
        {
            string primaryKey;
            string secondaryKey;

            if (this.ExtractStorageKeys(this.subscriptionId, this.StorageAccountName, out primaryKey, out secondaryKey))
            {
                const string cloudStorageFormat = "DefaultEndpointsProtocol={0};AccountName={1};AccountKey={2}";
                string storageHttpKey = string.Format(cloudStorageFormat, "http", this.StorageAccountName, primaryKey);
                string storageHttpsKey = string.Format(cloudStorageFormat, "https", this.StorageAccountName, primaryKey);

                for (int i = 0; i < cloudSvcConfig.Role.Length; i++)
                {
                    if (cloudSvcConfig.Role[i].name.Equals(this.CouchDBRoleName))
                    {
                        CouchDBDeployCmdlets.ServiceConfigurationSchema.ConfigurationSetting newSetting;
                        newSetting = new ServiceConfigurationSchema.ConfigurationSetting() { name = Resources.DataConnectionStringSettingName, value = storageHttpKey };
                        UpdateSetting(ref cloudSvcConfig.Role[i], newSetting);

                        newSetting = new ServiceConfigurationSchema.ConfigurationSetting() { name = Resources.DiagnosticsConnectionString, value = storageHttpKey };
                        UpdateSetting(ref cloudSvcConfig.Role[i], newSetting);

                        newSetting = new CouchDBDeployCmdlets.ServiceConfigurationSchema.ConfigurationSetting() { name = Resources.DiagnosticsConnectionString, value = storageHttpsKey };
                        UpdateSetting(ref cloudSvcConfig.Role[i], newSetting);
                    }
                }

                if (svcDef.WorkerRole != null)
                {
                    foreach (WorkerRole role in svcDef.WorkerRole)
                    {
                        if (role.name.Equals(this.CouchDBRoleName))
                        {
                            if (role.LocalResources != null)
                            {
                                foreach (LocalStore store in role.LocalResources.LocalStorage)
                                {
                                    if (store.name.Equals(Resources.LocalStorageCouchInstall) && session[Resources.LocalStorageCouchInstall] != null)
                                    {
                                        store.sizeInMB = Convert.ToInt32(session[Resources.LocalStorageCouchInstall]);
                                    }
                                    if (store.name.Equals(Resources.LocalStorageAzureDriveCache) && session[Resources.LocalStorageAzureDriveCache] != null)
                                    {
                                        store.sizeInMB = Convert.ToInt32(session[Resources.LocalStorageAzureDriveCache]);
                                    }
                                }
                            }
                        }
                    }
                }

                // Serialize local.cscfg
                General.SerializeXmlFile<ServiceConfiguration>(cloudSvcConfig, cloudSvcConfigFileName);
                General.SerializeXmlFile<ServiceDefinition>(svcDef, serviceDefinitionFileName);

                return true;
            }
            return false;
        }

        private bool UpdateSetting(ref RoleSettings rs, CouchDBDeployCmdlets.ServiceConfigurationSchema.ConfigurationSetting cs)
        {
            bool done = false;
            int count = (rs.ConfigurationSettings == null) ? 0 : rs.ConfigurationSettings.Length;
            for (int i = 0; i < count; i++)
            {
                CouchDBDeployCmdlets.ServiceConfigurationSchema.ConfigurationSetting setting = rs.ConfigurationSettings[i];

                if (setting.name == cs.name)
                {
                    setting.value = cs.value;
                    done = true;
                }
            }
            return done;
        }

        private bool ExtractStorageKeys(string subscriptionId, string storageName, out string primaryKey, out string secondaryKey)
        {
            StorageService storageService = null;
            try
            {
                storageService = this.Channel.GetStorageKeys(
                    subscriptionId,
                    storageName);
            }
            catch (CommunicationException)
            {
                throw;
            }
            primaryKey = storageService.StorageServiceKeys.Primary;
            secondaryKey = storageService.StorageServiceKeys.Secondary;

            return true;
        }

        private void GetRoleOccurrence(ServiceDefinition serviceDefinition, out int webRoleOccurrence, out int workerRoleOccurrence)
        {
            webRoleOccurrence = (serviceDefinition.WebRole == null) ? 0 : serviceDefinition.WebRole.Length;
            workerRoleOccurrence = (serviceDefinition.WorkerRole == null) ? 0 : serviceDefinition.WorkerRole.Length;
        }

        public void CreateStorageAccount(string storageAccountName, string label, string location, string affinityGroup)
        {
            AzureStorageAccount createStorageAccount = new AzureStorageAccount();
            createStorageAccount.Create(this.certificate, this.subscriptionId, storageAccountName, affinityGroup: affinityGroup, location: location);
        }

        private bool StorageAccountExists(string storageAccountName)
        {
            StorageService storageService = null;
            try
            {
                storageService = this.RetryCall(s => this.Channel.GetStorageService(s, storageAccountName));
            }
            catch (CommunicationException)
            {
                return false;
            }
            return (storageService != null);
        }

        private void InitializeArgs(string rootPath)
        {
            azureService = new AzureService(rootPath, null);
            azureService.Components.Settings.Location = SetLocation();
            azureService.Components.Settings.Subscription = new GlobalComponents(GlobalPathInfo.GlobalSettingsDirectory).GetSubscriptionId(Subscription);
            azureService.Components.Settings.StorageAccountName = SetStorageAccountName();
            this.subscriptionId = azureService.Components.Settings.Subscription;
            if (String.IsNullOrEmpty(this.CouchDBRoleName))
            {
                this.CouchDBRoleName = Resources.WorkerRole;
            }
        }

        private string SetStorageAccountName()
        {
            if (String.IsNullOrEmpty(StorageAccountName))
            {
                if (String.IsNullOrEmpty(azureService.Components.Settings.StorageAccountName))
                {
                    return azureService.ServiceName.ToLower();
                }
                else
                {
                    return azureService.Components.Settings.StorageAccountName;
                }
            }
            else
            {
                return StorageAccountName.ToLower();
            }
        }

        private string SetLocation()
        {
            // Check if user provided location or not
            //
            if (string.IsNullOrEmpty(StorageAccountLocation))
            {
                // Check if there is no location set in service settings
                //
                if (string.IsNullOrEmpty(azureService.Components.Settings.Location))
                {
                    if (string.IsNullOrEmpty(this.AffinityGroup) == true)
                    {
                        // Randomly use "North Central US" or "South Central US"
                        //
                        int randomLocation = General.GetRandomFromTwo(
                            (int)AzureDeploymentCmdlets.Model.Location.NorthCentralUS,
                            (int)AzureDeploymentCmdlets.Model.Location.SouthCentralUS);
                        return ArgumentConstants.Locations[(AzureDeploymentCmdlets.Model.Location)randomLocation];
                    }
                    else
                    {
                        return string.Empty;
                    }
                }

                return azureService.Components.Settings.Location;
            }
            else
            {
                // If location is provided use it
                //
                return StorageAccountLocation;
            }
        }
    }
}
