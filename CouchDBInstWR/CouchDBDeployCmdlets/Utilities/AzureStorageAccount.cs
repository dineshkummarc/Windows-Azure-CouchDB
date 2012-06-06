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
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Xml;
using System.Threading;
using System.Timers;

namespace CouchDBDeployCmdlets.Utilities
{
    public class AzureStorageAccount
    {
        private string _storageAccountName;
        private string _subscriptionId;
        private string _location;
        private string _affinityGroup;

        private X509Certificate2 _cert;
        private static System.Timers.Timer deployAppWaitTimer;

        public void Create(X509Certificate2 managementCert, string subscriptionId, string storageAccountName, string affinityGroup, string location)
        {
            _storageAccountName = storageAccountName.ToLower();
            _subscriptionId = subscriptionId;
            _cert = managementCert;
            _location = location;
            _affinityGroup = affinityGroup;

            Console.WriteLine("Creating storage account: " + _storageAccountName);
            Console.WriteLine("This may take a few minutes.");

            string token = BeginCreatStorageAccount();
            WaitForDeploymentComplete(token);
        }

        private string BeginCreatStorageAccount()
        {
            string requestUri;
            string requestXml;
            string deploymentError;

            HttpWebRequest webRequest;
            HttpWebResponse webResponse = null;
            WebResponse errorResponse;
            byte[] hostedServiceRequestXml;
            try
            {
                requestXml = GetCreateStorageAccRequestXml();
                requestUri = string.Format("https://management.core.windows.net/{0}/services/storageservices", _subscriptionId);

                webRequest = (HttpWebRequest)WebRequest.Create(requestUri);
                webRequest.Method = "POST";
                webRequest.ContentType = "application/xml";
                webRequest.ClientCertificates.Add(_cert);
                webRequest.Headers.Add("x-ms-version", "2011-06-01");

                using (Stream requestStream = webRequest.GetRequestStream())
                {
                    hostedServiceRequestXml = UTF8Encoding.UTF8.GetBytes(requestXml);
                    requestStream.Write(hostedServiceRequestXml, 0, hostedServiceRequestXml.Length);
                }
                try
                {
                    webResponse = (HttpWebResponse)webRequest.GetResponse();
                }
                catch (WebException ex)
                {
                    errorResponse = ex.Response;
                    using (StreamReader sr = new StreamReader(errorResponse.GetResponseStream()))
                    {
                        deploymentError = sr.ReadToEnd();
                    }
                    String errorMsg = GetErrorMessage(deploymentError);
                    Console.WriteLine("Error occured while sending deployment request. Error - " + 
                                      (String.IsNullOrEmpty(errorMsg) ? deploymentError : errorMsg));
                    throw; 
                }
                if (webResponse.StatusCode != HttpStatusCode.Accepted)
                {
                    throw new Exception(@"Error creating storage account. Error code - " + webResponse.StatusCode.ToString() + " Description - " + webResponse.StatusDescription);
                }
                return webResponse.Headers["x-ms-request-id"];
            }
            finally
            {
                if (webResponse != null) webResponse.Close();
            }
        }

        private string GetCreateStorageAccRequestXml()
        {
            string requestXml;
            StringBuilder sb = new StringBuilder();

            using (MemoryStream ms = new MemoryStream())
            {
                XmlWriter storageAccCreateXmlCreator = XmlTextWriter.Create(ms);
                storageAccCreateXmlCreator.WriteStartDocument();
                storageAccCreateXmlCreator.WriteStartElement("CreateStorageServiceInput", @"http://schemas.microsoft.com/windowsazure");
                storageAccCreateXmlCreator.WriteElementString("ServiceName", _storageAccountName);
                storageAccCreateXmlCreator.WriteElementString("Description", String.Empty);
                storageAccCreateXmlCreator.WriteElementString("Label", Convert.ToBase64String(System.Text.UTF8Encoding.UTF8.GetBytes(_storageAccountName)));
                if (string.IsNullOrEmpty(_location) == false)
                {
                    storageAccCreateXmlCreator.WriteElementString("Location", _location);
                }
                else
                {
                    storageAccCreateXmlCreator.WriteElementString("AffinityGroup", _affinityGroup);
                }
                storageAccCreateXmlCreator.WriteEndElement();
                storageAccCreateXmlCreator.WriteEndDocument();

                storageAccCreateXmlCreator.Flush();
                storageAccCreateXmlCreator.Close();

                using (StreamReader sr = new StreamReader(ms))
                {
                    ms.Position = 0;
                    requestXml = sr.ReadToEnd();
                    sr.Close();
                }
            }
            return requestXml;
        }

        private void WaitForDeploymentComplete(string requestToken)
        {
            AutoResetEvent threadBlocker = null;
            try
            {
                threadBlocker = new AutoResetEvent(false);

                deployAppWaitTimer = new System.Timers.Timer(5000);
                deployAppWaitTimer.Elapsed += new ElapsedEventHandler(
                    delegate(object sender, ElapsedEventArgs e)
                    {
                        string requestUri;
                        string responseXml;
                        bool isError;

                        HttpWebRequest webRequest;
                        HttpWebResponse webResponse = null;

                        try
                        {
                            Console.Write("Storage account creation status: ");
                            deployAppWaitTimer.Stop();
                            requestUri = string.Format("https://management.core.windows.net/{0}/operations/{1}", _subscriptionId, requestToken);

                            webRequest = (HttpWebRequest)WebRequest.Create(requestUri);
                            webRequest.Method = "GET";
                            webRequest.ClientCertificates.Add(_cert);
                            webRequest.Headers.Add("x-ms-version", "2009-10-01");

                            webResponse = (HttpWebResponse)webRequest.GetResponse();
                            if (webResponse.StatusCode != HttpStatusCode.OK)
                            {
                                throw new Exception(@"Error fetching status code for creating storage account. Error code - " +
                                                    webResponse.StatusCode.ToString() +
                                                    " Description - " + webResponse.StatusDescription);
                            }

                            using (Stream responseStream = webResponse.GetResponseStream())
                            using (StreamReader responseStreamReader = new StreamReader(responseStream))
                            {
                                responseXml = responseStreamReader.ReadToEnd();
                                if (IsDeploymentComplete(responseXml, out isError) == true)
                                {
                                    Console.WriteLine("Successfull.");
                                    deployAppWaitTimer.Dispose();
                                    threadBlocker.Set();
                                }
                                else if (isError == true) //Give up.
                                {
                                    Console.WriteLine("Failed.");
                                    deployAppWaitTimer.Dispose();
                                    threadBlocker.Set();
                                }
                                else
                                {
                                    Console.WriteLine("In progress.");
                                    deployAppWaitTimer.Start();
                                }
                            }
                        }
                        finally
                        {
                            if (webResponse != null) webResponse.Close();
                        }
                    });

                deployAppWaitTimer.Start();
                threadBlocker.WaitOne();
            }
            finally
            {
                if (threadBlocker != null) threadBlocker.Close();
            }
        }

        private String GetErrorMessage(string responseXml)
        {
            XmlDocument loadedXml = new XmlDocument();
            loadedXml.LoadXml(responseXml);
            XmlNode errorMsgNode = loadedXml.SelectSingleNode("Error/Message");
            return errorMsgNode == null ? String.Empty : errorMsgNode.InnerText;
        }

        private bool IsDeploymentComplete(string responseXml, out bool isError)
        {
            bool isComplete;
            XmlDocument responseXmlDoc;
            XmlNode statusCode;
            XmlNamespaceManager namespaceManager;

            responseXmlDoc = new XmlDocument();
            responseXmlDoc.LoadXml(responseXml);

            namespaceManager = new XmlNamespaceManager(responseXmlDoc.NameTable);
            namespaceManager.AddNamespace("wa", "http://schemas.microsoft.com/windowsazure");
            namespaceManager.AddNamespace("i", "http://www.w3.org/2001/XMLSchema-instance");

            statusCode = responseXmlDoc.SelectSingleNode("/wa:Operation/wa:Status", namespaceManager);

            switch (statusCode.InnerText)
            {
                case "InProgress":
                    isComplete = false;
                    isError = false;
                    break;
                case "Succeeded":
                    isComplete = true;
                    isError = false;
                    break;
                default:
                case "Failed":
                    isComplete = false;
                    isError = true;
                    XmlNode errorNode = responseXmlDoc.SelectSingleNode("/wa:Operation/wa:Error", namespaceManager);
                    XmlNode errorCode = errorNode.SelectSingleNode("wa:Code", namespaceManager);
                    XmlNode errorMessage = errorNode.SelectSingleNode("wa:Message", namespaceManager);
                    throw new Exception(String.Format("Error during storage account creation - Error code is {0} and Error message is {1}", errorCode.InnerText, errorMessage.InnerText));
            }
            return isComplete;
        }
    }
}
