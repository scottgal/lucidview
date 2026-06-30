using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MarkdownViewer.Lab.Models;

namespace MarkdownViewer.Lab.Services.Storage;

public enum WorkspaceState { Healthy, Degraded, ReadOnly }

public sealed record IntegritySweepReport(
    int Sampled,
    int Drifted,
    double DriftRatio,
    IReadOnlyList<string> Issues);

public sealed class Workspace : IAsyncDisposable
{
    public string Name { get; }
    public WorkspaceState State { get; internal set; }
    public MetadataStore Metadata { get; }
    public EvidenceStore Evidence { get; }
    public VectorStore Vectors { get; }
    public LexicalIndex Lexical { get; }
    public WorkspaceManifest Manifest { get; internal set; }
    public IntegritySweepReport LastSweep { get; internal set; }

    internal Workspace(
        string name,
        MetadataStore metadata,
        EvidenceStore evidence,
        VectorStore vectors,
        LexicalIndex lexical,
        WorkspaceManifest manifest,
        IntegritySweepReport sweep)
    {
        Name     = name;
        State    = WorkspaceState.Healthy;
        Metadata = metadata;
        Evidence = evidence;
        Vectors  = vectors;
        Lexical  = lexical;
        Manifest = manifest;
        LastSweep = sweep;
    }

    public async ValueTask DisposeAsync()
    {
        await Evidence.DisposeAsync();
        await Vectors.DisposeAsync();
        await Lexical.DisposeAsync();
        await Metadata.DisposeAsync();
    }
}

public sealed class WorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _root;

    public WorkspaceStore(string root) => _root = root;

    public Task<IReadOnlyList<string>> EnumerateAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_root))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var names = Directory.EnumerateDirectories(_root)
                             .Select(Path.GetFileName)
                             .Where(n => !string.IsNullOrEmpty(n))
                             .Select(n => n!)
                             .ToArray();
        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    public async Task<Workspace> CreateAsync(string name, CancellationToken ct)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);

        var manifest = new WorkspaceManifest(
            name,
            DateTimeOffset.UtcNow,
            Array.Empty<AttachedFolder>(),
            Array.Empty<LibraryEntry>(),
            new PersonalCorpusConfig(Enabled: true));

        await File.WriteAllTextAsync(
            Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOpts),
            ct);

        return await OpenAsync(name, ct);
    }

    public async Task<Workspace> OpenAsync(string name, CancellationToken ct)
    {
        var dir = Path.Combine(_root, name);
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Workspace '{name}' not found at {dir}");

        var manifestPath = Path.Combine(dir, "manifest.json");
        var manifest = JsonSerializer.Deserialize<WorkspaceManifest>(
            await File.ReadAllTextAsync(manifestPath, ct),
            JsonOpts)
            ?? throw new InvalidDataException($"manifest.json at '{manifestPath}' deserialised to null.");

        var metadata = await MetadataStore.OpenAsync(Path.Combine(dir, "index.db"), ct);
        var evidence = await EvidenceStore.OpenAsync(
            Path.Combine(dir, "evidence.db"),
            new EvidenceStoreOptions(),
            ct);
        var vectors  = await VectorStore.OpenAsync(
            Path.Combine(dir, "vectors.duckdb"),
            dimensions: 384,
            ct);
        var lexical  = await LexicalIndex.OpenAsync(
            Path.Combine(dir, "lucene"),
            ct);

        var sweep = await IntegritySweepAsync(metadata, evidence, vectors, lexical, ct);
        return new Workspace(name, metadata, evidence, vectors, lexical, manifest, sweep);
    }

    private static Task<IntegritySweepReport> IntegritySweepAsync(
        MetadataStore metadata,
        EvidenceStore evidence,
        VectorStore vectors,
        LexicalIndex lexical,
        CancellationToken ct)
    {
        // Stub: empty workspace short-circuit — zero drift.
        // Real cross-substrate drift detection lives in Task 18 (InfrastructureDashboard).
        return Task.FromResult(new IntegritySweepReport(
            Sampled: 0,
            Drifted: 0,
            DriftRatio: 0.0,
            Issues: Array.Empty<string>()));
    }
}
