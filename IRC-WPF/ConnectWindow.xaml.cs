using System;
using System.Windows;

namespace IRC_WPF
{
    public partial class ConnectWindow : Window
    {
        public string ServerAddress { get; private set; }
        public int Port { get; private set; }
        public string Nickname { get; private set; }

        public ConnectWindow()
        {
            InitializeComponent();
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ServerAddress = ServerAddressInput.Text;
            if (!int.TryParse(PortInput.Text, out int port))
            {
                MessageBox.Show("Invalid port number!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Port = port;
            Nickname = NicknameInput.Text;

            if (string.IsNullOrWhiteSpace(ServerAddress) || string.IsNullOrWhiteSpace(Nickname))
            {
                MessageBox.Show("Please fill all fields.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            DialogResult = true; // بسته شدن با موفقیت
            Close();
        }
    }
}
