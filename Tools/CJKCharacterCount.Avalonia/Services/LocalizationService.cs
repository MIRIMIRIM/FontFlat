using CJKCharacterCount.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CJKCharacterCount.Avalonia.Services;

public class LocalizationService : ObservableObject
{
    public static LocalizationService Instance { get; } = new();

    private LocalizationService()
    {
        Localization.CultureChanged += () =>
        {
            // Notify that all properties (indexer) have changed
            OnPropertyChanged(string.Empty); // string.Empty or null is valid for all properties 
            OnPropertyChanged("Item[]");
        };
    }

    public string this[string key] => Localization.Get(key);
}
