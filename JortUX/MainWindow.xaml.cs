using JortPob;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace JortUX
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private CancellationTokenSource JortCts = new();
        public MainWindow()
        {
            InitializeComponent();
            Task.Run(Check, JortCts.Token).ConfigureAwait(false);
            Task.Run(Main.Convert, JortCts.Token).ConfigureAwait(false);
        }

        private void Check()
        {
            while (!JortCts.IsCancellationRequested)
            {
                if (JortPob.Common.Lort.update)
                {
                    Dispatcher.Invoke(ReRender);
                }

                Thread.Yield();
            }
        }

        private void ReRender()
        {
            TextBlock main = (TextBlock)FindName("MainOutput");
            TextBlock debug = (TextBlock)FindName("DebugOutput");
            TextBlock progress = (TextBlock)FindName("ProgressOutput");
            ProgressBar bar = (ProgressBar)FindName("ProgressBar");

            string mainText = "", debugText = "";

            // top-to-bottom order
            foreach (string line in JortPob.Common.Lort.mainOutput)
                mainText += line + "\n";

            foreach (string line in JortPob.Common.Lort.debugOutput)
                debugText += line + "\n";

            main.Text = mainText;
            debug.Text = debugText;
            progress.Text = $"{JortPob.Common.Lort.progressOutput} [ {JortPob.Common.Lort.current} / {JortPob.Common.Lort.total} ]";

            float p = Math.Max(0, Math.Min(1, ((float)JortPob.Common.Lort.current / (float)JortPob.Common.Lort.total))) * 100f;
            if (float.IsNaN(p)) p = 0;
            bar.Value = p;

            JortPob.Common.Lort.update = false;
        }
        
        private void OnClose(object sender, CancelEventArgs e)
        {
            JortCts.Cancel(); // stop Lort
        }
    }
}