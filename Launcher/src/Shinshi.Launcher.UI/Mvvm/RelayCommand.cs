// System
using System;
using System.Windows.Input;

namespace Shinshi.Launcher.Mvvm
{
    /// <summary> 동기 명령. 실행 가능 여부가 바뀌면 호출자가 RaiseCanExecuteChanged를 부른다 </summary>
    public sealed class RelayCommand : ICommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Action<object?>      _execute;
        private readonly Func<object?, bool>? _canExecute;

        /****************************************
        *              Properties
        ****************************************/

        public event EventHandler? CanExecuteChanged;

        /****************************************
        *                Methods
        ****************************************/

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            ArgumentNullException.ThrowIfNull(execute);

            _execute    = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) { return (_canExecute == null) || _canExecute(parameter); }

        public void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            _execute(parameter);
        }

        public void RaiseCanExecuteChanged() { CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
