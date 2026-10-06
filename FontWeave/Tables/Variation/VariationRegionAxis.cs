using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>One RegionAxisCoordinates triple at its raw F2Dot14 wire values. Scalar
/// computation (coordinates to region coefficients) is oracle-only and deliberately
/// absent from the product API (card 3.2); consumers compare or re-emit these raw
/// values, which keeps round-trips exact.</summary>
public readonly record struct VariationRegionAxis(short StartCoord, short PeakCoord, short EndCoord);
