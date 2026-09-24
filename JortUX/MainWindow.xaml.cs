using JortPob;
using JortPob.Logging;
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
        public Thread job, log;
        public bool running;
        public MainWindow()
        {
            InitializeComponent();

            ((TextBlock)FindName("MainOutput")).Text = "";
            ((TextBlock)FindName("DebugOutput")).Text = "";

            running = true;
            job = new(Run);
            log = new(Check);


            job.Start();
            log.Start();
        }

        public void Check()
        {
            while (running)
            {
                if (Lort.update)
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        ReRender();
                    });
                }
                Thread.Yield();
            }
        }

        public void ReRender()
        {
            TextBlock main = (TextBlock)FindName("MainOutput");
            TextBlock debug = (TextBlock)FindName("DebugOutput");
            TextBlock progress = (TextBlock)FindName("ProgressOutput");
            ProgressBar bar = (ProgressBar)FindName("ProgressBar");

            string mainText = main.Text, debugText = debug.Text;

            // top-to-bottom order, so unfortunately stringbuilder doesn't work for us here
            while (Lort.MainScreenOutput.LogLines.TryDequeue(out var mainLine))
                mainText = $"{mainLine}\n{mainText}";

            while (Lort.ScreenOutput.LogLines.TryDequeue(out var line))
                debugText = $"{line}\n{debugText}";

            main.Text = mainText;
            debug.Text = debugText;
            progress.Text = $"{Lort.progressOutput} [ {Lort.current} / {Lort.total} ]";

            float p = Math.Max(0, Math.Min(1, ((float)Lort.current / (float)Lort.total))) * 100f;
            if (float.IsNaN(p)) p = 0;
            bar.Value = p;

            Lort.update = false;
        }

        public void Run()
        {
            Main.Convert();
            running = false;
        }

        private void OnClose(object sender, CancelEventArgs e)
        {
            if (job.IsAlive)
            {
                e.Cancel = true; // Prevent closing window. The job thread can't be stopped easily so guh. Use debug terminate to kill program.
            }
        }
    }
}