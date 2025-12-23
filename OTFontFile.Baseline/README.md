# OTFontFile

A parse and write opentype fonts library.

Base [Font-Validator/OTFontFile](https://github.com/HinTak/Font-Validator/tree/master/OTFontFile) which write by Microsoft and HinTak. I upgrade it to .NET 8 and modify some files.

## Simple usage

```csharp

using OTFontFile;

var file = "Your font";
var otf = new OTFile();

if (otf.open(fontFile))
{
    if (!otf.IsCollection())
    {
        var font = otf.GetFont(0);
        var nameTable = (Table_name)font.GetTable("name")!;
    }
}

```