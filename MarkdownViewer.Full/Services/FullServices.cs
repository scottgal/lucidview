using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StyloExtract.Abstractions;
using StyloExtract.Core;
using StyloExtract.Playwright;

namespace MarkdownViewer.Services;

internal static class FullServices
{
    private static readonly Lazy<IServiceProvider> _lazy = new(Build);

    public static IServiceProvider Provider => _lazy.Value;
    public static T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

    private static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

        services.AddStyloExtract(o =>
        {
            o.StorePath = AppPaths.TemplateStorePath;
            o.DefaultProfile = ExtractionProfile.RagFull;
        });

        // Task 5: register the Playwright rendered-DOM fetcher.
        services.AddSingleton<IRenderedHtmlFetcher>(_ => new PlaywrightHtmlFetcher());

        services.AddSingleton<IHtmlToMarkdownService, HtmlToMarkdownServiceFull>();

        return services.BuildServiceProvider();
    }
}
