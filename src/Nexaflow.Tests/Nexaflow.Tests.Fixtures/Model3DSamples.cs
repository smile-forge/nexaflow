namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// 3D model fixtures for the Model3D viewer: a tetrahedron as ASCII STL and Wavefront OBJ (text formats),
/// plus a single-triangle glTF with an embedded base64 buffer and a coloured material (self-contained — no
/// sidecar .bin). Enough geometry + material for the loaders to report real triangle/vertex counts and a
/// named material, and for the per-file UI test to assert each opens in the 3D viewer.
/// </summary>
internal sealed class Model3DSamples : ISampleSet
{
    public string SubDirectory => "model3d";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Text("tetra.stl", Stl),
        SampleFile.Text("tetra.obj", Obj),
        SampleFile.Text("triangle.gltf", Gltf()),
    ];

    // A tetrahedron: A(0,0,0) B(1,0,0) C(0,1,0) D(0,0,1), four triangular facets.
    private const string Stl =
        """
        solid tetra
          facet normal 0 0 0
            outer loop
              vertex 0 0 0
              vertex 1 0 0
              vertex 0 1 0
            endloop
          endfacet
          facet normal 0 0 0
            outer loop
              vertex 0 0 0
              vertex 0 1 0
              vertex 0 0 1
            endloop
          endfacet
          facet normal 0 0 0
            outer loop
              vertex 0 0 0
              vertex 0 0 1
              vertex 1 0 0
            endloop
          endfacet
          facet normal 0 0 0
            outer loop
              vertex 1 0 0
              vertex 0 0 1
              vertex 0 1 0
            endloop
          endfacet
        endsolid tetra
        """;

    private const string Obj =
        """
        # tetrahedron
        v 0 0 0
        v 1 0 0
        v 0 1 0
        v 0 0 1
        f 1 2 3
        f 1 3 4
        f 1 4 2
        f 2 4 3
        """;

    // A minimal glTF 2.0: one red triangle with the geometry buffer embedded as a base64 data URI,
    // so the fixture is a single self-contained text file the glTF loader can read by path.
    private static string Gltf()
    {
        var buffer = new byte[42];
        Buffer.BlockCopy(new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }, 0, buffer, 0, 36); // 3 × VEC3 positions
        Buffer.BlockCopy(new ushort[] { 0, 1, 2 }, 0, buffer, 36, 6);                  // 3 × SCALAR indices
        var data = Convert.ToBase64String(buffer);

        return
            """
            {
              "asset": { "version": "2.0" },
              "scene": 0,
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1, "material": 0 } ] } ],
              "materials": [ { "name": "Red", "pbrMetallicRoughness": { "baseColorFactor": [ 0.8, 0.1, 0.1, 1.0 ] } } ],
              "buffers": [ { "byteLength": 42, "uri": "data:application/octet-stream;base64,__BUFFER__" } ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 36, "target": 34962 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 6, "target": 34963 }
              ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [ 0, 0, 0 ], "max": [ 1, 1, 0 ] },
                { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ]
            }
            """.Replace("__BUFFER__", data);
    }
}
