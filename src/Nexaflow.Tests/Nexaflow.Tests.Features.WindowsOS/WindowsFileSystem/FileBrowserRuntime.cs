using System;
using System.Collections.Generic;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Operations;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// The file browser's services as one workspace runtime holds them — its companions, its operation queue and its feature
/// registry, one each and handed to one another as the feature DI hands them — and the browser tabs a test opens on that
/// runtime, which all share them.
/// </summary>
internal sealed class FileBrowserRuntime
{
    private readonly IShellServices _shell;
    private readonly IAIService _ai;
    private readonly IReadOnlyDictionary<Type, IFeatureConfig> _configs;
    private readonly IVirtualFileSystem _vfs;

    public FileBrowserRuntime(IShellServices shell, IAIService ai, IReadOnlyDictionary<Type, IFeatureConfig> configs,
                              IVirtualFileSystem vfs)
    {
        _shell     = shell;
        _ai        = ai;
        _configs   = configs;
        _vfs       = vfs;
        Companions = new FileCompanions(shell);
        Operations = new FileOperationQueue(shell, vfs, Companions);
        Registry   = new FileSystemFeatureRegistry(shell, ai, configs, vfs, Operations, Companions);
    }

    public FileCompanions Companions { get; }

    public FileOperationQueue Operations { get; }

    public FileSystemFeatureRegistry Registry { get; }

    /// <summary>A browser tab opened on <paramref name="folder"/>.</summary>
    public FileSystemViewModel Browse(string folder)
        => new(folder, _shell, _ai, _configs, _vfs, Registry, Operations, Companions);

    /// <summary>A browser tab opened on This PC.</summary>
    public FileSystemViewModel ThisPc()
        => FileSystemViewModel.CreateThisPc(_shell, _ai, _configs, _vfs, Registry, Operations, Companions);
}
