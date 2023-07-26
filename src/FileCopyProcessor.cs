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

        if (_sambaConnection.Client == null || _sambaConnection.FileStore == null)
        {
            throw new InvalidOperationException("Samba initialization failure.");
        }
    }

    public void CopyFromFolderToFolder(string sourcePath, string destPath)
    {
        SMB2Client client = _sambaConnection.Client!;
        ISMBFileStore fileStore = _sambaConnection.FileStore!;
        var server = _sambaConnection.Server;
        var tree = _sambaConnection.Tree;

        bool isDirectory = Directory.Exists(sourcePath);
        Console.WriteLine($"DEBUG - SourceIsDirectory:{isDirectory} MaxReadSize:{client.MaxReadSize}, MaxWriteSize:{client.MaxWriteSize}, MaxTransactSize:{client.MaxTransactSize},");
        Console.WriteLine($@"Copying files from {sourcePath} to \\{server}\{tree}\{destPath}");

        EnsureDestPathExists(destPath);

        // single file mode
        if (isDirectory == false)
        {
            try
            {
                var fileName = sourcePath;
                CopyFile(fileName, destPath);
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
                CopyFilesRecurse(sourcePath, destPath);
            }
            finally
            {
                fileStore?.Disconnect();
                client?.Logoff();
                client?.Disconnect();
            }
        }
    }

    private void EnsureDestPathExists(string destPath)
    {
        var parts = destPath.Split(new[] { "/", @"\" }, StringSplitOptions.RemoveEmptyEntries);
        var currentDirectory = string.Empty;

        foreach (var part in parts)
        {
            // we must use backslask for samba pathes
            currentDirectory += @$"{part}\";

            Debug.WriteLine($"Creating {currentDirectory} if it does not exist.");
            var status = _sambaConnection.FileStore.SambaCreateDirectory(currentDirectory.TrimEnd('\\'), out var fileHandle);

            if (status == NTStatus.STATUS_SUCCESS)
            {
                Console.WriteLine($"Destination directory {currentDirectory} created.");
                var closeStatus = _sambaConnection.FileStore.CloseFile(fileHandle);

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

    private void CopyFilesRecurse(string sourcePath, string destPath)
    {
        var directory = new DirectoryInfo(sourcePath);
        var newFiles = directory.EnumerateFiles("*", SearchOption.TopDirectoryOnly);

        foreach (var newFile in newFiles)
        {
            CopyFile(newFile.FullName, destPath);
            Thread.Sleep(1000);
        }

        var newDirectories = directory.GetDirectories("*", SearchOption.TopDirectoryOnly);

        foreach (var newDirectory in newDirectories)
        {
            var newDestinationDirectory = @$"{destPath}/{newDirectory.Name}";
            CreateDirectory(newDestinationDirectory);
            CopyFilesRecurse(newDirectory.FullName, newDestinationDirectory);
        }
    }

    private void CreateDirectory(string directory)
    {
        Console.WriteLine($"Creating directory {directory}");

        var status = _sambaConnection.FileStore.SambaCreateDirectory(directory, out var fileHandle);

        if (status != NTStatus.STATUS_SUCCESS)
        {
            Console.Error.WriteLine($"Could not create directory {directory}: Status={status}");
            return;
        }

        Console.WriteLine($"Directory {directory} created.");

        var closeStatus = _sambaConnection.FileStore.CloseFile(fileHandle);

        if (closeStatus != NTStatus.STATUS_SUCCESS)
        {
            Console.Error.WriteLine($"Could not close directory {directory}: Status={closeStatus}");
            return;
        }
    }

    private static bool IsCurrentDirectoryPath(string path) => string.IsNullOrEmpty(path) || path == "." || path == "./" || path == @".\";

    private void CopyFile(string fileName, string destPath)
    {
        var destFileName = Path.GetFileName(fileName);
        var dest = IsCurrentDirectoryPath(destPath) == false ? @$"{destPath}\{destFileName}" : destFileName;

        _sambaConnection.Reconnect();


        Console.WriteLine($"Creating file {dest}");

        NTStatus status;
        object? fileHandle = null;

        //try
        //{
            status = _sambaConnection.FileStore.SambaCreateFile(dest, out fileHandle);

            if (status != NTStatus.STATUS_SUCCESS)
            {
                Console.Error.WriteLine($"Could not create file {dest}: Status={status}");
                return;
            }
        //}
        //catch (Exception ex) when (ex.Message == "Not enough credits")
        //{
        //    _sambaConnection.Reconnect();
        //    status = _sambaConnection.FileStore.SambaCreateFile(dest, out fileHandle);

        //    if (status != NTStatus.STATUS_SUCCESS)
        //    {
        //        Console.Error.WriteLine($"Could not create file {dest}: Status={status} (second try)");
        //        return;
        //    }
        //}

        if (fileHandle == null)
        {
            Console.Error.WriteLine($"Could not create file {dest}: file handle is null.");
            return;
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var (writeOffset, success) = CopyFile(fileName, dest, fileHandle);

            if (success)
            {
                sw.Stop();
                Console.WriteLine($"File {dest} successfully transferred ({writeOffset / 1024} Kb - {sw.Elapsed.TotalMilliseconds} ms.)");
            }
            else
            {
                Console.Error.WriteLine($"Could not write to file {dest} ");
            }
        }
        finally
        {
            try
            {
                var closeStatus = _sambaConnection.FileStore.CloseFile(fileHandle);

                if (closeStatus != NTStatus.STATUS_SUCCESS)
                {
                    Console.Error.WriteLine($"Failed to close file {dest}: Status={closeStatus}");
                }
            }
            catch { }
        }
    }

    private (int, bool) CopyFile(string fileName, string dest, object? fileHandle)
    {
        int writeOffset = 0;
        var success = false;
        using var fs = File.OpenRead(fileName);
        Debug.WriteLine($"File {fileName} opened for reading.");

        while (fs.Position < fs.Length)
        {
            var buffer = new byte[(int)_sambaConnection.Client.MaxWriteSize];
            int bytesRead = fs.Read(buffer, 0, buffer.Length);

            if (bytesRead < (int)_sambaConnection.Client.MaxWriteSize)
            {
                Array.Resize(ref buffer, bytesRead);
            }

            (int numberOfBytesWritten, var writeSuccess) = TryWriteFile(dest, fileHandle, writeOffset, buffer);

            if (writeSuccess == false)
            {
                success = false;
                break;
            }

            Debug.WriteLine($"Written {numberOfBytesWritten} bytes to file {dest}");
            writeOffset += bytesRead;
        }

        return (writeOffset, success);
    }

    private (int, bool) TryWriteFile(string dest, object? fileHandle, int writeOffset, byte[] buffer)
    {
        int numberOfBytesWritten = 0;
        bool success = false;
        NTStatus? status = null;

        for (int retry = 1; retry <= MaxRetries; retry++)
        {
            try
            {
                // trying to fix exception:
                // Unhandled exception. System.Exception: Not enough credits
                // at SMBLibrary.Client.SMB2Client.TrySendCommand(SMB2Command request, Boolean encryptData)
                status = _sambaConnection.FileStore.WriteFile(out numberOfBytesWritten, fileHandle, writeOffset, buffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception caught: " + ex.Message);
            }

            if (status == NTStatus.STATUS_SUCCESS)
            {
                success = true;
                break;
            }

            Console.Error.WriteLine($"Failed to write to file {dest}: Status={status} (retry {retry} / {MaxRetries})");
            Thread.Sleep(1000);
        }

        return (numberOfBytesWritten, success);
    }
}
