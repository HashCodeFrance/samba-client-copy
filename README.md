# samba-client-copy

This simple program allows to copy a folder or a file to a samba share.

It uses .NET 7.0 and the [SMBLibrary](https://github.com/TalAloni/SMBLibrary) portable library.

Tested under Ubuntu 20.04

## Build

Execute :

```sh
cd src
dotnet restore
dotnet build --configuration Release --runtime ubuntu.20.04-x64 --no-self-contained -o /azp/bin -p:PublishSingleFile=true 
```

You could also make a symlink like this

```sh
ln -s /azp/bin/SambaFileCopy /bin/smbcp
```

## Run

### Usage



### Example

To copy files inside `/tmp/files` to `\\SERVER\SHARE\TempOutput\2023` :

```sh
SambaFileCopy --source /tmp/files/ --server 'SERVER' --tree 'SHARE' --destination 'TempOutput/2023' --username 'smbuser' --password 'smbpwd' --domain 'MYDOMAIN'
```

## Notes

### Not enough credits Exception

Sometimes this exception can be thrown :

    Unhandled exception. System.Exception: Not enough credits
    at SMBLibrary.Client.SMB2Client.TrySendCommand(SMB2Command request, Boolean encryptData)

There is a credit mechanism with the Samba protocol, it whill throttle the number of connections and data transferred by a client.

When transferring a lot of files, the only way I found to overcome this problem is to add a sleep between the write operations.

### Path format

When using pathes for the destination (samba) share, it only accepts blackslashes, not slashes. Thus inside the code, slashes are replaced with backslashes.

***Use this program at your own risk.***
