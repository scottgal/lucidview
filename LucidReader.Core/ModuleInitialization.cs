using System.Runtime.CompilerServices;
using System.Text;

namespace LucidReader.Core;

/// <summary>
/// Registers System.Text.Encoding.CodePages' legacy code pages (windows-1252
/// among them) once, unconditionally, the moment this assembly's module
/// loads - not as a side effect of constructing any particular type.
///
/// This used to be a static constructor on FeedFetcher. That worked for
/// FeedFetcher itself, but ArticleFetcher.GetEncoding depends on the exact
/// same registration and ArticleFetcher's only reference to FeedFetcher was
/// FeedFetcher.UserAgentString - a const the compiler inlines at every call
/// site. Reading an inlined const does not touch the declaring type at
/// runtime, so it does NOT trigger FeedFetcher's static constructor: the
/// registration only ever ran because a real composition happened to
/// construct a FeedFetcher before the app ever called ArticleFetcher. A
/// composition that downloaded articles without ever refreshing a feed first
/// (or a test exercising ArticleFetcher in isolation) would silently get
/// mojibaked non-UTF-8 article pages instead.
///
/// [ModuleInitializer] runs exactly once, before any other code in this
/// assembly, regardless of which type is used first - the one place this
/// registration cannot be skipped by accident.
/// </summary>
internal static class ModuleInitialization
{
    // CA2255 assumes [ModuleInitializer] belongs only in application entry
    // assemblies, not libraries. That is exactly backwards for this case: the
    // whole point is to guarantee the registration fires no matter which
    // application (or test project) references this library and no matter
    // which type it touches first - see the class remarks.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Initialize() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#pragma warning restore CA2255
}
