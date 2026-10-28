namespace DotnetCockpit;

internal sealed record CommandLineOptions(int Port = 0, bool OpenBrowser = true)
{
    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--no-browser":
                    options = options with { OpenBrowser = false };
                    break;
                case "--port" when i + 1 < args.Length && int.TryParse(args[++i], out var port) && port is >= 0 and <= 65535:
                    options = options with { Port = port };
                    break;
                default:
                    throw new ArgumentException("Use --port <0-65535>, --no-browser, or --help.");
            }
        }
        return options;
    }
}
