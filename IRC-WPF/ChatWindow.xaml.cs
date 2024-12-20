using System.IO;
using System.Windows;

namespace IRC_WPF
{
    public partial class ChatWindow : Window
    {
        private readonly string recipient;
        private readonly StreamWriter writer;

        public ChatWindow(string recipient, StreamWriter writer)
        {
            InitializeComponent();
            this.recipient = recipient;
            this.writer = writer;
            Title = $"Chat with {recipient}";
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            string message = MessageInput.Text;
            if (!string.IsNullOrWhiteSpace(message))
            {
                await writer.WriteLineAsync($"PRIVMSG {recipient} :{message}");
                ChatHistory.AppendText($"You: {message}\n");
                MessageInput.Clear();
            }
        }

        public void AppendMessage(string message)
        {
            ChatHistory.AppendText(message + "\n");
        }
    }
}
