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

namespace DeployCmdlets4WA
{
    public class DeploymentModelHelper
    {
        private String _xmlConfigPath;
        private DeploymentModel _model;

        private List<DeploymentModelParametersParameter> _deploymentParams;
        private DeploymentModelStepsStep[] _steps;

        public DeploymentModelHelper(string location)
        {
            _xmlConfigPath = location;
        }

        public void Init()
        {
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(DeploymentModel));
            using (FileStream configFileStream = new FileStream(_xmlConfigPath, FileMode.Open, FileAccess.Read))
            {
                _model = xmlSerializer.Deserialize(configFileStream) as DeploymentModel;
                for (int i = 0; i < _model.Items.Length; i++)
                {
                    if (_model.Items[i] is DeploymentModelParameters)
                    {
                        _deploymentParams = (_model.Items[i] as DeploymentModelParameters).Parameter.ToList();
                        continue;
                    }
                    if (_model.Items[i] is DeploymentModelSteps)
                    {
                        _steps = (_model.Items[i] as DeploymentModelSteps).Step;
                    }
                }
            }
        }

        public IEnumerable<String> GetAllParameters() 
        {
            return _deploymentParams.Select(e => e.Name);
        }

        // the value of a param is either given directly as the Value attribute Or as a combination of the value of another parameter and a suffix. In that case, the name of the other
        // parameter is specified as ValuePrefixRef and the suffix is specified as ValueSuffix.
        public string GetParameterByName(String key)
        {
            IEnumerable<DeploymentModelParametersParameter> paramEnum = _deploymentParams.Where(e => e.Name == key);
            if ((paramEnum == null) || (paramEnum.Count() == 0))
                return null;

            return paramEnum.Select(e => e.Value).FirstOrDefault();
        }

        public bool IsParamValueRequired(String key)
        {
            IEnumerable<DeploymentModelParametersParameter> paramEnum = _deploymentParams.Where(e => e.Name == key);
            if ((paramEnum == null) || (paramEnum.Count() == 0))
                return false;

            string value = paramEnum.Select(e => e.Required).FirstOrDefault();
            return (string.Compare(value, "yes", true) == 0) || (string.Compare(value, "true", true) == 0) || (value == "1");
        }

        public DeploymentModelStepsStep GetStepAtIndex(int stepIndex) 
        {
            return _steps.Length - 1 < stepIndex ? null : _steps[stepIndex];
        }

        public void SetParameterByName(String key, String value)
        {
            DeploymentModelParametersParameter param = _deploymentParams.Where(e => e.Name == key).FirstOrDefault();
            if (param == null)
            {
                param = new DeploymentModelParametersParameter();
                _deploymentParams.Add(param);
                param.Name = key;
            }

            param.Value = value;
        }

        public string GetParamValuePrefixRef(string key)
        {
            IEnumerable<DeploymentModelParametersParameter> paramEnum = _deploymentParams.Where(e => e.Name == key);
            if ((paramEnum == null) || (paramEnum.Count() == 0))
                return null;

            return paramEnum.Select(e => e.ValuePrefixRef).FirstOrDefault();
        }

        public string GetParamValueSuffix(string key)
        {
            IEnumerable<DeploymentModelParametersParameter> paramEnum = _deploymentParams.Where(e => e.Name == key);
            if ((paramEnum == null) || (paramEnum.Count() == 0))
                return null;

            return paramEnum.Select(e => e.ValueSuffix).FirstOrDefault();
        }
    }
}
