using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SysDash.Core.Mvvm;

/// <summary>
/// Minimal observable base class (INotifyPropertyChanged) used by every view model.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertiesChanged(params string[] propertyNames)
    {
        if (PropertyChanged is null)
        {
            return;
        }

        foreach (string name in propertyNames)
        {
            PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }
}