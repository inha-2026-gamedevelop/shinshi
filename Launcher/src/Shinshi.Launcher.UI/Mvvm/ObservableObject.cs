// System
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shinshi.Launcher.Mvvm
{
    /// <summary> 바인딩용 변경 알림 기반 클래스 </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        /****************************************
        *              Properties
        ****************************************/

        public event PropertyChangedEventHandler? PropertyChanged;

        /****************************************
        *                Methods
        ****************************************/

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
