"""Regenerates Hdf5Samples.cs: HDF5 fixtures written by libhdf5 itself, through h5py.

    python Hdf5Samples.py > Hdf5Samples.cs

The files are embedded as base64 because the fixtures library takes no dependencies; the viewer's own reader
never writes them, so the tests prove it reads what real tools write.
"""
import base64, io, os, sys, tempfile
import numpy as np
import h5py


def experiment(path):
    # The default format (readable by HDF5 1.8 and later) — what most files in the wild use.
    with h5py.File(path, "w", track_order=False) as f:
        f.attrs["creator"] = "h5py"
        f.attrs["experiment"] = np.int32(42)

        cal = f.create_group("calibration")
        cal.attrs["date"] = "2026-03-02"
        cal.create_dataset("gain", data=(1 + 0.02 * np.sin(np.arange(64) / 4)).astype("<f4"))
        cal["gain"].attrs["units"] = "dB"
        cal.create_dataset("offsets", data=np.linspace(-0.004, 0.004, 8), dtype="<f8")

        m = f.create_group("measurements")
        m.attrs["operator"] = "A. Jones"
        t = m.create_dataset("temperature", data=293.15 + 2.1 * np.sin(np.arange(500) / 30),
                             dtype="<f8", chunks=(100,), maxshape=(None,), compression="gzip", shuffle=True)
        t.attrs["units"] = "K"
        t.attrs["sensor"] = "PT100"
        t.attrs["sample_rate"] = 1000.0
        t.attrs["calibration"] = np.array([1.0, 0.5, 0.25])
        m.create_dataset("frames", data=(np.arange(4 * 16 * 16).reshape(4, 16, 16) % 1000).astype("<u2"),
                         chunks=(1, 16, 16), compression="gzip")
        ev = np.zeros(50, dtype=[("t", "<f8"), ("ch", "<i2"), ("energy", "<f4")])
        ev["t"] = np.arange(50) * 0.001
        ev["ch"] = np.arange(50) % 16
        ev["energy"] = 511 + np.arange(50)
        m.create_dataset("events", data=ev)
        m.create_dataset("counter", data=np.arange(2000, dtype="<i4"), chunks=(1024,), compression="gzip", shuffle=True)
        m.create_dataset("matrix", data=np.arange(200 * 300, dtype="<i4").reshape(200, 300),
                         chunks=(100, 300), compression="gzip", shuffle=True)

        md = f.create_group("metadata")
        md.create_dataset("config", data='{"bins": 4096}', dtype=h5py.string_dtype("utf-8"))
        md.create_dataset("labels", data=np.array([b"alpha", b"beta", b"gamma"], dtype="S8"))
        md.create_dataset("notes", data=["first", "second line\nwrapped", "third"], dtype=h5py.string_dtype("utf-8"))
        state = h5py.enum_dtype({"IDLE": 0, "RUN": 1, "FAULT": 2}, basetype="i1")
        md.create_dataset("state", data=np.array([0, 1, 1, 2, 0, 1], dtype="i1"), dtype=state)
        md.create_dataset("empty", shape=(0,), dtype="<f4", maxshape=(None,), chunks=(16,))
        md.create_dataset("scalar", data=np.float64(3.25))
        md.create_dataset("flags", data=np.array([True, False, True]))
        md.create_dataset("blob", data=np.void(b"\x01\x02\x03\x04"))

        f["latest"] = h5py.SoftLink("/measurements/temperature")
        f["dangling"] = h5py.SoftLink("/nowhere")
        f["elsewhere"] = h5py.ExternalLink("other.h5", "/data")
        f["typedef"] = np.dtype("<f8")


def latest_format(path):
    # The newest format writes enum and compound datatypes as datatype message version 4, which PureHDF does
    # not decode — the reader must still list what it can and say why it stopped.
    with h5py.File(path, "w", libver="latest") as f:
        f.create_dataset("readings", data=np.arange(4, dtype="<f8"))
        f.create_dataset("state", data=np.array([0, 1], dtype="i1"), dtype=h5py.enum_dtype({"OFF": 0, "ON": 1}, basetype="i1"))


def encoded(build):
    with tempfile.TemporaryDirectory() as d:
        path = os.path.join(d, "f.h5")
        build(path)
        data = base64.b64encode(open(path, "rb").read()).decode("ascii")
    return "\n".join("        " + data[i:i + 100] for i in range(0, len(data), 100))


sys.stdout.reconfigure(newline="\n")
print(f'''namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// HDF5 files written by libhdf5 itself (h5py {h5py.__version__}, HDF5 {h5py.version.hdf5_version}), so the
/// viewer is tested against what real tools write rather than against its own reader's output. Embedded as
/// base64 because this library takes no dependencies; <c>Hdf5Samples.py</c> beside this file regenerates it.
/// <list type="bullet">
/// <item><c>experiment.h5</c> — the default format: groups; soft, dangling and external links; a named
/// datatype; compact, contiguous and chunked (deflate, shuffle) layouts; 1-, 2- and 3-D numbers; a compound;
/// fixed and variable-length strings; an enum, a bool, an opaque, a scalar and an empty dataset; attributes on
/// the root, groups and datasets.</item>
/// <item><c>latest-format.h5</c> — the newest format, whose enum datatype the reader cannot decode, so listing
/// the root stops early with a reason.</item>
/// </list>
/// </summary>
internal sealed class Hdf5Samples : ISampleSet
{{
    public string SubDirectory => "hdf5";

    public IReadOnlyList<SampleFile> Files {{ get; }} =
    [
        SampleFile.Raw("experiment.h5", Convert.FromBase64String(Experiment)),
        SampleFile.Raw("latest-format.h5", Convert.FromBase64String(LatestFormat)),
    ];

    private const string Experiment = """
{encoded(experiment)}
        """;

    private const string LatestFormat = """
{encoded(latest_format)}
        """;
}}''')
