using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AiraAssistant.UI.ViewModels;

/// <summary>
/// Minimal base class for all view models. Implements <see cref="INotifyPropertyChanged"/>
/// with a hand-rolled <c>SetProperty</c> so we don't take a dependency on
/// CommunityToolkit's source generators (they are available, but explicit
/// code keeps the base class obvious to readers).
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }
}
