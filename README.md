Windows-Azure-CouchDB
=====================
The CouchDB Installer for Windows Azure is a tool that simplifies the creation, configuration, and deployment of CouchDB clusters hosted in Windows Azure virtual machines (for IaaS) or Windows Azure worker roles (for PaaS).


As part of this install the following Microsoft or third party software will be installed on your local machine and then deployed to Windows Azure. 

- CouchDB which is owned by The Apache Software Foundation., will be downloaded from http://couchdb.apache.org/ .The license agreement to Apache License, Version 2.0 may be included with the software.  You are responsible for and must separately locate, read and accept these license terms.

- Microsoft Windows Azure SDK for .Net and NodeJS owned by Microsoft , will be downloaded from http://www.microsoft.com/windowsazure/sdk/.

## Prerequisites for installer

1. Windows machine: Windows7(64 bit) or Win2008R2(64 bit)

2. IIS including the web roles ASP.Net, Tracing, logging & CGI Services needs to be enabled.((optional for the IaaS more required for the PaaS mode)
    - http://learn.iis.net/page.aspx/29/installing-iis-7-and-above-on-windows-server-2008-or-windows-server-2008-r2/ 
  
3. .Net Framework 4.0 Full version
   
4. Note if you start with a clean machine:  To download public setting file the enhanced security configuration of IE needs to be disabled. Go to Server Manager -> configure IE ESC -> disable for Administrators.

## Copy the binaries
1. Download and extract on your local computer the latest version for PaaS CouchDBInstWRMMDDYYYY.zip or for IaaS CouchDBInstVMMMDDYYYY.zip (for example CouchDBInstWR06072012.zip) from https://github.com/MSOpenTech/Windows-Azure-CouchDB/downloads

2. Copy the CouchDBInstWR.msi to your local web root. For example c:\inetpub\wwwroot

3. Launch a command prompt (cmd.exe) as an administrator and cd to the local folder selected above

## Run the installer:

To run the installer in the IaaS mode hosted in a Windoes Azure VM role you would need to use the CouchDBInstVM.xml that comes with this zip
  - Inst4WA.exe -XmlConfigPath <yourpath>/CouchDBInstVM.xml -DomainName <youruniquename>  -Subscription <yoursubscription>

To run the installer in the PaaS mode hosted in Windoes Azure Worker role you would need to use the CouchDBInstVM.xml that comes with this zip
  - Inst4WA.exe -XmlConfigPath <yourpath>/CouchDBInstWR.xml -DomainName <youruniquename>  -Subscription <yoursubscription>


Note: While the installer is running, it will open a browser to download your publish settings file. Save this file to either your downloads folder or the CouchDB Installer folder. You must save the file in one of those two locations for the installer to see it and import the settings.
Do not write your publish settings over an existing file. The installer will be watching these two locations for a new file to be created.