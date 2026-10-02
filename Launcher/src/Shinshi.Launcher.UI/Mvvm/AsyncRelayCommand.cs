// System
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Shinshi.Launcher.Mvvm
{
    /// <summary> 비동기 명령. 실행 중에는 다시 실행되지 않고, 예외는 onError로 넘겨 화면에 보인다 </summary>
    public sealed class AsyncRelayCommand : ICommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Func<object?, Task>  _execute;
        private readonly Func<object?, bool>? _canExecute;
        private readonly Action<Exception>    _onError;

        private bool _isRunning;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsRunning => _isRunning;

        public event EventHandler? CanExecuteChanged;

        /****************************************
        *                Methods
        ****************************************/

        public AsyncRelayCommand(Func<object?, Task> execute, Action<Exception> onError, Func<object?, bool>? canExecute = null)
        {
            ArgumentNullException.ThrowIfNull(execute);
            ArgumentNullException.ThrowIfNull(onError);

            _execute    = execute;
            _onError    = onError;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            if (_isRunning)
            {
                return false;
            }

            return (_canExecute == null) || _canExecute(parameter);
        }

        // ICommand.Execute는 void라 여기가 명령 경계다. 예외를 밖으로 내보내면 디스패처까지 올라가 앱이 죽는다
        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            _isRunning = true;
            RaiseCanExecuteChanged();
            try
            {
                await _execute(parameter);
            }
            catch (Exception exception)
            {
                _onError(exception);
            }
            finally
            {
                _isRunning = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged() { CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
