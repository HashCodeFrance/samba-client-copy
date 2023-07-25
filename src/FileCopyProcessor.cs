using System.Diagnostics;
using SMBLibrary;
using SMBLibrary.Client;

public class FileCopyProcessor
{
    private const int MaxRetries = 3;
    private readonly SambaConnection _sambaConnection;

    public FileCopyProcessor(SambaConnection sambaConnection)
    {
        _sambaConnection = sambaConnection;
    }

    public void CopyFromFolderToFolder(string sourcePath, string destPath)
    {
        bool isDirectory = Directory.Exists(sourcePath);
        var client = _sambaConnection.Client;
        var fileStore = _sambaConnection.FileStore;
        var server = _sambaConnection.Server;
        var tree = _sambaConnection.Tree;

        Console.WriteLine($"DEBUG - SourceIsDirectory:{isDirectory} MaxReadSize:{client.MaxReadSize}, MaxWriteSize:{client.MaxWriteSize}, MaxTransactSize:{client.MaxTransactSize},");
        Console.WriteLine($@"Copying files from {sourcePath} to \\{server}\{tree}\{destPath}");

        EnsureDestPathExists(client, fileStore, destPath);

        // single file mode
        if (isDirectory == false)
        {
            try
            {
                var fileName = sourcePath;
                CopyFile(client, fileStore, fileName, destPath);
            }
            finally
            {
                fileStore?.Disconnect();
                client?.Logoff();
                client?.Disconnect();
            }
        }
        else
        {
            try
            {
                CopyFilesRecurse(client, fileStore, sourcePath, destPath);
            }
            finally
            {
                fileStore?.Disconnect();
                client?.Logoff();
                client?.Disconnect();
            }
        }
    }

    private void EnsureDestPathExists(SMB2Client client, ISMBFileStore fileStore, string destPath)
    {
        var parts = destPath.Split(new[] { "/", @"\" }, StringSplitOptions.RemoveEmptyEntries);
        var currentDirectory = string.Empty;

        foreach (var part in parts)
        {
            // we must use backslask for samba pathes
            currentDirectory += @$"{part}\";

            Debug.WriteLine($"Creating {currentDirectory} if it does not exist.");
            var status = fileStore.SambaCreateDirectory(currentDirectory.TrimEnd('\\'), out var fileHandle);

            if (status == NTStatus.STATUS_SUCCESS)
            {
                Console.WriteLine($"Destination directory {currentDirectory} created.");
                var closeStatus = fileStore.CloseFile(fileHandle);

                if (closeStatus != NTStatus.STATUS_SUCCESS)
                {
                    Console.Error.WriteLine($"Could not close directory {part}: Status={closeStatus}");
                    return;
                }
            }
            else
            {
                Debug.WriteLine($"Destination directory {currentDirectory} was not created: Status={status}.");
            }
        }
    }

    private void CopyFilesRecurse(SMB2Client client, ISMBFileStore fileStore, string sourcePath, string destPath)
    {
        var directory = new DirectoryInfo(sourcePath);
        var newFiles = directory.EnumerateFiles("*", SearchOption.TopDirectoryOnly);

        foreach (var newFile in newFiles)
        {
            CopyFile(client, fileStore, newFile.FullName, destPath);
        }

        var newDirectories = directory.GetDirectories("*", SearchOption.TopDirectoryOnly);

        foreach (var newDirectory in newDirectories)
        {
            var newDestinationDirectory = @$"{destPath}/{newDirectory.Name}";
            CreateDirectory(client, fileStore, newDestinationDirectory);
            CopyFilesRecurse(client, fileStore, newDirectory.FullName, newDestinationDirectory);
        }
    }

    private void CreateDirectory(SMB2Client client, ISMBFileStore fileStore, string directory)
    {
        Console.WriteLine($"Creating directory {directory}");

        var status = fileStore.SambaCreateDirectory(directory, out var fileHandle);

        if (status != NTStatus.STATUS_SUCCESS)
        {
            Console.Error.WriteLine($"Could not create directory {directory}: Status={status}");
            return;
        }

        Console.WriteLine($"Directory {directory} created.");

        if (fileStore == null)
        {
            return;
        }

        var closeStatus = fileStore.CloseFile(fileHandle);

        if (closeStatus != NTStatus.STATUS_SUCCESS)
        {
            Console.Error.WriteLine($"Could not close directory {directory}: Status={closeStatus}");
            return;
        }
    }

    private static bool IsCurrentDirectoryPath(string path) => string.IsNullOrEmpty(path) || path == "." || path == "./" || path == @".\";

    private void CopyFile(SMB2Client client, ISMBFileStore fileStore, string fileName, string destPath)
    {
        var destFileName = Path.GetFileName(fileName);
        var dest = IsCurrentDirectoryPath(destPath) == false ? @$"{destPath}\{destFileName}" : destFileName;

        Console.WriteLine($"Creating file {dest}");

        var status = fileStore.SambaCreateFile(dest, out var fileHandle);

        if (status != NTStatus.STATUS_SUCCESS)
        {
            Console.Error.WriteLine($"Could not create file {dest}: Status={status}");
            return;
        }

        if (fileHandle == null)
        {
            return;
        }

        int writeOffset = 0;
        var sw = Stopwatch.StartNew();

        try
        {
            bool success = false;
            using var fs = File.OpenRead(fileName);
            Debug.WriteLine($"File {fileName} opened for reading.");

            while (fs.Position < fs.Length)
            {
                byte[] buffer = new byte[(int)client.MaxWriteSize];
                int bytesRead = fs.Read(buffer, 0, buffer.Length);

                if (bytesRead < (int)client.MaxWriteSize)
                {
                    Array.Resize<byte>(ref buffer, bytesRead);
                }

                int numberOfBytesWritten = 0;

                for (int retry = 0; retry < MaxRetries; retry++)
                {
                    status = fileStore.WriteFile(out numberOfBytesWritten, fileHandle, writeOffset, buffer);

                    if (status == NTStatus.STATUS_SUCCESS)
                    {
                        success = true;
                        break;
                    }

                    Console.Error.WriteLine($"Failed to write to file {dest}: Status={status} (retry {retry} / {MaxRetries})");
                    System.Threading.Thread.Sleep(1000);
                }

                Debug.WriteLine($"Written {numberOfBytesWritten} bytes to file {dest}");
                writeOffset += bytesRead;
            }

            if (success)
            {
                sw.Stop();
                Console.WriteLine($"File {dest} successfully transferred ({writeOffset / 1024} Kb - {sw.Elapsed.TotalMilliseconds} ms.)");
            }
        }
        finally
        {
            try
            {
                var closeStatus = fileStore.CloseFile(fileHandle);

                if (closeStatus != NTStatus.STATUS_SUCCESS)
                {
                    Console.Error.WriteLine($"Failed to close file {dest}: Status={closeStatus}");
                }
            }
            catch { }
        }
    }
}
