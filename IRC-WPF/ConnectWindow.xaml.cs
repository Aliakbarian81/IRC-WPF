using System;
using System.Windows;

namespace IRC_WPF
{
    public partial class ConnectWindow : Window
    {
        public string ServerAddress => ServerAddressInput.Text;
        public int Port => int.Parse(PortInput.Text);
        public string Nickname => NicknameInput.Text;
        public string Username => UsernameInput.Text;
        public string Password => PasswordInput.Password;
        public bool UseSSLConnection => UseSSL.IsChecked ?? false;


        public ConnectWindow()
        {
            InitializeComponent();
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ServerAddress) ||
            string.IsNullOrWhiteSpace(Nickname) ||
            !int.TryParse(PortInput.Text, out _))
            {
                MessageBox.Show("Please fill in all required fields correctly.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
