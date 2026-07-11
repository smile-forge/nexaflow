using System;

namespace Nexaflow.Features.GraphViewer.Layout;

/// <summary>
/// A Barnes-Hut quadtree for O(n log n) repulsion. Each cell aggregates a centre of mass, so a distant cluster is
/// approximated by a single push when the opening criterion (cell-size² &lt; θ²·distance²) holds. Deterministic:
/// bodies are inserted in a fixed (id-sorted) order and force traversal is a fixed pre-order walk.
/// </summary>
internal sealed class Quadtree
{
    /// <summary>Stop subdividing below this cell half-size — coincident (or sub-pixel) points otherwise recurse
    /// forever. Such points are kept together as a centre-of-mass bucket, which is exactly how repulsion uses them.</summary>
    private const double MinHalf = 0.5;

    private readonly double _cx, _cy, _half;   // cell centre + half-size
    private double _comX, _comY;               // centre of mass of the bodies within
    private int _mass;
    private bool _leaf = true;
    private double _bx, _by;                    // the single body while still a leaf
    private Quadtree? _nw, _ne, _sw, _se;

    public Quadtree(double centreX, double centreY, double half)
    {
        _cx = centreX;
        _cy = centreY;
        _half = half;
    }

    public void Insert(double x, double y)
    {
        _comX = (_comX * _mass + x) / (_mass + 1);
        _comY = (_comY * _mass + y) / (_mass + 1);
        _mass++;

        if (_mass == 1) { _bx = x; _by = y; return; }
        if (_half <= MinHalf) return;   // coincident/sub-pixel cell → COM bucket, don't subdivide (bounds recursion)
        if (_leaf) { _leaf = false; Place(_bx, _by); }
        Place(x, y);
    }

    private void Place(double x, double y)
    {
        var h = _half / 2;
        var east = x >= _cx;
        var north = y >= _cy;
        var ccx = _cx + (east ? h : -h);
        var ccy = _cy + (north ? h : -h);

        if (east && north) (_ne ??= new Quadtree(ccx, ccy, h)).Insert(x, y);
        else if (east)     (_se ??= new Quadtree(ccx, ccy, h)).Insert(x, y);
        else if (north)    (_nw ??= new Quadtree(ccx, ccy, h)).Insert(x, y);
        else               (_sw ??= new Quadtree(ccx, ccy, h)).Insert(x, y);
    }

    /// <summary>Accumulate repulsion on (px,py) into (fx,fy). <paramref name="k2"/> = k² (ideal length squared);
    /// magnitude is ~ k²·mass/d directed away from the centre of mass — the Fruchterman-Reingold repulsion.</summary>
    public void Repulsion(double px, double py, double k2, double theta2, ref double fx, ref double fy)
    {
        if (_mass == 0) return;

        double dx = px - _comX, dy = py - _comY;
        var d2 = (dx * dx) + (dy * dy) + 1e-6;

        if (_leaf || (4 * _half * _half) < theta2 * d2)
        {
            var inv = k2 * _mass / d2;   // |(inv·dx, inv·dy)| = k²·mass/d, pointing away from the COM
            fx += inv * dx;
            fy += inv * dy;
            return;
        }

        _nw?.Repulsion(px, py, k2, theta2, ref fx, ref fy);
        _ne?.Repulsion(px, py, k2, theta2, ref fx, ref fy);
        _sw?.Repulsion(px, py, k2, theta2, ref fx, ref fy);
        _se?.Repulsion(px, py, k2, theta2, ref fx, ref fy);
    }
}
