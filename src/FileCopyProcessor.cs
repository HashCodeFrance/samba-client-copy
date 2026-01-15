using System.Diagnostics;
using SMBLibrary;
using SMBLibrary.Client;

namespace SambaFileCopy;

public class FileCopyProcessor
{
    private const int MaxWriteFileRetries = 1;
    private const int MaxCopyFileRetries = 3;

    /// <summary>
    /// Duration in ms waited between two file copy operations.
    /// </summary>
    private const int WaitValueBetweenCopy = 200;

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
                try
                {
                    fileStore?.Disconnect();
                    client?.Logoff();
                    client?.Disconnect();
                }
                catch { }
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
            Thread.Sleep(WaitValueBetweenCopy);
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
        var status = _sambaConnection.FileStore.SambaCreateDirectory(directory, out var fileHandle);

        if (status == NTStatus.STATUS_OBJECT_NAME_COLLISION)
        {
            Console.WriteLine($"Creating directory {directory}: already exists");
            return;
        }

        if (status != NTStatus.STATUS_SUCCESS)
        {
            Console.Error.WriteLine($"Creating directory {directory}: Failure: Status={status}");
            return;
        }

        Console.WriteLine($"Creating directory {directory}: Success.");

        var closeStatus = _sambaConnection.FileStore.CloseFile(fileHandle);

        if (closeStatus != NTStatus.STATUS_SUCCESS)
        {
            Console.Error.WriteLine($"Could not close directory {directory}: Status={closeStatus}");
            return;
        }
    }

    private void CopyFile(string fileName, string destPath)
    {
        var destFileName = Path.GetFileName(fileName);
        var dest = IsCurrentDirectoryPath(destPath) == false ? @$"{destPath}\{destFileName}" : destFileName;

        Console.WriteLine($"Creating file {dest}");

        for (int retry = 1; retry <= MaxCopyFileRetries; retry++)
        {
            if (CopyFileInternal(fileName, dest) == true)
            {
                break;
            }
            if (_sambaConnection.Reconnect() == false)
            {
                break;
            }
        }

    }

    private bool CopyFileInternal(string fileName, string dest)
    {
        NTStatus status;
        object? fileHandle;

        try
        {
            status = _sambaConnection.FileStore.SambaCreateFile(dest, out fileHandle, _sambaConnection.SkipExistingFiles);

            if (_sambaConnection.SkipExistingFiles == true && status == NTStatus.STATUS_OBJECT_NAME_COLLISION)
            {
                Console.WriteLine("  Skipped (already exists)");
                return true;
            }

            if (status != NTStatus.STATUS_SUCCESS)
            {
                Console.Error.WriteLine($"  Could not create file {dest}: Status={status}");
                return false;
            }
        }
        catch (Exception ex) when (ex.Message == "Not enough credits")
        {
            return false;
        }

        if (fileHandle == null)
        {
            Console.Error.WriteLine($"  Could not create file {dest}: file handle is null.");
            return false;
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var (writeOffset, success) = WriteFileInternal(fileName, dest, fileHandle);

            if (success)
            {
                sw.Stop();
                Console.WriteLine($"  File {dest} successfully transferred ({writeOffset / 1024}Kb - {(int)sw.Elapsed.TotalMilliseconds}ms)");
            }
            else
            {
                Console.Error.WriteLine($"  Could not write to file {dest} ");
                return false;
            }
        }
        catch (Exception ex) when (ex.Message == "Not enough credits")
        {
            return false;
        }
        finally
        {
            try
            {
                var closeStatus = _sambaConnection.FileStore.CloseFile(fileHandle);

                if (closeStatus != NTStatus.STATUS_SUCCESS)
                {
                    Console.Error.WriteLine($"  Failed to close file {dest}: Status={closeStatus}");
                }
            }
            catch { }
        }

        return true;
    }

    private (int, bool) WriteFileInternal(string fileName, string dest, object? fileHandle)
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
            success = true;
        }

        fs.Close();
        return (writeOffset, success);
    }

    private (int, bool) TryWriteFile(string dest, object? fileHandle, int writeOffset, byte[] buffer)
    {
        int numberOfBytesWritten = 0;
        bool success = false;
        NTStatus? status = null;

        for (int retry = 1; retry <= MaxWriteFileRetries; retry++)
        {
            try
            {
                // trying to fix exception:
                // Unhandled exception. System.Exception: Not enough credits
                // at SMBLibrary.Client.SMB2Client.TrySendCommand(SMB2Command request, Boolean encryptData)
                status = _sambaConnection.FileStore.WriteFile(out numberOfBytesWritten, fileHandle, writeOffset, buffer);

                if (status == NTStatus.STATUS_SUCCESS)
                {
                    success = true;
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception caught: " + ex.Message);
            }

            Console.Error.WriteLine($"Failed to write to file {dest}: Status={status} (retry {retry} / {MaxWriteFileRetries})");
        }

        return (numberOfBytesWritten, success);
    }

    private static bool IsCurrentDirectoryPath(string path) => string.IsNullOrEmpty(path) || path == "." || path == "./" || path == @".\";
}
