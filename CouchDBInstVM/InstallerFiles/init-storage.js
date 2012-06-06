/**
*  Copyright © Microsoft Open Technologies, Inc.
*  All Rights Reserved
*  Apache 2.0 License
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
*   http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*
* See the Apache Version 2.0 License for specific language governing permissions and limitations under the License.
*/

var fs = require('fs');

var red = '\033[31m';
var green = '\033[32m';
var blue = '\033[34m';
var yellow = '\033[33m';
var reset = '\033[0m';

// **************************** Log functions **********************************************************
function logErr (message) {
    console.log('\033[31m' + "error:  " + message + '\033[0m');
}

function logStatus (message) {
    console.log('\033[33m' + "info:   " + message + '\033[0m');
}

function logStatus2 (message) {
    console.log('\033[34m' + "info:   " + message + '\033[0m');
}

showUsageAndExit = function(message) {
    var usage = 'node.exe init-storage.js lib:<path-to-lib> pem:<pem-path> s:<subscription-id> location:<location> host:<host>';
    console.log('\033[33m' + "USAGE:  " + usage + '\033[0m');
    process.exit(1);
};

// **************************** Command-Line Args Processing *******************************************

var input = {
  lib: { 'name': 'path-to-lib', 'value': null, 'found': false },
  pem: { 'name': 'pem-path', 'value': null, 'found': false },
  s: { 'name': 'subscription-id', 'value': null, 'found': false },
  location: { 'name': 'location', 'value': null, 'found': false },
  host: { 'name': 'host', 'value': null, 'found': false }
};

var args = getCommandLineArgs();
if (args.length != 5) {
  showUsageAndExit();
}

for (var i = 0; i < args.length; i++) {
  var k = args[i].key;
  if (typeof input[k] == "undefined") {
    logErr('Unknown option ' + k);
    showUsageAndExit();
  }

  if (input[k].found) {
    logErr('found repeated-option ' + k);
    showUsageAndExit();
  } else {
    input[k].value = args[i].value;
    input[k].found = true;
  }
}

function getCommandLineArgs() {
  var args = [];
  process.argv.forEach(function (val, index, array) {
    if (index != 0 && index != 1) {
      var parts = val.split('=', 2);
      if (parts.length != 2) {
        showUsageAndExit();
      }

      var keyValue = {
        key: parts[0],
        value: parts[1]
      }

      if (keyValue.key == '' || keyValue.value == '') {
        showUsageAndExit();
      }

      args.push(keyValue);
    }
  });

  return args;
}

//***************************** Get the certificate ****************************************************

var KEY_PATT = /(-+BEGIN RSA PRIVATE KEY-+)(\n\r?|\r\n?)([A-Za-z0-9\+\/\n\r]+\=*)(\n\r?|\r\n?)(-+END RSA PRIVATE KEY-+)/;
var CERT_PATT = /(-+BEGIN CERTIFICATE-+)(\n\r?|\r\n?)([A-Za-z0-9\+\/\n\r]+\=*)(\n\r?|\r\n?)(-+END CERTIFICATE-+)/;

var keyCert = readFromFile(input['pem'].value);
var param = {
  subscriptionId : input['s'].value,
  auth : { keyvalue : keyCert.key, certvalue : keyCert.cert}
};

function readFromFile(fileName) {
  // other parameters are optional
  var data = fs.readFileSync(fileName, 'utf8');
  var ret = {};
  var matchKey = data.match(KEY_PATT);
  if (matchKey) {
    ret.key = matchKey[1] + '\n' + matchKey[3] + '\n' + matchKey[5] + '\n';
  } 
  var matchCert = data.match(CERT_PATT);
  if (matchCert) {
    ret.cert = matchCert[1] + '\n' + matchCert[3] + '\n' + matchCert[5] + '\n';
  }
  return ret;
};

//***************************** Check for Status *******************************************************
var azure = require(input['lib'].value + '/azure');
var cli = require(input['lib'].value + '/cli/cli');
var utils = require(input['lib'].value + '/cli/utils');
var blobUtils = require(input['lib'].value + '/cli/blobUtils');

param["host"] = input['host'].value

var svcMgmtChannel = tryGetMgmtServiceInstance(param);
utils.getOrCreateBlobStorage(cli, svcMgmtChannel, input['location'].value, null, 'blobStg', function (error, result) {
    if (error) {
        logErr("Failed to create or retrieve storage account for your subscription in location \"" +  input['location'].value + "\"");
        console.log(error);
        process.exit(1);
    } else {
        var stgUrlInfo = blobUtils.splitDestinationUri(result);
        var progress = cli.progress('Retrieving Storage account keys');
        utils.doServiceManagementOperation(svcMgmtChannel, 'getStorageAccountKeys', stgUrlInfo.accountName, function(error, response) {
            progress.end();
            if (error) {
                logErr("Failed to retrieve keys for the storage account " + stgUrlInfo.accountName);
                console.log(error);
                process.exit(1);
            } else {
                if (!response || !response.body || !response.body.StorageServiceKeys) {
                    logErr("Failed to retrieve keys from server response for the account " + stgUrlInfo.accountName);
                    console.log(response);
                    process.exit(1);
                }
                
                var params2 = {
                    accountName : stgUrlInfo.accountName,
                    key : response.body.StorageServiceKeys.Primary,
                    host : stgUrlInfo.host
                };
                
                var blobService = tryGetBlobServiceInstance(params2);
                progress = cli.progress('Creating image container vm-images if not exists');
                blobService.createContainerIfNotExists('vm-images', null, function(error, result) {
                    progress.end();
                    if (error) {
                        logErr("Creation of image container failed");
                        process.exit(1);
                    } else {
                        if(!result) {
                            logStatus("Image container 'vm-images' already exists");
                        } else {
                            logStatus("Image container 'vm-images' created");
                        }
                        
                        progress = cli.progress('Setting ACL for the image container');
                        blobService.setContainerAcl('vm-images', azure.Constants.BlobConstants.BlobContainerPublicAccessType.BLOB, null, function (error, result) {
                            progress.end();
                            if (error) {
                                logErr("Failed to set ACL");
                                process.exit(1);
                            } else {
                                process.exit(0);
                            }
                        });
                    }
                });
            }	      
        });
    }
});

function tryGetBlobServiceInstance(param)
{
  var managementService = null;
  try {
    managementService = azure.createBlobService(
      param.accountName,
      param.key,
      param.host);
  } catch (error) {
    exitWithError('Failed to create ServiceManagementService instance -- ' + error);
  }

  return managementService;
}

function tryGetMgmtServiceInstance(param)
{
  var managementService = null;
  try {
    managementService = azure.createServiceManagementService(
      param.subscriptionId,
      param.auth,
      {
          host: param.host
      }
    );
  } catch (error) {
    exitWithError('Failed to create ServiceManagementService instance -- ' + error);
  }

  return managementService;
}