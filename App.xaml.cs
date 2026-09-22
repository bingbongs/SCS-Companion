using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;

namespace SCSCompanion
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private static readonly SizeInt32 FixedWindowSize = new(458, 660);
        private Window? window;
        private Mutex? instanceMutex;
        private bool restoringFixedSize;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            UnhandledException += (_, args) =>
            {
                try
                {
                    var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCSCompanion");
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(Path.Combine(directory, "last-crash.txt"), args.Exception.ToString());
                }
                catch { }
            };
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            instanceMutex = new Mutex(true, "Local\\SCSCompanion", out var firstInstance);
            if (!firstInstance)
            {
                instanceMutex.Dispose();
                instanceMutex = null;
                Exit();
                return;
            }
            window ??= new Window();

            if (window.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                window.Content = rootFrame;
            }

            _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);

            window.Title = "SCS Companion";
            window.SystemBackdrop = new MicaBackdrop();
            window.ExtendsContentIntoTitleBar = true;
            if (rootFrame.Content is MainPage mainPage)
            {
                window.SetTitleBar(mainPage.TitleBarDragRegion);
            }
            window.Closed += (_, _) =>
            {
                (rootFrame.Content as MainPage)?.Shutdown();
                instanceMutex?.Dispose();
                instanceMutex = null;
            };
            window.Activate();

            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(FixedWindowSize);
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var maximumX = workArea.X + Math.Max(0, workArea.Width - FixedWindowSize.Width);
            var clampedX = Math.Clamp(appWindow.Position.X, workArea.X, maximumX);
            appWindow.Move(new PointInt32(clampedX, workArea.Y + 6));
            appWindow.Changed += OnAppWindowChanged;
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
            }
            var titleBar = appWindow.TitleBar;
            titleBar.BackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(36, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(54, 255, 83, 92);
        }

        private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (!args.DidSizeChange || restoringFixedSize || sender.Size == FixedWindowSize)
            {
                return;
            }

            restoringFixedSize = true;
            sender.Resize(FixedWindowSize);
            restoringFixedSize = false;
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
