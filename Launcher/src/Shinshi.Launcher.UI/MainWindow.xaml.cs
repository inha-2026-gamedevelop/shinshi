// System
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

// Project
using Shinshi.Launcher.ViewModels;

namespace Shinshi.Launcher
{
    /// <summary> 런처 창. 코드 비하인드는 창 테두리 단추, 메뉴, 비밀번호 입력만 다룬다 </summary>
    public partial class MainWindow : Window
    {
        /****************************************
        *                Fields
        ****************************************/

        private const char   GLYPH_MAXIMIZE = (char)0xE922;
        private const char   GLYPH_RESTORE  = (char)0xE923;
        private const string TIP_MAXIMIZE   = "최대화";
        private const string TIP_RESTORE    = "이전 크기로";

        // Windows 11 DWM 창 모서리 설정. Windows 10에서는 호출이 실패하고 무시된다
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND                   = 2;

        /****************************************
        *                Methods
        ****************************************/

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
            StateChanged      += OnWindowStateChanged;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            IntPtr handle     = new WindowInteropHelper(this).Handle;
            int    preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            bool isMaximized = WindowState == WindowState.Maximized;
            MaximizeButton.Content = (isMaximized ? GLYPH_RESTORE : GLYPH_MAXIMIZE).ToString();
            MaximizeButton.ToolTip = isMaximized ? TIP_RESTORE : TIP_MAXIMIZE;
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e) { SystemCommands.MinimizeWindow(this); }

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                SystemCommands.RestoreWindow(this);
                return;
            }

            SystemCommands.MaximizeWindow(this);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) { SystemCommands.CloseWindow(this); }

        private void OnMoreClick(object sender, RoutedEventArgs e) { OpenMenu(MoreButton); }

        private void OnAccountClick(object sender, RoutedEventArgs e) { OpenMenu(AccountButton); }

        private void OnLoginClick(object sender, RoutedEventArgs e) { SubmitLogin(); }

        private void OnLoginInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            SubmitLogin();
        }

        // PasswordBox.Password는 바인딩할 수 없어 여기서 읽는다. 넘긴 뒤 바로 지워 화면과 메모리에 오래 남기지 않는다
        private void SubmitLogin()
        {
            if (DataContext is not MainViewModel viewModel)
            {
                return;
            }

            if (!viewModel.CanSubmitLogin)
            {
                return;
            }

            string password = PasswordInput.Password;
            PasswordInput.Clear();
            viewModel.Login(password);
        }

        private void OpenMenu(Button owner)
        {
            ContextMenu? menu = owner.ContextMenu;
            if (menu == null)
            {
                return;
            }

            menu.DataContext     = DataContext;
            menu.PlacementTarget = owner;
            menu.IsOpen          = true;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
