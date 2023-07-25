using SMBLibrary.Client;

public class SambaConnection
{
    public SambaConnection(SMB2Client client, ISMBFileStore fileStore, string server, string tree, string domain)
    {
        Client = client;
        FileStore = fileStore;
        Server = server;
        Tree = tree;
        Domain = domain;
    }

    public ISMBFileStore FileStore { get; }
    public SMB2Client Client { get; }
    public string Server { get; }
    public string Tree { get; }
    public string Domain { get; }
}
