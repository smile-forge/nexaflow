using System.Threading;
using System.Threading.Tasks;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.IO.Common;

/// <summary>
/// Where one file of an archive being extracted lands: given the entry's name as the archive holds it, the write that receives
/// its bytes — committed once the entry is whole, disposed uncommitted when the extraction stops part-way — or null to leave
/// the entry out. The destination decides every name, so one that keeps names a Windows folder cannot hold, such as a WSL
/// distro, receives them as the archive has them; and it decides what lies outside it, so an entry climbing out is left out.
/// </summary>
public delegate ValueTask<ILocationWrite?> ArchiveEntryPlacement(string entryName, CancellationToken ct);
