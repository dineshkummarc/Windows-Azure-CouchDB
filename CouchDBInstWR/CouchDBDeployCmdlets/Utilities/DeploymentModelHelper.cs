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
using System.Xml.Serialization;
using System.IO;

namespace CouchDBDeployCmdlets.Utilities
{
    public class DeploymentModelHelper
    {
        private String _locationOfDeploymentModelXml;
        private DeploymentModel _model;

        private DeploymentModelParametersParameter[] _deploymentParams;
        private DeploymentModelStepsStep[] _steps;

        public DeploymentModelHelper(string location)
        {
            _locationOfDeploymentModelXml = location;
        }

        public void Init()
        {
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(DeploymentModel));
            using (FileStream configFileStream = new FileStream(_locationOfDeploymentModelXml, FileMode.Open, FileAccess.Read))
            {
                _model = xmlSerializer.Deserialize(configFileStream) as DeploymentModel;
                for (int i = 0; i < _model.Items.Length; i++)
                {
                    if (_model.Items[i] is DeploymentModelParameters)
                    {
                        _deploymentParams = (_model.Items[i] as DeploymentModelParameters).Parameter;
                        continue;
                    }
                    if (_model.Items[i] is DeploymentModelSteps)
                    {
                        _steps = (_model.Items[i] as DeploymentModelSteps).Step;
                    }
                }
            }
        }

        public string GetParameterByName(String key)
        {
            return _deploymentParams.Where(e => e.Name == key).Select(e => e.Value).FirstOrDefault();
        }

        public DeploymentModelStepsStep GetStepAtIndex(int stepIndex) 
        {
            return _steps.Length - 1 < stepIndex ? null : _steps[stepIndex];
        }  
    }
}
