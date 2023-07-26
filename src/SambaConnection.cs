using SMBLibrary;
using SMBLibrary.Client;

public class SambaConnection
{
    private readonly string? _username;
    private readonly string? _password;

    public SambaConnection(SMB2Client client, ISMBFileStore fileStore, string server, string tree, string domain, string? username, string? password)
    {
        Client = client;
        FileStore = fileStore;
        Server = server;
        Tree = tree;
        Domain = domain;
        _username = username;
        _password = password;
    }

    public ISMBFileStore FileStore { get; private set; }
    public SMB2Client Client { get; }
    public string Server { get; }
    public string Tree { get; }
    public string Domain { get; }

    public bool Reconnect()
    {
        Console.WriteLine("Reconnecting...");

        Client.Logoff();
        Client.Disconnect();

        Console.WriteLine($"Connecting to Server={Server} Tree={Tree}...");
        var isConnected = Client.Connect(Server, SMBTransportType.DirectTCPTransport);

        if (!isConnected)
        {
            Console.Error.WriteLine($"Could not connect to {Server}");
            return false;
        }

        Console.WriteLine($"Logging to Domain={Domain} with Username={_username}");
        var status = Client.Login(Domain, _username, _password);

        if (status != NTStatus.STATUS_SUCCESS)
        {
            Console.Error.WriteLine($"Could not login to {Server} with credentials: Status={status}");
            Client.Disconnect();
            return false;
        }

        var fileStore = Client.TreeConnect(Tree, out status);

        if (status != NTStatus.STATUS_SUCCESS)
        {
            Console.Error.WriteLine($"Could not connect to Server={Server} Tree={Tree}: Status={status}");
            Client.Logoff();
            Client.Disconnect();
            return false;
        }

        FileStore = fileStore;

        return true;
    }

    public static SambaConnection? Create(string server, string tree, string domain, string? username, string? password)
    {
        var client = new SMB2Client();

        Console.WriteLine($"Connecting to Server={server} Tree={tree}...");
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

        return new SambaConnection(client, fileStore, server, tree, domain, username, password);
    }
}
