# Nets & connectivity

For each net on a board, count the copper objects that carry it (pads, tracks, vias,
arcs, regions, polygon pours, fills). It's the kind of summary you use to spot an
unrouted net or an unexpectedly large one.

The complete, compiling source for this guide is [Program.cs](Program.cs).

## Run

```bash
dotnet run --project examples/NetReport                  # bundled board
dotnet run --project examples/NetReport -- "C:\My.PcbDoc"
```

## Where the data lives

Cast the `IPcbDocument` to `PcbDocument` for the `Nets` list. Each copper primitive
references its net by **`NetIndex`** — a `ushort` index into `doc.Nets`, with `0xFFFF`
meaning "no net". The `.Net` *name string* is often left unset by the reader, so resolve
through the index:

```csharp
var doc = (PcbDocument)await AltiumLibrary.OpenPcbDocAsync(input);

string ResolveNet(string? netStr, ushort index)
{
    if (index != 0xFFFF && index < doc.Nets.Count) return doc.Nets[index].Name;
    return string.IsNullOrWhiteSpace(netStr) ? "(unassigned)" : netStr;
}

foreach (var p in doc.Pads)
{
    var pad = (PcbPad)p;
    var net = ResolveNet(pad.Net, pad.NetIndex);
    // bucket counts by net ...
}
```

`NetIndex` is present on `PcbPad`, `PcbTrack`, `PcbVia`, `PcbArc`, `PcbRegion`,
`PcbFill`. Polygons store their net on `PcbPolygon.Net` instead.

## Sample output

```
Net                     Pads   Trk   Via  Poly  Total
--------------------------------------------------------
(unassigned)              12   612     0     0    670
GND                       46    94   436     4    580
5V                        19    36    16     2     73
3V3                       10    17     7     1     35
BUFF_OUT                   5     8     1     0     14
```

## What the model does and doesn't track

On the **PCB** side, net membership *is* modelled — every copper primitive names its net
(by index). On the **schematic** side it is *not*: pins are joined by wires drawn on the
canvas and there is no pin-to-net API, so a schematic netlist would have to be inferred
from wire geometry. This example therefore works from the PCB.

See the [guides index](../../guides/README.md) for the full set of examples.
