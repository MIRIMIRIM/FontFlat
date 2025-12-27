using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace OTFontFile.Subsetting;

/// <summary>
/// CFF CharString Type 2 interpreter for de-subroutinization.
/// Expands all callsubr/callgsubr calls inline.
/// </summary>
public class CFFCharStringInterpreter
{
    private readonly byte[] _globalSubrs;
    private readonly byte[] _localSubrs;
    private readonly int _globalSubrBias;
    private readonly int _localSubrBias;
    private readonly List<byte> _output = new();
    private readonly Stack<double> _stack = new();
    private readonly HashSet<int> _visitedGlobalSubrs = new();
    private readonly HashSet<int> _visitedLocalSubrs = new();
    
    // Type 2 CharString opcodes
    private const byte OpHstem = 1;
    private const byte OpVstem = 3;
    private const byte OpVmoveto = 4;
    private const byte OpRlineto = 5;
    private const byte OpHlineto = 6;
    private const byte OpVlineto = 7;
    private const byte OpRrcurveto = 8;
    private const byte OpCallsubr = 10;
    private const byte OpReturn = 11;
    private const byte OpEndchar = 14;
    private const byte OpHstemhm = 18;
    private const byte OpHintmask = 19;
    private const byte OpCntrmask = 20;
    private const byte OpRmoveto = 21;
    private const byte OpHmoveto = 22;
    private const byte OpVstemhm = 23;
    private const byte OpRcurveline = 24;
    private const byte OpRlinecurve = 25;
    private const byte OpVvcurveto = 26;
    private const byte OpHhcurveto = 27;
    private const byte OpShortint = 28;
    private const byte OpCallgsubr = 29;
    private const byte OpVhcurveto = 30;
    private const byte OpHvcurveto = 31;
    private const byte OpEscape = 12;

    public CFFCharStringInterpreter(
        byte[]? globalSubrIndex,
        byte[]? localSubrIndex)
    {
        _globalSubrs = globalSubrIndex ?? Array.Empty<byte>();
        _localSubrs = localSubrIndex ?? Array.Empty<byte>();
        
        // Calculate subroutine bias based on count
        _globalSubrBias = CalculateBias(GetSubrCount(_globalSubrs));
        _localSubrBias = CalculateBias(GetSubrCount(_localSubrs));
    }

    private static int CalculateBias(int count)
    {
        if (count < 1240) return 107;
        if (count < 33900) return 1131;
        return 32768;
    }

    private static int GetSubrCount(byte[] indexData)
    {
        if (indexData.Length < 2) return 0;
        return BinaryPrimitives.ReadUInt16BigEndian(indexData.AsSpan(0, 2));
    }

    /// <summary>
    /// De-subroutinize a CharString by inlining all subroutine calls.
    /// </summary>
    public byte[] DesubroutinizeCharString(byte[] charString)
    {
        _output.Clear();
        _stack.Clear();
        _visitedGlobalSubrs.Clear();
        _visitedLocalSubrs.Clear();
        
        ProcessCharString(charString, 0);
        
        return _output.ToArray();
    }

    private void ProcessCharString(byte[] data, int depth)
    {
        if (depth > 10)
        {
            // Prevent infinite recursion
            return;
        }

        int i = 0;
        int hintCount = 0;
        bool hasWidth = false;

        while (i < data.Length)
        {
            byte b0 = data[i];

            // Check for operator
            if (b0 <= 31 || b0 == 255)
            {
                if (b0 == OpCallsubr)
                {
                    // Local subroutine call
                    if (_stack.Count > 0)
                    {
                        int subrNum = (int)_stack.Pop() + _localSubrBias;
                        byte[]? subrData = GetSubroutine(_localSubrs, subrNum);
                        if (subrData != null && !_visitedLocalSubrs.Contains(subrNum))
                        {
                            _visitedLocalSubrs.Add(subrNum);
                            ProcessCharString(subrData, depth + 1);
                            _visitedLocalSubrs.Remove(subrNum);
                        }
                    }
                    i++;
                    continue;
                }
                else if (b0 == OpCallgsubr)
                {
                    // Global subroutine call
                    if (_stack.Count > 0)
                    {
                        int subrNum = (int)_stack.Pop() + _globalSubrBias;
                        byte[]? subrData = GetSubroutine(_globalSubrs, subrNum);
                        if (subrData != null && !_visitedGlobalSubrs.Contains(subrNum))
                        {
                            _visitedGlobalSubrs.Add(subrNum);
                            ProcessCharString(subrData, depth + 1);
                            _visitedGlobalSubrs.Remove(subrNum);
                        }
                    }
                    i++;
                    continue;
                }
                else if (b0 == OpReturn)
                {
                    // Return from subroutine - don't emit, just stop processing this level
                    return;
                }
                else if (b0 == OpEndchar)
                {
                    // End of charstring - emit and stop
                    FlushStack();
                    _output.Add(OpEndchar);
                    return;
                }
                else if (b0 == OpHintmask || b0 == OpCntrmask)
                {
                    // Hint mask - followed by hint bytes
                    FlushStack();
                    _output.Add(b0);
                    i++;
                    
                    // Calculate number of hint mask bytes
                    int hintBytes = (hintCount + 7) / 8;
                    for (int j = 0; j < hintBytes && i < data.Length; j++, i++)
                    {
                        _output.Add(data[i]);
                    }
                    continue;
                }
                else if (b0 == OpHstem || b0 == OpVstem || b0 == OpHstemhm || b0 == OpVstemhm)
                {
                    // Stem hints - count them
                    hintCount += _stack.Count / 2;
                    FlushStack();
                    _output.Add(b0);
                    i++;
                    continue;
                }
                else if (b0 == OpEscape)
                {
                    // Two-byte operator
                    FlushStack();
                    _output.Add(b0);
                    i++;
                    if (i < data.Length)
                    {
                        _output.Add(data[i]);
                        i++;
                    }
                    continue;
                }
                else if (b0 == OpShortint)
                {
                    // 3-byte integer: 28 + 2 bytes
                    if (i + 2 < data.Length)
                    {
                        short value = (short)((data[i + 1] << 8) | data[i + 2]);
                        _stack.Push(value);
                        i += 3;
                    }
                    else
                    {
                        i++;
                    }
                    continue;
                }
                else
                {
                    // Other operators - flush stack and emit
                    FlushStack();
                    _output.Add(b0);
                    i++;
                    continue;
                }
            }
            else if (b0 >= 32 && b0 <= 246)
            {
                // Single-byte integer: value = b0 - 139
                _stack.Push(b0 - 139);
                i++;
            }
            else if (b0 >= 247 && b0 <= 250)
            {
                // Two-byte positive integer
                if (i + 1 < data.Length)
                {
                    int value = (b0 - 247) * 256 + data[i + 1] + 108;
                    _stack.Push(value);
                    i += 2;
                }
                else
                {
                    i++;
                }
            }
            else if (b0 >= 251 && b0 <= 254)
            {
                // Two-byte negative integer
                if (i + 1 < data.Length)
                {
                    int value = -(b0 - 251) * 256 - data[i + 1] - 108;
                    _stack.Push(value);
                    i += 2;
                }
                else
                {
                    i++;
                }
            }
            else if (b0 == 255)
            {
                // 5-byte fixed-point number (16.16)
                if (i + 4 < data.Length)
                {
                    int value = (data[i + 1] << 24) | (data[i + 2] << 16) | 
                                (data[i + 3] << 8) | data[i + 4];
                    _stack.Push(value / 65536.0);
                    i += 5;
                }
                else
                {
                    i++;
                }
            }
            else
            {
                i++;
            }
        }
    }

    private void FlushStack()
    {
        // Emit all values on the stack as encoded numbers
        var values = new List<double>(_stack);
        values.Reverse();
        _stack.Clear();

        foreach (var value in values)
        {
            EncodeNumber(value);
        }
    }

    private void EncodeNumber(double value)
    {
        int intValue = (int)Math.Round(value);
        
        if (intValue == value)
        {
            // Integer encoding
            if (intValue >= -107 && intValue <= 107)
            {
                _output.Add((byte)(intValue + 139));
            }
            else if (intValue >= 108 && intValue <= 1131)
            {
                int v = intValue - 108;
                _output.Add((byte)(247 + (v >> 8)));
                _output.Add((byte)(v & 0xFF));
            }
            else if (intValue >= -1131 && intValue <= -108)
            {
                int v = -intValue - 108;
                _output.Add((byte)(251 + (v >> 8)));
                _output.Add((byte)(v & 0xFF));
            }
            else if (intValue >= -32768 && intValue <= 32767)
            {
                _output.Add(28);
                _output.Add((byte)(intValue >> 8));
                _output.Add((byte)(intValue & 0xFF));
            }
            else
            {
                // Fixed-point for large values
                int fixed16 = (int)(value * 65536);
                _output.Add(255);
                _output.Add((byte)(fixed16 >> 24));
                _output.Add((byte)(fixed16 >> 16));
                _output.Add((byte)(fixed16 >> 8));
                _output.Add((byte)(fixed16 & 0xFF));
            }
        }
        else
        {
            // Fixed-point number
            int fixed16 = (int)(value * 65536);
            _output.Add(255);
            _output.Add((byte)(fixed16 >> 24));
            _output.Add((byte)(fixed16 >> 16));
            _output.Add((byte)(fixed16 >> 8));
            _output.Add((byte)(fixed16 & 0xFF));
        }
    }

    private byte[]? GetSubroutine(byte[] indexData, int subrNum)
    {
        if (indexData.Length < 2) return null;
        
        int count = BinaryPrimitives.ReadUInt16BigEndian(indexData.AsSpan(0, 2));
        if (count == 0 || subrNum < 0 || subrNum >= count) return null;
        
        int offSize = indexData[2];
        if (offSize < 1 || offSize > 4) return null;
        
        int offsetArrayStart = 3;
        int offsetsLength = (count + 1) * offSize;
        
        if (indexData.Length < offsetArrayStart + offsetsLength) return null;

        // Read offsets
        int offset1 = ReadOffset(indexData, offsetArrayStart + subrNum * offSize, offSize);
        int offset2 = ReadOffset(indexData, offsetArrayStart + (subrNum + 1) * offSize, offSize);
        
        int dataStart = offsetArrayStart + offsetsLength;
        int start = dataStart + offset1 - 1;  // CFF offsets are 1-based
        int end = dataStart + offset2 - 1;

        if (start < 0 || end > indexData.Length || start >= end) return null;

        var result = new byte[end - start];
        Array.Copy(indexData, start, result, 0, result.Length);
        return result;
    }

    private static int ReadOffset(byte[] data, int pos, int offSize)
    {
        int result = 0;
        for (int i = 0; i < offSize; i++)
        {
            result = (result << 8) | data[pos + i];
        }
        return result;
    }
}
