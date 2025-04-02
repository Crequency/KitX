using System.Diagnostics;
using Common.BasicHelper.Core.Shell;
using Common.BasicHelper.Utils.Extensions;
using Spectre.Console;

var utilsToLaunch = AnsiConsole.Prompt(
    new MultiSelectionPrompt<string>()
        .Title("Which [green]i18n utils[/] are to be launched ?")
        .NotRequired()
        .PageSize(10)
        .MoreChoicesText("[grey](Move up and down to reveal more utils)[/]")
        .InstructionsText("[grey](Press [blue]<space>[/] to toggle a i18n util, [green]<enter>[/] to accept)[/]")
        .AddChoices(
            new[] {
                "KitX Dashboard (XamlMLE)"
            }
        )
    );

var baseDir = PathHelper.Instance.BaseSlnDir;

foreach (var util in utilsToLaunch)
{
    AnsiConsole.MarkupLine($"@ Launching {util}");

    switch (util)
    {
        case "KitX Dashboard (XamlMLE)":
            var xamlMleDir = $"{baseDir}/KitX SDK/Reference/XamlMultiLanguageEditor".GetFullPath();
            var scriptPath = $"{xamlMleDir}/run.ps1".GetFullPath();

            var args = new StringBuilder()
                .Append($" -WorkingDirectory \"{xamlMleDir}\"")
                .Append($" -Command \"& '{scriptPath}'\"")
                .ToString()
                ;

            Console.WriteLine(args);

            var result = "pwsh".ExecuteAsCommand(args: args, findInPath: true);

            Console.WriteLine(result);

            break;
    }
}
