using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace FanqieReader
{
    sealed partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var frame = Window.Current.Content as Frame;
            if (frame == null)
            {
                frame = new Frame();
                Window.Current.Content = frame;
            }
            if (frame.Content == null) frame.Navigate(typeof(MainPage));
            Window.Current.Activate();
        }

        private void OnSuspending(object sender, SuspendingEventArgs args)
        {
            // Settings are committed as the user changes chapters or controls.
            var page = (Window.Current.Content as Frame)?.Content as MainPage;
            page?.CancelPendingLoad();
        }
    }
}
