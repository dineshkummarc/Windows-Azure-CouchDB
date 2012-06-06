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

function logErr (message) {
    console.log('\033[31m' + "error:  " + message +'\033[0m');
}

function logStatus (message) {
    console.log('\033[33m' + "info:   " + message +'\033[0m');
}

function logStatus2 (message) {
    console.log('\033[32m' + "info:   " + message +'\033[0m');
}

// ************* Progress display **********************************************************************
var progressChars = ['-', '\\', '|', '/'];
var progressIndex = 0;
var activeProgressTimer;

function drawAndUpdateProgress() {
    fs.writeSync(2, '\r');
    process.stderr.write(progressChars[progressIndex]);

    progressIndex = progressIndex + 1;
    if (progressIndex == progressChars.length) {
      progressIndex = 0;
    }
}

function clearProgress() {
    fs.writeSync(2, '\r');
    fs.writeSync(2, clearBuffer);
    fs.writeSync(2, '\r');
}

progress = function(label) {

    // Clear the console
    fs.writeSync(2, '\r');
    fs.writeSync(2, clearBuffer);
    
    // Draw initial progress
    drawAndUpdateProgress();
    
    // Draw label
    if (label) {
        fs.writeSync(2, ' ' + label);
    }
    
    activeProgressTimer = setInterval(function() {
        drawAndUpdateProgress();
    }, 200);

    return {
        end: function() {
            clearInterval(activeProgressTimer);
            activeProgressTimer = null;
            
            clearProgress();
        }
    };
};

var clearBuffer = new Buffer(79, 'utf8');
clearBuffer.fill(' ');
clearBuffer = clearBuffer.toString();

// **************************** Exit *******************************************************************
exitWithError = function(message) {
    // Stop progress
    if (activeProgressTimer) {
        clearInterval(activeProgressTimer);
    }

    logErr(message);
    process.exit(1);
};

// *************************** Usage *******************************************************************

showUsageAndExit = function(message) {
    var usage = 'node.exe validate-image-name.js lib:<path-to-lib> pem:<pem-path> s:<subscription-id> imagename:<image-name> host:<host>';
    // Stop progress
    if (activeProgressTimer) {
        clearInterval(activeProgressTimer);
    }

    console.log( '\033[33m' + "USAGE:  " + usage + '\033[0m');
    process.exit(1);
};

// **************************** Command-Line Args Processing *******************************************

var input = {
  lib: { 'name': 'path-to-lib', 'value': null, 'found': false },
  pem: { 'name': 'pem-path', 'value': null, 'found': false },
  s: { 'name': 'subscription-id', 'value': null, 'found': false },
  imagename: { 'name': 'imagename', 'value': null, 'found': false },
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

function getCommandLineArgs(usage) {
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

//***************************** Get the certificates ***************************************************

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

param["host"] = input['host'].value

var managementService = tryGetMgmtServiceInstance(param);
var prg = progress('Retrieving OS images')

managementService.listOSImage(function (error, rspobj) {
    if (rspobj.isSuccessful && rspobj.body) {
       prg.end();
       var images = rspobj.body
       var imageCount = images.length;
       for(var i = 0; i < imageCount; i++) {
         if (images[i].Name === input['imagename'].value) {
           logStatus("Found OS image with name '" + input['imagename'].value + "'");
           if (images[i].Category === 'Microsoft' || images[i].Category === 'microsoft') {
             logErr("This OS image is a Windows Platform Image, which will not have WinRM service enabled")
             logStatus2("If you already have a custom Windows image with WinRM service enabled in this subscription, please rerun the script with name of that image as <image-name> ")
             logStatus2("If you don't have then rerun this script again with <image-name> which does not exists, we will create WinRM enabled image for you with that name")
             process.exit(1)
           } else if (images[i].Category !== 'User' && images[i].Category !== 'user') {
             logErr("This OS image belongs to the category " + images[i].Category + " (" + images[i].OS + "), tool requires a custom Windows OS image with WinRM service enabled")
             logStatus2("If you already have a custom Windows image with WinRM service enabled in this subscription, please rerun the script with name of that image as <image-name> ")
             logStatus2("If you don't have then rerun this script again with <image-name> which does not exists, we will create WinRM enabled image for you with that name")
             process.exit(1)
           } else {
             // User Category
             if (images[i].OS !== 'Windows' && images[i].OS !== 'windows') {
               logErr("This OS image is a custom image, but OS type is " + images[i].OS + ", tool requires a custom Windows OS image with WinRM service enabled")
               logStatus2("If you already have a custom Windows image with WinRM service enabled in this subscription, please rerun the script with name of that image as <image-name> ")
               logStatus2("If you don't have then rerun this script again with <image-name> which does not exists, we will create WinRM enabled image for you with that name")
               process.exit(1)
             } else {
               // This is a Custom (user) Windows image, but get confirmation from user whether this is a
               // winrm enabled image before proceeding
               process.exit(0)
             }
           }
         }
       }
       
       if (i === imageCount) {
         // No image found with <image-name>, ps1 script should ask permission to create 
         // custom Windows WinRM image with this name
         process.exit(2)
       }
       
    } else {
       logErr(error);
       prg.end();
       process.exit(1)
    }
})



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
