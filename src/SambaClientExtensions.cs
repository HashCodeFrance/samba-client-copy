using SMBLibrary;
using SMBLibrary.Client;

public static class SambaClientExtensions
{
    public static NTStatus SambaCreateFile(this SMB2Client client, ISMBFileStore fileStore, string dest, out object? fileHandle)
    {
        dest = dest.Replace("/", @"\");

        var status = fileStore.CreateFile(
            out fileHandle,
            out var fileStatus,
            fileStore is SMB1FileStore ? @$"\\{dest}" : dest,
            AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
            SMBLibrary.FileAttributes.Normal,
            ShareAccess.None,
            CreateDisposition.FILE_OVERWRITE_IF,
            CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
            null);
        return status;
    }

    public static NTStatus SambaCreateDirectory(this SMB2Client client, ISMBFileStore fileStore, string directory, out object? fileHandle)
    {
        directory = directory.Replace("/", @"\");

        if (string.IsNullOrEmpty(directory) || directory == "." || directory == @".\")
        {
            fileHandle = null;
            return NTStatus.STATUS_SUCCESS;
        }

        var status = fileStore.CreateFile(
            out fileHandle,
            out var fileStatus,
            fileStore is SMB1FileStore ? @$"\\{directory}" : directory,
             AccessMask.GENERIC_WRITE | AccessMask.GENERIC_READ | AccessMask.GENERIC_EXECUTE,
            SMBLibrary.FileAttributes.Directory,
            ShareAccess.None,
            CreateDisposition.FILE_CREATE,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);
        return status;
    }
}
