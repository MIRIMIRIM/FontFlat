using System;
using System.Collections.Generic;
using OTFontFile;

namespace OTFontFile.Subsetting.Layout
{
    /// <summary>
    /// Interface for subtable subsetters.
    /// Reads binary source subtable and writes binary subset subtable.
    /// </summary>
    public interface ISubtableSubsetter
    {
        /// <summary>
        /// Subsets a subtable.
        /// </summary>
        /// <returns>Bytes of new subtable, or null/empty if subtable should be dropped.</returns>
        byte[]? Subset(MBOBuffer file, uint offset, SubsetPlan plan);
    }

    /// <summary>
    /// Subsetter for GSUB Lookup Type 1: Single Substitution.
    /// Supports Format 1 (Delta) and Format 2 (Array).
    /// </summary>
    public class SingleSubstSubsetter : ISubtableSubsetter
    {
        public byte[]? Subset(MBOBuffer file, uint offset, SubsetPlan plan)
        {
            ushort format = file.GetUshort(offset);
            uint coverageOffset = offset + (uint)file.GetUshort(offset + 2);
            
            // Initial candidate coverage
            var candidateCoverage = CoverageSubsetter.Subset(file, coverageOffset, plan);
            if (candidateCoverage.Count == 0)
                return null;

            // List of (NewInputGid, NewSubstituteGid)
            var rules = new List<(ushort Input, ushort Output)>();

            if (format == 1)
            {
                // Format 1: DeltaGlyphID
                short oldDelta = file.GetShort(offset + 4);
                
                foreach (var item in candidateCoverage)
                {
                    // Calculate old substitute
                    // OpenType: Substitute = Input + Delta (modulo 65536)
                    int oldSubstInt = (item.OldGid + oldDelta) % 65536;
                    if (oldSubstInt < 0) oldSubstInt += 65536;
                    ushort oldSubst = (ushort)oldSubstInt;

                    // Check if substitute is retained
                    if (plan.TryGetNewGid(oldSubst, out ushort newSubst))
                    {
                        rules.Add((item.NewGid, newSubst));
                    }
                }
            }
            else if (format == 2)
            {
                // Format 2: Array of substitutes
                uint glyphCount = file.GetUshort(offset + 4);
                // The coverage index corresponds to index in substitute array
                
                // We shouldn't read the whole array if we only need specific indices,
                // but for simplicity/safety we access by index.
                foreach (var item in candidateCoverage)
                {
                    if (item.OldCovIndex >= glyphCount) continue; // Should not happen if font valid

                    uint substOffset = offset + 6 + (uint)item.OldCovIndex * 2;
                    ushort oldSubst = file.GetUshort(substOffset);

                    if (plan.TryGetNewGid(oldSubst, out ushort newSubst))
                    {
                        rules.Add((item.NewGid, newSubst));
                    }
                }
            }
            else
            {
                return null; // Unknown format
            }

            if (rules.Count == 0)
                return null;

            // Ensure rules are sorted by Input GID (they should be from CoverageSubsetter)
            // But we might have dropped some, so the list order is preserved.
            
            // Determine best output format
            bool canUseFormat1 = true;
            int firstDelta = 0;

            if (rules.Count > 0)
            {
                firstDelta = rules[0].Output - rules[0].Input;
                for (int i = 1; i < rules.Count; i++)
                {
                    int delta = rules[i].Output - rules[i].Input;
                    if (delta != firstDelta)
                    {
                        canUseFormat1 = false;
                        break;
                    }
                }
            }

            // Build output
            var inputs = new List<ushort>(rules.Count);
            foreach (var r in rules) inputs.Add(r.Input);
            byte[] newCoverageBytes = CoverageSubsetter.BuildCoverage(inputs);

            // Align to 2-byte boundary (though byte[] alloc matches length)
            // Padding logic if needed? OpenType tables usually 2-byte aligned relative to start.
            // But here we return a byte array. The caller assumes it's the subtable.

            List<byte> output = new();
            
            if (canUseFormat1)
            {
                // Format 1
                // uint16 SubstituionFormat = 1
                // Offset16 Coverage
                // int16  DeltaGlyphID
                
                output.Add(0); output.Add(1); // Format 1
                
                // Coverage Offset: 6 bytes header (2+2+2)
                ushort covOffset = 6;
                output.Add((byte)(covOffset >> 8)); output.Add((byte)covOffset);
                
                // Delta
                short newDelta = (short)firstDelta;
                output.Add((byte)(newDelta >> 8)); output.Add((byte)newDelta);
            }
            else
            {
                // Format 2
                // uint16 SubstitutionFormat = 2
                // Offset16 Coverage
                // uint16 GlyphCount
                // GlyphID Substitute[GlyphCount]
                
                output.Add(0); output.Add(2); // Format 2
                
                // Coverage Offset: 6 + 2*count
                // Header is 2 (Fmt) + 2 (Cov) + 2 (Count) = 6 bytes
                // Array is rules.Count * 2 bytes
                int headerSize = 6 + (rules.Count * 2);
                
                output.Add((byte)(headerSize >> 8)); output.Add((byte)headerSize);
                
                ushort count = (ushort)rules.Count;
                output.Add((byte)(count >> 8)); output.Add((byte)count);
                
                foreach (var r in rules)
                {
                    output.Add((byte)(r.Output >> 8)); output.Add((byte)r.Output);
                }
            }

            // Append Coverage
            output.AddRange(newCoverageBytes);

            return output.ToArray();
        }
    }

    /// <summary>
    /// Subsetter for GSUB Lookup Type 4: Ligature Substitution.
    /// Filters ligatures based on component availability.
    /// </summary>
    public class LigatureSubstSubsetter : ISubtableSubsetter
    {
        public byte[]? Subset(MBOBuffer file, uint offset, SubsetPlan plan)
        {
            ushort format = file.GetUshort(offset);
            if (format != 1) return null; // Only Format 1 defined

            uint coverageOffset = offset + (uint)file.GetUshort(offset + 2);
            ushort ligSetCount = file.GetUshort(offset + 4);
            
            // Subset Coverage
            var candidateCoverage = CoverageSubsetter.Subset(file, coverageOffset, plan);
            if (candidateCoverage.Count == 0)
                return null;

            // We need to filter LigatureSets. 
            // If a LigatureSet becomes empty, we drop the corresponding coverage glyph.
            
            var retainedLigatureSets = new List<(ushort NewGid, byte[] Data)>();
            
            foreach (var item in candidateCoverage)
            {
                if (item.OldCovIndex >= ligSetCount) continue;

                uint ligSetOffset = offset + (uint)file.GetUshort(offset + 6 + (uint)item.OldCovIndex * 2);
                byte[]? newLigSetData = SubsetLigatureSet(file, offset + ligSetOffset, plan); // Note: offset is absolute or relative? Specs: "Offset to LigatureSet table... from beginning of LigatureSubst"
                // Actually spec says Offset16 LigatureSetOffset[LigSetCount], from beginning of LigatureSubst.
                
                // My helper passing logic: Offset + relative.
                // Re-check: file.GetUshort returns relative offset. The absolute pos is offset + relative.
                // BUT LigatureSet offsets are from start of LigatureSubst (offset).
                // So passed `offset + ligSetOffset` is incorrect if `ligSetOffset` variable already adds `offset`.
                
                // Correct logic:
                // uint relativeLigSetOffset = file.GetUshort(offset + 6 + item.OldCovIndex * 2);
                // uint absoluteLigSetOffset = offset + relativeLigSetOffset;
                
                // Wait, in my previous line:
                // uint ligSetOffset = offset + (uint)file.GetUshort(...)
                // That was correct absolute offset.
                
                // Wait... `SubsetLigatureSet` logic needs to be robust.
                
                if (newLigSetData != null)
                {
                    retainedLigatureSets.Add((item.NewGid, newLigSetData));
                }
            }

            if (retainedLigatureSets.Count == 0)
                return null;

            // Build new Coverage
            var newCoverageGlyphs = new List<ushort>();
            foreach (var item in retainedLigatureSets) newCoverageGlyphs.Add(item.NewGid);
            byte[] newCoverageBytes = CoverageSubsetter.BuildCoverage(newCoverageGlyphs);

            // Calculate Size
            // Header: 6 bytes
            // Offsets array: Count * 2 bytes
            // LigatureSets data
            // Coverage data
            
            int headerSize = 6 + (retainedLigatureSets.Count * 2);
            List<byte> output = new();
            
            output.Add(0); output.Add(1); // Format 1
            
            // Coverage Offset (placeholder, put at end)
            // But we can calculate it now.
            // Current pos in output buffer is 2. 
            // We need to know TOTAL size of LigatureSets to know where Coverage starts.
            
            int totalLigSetsSize = 0;
            foreach (var s in retainedLigatureSets) totalLigSetsSize += s.Data.Length;
            
            int coverageOffsetVal = headerSize + totalLigSetsSize;
            if (coverageOffsetVal > 65535) throw new Exception("LigatureSubst: Coverage Offset Overflow");
            
            output.Add((byte)(coverageOffsetVal >> 8)); output.Add((byte)coverageOffsetVal);
            
            ushort count = (ushort)retainedLigatureSets.Count;
            output.Add((byte)(count >> 8)); output.Add((byte)count);
            
            // Offsets to LigatureSets
            int currentLigSetRelativeOffset = headerSize; // First one starts after header+array
            
            foreach (var s in retainedLigatureSets)
            {
                output.Add((byte)(currentLigSetRelativeOffset >> 8)); output.Add((byte)currentLigSetRelativeOffset);
                currentLigSetRelativeOffset += s.Data.Length;
            }
            
            // Write LigatureSets
            foreach (var s in retainedLigatureSets)
            {
                output.AddRange(s.Data);
            }
            
            // Write Coverage
            output.AddRange(newCoverageBytes);
            
            return output.ToArray();
        }

        private byte[]? SubsetLigatureSet(MBOBuffer file, uint offset, SubsetPlan plan)
        {
            ushort ligCount = file.GetUshort(offset);
            var retainedLigatures = new List<byte[]>();

            for (int i = 0; i < ligCount; i++)
            {
                uint ligOffset = offset + (uint)file.GetUshort(offset + 2 + (uint)i * 2);
                byte[]? ligData = SubsetLigature(file, ligOffset, plan);
                if (ligData != null)
                {
                    retainedLigatures.Add(ligData);
                }
            }

            if (retainedLigatures.Count == 0) return null;

            // Build LigatureSet
            // Header: 2 + Count * 2
            int headerSize = 2 + (retainedLigatures.Count * 2);
            List<byte> output = new();
            
            ushort newCount = (ushort)retainedLigatures.Count;
            output.Add((byte)(newCount >> 8)); output.Add((byte)newCount);
            
            int currentRelativeOffset = headerSize;
            foreach (var lig in retainedLigatures)
            {
                output.Add((byte)(currentRelativeOffset >> 8)); output.Add((byte)currentRelativeOffset);
                currentRelativeOffset += lig.Length;
            }
            
            foreach (var lig in retainedLigatures)
            {
                output.AddRange(lig);
            }
            
            return output.ToArray();
        }

        private byte[]? SubsetLigature(MBOBuffer file, uint offset, SubsetPlan plan)
        {
            ushort ligGlyph = file.GetUshort(offset);
            if (!plan.TryGetNewGid(ligGlyph, out ushort newLigGlyph))
            {
                // Ligature Output Glyph not retained -> Drop rule
                return null; 
            }

            ushort compCount = file.GetUshort(offset + 2);
            // Component 0 is implied (Coverage glyph), so array has compCount - 1 components.
            var newComponents = new List<ushort>();
            
            for (int i = 0; i < compCount - 1; i++)
            {
                ushort compGid = file.GetUshort(offset + 4 + (uint)i * 2);
                if (!plan.TryGetNewGid(compGid, out ushort newCompGid))
                {
                    // Component missing -> Drop rule
                    return null;
                }
                newComponents.Add(newCompGid);
            }

            // Build Ligature Table
            // LigGlyph (2) + CompCount (2) + Components[] (2 * count-1)
            List<byte> output = new();
            output.Add((byte)(newLigGlyph >> 8)); output.Add((byte)newLigGlyph);
            output.Add((byte)(compCount >> 8)); output.Add((byte)compCount);
            
            foreach (var comp in newComponents)
            {
                output.Add((byte)(comp >> 8)); output.Add((byte)comp);
            }
            
            return output.ToArray();
        }
    }

    /// <summary>
    /// Subsetter for GPOS Lookup Type 1: Single Positioning.
    /// Adjusts positioning values for individual glyphs.
    /// </summary>
    public class SinglePosSubsetter : ISubtableSubsetter
    {
        public byte[]? Subset(MBOBuffer file, uint offset, SubsetPlan plan)
        {
            ushort format = file.GetUshort(offset);
            uint coverageOffset = offset + (uint)file.GetUshort(offset + 2);
            ushort valueFormat = file.GetUshort(offset + 4);
            
            var candidateCoverage = CoverageSubsetter.Subset(file, coverageOffset, plan);
            if (candidateCoverage.Count == 0)
                return null;

            uint valueRecordSize = GetValueRecordSize(valueFormat);
            
            List<byte> output = new();
            
            if (format == 1)
            {
                // Format 1: Single ValueRecord for all glyphs
                // Just rebuild coverage and copy the single ValueRecord
                output.Add(0); output.Add(1); // Format 1
                
                // Coverage offset placeholder (will be at 6 + valueRecordSize)
                int headerSize = 6 + (int)valueRecordSize;
                output.Add((byte)(headerSize >> 8)); output.Add((byte)headerSize);
                
                // ValueFormat
                output.Add((byte)(valueFormat >> 8)); output.Add((byte)valueFormat);
                
                // Copy ValueRecord from source (offset + 6)
                for (uint i = 0; i < valueRecordSize; i++)
                {
                    output.Add(file.GetByte(offset + 6 + i));
                }
            }
            else if (format == 2)
            {
                // Format 2: Array of ValueRecords (one per glyph in coverage)
                ushort valueCount = file.GetUshort(offset + 6);
                
                // Collect retained ValueRecords
                var retainedValues = new List<byte[]>();
                foreach (var item in candidateCoverage)
                {
                    if (item.OldCovIndex >= valueCount) continue;
                    
                    uint valueOffset = offset + 8 + (uint)item.OldCovIndex * valueRecordSize;
                    byte[] valueData = new byte[valueRecordSize];
                    for (uint i = 0; i < valueRecordSize; i++)
                    {
                        valueData[i] = file.GetByte(valueOffset + i);
                    }
                    retainedValues.Add(valueData);
                }
                
                if (retainedValues.Count == 0) return null;
                
                // Build output (Format 2)
                output.Add(0); output.Add(2); // Format 2
                
                // Coverage offset: 8 + (count * valueRecordSize)
                int headerSize = 8 + retainedValues.Count * (int)valueRecordSize;
                output.Add((byte)(headerSize >> 8)); output.Add((byte)headerSize);
                
                // ValueFormat
                output.Add((byte)(valueFormat >> 8)); output.Add((byte)valueFormat);
                
                // ValueCount
                ushort count = (ushort)retainedValues.Count;
                output.Add((byte)(count >> 8)); output.Add((byte)count);
                
                // ValueRecords
                foreach (var vr in retainedValues)
                {
                    output.AddRange(vr);
                }
            }
            else
            {
                return null; // Unknown format
            }

            // Build and append coverage
            var newCoverageGlyphs = new List<ushort>();
            foreach (var item in candidateCoverage) newCoverageGlyphs.Add(item.NewGid);
            byte[] coverageBytes = CoverageSubsetter.BuildCoverage(newCoverageGlyphs);
            output.AddRange(coverageBytes);

            return output.ToArray();
        }

        private static uint GetValueRecordSize(ushort valueFormat)
        {
            uint size = 0;
            if ((valueFormat & 0x0001) != 0) size += 2; // XPlacement
            if ((valueFormat & 0x0002) != 0) size += 2; // YPlacement
            if ((valueFormat & 0x0004) != 0) size += 2; // XAdvance
            if ((valueFormat & 0x0008) != 0) size += 2; // YAdvance
            if ((valueFormat & 0x0010) != 0) size += 2; // XPlaDevice
            if ((valueFormat & 0x0020) != 0) size += 2; // YPlaDevice
            if ((valueFormat & 0x0040) != 0) size += 2; // XAdvDevice
            if ((valueFormat & 0x0080) != 0) size += 2; // YAdvDevice
            return size;
        }
    }

    /// <summary>
    /// Subsetter for GPOS Lookup Type 2: Pair Positioning (Kerning).
    /// Handles pair adjustments between glyph pairs.
    /// </summary>
    public class PairPosSubsetter : ISubtableSubsetter
    {
        public byte[]? Subset(MBOBuffer file, uint offset, SubsetPlan plan)
        {
            ushort format = file.GetUshort(offset);
            
            if (format == 1)
            {
                return SubsetFormat1(file, offset, plan);
            }
            else if (format == 2)
            {
                // Format 2 (Class-based) is more complex - drop for now
                // TODO: Implement class-based pair positioning
                return null;
            }
            
            return null;
        }

        private byte[]? SubsetFormat1(MBOBuffer file, uint offset, SubsetPlan plan)
        {
            uint coverageOffset = offset + (uint)file.GetUshort(offset + 2);
            ushort valueFormat1 = file.GetUshort(offset + 4);
            ushort valueFormat2 = file.GetUshort(offset + 6);
            ushort pairSetCount = file.GetUshort(offset + 8);
            
            uint valueRecordSize1 = GetValueRecordSize(valueFormat1);
            uint valueRecordSize2 = GetValueRecordSize(valueFormat2);
            uint pairValueRecordSize = 2 + valueRecordSize1 + valueRecordSize2;
            
            var candidateCoverage = CoverageSubsetter.Subset(file, coverageOffset, plan);
            if (candidateCoverage.Count == 0)
                return null;

            // Filter PairSets
            var retainedPairSets = new List<(ushort NewGid, byte[] Data)>();
            
            foreach (var item in candidateCoverage)
            {
                if (item.OldCovIndex >= pairSetCount) continue;
                
                uint pairSetRelOffset = file.GetUshort(offset + 10 + (uint)item.OldCovIndex * 2);
                uint pairSetAbsOffset = offset + pairSetRelOffset;
                
                byte[]? pairSetData = SubsetPairSet(file, pairSetAbsOffset, plan, valueFormat1, valueFormat2);
                if (pairSetData != null)
                {
                    retainedPairSets.Add((item.NewGid, pairSetData));
                }
            }

            if (retainedPairSets.Count == 0)
                return null;

            // Build output
            List<byte> output = new();
            
            output.Add(0); output.Add(1); // Format 1
            
            // Calculate sizes
            int headerSize = 10 + retainedPairSets.Count * 2;
            int totalPairSetsSize = 0;
            foreach (var ps in retainedPairSets) totalPairSetsSize += ps.Data.Length;
            int coverageOffsetVal = headerSize + totalPairSetsSize;
            
            // Coverage offset
            output.Add((byte)(coverageOffsetVal >> 8)); output.Add((byte)coverageOffsetVal);
            
            // ValueFormats
            output.Add((byte)(valueFormat1 >> 8)); output.Add((byte)valueFormat1);
            output.Add((byte)(valueFormat2 >> 8)); output.Add((byte)valueFormat2);
            
            // PairSetCount
            ushort count = (ushort)retainedPairSets.Count;
            output.Add((byte)(count >> 8)); output.Add((byte)count);
            
            // PairSet offsets
            int currentOffset = headerSize;
            foreach (var ps in retainedPairSets)
            {
                output.Add((byte)(currentOffset >> 8)); output.Add((byte)currentOffset);
                currentOffset += ps.Data.Length;
            }
            
            // PairSet data
            foreach (var ps in retainedPairSets)
            {
                output.AddRange(ps.Data);
            }
            
            // Coverage
            var newCoverageGlyphs = new List<ushort>();
            foreach (var item in retainedPairSets) newCoverageGlyphs.Add(item.NewGid);
            byte[] coverageBytes = CoverageSubsetter.BuildCoverage(newCoverageGlyphs);
            output.AddRange(coverageBytes);

            return output.ToArray();
        }

        private byte[]? SubsetPairSet(MBOBuffer file, uint offset, SubsetPlan plan, 
                                       ushort valueFormat1, ushort valueFormat2)
        {
            ushort pairValueCount = file.GetUshort(offset);
            uint valueRecordSize1 = GetValueRecordSize(valueFormat1);
            uint valueRecordSize2 = GetValueRecordSize(valueFormat2);
            uint pairValueRecordSize = 2 + valueRecordSize1 + valueRecordSize2;
            
            var retainedRecords = new List<byte[]>();
            
            for (int i = 0; i < pairValueCount; i++)
            {
                uint recordOffset = offset + 2 + (uint)i * pairValueRecordSize;
                ushort secondGlyph = file.GetUshort(recordOffset);
                
                // Check if second glyph is retained
                if (plan.TryGetNewGid(secondGlyph, out ushort newSecondGlyph))
                {
                    // Build new record with remapped glyph
                    byte[] record = new byte[pairValueRecordSize];
                    record[0] = (byte)(newSecondGlyph >> 8);
                    record[1] = (byte)newSecondGlyph;
                    
                    // Copy value records
                    for (uint j = 0; j < valueRecordSize1 + valueRecordSize2; j++)
                    {
                        record[2 + j] = file.GetByte(recordOffset + 2 + j);
                    }
                    retainedRecords.Add(record);
                }
            }

            if (retainedRecords.Count == 0)
                return null;

            // Build PairSet
            List<byte> output = new();
            ushort newCount = (ushort)retainedRecords.Count;
            output.Add((byte)(newCount >> 8)); output.Add((byte)newCount);
            
            foreach (var rec in retainedRecords)
            {
                output.AddRange(rec);
            }

            return output.ToArray();
        }

        private static uint GetValueRecordSize(ushort valueFormat)
        {
            uint size = 0;
            if ((valueFormat & 0x0001) != 0) size += 2;
            if ((valueFormat & 0x0002) != 0) size += 2;
            if ((valueFormat & 0x0004) != 0) size += 2;
            if ((valueFormat & 0x0008) != 0) size += 2;
            if ((valueFormat & 0x0010) != 0) size += 2;
            if ((valueFormat & 0x0020) != 0) size += 2;
            if ((valueFormat & 0x0040) != 0) size += 2;
            if ((valueFormat & 0x0080) != 0) size += 2;
            return size;
        }
    }
}
