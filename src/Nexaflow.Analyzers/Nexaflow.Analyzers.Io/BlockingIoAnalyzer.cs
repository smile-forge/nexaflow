using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Nexaflow.Analyzers.Io;

/// <summary>
/// How a feature reaches a file. Everything a person can open may live on a location — another machine, reached over the
/// network — so a feature reaches it through the awaitable members of <c>IVirtualFileSystem</c>, and never waits on the disk
/// on whatever thread happened to ask.
///
/// <para>
/// <b>NXIO001</b> — a call to a member of the file system marked <c>[BlocksOnIo]</c>; the report names the member to await
/// instead. <b>NXIO002</b> — <c>System.IO</c> reaching the disk itself: a static of <c>File</c>, <c>Directory</c> or
/// <c>ZipFile</c>, <c>Path.Exists</c>, or making a <c>FileInfo</c>, <c>DirectoryInfo</c>, <c>FileStream</c>,
/// <c>FileSystemWatcher</c>, or a <c>StreamReader</c> / <c>StreamWriter</c> on a path. A path from a location never arrives
/// there. <b>NXIO003</b> — a <c>[LocalDiskOnly]</c> that does not say where its files are.
/// </para>
/// <para>
/// Code that only ever touches this machine's own disk — settings, caches and stores under the app's data folders — is
/// marked <c>[LocalDiskOnly("where")]</c>, on the type or the member, and is not reported: the mark covers what is lexically
/// inside it, lambdas and local functions included. The reason is what a reviewer reads, so an empty one is an error.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BlockingIoAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "IO";

    private const string FileSystem    = "Nexaflow.IO.Common.IVirtualFileSystem";
    private const string BlocksOnIo    = "Nexaflow.IO.Common.BlocksOnIoAttribute";
    private const string LocalDiskOnly = "Nexaflow.IO.Common.LocalDiskOnlyAttribute";

    /// <summary>Types whose every static reaches the disk.</summary>
    private static readonly string[] DiskStatics = ["System.IO.File", "System.IO.Directory", "System.IO.Compression.ZipFile"];

    /// <summary>Types that, once made, stand for one file or folder on the disk.</summary>
    private static readonly string[] DiskBound =
        ["System.IO.FileInfo", "System.IO.DirectoryInfo", "System.IO.FileStream", "System.IO.FileSystemWatcher"];

    /// <summary>Types that open the disk when made from a path rather than handed a stream.</summary>
    private static readonly string[] PathStreams = ["System.IO.StreamReader", "System.IO.StreamWriter"];

    private static readonly DiagnosticDescriptor Blocking = new(
        "NXIO001",
        "A feature awaits the file system rather than blocking on it",
        "{0} blocks on the disk or the network; {1}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "On a location the wait is a network round trip on whatever thread asked, and a runtime's file system refuses the call there. Await the member the report names, or, for code that only touches this machine's own disk, mark it [LocalDiskOnly] with where its files are.");

    private static readonly DiagnosticDescriptor Direct = new(
        "NXIO002",
        "A feature reaches files through the file system, not System.IO",
        "{0} reaches the disk directly, so a path from a location never arrives here; go through IVirtualFileSystem, or mark the code [LocalDiskOnly] with where its files are",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "A path a person opened may be on another machine. System.IO sees only this one, so a feature that uses it on such a path fails, or worse, finds a different file of the same name.");

    private static readonly DiagnosticDescriptor Unreasoned = new(
        "NXIO003",
        "An exemption says where its files are",
        "[LocalDiskOnly] on {0} gives no reason; say where its files are",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "The reason is what a reviewer weighs. Without one the mark is a blanket escape from NXIO001 and NXIO002.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Blocking, Direct, Unreasoned);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var compilation = start.Compilation;
            var scan = new Scan(
                compilation.GetTypeByMetadataName(FileSystem),
                compilation.GetTypeByMetadataName(BlocksOnIo),
                compilation.GetTypeByMetadataName(LocalDiskOnly),
                Types(compilation, DiskStatics),
                Types(compilation, DiskBound),
                Types(compilation, PathStreams),
                compilation.GetTypeByMetadataName("System.IO.Path"));

            start.RegisterOperationAction(scan.Invoked, OperationKind.Invocation);
            start.RegisterOperationAction(scan.Made, OperationKind.ObjectCreation);
            if (scan.Exempting is not null)
                start.RegisterSymbolAction(scan.Reasoned, SymbolKind.NamedType, SymbolKind.Method, SymbolKind.Property);
        });
    }

    private static ImmutableHashSet<INamedTypeSymbol> Types(Compilation compilation, IEnumerable<string> names) =>
        names.Select(compilation.GetTypeByMetadataName)
             .Where(type => type is not null)
             .Select(type => type!)
             .ToImmutableHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

    /// <summary>One compilation's view of what reaches the disk, and what is exempt from saying so.</summary>
    private sealed class Scan
    {
        private readonly INamedTypeSymbol? _fileSystem;
        private readonly INamedTypeSymbol? _blocksOnIo;
        private readonly ImmutableHashSet<INamedTypeSymbol> _diskStatics;
        private readonly ImmutableHashSet<INamedTypeSymbol> _diskBound;
        private readonly ImmutableHashSet<INamedTypeSymbol> _pathStreams;
        private readonly INamedTypeSymbol? _path;

        /// <summary>The marked members of the file system, each with the member to await instead.</summary>
        private readonly ImmutableArray<(IMethodSymbol Member, string? Alternative)> _blocking;

        /// <summary>What each method called on a file system was found to be — a marked member's implementation or not.</summary>
        private readonly ConcurrentDictionary<IMethodSymbol, (bool Blocks, string? Alternative)> _known =
            new(SymbolEqualityComparer.Default);

        public INamedTypeSymbol? Exempting { get; }

        public Scan(INamedTypeSymbol? fileSystem, INamedTypeSymbol? blocksOnIo, INamedTypeSymbol? exempting,
                    ImmutableHashSet<INamedTypeSymbol> diskStatics, ImmutableHashSet<INamedTypeSymbol> diskBound,
                    ImmutableHashSet<INamedTypeSymbol> pathStreams, INamedTypeSymbol? path)
        {
            _fileSystem  = fileSystem;
            _blocksOnIo  = blocksOnIo;
            Exempting    = exempting;
            _diskStatics = diskStatics;
            _diskBound   = diskBound;
            _pathStreams = pathStreams;
            _path        = path;
            _blocking    = fileSystem is null || blocksOnIo is null
                ? ImmutableArray<(IMethodSymbol, string?)>.Empty
                : fileSystem.GetMembers().OfType<IMethodSymbol>()
                            .Select(member => (member, mark: Mark(member)))
                            .Where(pair => pair.mark is not null)
                            .Select(pair => (pair.member, (string?)pair.mark!.ConstructorArguments.FirstOrDefault().Value))
                            .ToImmutableArray();
        }

        public void Invoked(OperationAnalysisContext context)
        {
            var target = ((IInvocationOperation)context.Operation).TargetMethod.OriginalDefinition;

            if (Blocks(target) is { Blocks: true } blocking)
            {
                if (Exempt(context.ContainingSymbol)) return;
                var instead = blocking.Alternative is { } alternative
                    ? $"await {alternative}"
                    : "it reaches local paths only, so open through OpenReadAsync or BeginWriteAsync";
                context.ReportDiagnostic(Diagnostic.Create(Blocking, context.Operation.Syntax.GetLocation(),
                                                           $"{target.ContainingType.Name}.{target.Name}", instead));
                return;
            }

            if (!target.IsStatic) return;
            var onDisk = _diskStatics.Contains(target.ContainingType)
                      || (target.Name == "Exists" && SymbolEqualityComparer.Default.Equals(target.ContainingType, _path));
            if (onDisk && !Exempt(context.ContainingSymbol))
                context.ReportDiagnostic(Diagnostic.Create(Direct, context.Operation.Syntax.GetLocation(),
                                                           $"{target.ContainingType.Name}.{target.Name}"));
        }

        public void Made(OperationAnalysisContext context)
        {
            var creation = (IObjectCreationOperation)context.Operation;
            if (creation.Type is not INamedTypeSymbol made) return;

            var onDisk = _diskBound.Contains(made)
                      || (_pathStreams.Contains(made)
                          && creation.Constructor is { Parameters.Length: > 0 } constructor
                          && constructor.Parameters[0].Type.SpecialType == SpecialType.System_String);
            if (onDisk && !Exempt(context.ContainingSymbol))
                context.ReportDiagnostic(Diagnostic.Create(Direct, creation.Syntax.GetLocation(), $"new {made.Name}"));
        }

        public void Reasoned(SymbolAnalysisContext context)
        {
            foreach (var mark in context.Symbol.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(mark.AttributeClass, Exempting)) continue;
                if (mark.ConstructorArguments.FirstOrDefault().Value is string reason && !string.IsNullOrWhiteSpace(reason)) continue;

                var place = mark.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                         ?? context.Symbol.Locations.FirstOrDefault();
                context.ReportDiagnostic(Diagnostic.Create(Unreasoned, place, context.Symbol.Name));
            }
        }

        /// <summary>Whether <paramref name="target"/> is a marked member of the file system, or a type's implementation of one.</summary>
        private (bool Blocks, string? Alternative) Blocks(IMethodSymbol target)
        {
            if (_blocking.IsEmpty || target.ContainingType is not { } type) return default;

            return _known.GetOrAdd(target, method =>
            {
                if (Mark(method) is { } mark)
                    return (true, mark.ConstructorArguments.FirstOrDefault().Value as string);

                if (!SymbolEqualityComparer.Default.Equals(type, _fileSystem) && !type.AllInterfaces.Contains(_fileSystem!, SymbolEqualityComparer.Default))
                    return default;

                foreach (var (member, alternative) in _blocking)
                    if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), method))
                        return (true, alternative);

                return default;
            });
        }

        private AttributeData? Mark(IMethodSymbol method) =>
            method.GetAttributes().FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, _blocksOnIo));

        /// <summary>Whether the code holding an operation sits inside a declaration marked <c>[LocalDiskOnly]</c>.</summary>
        private bool Exempt(ISymbol? holder)
        {
            if (Exempting is null) return false;

            for (var at = holder; at is not null and not INamespaceSymbol; at = at.ContainingSymbol)
            {
                if (Marked(at)) return true;
                if (at is IMethodSymbol { AssociatedSymbol: { } property } && Marked(property)) return true;
            }

            return false;
        }

        private bool Marked(ISymbol symbol) =>
            symbol.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, Exempting));
    }
}
