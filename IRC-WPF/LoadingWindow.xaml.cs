using System;
using System.Windows;
using System.Windows.Threading;

namespace IRC_WPF
{
    public partial class LoadingWindow : Window
    {
        private readonly string[] tips = new[]
        {
            "Did you know? IRC was created in 1988 by Jarkko Oikarinen.",
            "Tip: Use /join to enter a channel",
            "Tip: Use /msg to send private messages",
            "Tip: Use /me to perform an action",
            "Tip: Double-click on a username to start a private chat",
            "Fun fact: IRC played a crucial role in the early days of the internet",
            "Tip: Use tab completion to quickly type usernames",
            "Tip: Most IRC clients support emoji 😊",
            "Tip: You can join multiple channels at once"
        };

        private readonly DispatcherTimer tipTimer;

        public LoadingWindow()
        {
            InitializeComponent();

            tipTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            tipTimer.Tick += TipTimer_Tick;
            tipTimer.Start();

            ShowRandomTip();
        }

        private void TipTimer_Tick(object sender, EventArgs e)
        {
            ShowRandomTip();
        }

        private void ShowRandomTip()
        {
            Random random = new Random();
            TipText.Text = tips[random.Next(tips.Length)];
        }
    }
}