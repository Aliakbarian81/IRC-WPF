using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;


namespace IRC_WPF
{
    public partial class MainWindow : Window
    {
        private StreamWriter writer;
        private StreamReader reader;
        private HashSet<string> users = new HashSet<string>();
        private HashSet<string> channels = new HashSet<string>();
        private string currentChannel = "#default";
        private bool _channelsLoaded = false;




        public MainWindow()
        {
            InitializeComponent();
            ConnectToServer();

        }

        private async void ConnectToServer()
        {
            string server = "irc.unibg.org";
            int port = 6667;
            string nickname = "mamatiiiii";

            TcpClient client = new TcpClient(server, port);
            NetworkStream stream = client.GetStream();
            writer = new StreamWriter(stream) { AutoFlush = true };
            reader = new StreamReader(stream);

            await writer.WriteLineAsync($"NICK {nickname}");
            await writer.WriteLineAsync($"USER {nickname} 0 * :{nickname}");

            await writer.WriteLineAsync("LIST");


            _ = Task.Run(() => ListenForMessages());
        }

        private async Task ListenForMessages()
        {
            bool listRequested = false;

            while (true)
            {
                string response = await reader.ReadLineAsync();
                if (response != null)
                {
                    if (!listRequested && response.Contains("Welcome"))
                    {
                        await writer.WriteLineAsync("LIST");
                        listRequested = true;
                    }

                    // فیلتر کردن پیام‌های نامربوط
                    if (response.StartsWith(":") && response.Contains("are supported by this server"))
                    {
                        continue; // پیام‌های غیرمرتبط را نادیده بگیر
                    }

                    if (response.Contains("PRIVMSG"))
                    {
                        string sender = GetUserFromResponse(response);
                        string target = response.Split(' ')[2]; // کانال یا کاربر
                        string message = response.Substring(response.IndexOf(':', 1) + 1);

                        Dispatcher.Invoke(() =>
                        {
                            AppendMessageToTab(target == currentChannel ? currentChannel : sender, $"{sender}: {message}");
                        });
                    }

                    Dispatcher.Invoke(() => ChatBox.AppendText(response + "\n"));

                    if (response.StartsWith("PING"))
                    {
                        string pongResponse = response.Replace("PING", "PONG");
                        await writer.WriteLineAsync(pongResponse);
                    }
                    else if (response.StartsWith(":") && response.Contains(" 322 "))
                    {
                        string[] parts = response.Split(' ');
                        if (parts.Length > 4)
                        {
                            string channelName = parts[3];
                            AddChannel(channelName);
                        }
                    }
                    else if (response.Contains("323")) // End of channel list
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ChatBox.AppendText("Channel list loaded.\n");
                            _channelsLoaded = true; // تنظیم پرچم برای جلوگیری از درخواست مجدد
                        });
                    }
                    else if (response.Contains("353")) // Response for NAMES list
                    {
                        string usersPart = response.Substring(response.LastIndexOf(':') + 1).Trim();
                        string[] usersArray = usersPart.Split(' '); // جدا کردن کاربران
                        AddUsers(usersArray); // اضافه کردن کاربران به لیست
                    }
                    else if (response.Contains("JOIN")) // User joins channel
                    {
                        string user = GetUserFromResponse(response);
                        Dispatcher.Invoke(() =>
                        {
                            TabItem channelTab = ChatTabs.Items.Cast<TabItem>()
                                .FirstOrDefault(tab => tab.Header.ToString() == currentChannel);

                            if (channelTab?.Content is Grid grid)
                            {
                                TextBox chatBox = grid.Children.OfType<TextBox>().FirstOrDefault();
                                chatBox?.AppendText($"{user} joined the channel.\n");
                            }
                        });
                    }
                    else if (response.Contains("PART") || response.Contains("QUIT")) // User leaves channel
                    {
                        string user = GetUserFromResponse(response);
                        RemoveUser(user);
                    }
                    else if (response.StartsWith(":") && response.Contains("are supported by this server"))
                    {
                        continue;
                    }
                    else if (!response.Contains("PRIVMSG") && !response.StartsWith(":"))
                    {
                        continue;
                    }
                }
            }
        }



        private void AppendMessageToTab(string header, string message)
        {
            TabItem existingTab = ChatTabs.Items.Cast<TabItem>().FirstOrDefault(tab => tab.Header.ToString() == header);

            if (existingTab == null)
            {
                CreateChatTab(header);
                existingTab = ChatTabs.Items.Cast<TabItem>().Last();
            }

            if (existingTab.Content is Grid grid)
            {
                TextBox chatBox = grid.Children.OfType<TextBox>().FirstOrDefault();
                if (chatBox != null)
                {
                    chatBox.AppendText(message + "\n");
                    chatBox.ScrollToEnd();
                }
            }
        }


        // extract users nickname
        private string GetUserFromResponse(string response)
        {
            int exclamationIndex = response.IndexOf('!');
            return exclamationIndex > 0 ? response.Substring(1, exclamationIndex - 1) : string.Empty;
        }

        // add users to users list
        private void AddUsers(string[] usersToAdd)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var user in usersToAdd.Where(u => !string.IsNullOrEmpty(u)))
                {
                    if (users.Add(user)) // بررسی اضافه شدن به HashSet
                    {
                        if (!UsersList.Items.Contains(user)) // جلوگیری از افزودن تکراری
                        {
                            UsersList.Items.Add(user);
                        }
                    }
                }
                SortListBox(UsersList); // مرتب کردن لیست کاربران
            });
        }


        // add user to users list when a user join to server
        private void AddUser(string user)
        {
            if (!string.IsNullOrEmpty(user) && users.Add(user)) // بررسی تکراری نبودن کاربر
            {
                Dispatcher.Invoke(() =>
                {
                    if (!UsersList.Items.Contains(user)) // جلوگیری از افزودن تکراری به لیست
                    {
                        UsersList.Items.Add(user);
                    }
                });
            }
        }


        // remove user from users list when a user leave the server
        private void RemoveUser(string user)
        {
            if (!string.IsNullOrEmpty(user))
            {
                Dispatcher.Invoke(() =>
                {
                    if (users.Remove(user))
                    {
                        UsersList.Items.Remove(user);
                    }
                });
            }
        }


        private async void ChannelsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ChannelsList.SelectedItem != null)
            {
                string selectedChannel = ChannelsList.SelectedItem.ToString();
                await writer.WriteLineAsync($"JOIN {selectedChannel}");
                currentChannel = selectedChannel;

                Dispatcher.Invoke(() =>
                {
                    ChatBox.AppendText($"Joining channel: {selectedChannel}\n");
                    CreateChatTab(selectedChannel);
                });

                // درخواست لیست کاربران
                await writer.WriteLineAsync($"NAMES {selectedChannel}");
            }
        }




        private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tabControl && tabControl.SelectedItem is TabItem selectedTab)
            {
                if (selectedTab.Header.ToString() == "Channels" && !_channelsLoaded)
                {
                    ChatBox.AppendText("Channel list already loaded.\n");
                }
            }
        }



        // add chanels to chanels list
        private void AddChannel(string channelName)
        {
            Dispatcher.Invoke(() =>
            {
                if (!channels.Contains(channelName))
                {
                    channels.Add(channelName);
                    ChannelsList.Items.Add(channelName);
                }
            });
        }

        // create new tab for chat message
        private void CreateChatTab(string header)
        {
            Grid chatGrid = new Grid();
            chatGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            chatGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBox chatBox = new TextBox
            {
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(5)
            };
            Grid.SetRow(chatBox, 0);

            DockPanel messagePanel = new DockPanel { Margin = new Thickness(5) };
            TextBox messageInput = new TextBox { Name = "MessageInput", Margin = new Thickness(0, 0, 5, 0) };
            DockPanel.SetDock(messageInput, Dock.Left);

            Button sendButton = new Button { Content = "Send", Width = 75 };
            sendButton.Click += (sender, e) =>
            {
                SendMessage(header, messageInput.Text);
                messageInput.Clear();
            };

            messagePanel.Children.Add(messageInput);
            messagePanel.Children.Add(sendButton);
            Grid.SetRow(messagePanel, 1);

            chatGrid.Children.Add(chatBox);
            chatGrid.Children.Add(messagePanel);

            TabItem newTab = new TabItem
            {
                Header = header,
                Content = chatGrid
            };

            ChatTabs.Items.Add(newTab);
            ChatTabs.SelectedItem = newTab;
        }


        // close tab
        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {

            if (sender is Button button && button.Tag is TabItem tab)
            {
                ChatTabs.Items.Remove(tab);
            }
        }




        private void UserChatMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (UsersList.SelectedItem is string selectedUser)
            {
                CreateChatTab($"{selectedUser}");
            }
        }


        private void ChannelChatMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ChannelsList.SelectedItem is string selectedChannel)
            {
                CreateChatTab($"Channel: {selectedChannel}");
            }
        }

        // sorting chanels and users list
        private void SortListBox(ListBox listBox)
        {
            var items = listBox.Items.Cast<string>().OrderBy(i => i).ToList();
            listBox.Items.Clear();
            foreach (var item in items)
            {
                listBox.Items.Add(item);
            }
        }

        private void SendMessageToTab(string tabHeader, string message, TextBox chatBox)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            // اضافه کردن پیام به چت‌باکس
            chatBox.AppendText($"You: {message}\n");

            // ارسال پیام به سرور
            Task.Run(async () =>
            {
                if (!string.IsNullOrEmpty(currentChannel) && currentChannel == tabHeader)
                {
                    await writer.WriteLineAsync($"PRIVMSG {currentChannel} :{message}");
                    await writer.FlushAsync();
                }
            });
        }


        private async void SendMessage(string target, string message)
        {
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(message))
                return;

            await writer.WriteLineAsync($"PRIVMSG {target} :{message}");
            Dispatcher.Invoke(() =>
            {
                AppendMessageToTab(target, $"You: {message}");
            });
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            string message = MessageInput.Text;
            if (string.IsNullOrEmpty(message)) return;

            string target = currentChannel; // کانال فعلی یا کاربر انتخاب‌شده
            if (ChatTabs.SelectedItem is TabItem selectedTab)
            {
                target = selectedTab.Header.ToString();
            }

            SendMessage(target, message);
            MessageInput.Clear();
        }





    }
}


public class ChatMessage
{
    public string Sender { get; set; }
    public string Receiver { get; set; } // کانال یا کاربر
    public string Message { get; set; }
    public DateTime Timestamp { get; set; }
}
