using System.Runtime.InteropServices;
using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the <c>SING</c> table.
/// </summary>
[OtTableBuilder("SING")]
public sealed partial class SingTableBuilder : ISfntTableSource
{
    private ushort _tableVersionMajor;
    private ushort _tableVersionMinor;
    private ushort _glyphletVersion;
    private short _permissions;
    private ushort _mainGid;
    private ushort _unitsPerEm;
    private short _vertAdvance;
    private short _vertOrigin;

    private readonly byte[] _uniqueName = new byte[28];
    private readonly byte[] _metaMd5 = new byte[16];
    private ReadOnlyMemory<byte> _baseGlyphName;

    public ushort TableVersionMajor
    {
        get => _tableVersionMajor;
        set
        {
            if (value == _tableVersionMajor)
                return;
            _tableVersionMajor = value;
            MarkDirty();
        }
    }

    public ushort TableVersionMinor
    {
        get => _tableVersionMinor;
        set
        {
            if (value == _tableVersionMinor)
                return;
            _tableVersionMinor = value;
            MarkDirty();
        }
    }

    public ushort GlyphletVersion
    {
        get => _glyphletVersion;
        set
        {
            if (value == _glyphletVersion)
                return;
            _glyphletVersion = value;
            MarkDirty();
        }
    }

    public short Permissions
    {
        get => _permissions;
        set
        {
            if (value == _permissions)
                return;
            _permissions = value;
            MarkDirty();
        }
    }

    public ushort MainGid
    {
        get => _mainGid;
        set
        {
            if (value == _mainGid)
                return;
            _mainGid = value;
            MarkDirty();
        }
    }

    public ushort UnitsPerEm
    {
        get => _unitsPerEm;
        set
        {
            if (value == _unitsPerEm)
                return;
            _unitsPerEm = value;
            MarkDirty();
        }
    }

    public short VertAdvance
    {
        get => _vertAdvance;
        set
        {
            if (value == _vertAdvance)
                return;
            _vertAdvance = value;
            MarkDirty();
        }
    }

    public short VertOrigin
    {
        get => _vertOrigin;
        set
        {
            if (value == _vertOrigin)
                return;
            _vertOrigin = value;
            MarkDirty();
        }
    }

    public ReadOnlySpan<byte> UniqueNameBytes => _uniqueName;
    public ReadOnlySpan<byte> MetaMd5Bytes => _metaMd5;

    public ReadOnlyMemory<byte> BaseGlyphNameBytes => _baseGlyphName;

    public void Clear()
    {
        _tableVersionMajor = 0;
        _tableVersionMinor = 0;
        _glyphletVersion = 0;
        _permissions = 0;
        _mainGid = 0;
        _unitsPerEm = 0;
        _vertAdvance = 0;
        _vertOrigin = 0;

        _uniqueName.AsSpan().Clear();
        _metaMd5.AsSpan().Clear();
        _baseGlyphName = ReadOnlyMemory<byte>.Empty;

        MarkDirty();
    }

    public void SetUniqueNameBytes(ReadOnlySpan<byte> value, byte padByte = 0)
    {
        if (value.Length > _uniqueName.Length)
            throw new ArgumentOutOfRangeException(nameof(value), $"Value must be <= {_uniqueName.Length} bytes.");

        _uniqueName.AsSpan().Fill(padByte);
        value.CopyTo(_uniqueName);
        MarkDirty();
    }

    public void SetMetaMd5Bytes(ReadOnlySpan<byte> value)
    {
        if (value.Length != _metaMd5.Length)
            throw new ArgumentOutOfRangeException(nameof(value), $"Value must be exactly {_metaMd5.Length} bytes.");

        value.CopyTo(_metaMd5);
        MarkDirty();
    }

    public void SetBaseGlyphNameBytes(ReadOnlyMemory<byte> value)
    {
        if (value.Length > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "Base glyph name length must fit in a byte.");

        _baseGlyphName = value;
        MarkDirty();
    }

    public void SetBaseGlyphNameString(string ascii)
    {
        if (ascii is null) throw new ArgumentNullException(nameof(ascii));
        SetBaseGlyphNameBytes(Encoding.ASCII.GetBytes(ascii));
    }

    public static bool TryFrom(SingTable sing, out SingTableBuilder builder)
    {
        builder = null!;

        var b = new SingTableBuilder
        {
            TableVersionMajor = sing.TableVersionMajor,
            TableVersionMinor = sing.TableVersionMinor,
            GlyphletVersion = sing.GlyphletVersion,
            Permissions = sing.Permissions,
            MainGid = sing.MainGid,
            UnitsPerEm = sing.UnitsPerEm,
            VertAdvance = sing.VertAdvance,
            VertOrigin = sing.VertOrigin
        };

        sing.UniqueNameBytes.CopyTo(b._uniqueName);
        sing.MetaMd5Bytes.CopyTo(b._metaMd5);

        if (sing.TryGetBaseGlyphNameBytes(out var baseName))
            b._baseGlyphName = baseName.ToArray();

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        int baseLen = _baseGlyphName.Length;
        if (baseLen > byte.MaxValue)
            throw new InvalidOperationException("SING baseGlyphName length must fit in a byte.");

        int length = checked(61 + baseLen);
        byte[] table = new byte[length];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, TableVersionMajor);
        BigEndian.WriteUInt16(span, 2, TableVersionMinor);
        BigEndian.WriteUInt16(span, 4, GlyphletVersion);
        BigEndian.WriteInt16(span, 6, Permissions);
        BigEndian.WriteUInt16(span, 8, MainGid);
        BigEndian.WriteUInt16(span, 10, UnitsPerEm);
        BigEndian.WriteInt16(span, 12, VertAdvance);
        BigEndian.WriteInt16(span, 14, VertOrigin);

        _uniqueName.AsSpan().CopyTo(span.Slice(16, 28));
        _metaMd5.AsSpan().CopyTo(span.Slice(44, 16));

        span[60] = checked((byte)baseLen);
        if (baseLen != 0)
            _baseGlyphName.Span.CopyTo(span.Slice(61, baseLen));

        return table;
    }
}

