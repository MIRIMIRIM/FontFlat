using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace OTFontFile.Subsetting;

/// <summary>
/// CFF table builder for subsetting.
/// Handles INDEX structure construction and CFF rebuilding.
/// </summary>
public class CFFBuilder
{
    private readonly List<byte> _buffer = new();

    /// <summary>
    /// Build a CFF INDEX structure from a list of data items.
    /// </summary>
    public byte[] BuildIndex(IList<byte[]> items)
    {
        _buffer.Clear();

        int count = items.Count;
        
        if (count == 0)
        {
            // Empty INDEX: just count = 0
            _buffer.Add(0);
            _buffer.Add(0);
            return _buffer.ToArray();
        }

        // Calculate total data size and determine offSize
        long dataSize = 0;
        foreach (var item in items)
        {
            dataSize += item.Length;
        }
        
        // offSize must be able to hold the largest offset (dataSize + 1)
        int offSize;
        if (dataSize + 1 <= 0xFF)
            offSize = 1;
        else if (dataSize + 1 <= 0xFFFF)
            offSize = 2;
        else if (dataSize + 1 <= 0xFFFFFF)
            offSize = 3;
        else
            offSize = 4;

        // Write count (2 bytes)
        _buffer.Add((byte)(count >> 8));
        _buffer.Add((byte)(count & 0xFF));

        // Write offSize (1 byte)
        _buffer.Add((byte)offSize);

        // Write offset array (count+1 offsets)
        int offset = 1;  // CFF offsets are 1-based
        for (int i = 0; i <= count; i++)
        {
            WriteOffset(offset, offSize);
            if (i < count)
            {
                offset += items[i].Length;
            }
        }

        // Write data
        foreach (var item in items)
        {
            _buffer.AddRange(item);
        }

        return _buffer.ToArray();
    }

    private void WriteOffset(int offset, int offSize)
    {
        switch (offSize)
        {
            case 1:
                _buffer.Add((byte)offset);
                break;
            case 2:
                _buffer.Add((byte)(offset >> 8));
                _buffer.Add((byte)(offset & 0xFF));
                break;
            case 3:
                _buffer.Add((byte)(offset >> 16));
                _buffer.Add((byte)(offset >> 8));
                _buffer.Add((byte)(offset & 0xFF));
                break;
            case 4:
                _buffer.Add((byte)(offset >> 24));
                _buffer.Add((byte)(offset >> 16));
                _buffer.Add((byte)(offset >> 8));
                _buffer.Add((byte)(offset & 0xFF));
                break;
        }
    }

    /// <summary>
    /// Build a minimal CFF table with subset CharStrings.
    /// </summary>
    public byte[] BuildCFF(
        byte[] originalCFF,
        IReadOnlyList<int> retainedGlyphs,
        Dictionary<int, int> glyphIdMap)
    {
        var result = new List<byte>();

        // Parse original CFF structure
        if (originalCFF.Length < 4) return originalCFF;

        int major = originalCFF[0];
        int minor = originalCFF[1];
        int hdrSize = originalCFF[2];
        int offSize = originalCFF[3];

        // Only support CFF1 for now (major = 1)
        if (major != 1) return originalCFF;

        // Parse INDEX structures
        int pos = hdrSize;

        // Name INDEX
        var (nameIndex, nameEnd) = ParseIndex(originalCFF, pos);
        pos = nameEnd;

        // Top DICT INDEX
        var (topDictIndex, topDictEnd) = ParseIndex(originalCFF, pos);
        pos = topDictEnd;

        // String INDEX
        var (stringIndex, stringEnd) = ParseIndex(originalCFF, pos);
        pos = stringEnd;

        // Global Subr INDEX
        var (globalSubrIndex, globalSubrEnd) = ParseIndex(originalCFF, pos);

        // Parse Top DICT to find CharStrings offset
        if (topDictIndex.Count == 0) return originalCFF;
        var topDict = ParseDict(topDictIndex[0]);

        // Find CharStrings offset (operator 17)
        if (!topDict.TryGetValue(17, out var charStringsOffsetList) || charStringsOffsetList.Count == 0)
            return originalCFF;
        int charStringsOffset = (int)charStringsOffsetList[0];

        // Find Private DICT offset and size (operator 18)
        int privateOffset = 0;
        int privateSize = 0;
        byte[]? localSubrIndexData = null;
        
        if (topDict.TryGetValue(18, out var privateInfo) && privateInfo.Count >= 2)
        {
            privateSize = (int)privateInfo[0];
            privateOffset = (int)privateInfo[1];
            
            // Parse Private DICT to find Local Subr offset
            if (privateOffset > 0 && privateOffset + privateSize <= originalCFF.Length)
            {
                var privateData = new byte[privateSize];
                Array.Copy(originalCFF, privateOffset, privateData, 0, privateSize);
                var privateDict = ParseDict(privateData);
                
                // Local Subr offset (operator 19) is relative to Private DICT
                if (privateDict.TryGetValue(19, out var localSubrOffsetList) && localSubrOffsetList.Count > 0)
                {
                    int localSubrOffset = (int)localSubrOffsetList[0];
                    int absoluteLocalSubrOffset = privateOffset + localSubrOffset;
                    if (absoluteLocalSubrOffset < originalCFF.Length)
                    {
                        var (localSubrIndex, localSubrEnd) = ParseIndex(originalCFF, absoluteLocalSubrOffset);
                        if (localSubrIndex.Count > 0)
                        {
                            // Rebuild local subr index as raw bytes
                            localSubrIndexData = BuildIndex(localSubrIndex);
                        }
                    }
                }
            }
        }

        // Parse CharStrings INDEX
        var (charStringsIndex, charStringsEnd) = ParseIndex(originalCFF, charStringsOffset);

        // Build Global Subr INDEX data for interpreter
        byte[]? globalSubrData = globalSubrIndex.Count > 0 ? BuildIndex(globalSubrIndex) : null;

        // Create CharString interpreter for de-subroutinization
        var interpreter = new CFFCharStringInterpreter(globalSubrData, localSubrIndexData);

        // Subset and de-subroutinize CharStrings
        var subsetCharStrings = new List<byte[]>();
        foreach (var oldGid in retainedGlyphs)
        {
            if (oldGid >= 0 && oldGid < charStringsIndex.Count)
            {
                var originalCharString = charStringsIndex[oldGid];
                // De-subroutinize the charstring
                var desubroutinized = interpreter.DesubroutinizeCharString(originalCharString);
                subsetCharStrings.Add(desubroutinized);
            }
            else
            {
                // Empty charstring for missing glyphs
                subsetCharStrings.Add(new byte[] { 14 }); // endchar
            }
        }

        // === Build new CFF ===

        // 1. Header (4 bytes)
        result.Add(1);       // major
        result.Add(0);       // minor
        result.Add(4);       // hdrSize
        result.Add(1);       // offSize (will be recalculated)

        // 2. Name INDEX (keep first name only)
        var subsetNameIndex = nameIndex.Take(1).ToList();
        result.AddRange(BuildIndex(subsetNameIndex));

        // 3. Top DICT INDEX - will update offsets later
        int topDictPlaceholderPos = result.Count;
        // Create minimal Top DICT with updated offsets
        var newTopDict = BuildMinimalTopDict(topDict, 0, 0, 0);  // Placeholder offsets
        var topDictIndexBytes = BuildIndex(new[] { newTopDict });
        result.AddRange(topDictIndexBytes);

        // 4. String INDEX (simplified - keep minimal)
        var subsetStringIndex = new List<byte[]>();
        // Always include at least one string for CID-keyed fonts
        if (stringIndex.Count > 0)
        {
            subsetStringIndex = stringIndex.Take(Math.Min(stringIndex.Count, 10)).ToList();
        }
        result.AddRange(BuildIndex(subsetStringIndex));

        // 5. Global Subr INDEX (empty - we de-subroutinized)
        result.AddRange(BuildIndex(Array.Empty<byte[]>()));

        // Record CharStrings offset
        int newCharStringsOffset = result.Count;

        // 6. CharStrings INDEX
        result.AddRange(BuildIndex(subsetCharStrings));

        // 7. Private DICT (simplified)
        int newPrivateOffset = result.Count;
        var newPrivateDict = BuildMinimalPrivateDict();
        int newPrivateSize = newPrivateDict.Length;
        result.AddRange(newPrivateDict);

        // Update Top DICT with correct offsets
        newTopDict = BuildMinimalTopDict(topDict, newCharStringsOffset, newPrivateOffset, newPrivateSize);
        var finalTopDictIndex = BuildIndex(new[] { newTopDict });

        // Recalculate positions if Top DICT size changed
        int sizeDiff = finalTopDictIndex.Length - topDictIndexBytes.Length;
        if (sizeDiff != 0)
        {
            // Need to rebuild with adjusted offsets
            result.Clear();
            
            result.Add(1);       // major
            result.Add(0);       // minor
            result.Add(4);       // hdrSize
            result.Add(1);       // offSize

            result.AddRange(BuildIndex(subsetNameIndex));
            result.AddRange(finalTopDictIndex);
            result.AddRange(BuildIndex(subsetStringIndex));
            result.AddRange(BuildIndex(Array.Empty<byte[]>())); // Empty global subr

            newCharStringsOffset = result.Count;
            result.AddRange(BuildIndex(subsetCharStrings));

            newPrivateOffset = result.Count;
            newPrivateDict = BuildMinimalPrivateDict();
            result.AddRange(newPrivateDict);

            // Final Top DICT with correct offsets
            newTopDict = BuildMinimalTopDict(topDict, newCharStringsOffset, newPrivateOffset, newPrivateDict.Length);
            
            // Replace Top DICT in result
            result.Clear();
            result.Add(1);
            result.Add(0);
            result.Add(4);
            result.Add(1);
            result.AddRange(BuildIndex(subsetNameIndex));
            result.AddRange(BuildIndex(new[] { newTopDict }));
            result.AddRange(BuildIndex(subsetStringIndex));
            result.AddRange(BuildIndex(Array.Empty<byte[]>()));
            
            newCharStringsOffset = result.Count;
            result.AddRange(BuildIndex(subsetCharStrings));
            
            newPrivateOffset = result.Count;
            result.AddRange(BuildMinimalPrivateDict());
            
            // One more pass with final offsets
            newTopDict = BuildMinimalTopDict(topDict, newCharStringsOffset, newPrivateOffset, newPrivateDict.Length);
            
            result.Clear();
            result.Add(1);
            result.Add(0);
            result.Add(4);
            result.Add(1);
            result.AddRange(BuildIndex(subsetNameIndex));
            result.AddRange(BuildIndex(new[] { newTopDict }));
            result.AddRange(BuildIndex(subsetStringIndex));
            result.AddRange(BuildIndex(Array.Empty<byte[]>()));
            result.AddRange(BuildIndex(subsetCharStrings));
            result.AddRange(BuildMinimalPrivateDict());
        }

        return result.ToArray();
    }

    private byte[] BuildMinimalTopDict(Dictionary<int, List<double>> originalDict, 
        int charStringsOffset, int privateOffset, int privateSize)
    {
        var dict = new List<byte>();

        // Copy essential operators from original (charset, encoding, etc.)
        // operator 15 = charset
        if (originalDict.TryGetValue(15, out var charsetVal))
        {
            EncodeNumber(dict, charsetVal[0]);
            dict.Add(15);
        }

        // operator 17 = CharStrings (required)
        EncodeNumber(dict, charStringsOffset);
        dict.Add(17);

        // operator 18 = Private (required): size then offset
        EncodeNumber(dict, privateSize);
        EncodeNumber(dict, privateOffset);
        dict.Add(18);

        return dict.ToArray();
    }

    private byte[] BuildMinimalPrivateDict()
    {
        var dict = new List<byte>();

        // BlueValues (empty array) - operator 6
        // defaultWidthX = 0 - operator 20
        EncodeNumber(dict, 0);
        dict.Add(20);

        // nominalWidthX = 0 - operator 21
        EncodeNumber(dict, 0);
        dict.Add(21);

        return dict.ToArray();
    }

    private void EncodeNumber(List<byte> output, double value)
    {
        int intValue = (int)Math.Round(value);

        if (intValue >= -107 && intValue <= 107)
        {
            output.Add((byte)(intValue + 139));
        }
        else if (intValue >= 108 && intValue <= 1131)
        {
            int v = intValue - 108;
            output.Add((byte)(247 + (v >> 8)));
            output.Add((byte)(v & 0xFF));
        }
        else if (intValue >= -1131 && intValue <= -108)
        {
            int v = -intValue - 108;
            output.Add((byte)(251 + (v >> 8)));
            output.Add((byte)(v & 0xFF));
        }
        else if (intValue >= -32768 && intValue <= 32767)
        {
            output.Add(28);
            output.Add((byte)(intValue >> 8));
            output.Add((byte)(intValue & 0xFF));
        }
        else
        {
            // 5-byte number
            output.Add(29);
            output.Add((byte)(intValue >> 24));
            output.Add((byte)(intValue >> 16));
            output.Add((byte)(intValue >> 8));
            output.Add((byte)(intValue & 0xFF));
        }
    }

    private (List<byte[]> items, int endOffset) ParseIndex(byte[] data, int offset)
    {
        var items = new List<byte[]>();
        
        if (offset + 2 > data.Length)
            return (items, offset);

        int count = (data[offset] << 8) | data[offset + 1];
        
        if (count == 0)
            return (items, offset + 2);

        if (offset + 3 > data.Length)
            return (items, offset + 2);

        int offSize = data[offset + 2];
        int offsetArrayStart = offset + 3;
        int offsetsLength = (count + 1) * offSize;

        if (offsetArrayStart + offsetsLength > data.Length)
            return (items, offset + 3);

        // Read all offsets
        var offsets = new int[count + 1];
        for (int i = 0; i <= count; i++)
        {
            offsets[i] = ReadOffset(data, offsetArrayStart + i * offSize, offSize);
        }

        int dataStart = offsetArrayStart + offsetsLength;
        
        // Extract items
        for (int i = 0; i < count; i++)
        {
            int start = dataStart + offsets[i] - 1;
            int end = dataStart + offsets[i + 1] - 1;
            
            if (start >= 0 && end <= data.Length && start < end)
            {
                var item = new byte[end - start];
                Array.Copy(data, start, item, 0, item.Length);
                items.Add(item);
            }
            else
            {
                items.Add(Array.Empty<byte>());
            }
        }

        int endOffset = dataStart + offsets[count] - 1;
        return (items, endOffset);
    }

    private static int ReadOffset(byte[] data, int pos, int offSize)
    {
        int result = 0;
        for (int i = 0; i < offSize && pos + i < data.Length; i++)
        {
            result = (result << 8) | data[pos + i];
        }
        return result;
    }

    private Dictionary<int, List<double>> ParseDict(byte[] data)
    {
        var dict = new Dictionary<int, List<double>>();
        var operandStack = new List<double>();
        int i = 0;

        while (i < data.Length)
        {
            byte b0 = data[i];

            if (b0 <= 21)
            {
                // Operator
                int op = b0;
                if (b0 == 12 && i + 1 < data.Length)
                {
                    // Two-byte operator
                    op = (12 << 8) | data[i + 1];
                    i++;
                }
                
                dict[op] = new List<double>(operandStack);
                operandStack.Clear();
                i++;
            }
            else if (b0 >= 32 && b0 <= 246)
            {
                operandStack.Add(b0 - 139);
                i++;
            }
            else if (b0 >= 247 && b0 <= 250 && i + 1 < data.Length)
            {
                operandStack.Add((b0 - 247) * 256 + data[i + 1] + 108);
                i += 2;
            }
            else if (b0 >= 251 && b0 <= 254 && i + 1 < data.Length)
            {
                operandStack.Add(-(b0 - 251) * 256 - data[i + 1] - 108);
                i += 2;
            }
            else if (b0 == 28 && i + 2 < data.Length)
            {
                short value = (short)((data[i + 1] << 8) | data[i + 2]);
                operandStack.Add(value);
                i += 3;
            }
            else if (b0 == 29 && i + 4 < data.Length)
            {
                int value = (data[i + 1] << 24) | (data[i + 2] << 16) | 
                            (data[i + 3] << 8) | data[i + 4];
                operandStack.Add(value);
                i += 5;
            }
            else if (b0 == 30)
            {
                // Real number - skip for now
                i++;
                while (i < data.Length)
                {
                    byte nibbles = data[i++];
                    if ((nibbles & 0x0F) == 0x0F || (nibbles >> 4) == 0x0F)
                        break;
                }
            }
            else
            {
                i++;
            }
        }

        return dict;
    }
}
