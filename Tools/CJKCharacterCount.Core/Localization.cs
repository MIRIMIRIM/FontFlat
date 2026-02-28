using System.Globalization;
using System.Resources;

namespace CJKCharacterCount.Core;

public static class Localization
{
    private static readonly ResourceManager _resourceManager =
        new("CJKCharacterCount.Core.Resources.Strings", typeof(Localization).Assembly);

    public static event Action? CultureChanged;

    public static CultureInfo Culture
    {
        get => CultureInfo.CurrentUICulture;
        set
        {
            if (CultureInfo.CurrentUICulture.Name != value.Name)
            {
                CultureInfo.CurrentUICulture = value;
                CultureChanged?.Invoke();
            }
        }
    }

    public static string Get(string key)
    {
        return _resourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }
}
