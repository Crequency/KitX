namespace KitX.ToolKits.Publisher;

internal static class Configs
{
    internal static bool SkipGenerate = false;

    internal static void ProcessParameters(string[] args)
    {
        if (args.Any((x) => x.ToLower().Equals("--skip-generate")))
            SkipGenerate = true;
    }
}
