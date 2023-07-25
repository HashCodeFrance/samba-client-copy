using CommandLine;
using SMBLibrary;
using SMBLibrary.Client;

Parser.Default.ParseArguments<Options>(args).WithParsed<Options>(o =>
{
    var sourcePath = o.Source!;

    if (Directory.Exists(sourcePath) == false && File.Exists(sourcePath) == false)
    {
        Console.Error.WriteLine("Path {source} does not exist.");
        return;
    }

    var sambaFileStore = GetSambaFileStore(o.Server!, o.Tree!, o.Domain!, o.Username, o.Password);

    if (sambaFileStore == null)
    {
        return;
    }

    var processor = new FileCopyProcessor(sambaFileStore);
    processor.CopyFromFolderToFolder(o.Source!, o.Destination!);
});


SambaFileStore? GetSambaFileStore(string server, string tree, string domain, string? username, string? password)
{
    Console.WriteLine($"Connecting to Server={server} Tree={tree}...");

    var client = new SMB2Client();

    var isConnected = client.Connect(server, SMBTransportType.DirectTCPTransport);
    if (!isConnected)
    {
        Console.Error.WriteLine($"Could not connect to {server}");
        return null;
    }

    Console.WriteLine($"Logging to Domain={domain} with Username={username}");
    var status = client.Login(domain, username, password);

    if (status != NTStatus.STATUS_SUCCESS)
    {
        Console.Error.WriteLine($"Could not login to {server} with credentials: Status={status}");
        client.Disconnect();
        return null;
    }

    var fileStore = client.TreeConnect(tree, out status);

    if (status != NTStatus.STATUS_SUCCESS)
    {
        Console.Error.WriteLine($"Could not connect to Server={server} Tree={tree}: Status={status}");
        client.Logoff();
        client.Disconnect();
        return null;
    }

    return new SambaFileStore(client, fileStore, server, tree, domain);
}
