# Building a multi-part schematic component

A multi-part (multi-unit) component is one physical package drawn as several independent
symbols — e.g. a dual gate placed as part A and part B. The package is one `SchComponent`
with `PartCount > 1`; each child primitive carries an `OwnerPartId` that says which part
it belongs to.

The complete, compiling source for this guide is [Program.cs](Program.cs).

## Run

```bash
dotnet run --project examples/BuildMultiPartComponent
```

## What it shows

```csharp
var gate = new SchComponent { Name = "DUAL_GATE", PartCount = 2, DesignatorPrefix = "U" };

for (var part = 1; part <= 2; part++)
{
    gate.AddRectangle(new SchRectangle { /* corners, color */ OwnerPartId = part });

    var pin = SchPin.Create("1").WithName("A")
        .At(Coord.FromMm(-7.62), Coord.FromMm(2.54))
        .Length(Coord.FromMm(2.54)).Orient(PinOrientation.Right)
        .Electrical(PinElectricalType.Input).Build();
    pin.OwnerPartId = part;            // the pin builder doesn't set this — set it after Build()
    gate.AddPin(pin);
}
```

`OwnerPartId` lives on every schematic primitive (`SchPin`, `SchRectangle`, …) but not on
the builders, so set it on the built object.

## Sample output

```
Built DUAL_GATE: PartCount = 2

In memory:
  Part 1: 5 pins (1:A, 2:B, 3:Y, 14:VCC, 7:GND)
  Part 2: 3 pins (4:A, 5:B, 6:Y)

Reloaded from MultiPart.SchLib (PartCount = 2):
  Part 1: 5 pins (1:A, 2:B, 3:Y, 14:VCC, 7:GND)
  Part 2: 3 pins (4:A, 5:B, 6:Y)
```

The save/reload confirms the per-part assignment survives the round-trip: pins come back
split across their parts exactly as built.

## Notes

- `PartCount` and per-pin `OwnerPartId` both round-trip through the SchLib writer/reader.
- Pins with `OwnerPartId` left at its default are written as part 1 (parts are 1-based).

See the [guides index](../../guides/README.md) for the full set of examples.
