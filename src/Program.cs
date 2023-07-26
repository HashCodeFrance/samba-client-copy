using CommandLine;

Parser.Default.ParseArguments<Options>(args).WithParsed(o =>
{
    var sourcePath = o.Source!;

    if (Directory.Exists(sourcePath) == false && File.Exists(sourcePath) == false)
    {
        Console.Error.WriteLine("Path {source} does not exist.");
        return;
    }

    var sambaConnection = SambaConnection.Create(o.Server!, o.Tree!, o.Domain!, o.Username, o.Password);

    if (sambaConnection == null )
    {
        // error was logged in console
        return;
    }

    var processor = new FileCopyProcessor(sambaConnection);
    processor.CopyFromFolderToFolder(o.Source!, o.Destination!);
});
