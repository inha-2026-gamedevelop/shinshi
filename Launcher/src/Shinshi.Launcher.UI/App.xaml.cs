// System
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

// Project
using Shinshi.Launcher.Diagnostics;
using Shinshi.Launcher.ViewModels;

namespace Shinshi.Launcher
{
    /// <summary> 런처 진입점. 한 번에 하나만 뜨고, 처리하지 못한 예외는 로그로 남긴다 </summary>
    public partial class App : Application
    {
        /****************************************
        *                Fields
        ****************************************/

        // Local 접두는 같은 Windows 세션 안에서만 하나로 막는다. 다른 사용자 세션의 런처는 막지 않는다
        private const string SINGLE_INSTANCE_MUTEX = @"Local\Shinshi.Launcher.SingleInstance";

        private Mutex?         _singleInstance;
        private MainViewModel? _viewModel;

        /****************************************
        *                Methods
        ****************************************/

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstance = new Mutex(true, SINGLE_INSTANCE_MUTEX, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                // TODO(민성): 이미 떠 있는 런처 창을 앞으로 가져오기
                _singleInstance.Dispose();
                _singleInstance = null;
                Shutdown();
                return;
            }

            base.OnStartup(e);

            DispatcherUnhandledException               += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException      += OnUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            _viewModel = new MainViewModel();
            MainWindow window = new MainWindow { DataContext = _viewModel };
            MainWindow = window;
            window.Show();
            _viewModel.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _viewModel?.Dispose();
            if (_singleInstance != null)
            {
                _singleInstance.ReleaseMutex();
                _singleInstance.Dispose();
            }

            LauncherLog.Info("Launcher exit.");
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LauncherLog.Error("Unhandled UI exception.", e.Exception);
            e.Handled = true;
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LauncherLog.Error("Unobserved task exception.", e.Exception);
            e.SetObserved();
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LauncherLog.Error("Unhandled exception. terminating=" + e.IsTerminating, e.ExceptionObject as Exception);
        }
    }
}
