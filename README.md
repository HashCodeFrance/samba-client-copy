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

## Usage

To copy files inside `/tmp/files` to `\\SERVER\SHARE\TempOutput\2023` :

```sh
SambaFileCopy --source /tmp/files/ --server 'SERVER' --tree 'SHARE' --destination 'TempOutput/2023' --username 'smbuser' --password 'smbpwd' --domain 'MYDOMAIN'
```

## Notes

When using pathes for the destination (samba) share, it only accepts blackslashes, not slashes.

***Use this program at your own risk.***
