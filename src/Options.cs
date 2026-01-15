using CommandLine;

namespace SambaFileCopy;

public class Options
{
    [Option('s', "source", Required = true, HelpText = "Source path")]
    public string? Source { get; set; }

    [Option('r', "server", Required = true, HelpText = "Destination samba server")]
    public string? Server { get; set; }

    [Option('t', "tree", Required = true, HelpText = "Destination samba tree")]
    public string? Tree { get; set; }

    [Option('d', "destination", Required = true, HelpText = "Destination path")]
    public string? Destination { get; set; }

    [Option('n', "domain", Required = true, HelpText = "Domain")]
    public string? Domain { get; set; }

    [Option('u', "username", Required = false, HelpText = "Username")]
    public string? Username { get; set; }

    [Option('p', "password", Required = false, HelpText = "Password")]
    public string? Password { get; set; }

    [Option('k', "skip-existing", Required = false, HelpText = "Skip the files that already exist on the server")]
    public bool SkipExistingFiles { get; set; }
}
