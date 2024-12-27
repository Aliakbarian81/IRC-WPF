using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Windows.Input;
using System.Text.Json;


namespace IRC_WPF
{
    public partial class MainWindow : Window
    {
        private StreamWriter writer;
        private StreamReader reader;
        private HashSet<string> users = new HashSet<string>();
        private HashSet<string> channels = new HashSet<string>();
        private Dictionary<string, HashSet<string>> channelUsers = new Dictionary<string, HashSet<string>>();
        private string currentTab = "General Chat";
        private string currentChannel = "#default";
        private bool _channelsLoaded = false;
        private LoadingWindow loadingWindow;
        private bool isDarkTheme = true;
        private HashSet<string> closedTabs = new HashSet<string>();
        private string currentNickname;






        public MainWindow()
        {
            InitializeComponent();
            loadingWindow = new LoadingWindow();

        }

        // حلقه نامحدود برای دریافت پاسخ از سرور
        private async Task ListenForMessages()
        {
            bool listRequested = false;
            bool usersRequested = false;

            while (true)
            {
                string response = await reader.ReadLineAsync();
                if (response != null)
                {
                    if (response.Contains("Welcome"))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            loadingWindow.Close();
                        });
                    }
                    if (!listRequested && response.Contains("Welcome"))
                    {
                        await writer.WriteLineAsync("LIST");
                        await writer.WriteLineAsync("WHO *");
                        listRequested = true;
                        usersRequested = true;

                    }

                    if (response.Contains("NOTICE"))
                    {
                        string message = response.Substring(response.IndexOf(':', 1) + 1);
                        string sender = GetUserFromResponse(response);

                        Dispatcher.Invoke(() =>
                        {
                            ChatBox.AppendText($"NOTICE from {sender}: {message}\n");
                        });
                    }

                    if (response.Contains("PRIVMSG"))
                    {
                        string sender = GetUserFromResponse(response);
                        string target = response.Split(' ')[2];
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
                    else if (response.StartsWith(":"))
                    {
                        if (response.Contains(" 322 "))
                        {
                            string[] parts = response.Split(' ');
                            if (parts.Length > 4)
                            {
                                string channelName = parts[3];
                                AddChannel(channelName);
                            }
                        }
                        else if (response.Contains("323"))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                ChatBox.AppendText("Channel list loaded.\n");
                                _channelsLoaded = true;
                            });
                        }
                        if (response.Contains(" 352 "))
                        {
                            string[] parts = response.Split(' ');
                            if (parts.Length >= 8)
                            {
                                string username = parts[7];
                                AddUser(username);
                            }
                        }
                        else if (response.Contains("353"))
                        {
                            string[] parts = response.Split(' ');
                            if (parts.Length >= 6)
                            {
                                string channel = parts[4];
                                string usersPart = response.Substring(response.LastIndexOf(':') + 1).Trim();
                                string[] channelUsersList = usersPart.Split(' ');

                                if (!channelUsers.ContainsKey(channel))
                                {
                                    channelUsers[channel] = new HashSet<string>();
                                }

                                foreach (string user in channelUsersList)
                                {
                                    string cleanUser = user.TrimStart('@', '+', '~', '&', '%');
                                    channelUsers[channel].Add(cleanUser);
                                }

                                // Update the users list if we're currently on this channel's tab
                                if (currentTab == channel)
                                {
                                    UpdateUsersListForCurrentTab();
                                }
                            }
                        }
                        else if (response.Contains("JOIN"))
                        {
                            string user = GetUserFromResponse(response);
                            string channel = response.Split(new[] { ' ' }, 3)[2].TrimStart(':');

                            // Only create a tab if the current user joined the channel
                            string currentNick = GetCurrentNickname(); // You'll need to track this when connecting
                            if (user == currentNickname && !response.Contains("AUTO"))
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    // Check if we already have this tab
                                    if (!ChatTabs.Items.Cast<TabItem>().Any(tab => tab.Header.ToString() == channel))
                                    {
                                        CreateChatTab(channel);
                                    }
                                });
                            }

                            // Add user to channel's user list
                            Dispatcher.Invoke(() =>
                            {
                                if (!channelUsers.ContainsKey(channel))
                                {
                                    channelUsers[channel] = new HashSet<string>();
                                }
                                channelUsers[channel].Add(user);

                                if (currentTab == channel)
                                {
                                    if (!UsersList.Items.Contains(user))
                                    {
                                        UsersList.Items.Add(user);
                                        SortListBox(UsersList);
                                    }
                                }

                                AppendMessageToTab(channel, $"{user} has joined the channel");
                            });
                        }
                        else if (response.Contains("PART") || response.Contains("QUIT"))
                        {
                            string user = GetUserFromResponse(response);
                            string channel = "";

                            if (response.Contains("PART"))
                            {
                                channel = response.Split(new[] { ' ' }, 3)[2].TrimStart(':');
                            }

                            Dispatcher.Invoke(() =>
                            {
                                if (response.Contains("PART"))
                                {
                                    // Remove user from specific channel
                                    if (channelUsers.ContainsKey(channel))
                                    {
                                        channelUsers[channel].Remove(user);
                                        if (currentTab == channel)
                                        {
                                            UsersList.Items.Remove(user);
                                        }
                                    }
                                    AppendMessageToTab(channel, $"{user} has left the channel");
                                }
                                else // QUIT
                                {
                                    // Remove user from all channels
                                    foreach (var channelUsers in channelUsers.Values)
                                    {
                                        channelUsers.Remove(user);
                                    }
                                    users.Remove(user);
                                    UsersList.Items.Remove(user);

                                    foreach (TabItem tab in ChatTabs.Items)
                                    {
                                        if (tab.Header.ToString().StartsWith("#"))
                                        {
                                            AppendMessageToTab(tab.Header.ToString(), $"{user} has quit");
                                        }
                                    }
                                }
                            });
                        }
                    }
                    if (response.Contains("PRIVMSG") && response.Contains("TRANSFER_REQUEST"))
                    {
                        try
                        {
                            string sender = GetUserFromResponse(response);
                            int startIndex = response.IndexOf("TRANSFER_REQUEST") + 16;
                            string jsonData = response.Substring(startIndex).Trim();

                            var transferInfo = JsonSerializer.Deserialize<FileTransferInfo>(jsonData);

                            Dispatcher.Invoke(() =>
                            {
                                var result = MessageBox.Show(
                                    $"Accept file '{transferInfo.FileName}' from {sender}?\nSize: {transferInfo.FileSize / 1024} KB",
                                    "File Transfer Request",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Question
                                );

                                if (result == MessageBoxResult.Yes)
                                {
                                    _ = ReceiveFileAsync(transferInfo);
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                ChatBox.AppendText($"Error processing transfer request: {ex.Message}\n");
                            });
                        }
                    }

                }
            }
        }

        private async Task ConnectToServer(string server, int port, string nickname, string username = null, string password = null)
        {
            loadingWindow.Show();
            currentNickname = nickname;

            try
            {
                await Task.Run(async () =>
                {
                    TcpClient client = new TcpClient(server, port);
                    NetworkStream stream = client.GetStream();
                    writer = new StreamWriter(stream) { AutoFlush = true };
                    reader = new StreamReader(stream);

                    if (!string.IsNullOrEmpty(password))
                    {
                        await writer.WriteLineAsync($"PASS {password}");
                    }

                    await writer.WriteLineAsync($"NICK {nickname}");
                    string realName = username ?? nickname;
                    await writer.WriteLineAsync($"USER {realName} 0 * :{realName}");
                });

                // Start listening for messages
                _ = Task.Run(() => ListenForMessages());
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    loadingWindow.Close();
                    MessageBox.Show($"Failed to connect: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private string GetCurrentNickname()
        {
            return currentNickname;
        }



        private void UpdateChannelUsers(string channel, IEnumerable<string> users)
        {
            if (!channelUsers.ContainsKey(channel))
            {
                channelUsers[channel] = new HashSet<string>();
            }

            channelUsers[channel].Clear();
            foreach (var user in users)
            {
                channelUsers[channel].Add(user);
            }

            UpdateUsersListForCurrentTab();
        }

        private void UpdateUsersListForCurrentTab()
        {
            Dispatcher.Invoke(() =>
            {
                UsersList.Items.Clear();

                if (currentTab == "General Chat")
                {
                    // Show server-wide users
                    foreach (var user in users)
                    {
                        UsersList.Items.Add(user);
                    }
                }
                else if (channelUsers.ContainsKey(currentTab))
                {
                    // Show channel-specific users
                    foreach (var user in channelUsers[currentTab])
                    {
                        UsersList.Items.Add(user);
                    }
                }

                SortListBox(UsersList);
            });
        }


        private void AddUser(string username)
        {
            if (!string.IsNullOrEmpty(username))
            {
                Dispatcher.Invoke(() =>
                {
                    if (users.Add(username) && !UsersList.Items.Contains(username))
                    {
                        UsersList.Items.Add(username);
                        SortListBox(UsersList);
                    }
                });
            }
        }

        private void ChatTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ChatTabs.SelectedItem is TabItem selectedTab)
            {
                currentTab = selectedTab.Header.ToString();
                UpdateUsersListForCurrentTab();
            }
        }

        // اتصال به سرور
        private async void ConnectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var connectWindow = new ConnectWindow();
            if (connectWindow.ShowDialog() == true)
            {
                // گرفتن مقادیر از فرم
                string serverAddress = connectWindow.ServerAddress;
                int port = connectWindow.Port;
                string nickname = connectWindow.Nickname;
                string username = string.IsNullOrWhiteSpace(connectWindow.Username) ? null : connectWindow.Username;
                string password = string.IsNullOrWhiteSpace(connectWindow.Password) ? null : connectWindow.Password;

                // اتصال به سرور
                await ConnectToServer(serverAddress, port, nickname, username, password);
            }
        }



        private void AppendMessageToTab(string header, string message)
        {
            if (string.IsNullOrWhiteSpace(header)) return;


            TabItem existingTab = ChatTabs.Items.Cast<TabItem>().FirstOrDefault(tab => tab.Header.ToString() == header);

            if (existingTab == null)
            {
                CreateChatTab(header);
                existingTab = ChatTabs.Items.Cast<TabItem>().Last();
            }

            if (existingTab.Content is Grid grid)
            {
                TextBox chatBox = grid.Children.OfType<TextBox>().FirstOrDefault();
                chatBox?.AppendText(message + "\n");
                chatBox?.ScrollToEnd();
            }
        }



        // گرفتن نیک نیم
        private string GetUserFromResponse(string response)
        {
            int exclamationIndex = response.IndexOf('!');
            return exclamationIndex > 0 ? response.Substring(1, exclamationIndex - 1) : string.Empty;
        }

        // دریافت لیست کاربران
        private void AddUsers(string[] usersToAdd)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var user in usersToAdd.Where(u => !string.IsNullOrEmpty(u)))
                {
                    if (users.Add(user) && !UsersList.Items.Contains(user))
                    {

                        UsersList.Items.Add(user);
                    }
                }
                SortListBox(UsersList);
            });
        }


        // حذف کاربران از لیست در صورت لفت دادن از کانال
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



        // دریافت لیست کانال ها
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


        // ساخت تب جدید برای چت
        private void CreateChatTab(string header)
        {
            if (closedTabs.Contains(header))
                return;


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

            Grid messageGrid = new Grid
            {
                Margin = new Thickness(5)
            };
            messageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            messageGrid.ColumnDefinitions.Add(new ColumnDefinition());
            messageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });


            Button fileButton = new Button
            {
                Width = 40,
                Height = 40,
                Padding = new Thickness(5),
                Margin = new Thickness(0, 0, 5, 0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Content = new Image
                {
                    Source = new BitmapImage(new Uri("pack://application:,,,/Resources/attach_file.png")),
                    Width = 24,
                    Height = 24
                }
            };
            fileButton.Click += async (sender, e) =>
            {
                string filePath = SelectFile();
                if (!string.IsNullOrEmpty(filePath))
                {
                    try
                    {
                        await SendFileRequestAsync(filePath, header);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error while sending file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            };

            Grid.SetColumn(fileButton, 0);


            TextBox messageInput = new TextBox
            {
                Name = "MessageInput",
                Height = 50,
                Margin = new Thickness(5, 0, 5, 0)
            };
            messageInput.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    SendMessage(header, messageInput.Text);
                    messageInput.Clear();
                    e.Handled = true;
                }
            };
            Grid.SetColumn(messageInput, 1);


            Button sendButton = new Button
            {
                Content = "Send",
                Width = 75,
                Height = 40,
                Margin = new Thickness(0, 0, 5, 0)
            };
            sendButton.Click += (sender, e) =>
            {
                SendMessage(header, messageInput.Text);
                messageInput.Clear();
            };
            Grid.SetColumn(sendButton, 2);


            messageGrid.Children.Add(fileButton);
            messageGrid.Children.Add(messageInput);
            messageGrid.Children.Add(sendButton);

            chatGrid.Children.Add(chatBox);
            Grid.SetRow(chatBox, 0);

            chatGrid.Children.Add(messageGrid);
            Grid.SetRow(messageGrid, 1);

            TabItem newTab = new TabItem
            {
                Header = header,
                Content = chatGrid,
                Tag = true

            };

            ChatTabs.Items.Add(newTab);
            ChatTabs.SelectedItem = newTab;
        }

        // بستن تب ها
        private async void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TabItem tab)
            {
                if (tab.Tag is bool canClose && canClose)
                {
                    string tabHeader = tab.Header.ToString();

                    // Check if this is a channel tab
                    if (tabHeader.StartsWith("#"))
                    {
                        // Add to closed tabs set
                        closedTabs.Add(tabHeader);

                        // Send PART command before closing the tab
                        await writer.WriteLineAsync($"PART {tabHeader}");

                        // Remove the channel's users from tracking
                        if (channelUsers.ContainsKey(tabHeader))
                        {
                            channelUsers.Remove(tabHeader);
                        }

                        // Remove the tab immediately
                        ChatTabs.Items.Remove(tab);

                        // Select the General Chat tab if it exists
                        var generalTab = ChatTabs.Items.Cast<TabItem>()
                            .FirstOrDefault(t => t.Header.ToString() == "General Chat");
                        if (generalTab != null)
                        {
                            ChatTabs.SelectedItem = generalTab;
                        }
                    }
                    else
                    {
                        ChatTabs.Items.Remove(tab);
                    }
                }
            }
        }


        // منوی چت برای کلیک راست کاربران
        private async void UserChatMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (UsersList.SelectedItem is string selectedUser)
            {
                // Check if tab already exists
                TabItem existingTab = ChatTabs.Items.Cast<TabItem>()
                    .FirstOrDefault(tab => tab.Header.ToString() == selectedUser);

                if (existingTab != null)
                {
                    ChatTabs.SelectedItem = existingTab;
                }
                else
                {
                    CreateChatTab(selectedUser);
                }
            }
        }


        // منوی چت برای کلیک راست کانال ها
        private async void ChannelChatMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ChannelsList.SelectedItem is string selectedChannel)
            {
                // Remove from closedTabs if it exists
                closedTabs.Remove(selectedChannel);

                // Check if tab already exists
                TabItem existingTab = ChatTabs.Items.Cast<TabItem>()
                    .FirstOrDefault(tab => tab.Header.ToString() == selectedChannel);

                if (existingTab != null)
                {
                    ChatTabs.SelectedItem = existingTab;
                }
                else
                {
                    await writer.WriteLineAsync($"JOIN {selectedChannel}");
                    currentChannel = selectedChannel;

                    Dispatcher.Invoke(() =>
                    {
                        ChatBox.AppendText($"Joining channel: {selectedChannel}\n");
                        CreateChatTab(selectedChannel);
                    });
                }
            }
        }



        // مرتب کردن لیست ها بر اساس حروف الفبا
        private void SortListBox(ListBox listBox)
        {
            var items = listBox.Items.Cast<string>().OrderBy(i => i).ToList();
            listBox.Items.Clear();
            foreach (var item in items)
            {
                listBox.Items.Add(item);
            }
        }


        // ارسال پیام
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


        public async Task SendFileRequestAsync(string filePath, string recipient)
        {
            TcpListener listener = null;
            try
            {
                // پیدا کردن آی‌پی لوکال سیستم
                string localIP = GetLocalIPAddress();
                int port = 8001;

                listener = new TcpListener(IPAddress.Parse(localIP), port);
                listener.Start();

                string fileName = Path.GetFileName(filePath);
                long fileSize = new FileInfo(filePath).Length;

                var transferInfo = new FileTransferInfo
                {
                    FileName = fileName,
                    FileSize = fileSize,
                    SenderName = Environment.MachineName,
                    ReceiverName = recipient,
                    SenderIP = localIP,
                    Port = port
                };

                string transferRequest = $"TRANSFER_REQUEST {JsonSerializer.Serialize(transferInfo)}";
                await writer.WriteLineAsync($"PRIVMSG {recipient} :{transferRequest}");

                Dispatcher.Invoke(() =>
                {
                    AppendMessageToTab(recipient, $"Waiting for {recipient} to accept file transfer...");
                });

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                using var client = await listener.AcceptTcpClientAsync().WaitAsync(cts.Token);
                using var stream = client.GetStream();
                using var fileStream = File.OpenRead(filePath);

                byte[] buffer = new byte[8192];
                int bytesRead;
                long totalBytesSent = 0;
                var sw = Stopwatch.StartNew();

                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await stream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesSent += bytesRead;

                    if (sw.ElapsedMilliseconds > 500)
                    {
                        double progress = (double)totalBytesSent / fileSize * 100;
                        Dispatcher.Invoke(() =>
                        {
                            AppendMessageToTab(recipient, $"Sending: {progress:F1}% complete");
                        });
                        sw.Restart();
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    AppendMessageToTab(recipient, $"File {fileName} sent successfully!");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    AppendMessageToTab(recipient, $"Error sending file: {ex.Message}");
                    MessageBox.Show($"Error sending file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                listener?.Stop();
            }
        }



        // ارسال فایل
        public async Task SendFileRequestAsync2(string filePath, string recipient)
        {
            TcpListener listener = null;
            try
            {
                // Try to find an available port
                int port = 8001;
                const int maxPortAttempts = 10;

                for (int i = 0; i < maxPortAttempts; i++)
                {
                    try
                    {
                        AddFirewallRule(port);
                        listener = new TcpListener(IPAddress.Any, port);
                        listener.Start();
                        break;
                    }
                    catch (SocketException)
                    {
                        port++;
                        if (i == maxPortAttempts - 1)
                            throw new Exception("Unable to find available port");
                    }
                }

                string fileName = Path.GetFileName(filePath);
                long fileSize = new FileInfo(filePath).Length;

                // Get the correct IP address
                string localIP = GetLocalIPAddress();
                long ipAsLong = IPToInteger(localIP);

                string dccMessage = $"\u0001DCC SEND {fileName} {ipAsLong} {port} {fileSize}\u0001";
                await writer.WriteLineAsync($"PRIVMSG {recipient} :{dccMessage}");

                Dispatcher.Invoke(() =>
                {
                    AppendMessageToTab(recipient, $"Waiting for {recipient} to accept file transfer...");
                });

                using var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromMinutes(1));

                using var client = await listener.AcceptTcpClientAsync().WaitAsync(cts.Token);
                client.SendTimeout = 30000;
                client.ReceiveTimeout = 30000;

                using var stream = client.GetStream();
                using var fileStream = File.OpenRead(filePath);

                byte[] buffer = new byte[8192];
                int bytesRead;
                long totalBytesSent = 0;

                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await stream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesSent += bytesRead;

                    if (totalBytesSent % (fileSize / 10) == 0)
                    {
                        double progress = (double)totalBytesSent / fileSize * 100;
                        Dispatcher.Invoke(() =>
                        {
                            AppendMessageToTab(recipient, $"Sending: {progress:F1}% complete");
                        });
                    }
                }

                await stream.FlushAsync();

                Dispatcher.Invoke(() =>
                {
                    AppendMessageToTab(recipient, $"File {fileName} sent successfully!");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    AppendMessageToTab(recipient, $"Error sending file: {ex.Message}");
                    MessageBox.Show($"Error sending file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                listener?.Stop();
            }
        }


        public async Task ReceiveFileAsync(FileTransferInfo transferInfo)
        {
            TcpClient client = null;
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = transferInfo.FileName,
                    DefaultExt = Path.GetExtension(transferInfo.FileName),
                    Filter = "All files (*.*)|*.*"
                };

                bool? dialogResult = Dispatcher.Invoke(() => saveDialog.ShowDialog());
                if (dialogResult != true) return;

                string savePath = saveDialog.FileName;

                client = new TcpClient();
                await client.ConnectAsync(transferInfo.SenderIP, transferInfo.Port);

                using var stream = client.GetStream();
                using var fileStream = File.Create(savePath);

                byte[] buffer = new byte[8192];
                int bytesRead;
                long totalBytesRead = 0;
                var sw = Stopwatch.StartNew();

                while (totalBytesRead < transferInfo.FileSize)
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    if (sw.ElapsedMilliseconds > 500)
                    {
                        double progress = (double)totalBytesRead / transferInfo.FileSize * 100;
                        Dispatcher.Invoke(() =>
                        {
                            ChatBox.AppendText($"Receiving {Path.GetFileName(savePath)}: {progress:F1}% complete\n");
                        });
                        sw.Restart();
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    ChatBox.AppendText($"File saved successfully to: {savePath}\n");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ChatBox.AppendText($"Error receiving file: {ex.Message}\n");
                    MessageBox.Show($"Error receiving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                client?.Close();
                client?.Dispose();
            }
        }


        // دریافت فایل
        public async Task ReceiveFileAsync2(string fileName, string ipAddress, int port, long fileSize)
        {
            TcpClient client = null;
            try
            {
                string savePath;
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = fileName,
                    DefaultExt = Path.GetExtension(fileName),
                    Filter = "All files (*.*)|*.*"
                };

                bool? dialogResult = Dispatcher.Invoke(() => saveDialog.ShowDialog());
                if (dialogResult != true) return;

                savePath = saveDialog.FileName;

                client = new TcpClient();
                client.SendTimeout = 30000;
                client.ReceiveTimeout = 30000;

                using var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromSeconds(30));

                await client.ConnectAsync(ipAddress, port).WaitAsync(cts.Token);

                using var stream = client.GetStream();
                using var fileStream = File.Create(savePath);

                byte[] buffer = new byte[8192];
                int bytesRead;
                long totalBytesRead = 0;
                DateTime lastRead = DateTime.Now;

                while (totalBytesRead < fileSize)
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;
                    lastRead = DateTime.Now;

                    if (totalBytesRead % (fileSize / 10) == 0)
                    {
                        double progress = (double)totalBytesRead / fileSize * 100;
                        Dispatcher.Invoke(() =>
                        {
                            ChatBox.AppendText($"Receiving {Path.GetFileName(savePath)}: {progress:F1}% complete\n");
                        });
                    }

                    if (DateTime.Now - lastRead > TimeSpan.FromSeconds(30))
                    {
                        throw new TimeoutException("No data received for 30 seconds.");
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    ChatBox.AppendText($"File saved successfully to: {savePath}\n");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ChatBox.AppendText($"Error receiving file: {ex.Message}\n");
                    MessageBox.Show($"Error receiving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                client?.Close();
                client?.Dispose();
            }
        }


        // انتخاب فایل برای ارسال
        private string SelectFile()
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                return openFileDialog.FileName;
            }
            return string.Empty;
        }


        // بستن برنامه
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }


        // درباره ما
        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("IRC Chat Application\nVersion 1.0", "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }



        // تبدیل آی پی به عدد
        private long IPToInteger(string ipAddress)
        {
            var parts = ipAddress.Split('.');
            long result = 0;
            for (int i = 0; i < 4; i++)
            {
                result = (result << 8) | byte.Parse(parts[i]);
            }
            return result;
        }



        // تبتدیل عدد به آی پی
        private string IntegerToIP(long ipInt)
        {
            return $"{(ipInt >> 24) & 0xFF}.{(ipInt >> 16) & 0xFF}.{(ipInt >> 8) & 0xFF}.{ipInt & 0xFF}";
        }

        // دریافت آدرس گلوبال
        private string GetPublicIPAddress()
        {
            try
            {
                using (var client = new WebClient())
                {
                    return client.DownloadString("https://api.ipify.org").Trim();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Unable to retrieve public IP address.", ex);
            }
        }


        private string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("Local IP Address not found!");
        }


        private void AddFirewallRule(int port)
        {
            try
            {
                Process process = new Process();
                process.StartInfo.FileName = "netsh";
                process.StartInfo.Arguments = $"advfirewall firewall add rule name=\"IRC DCC Port {port}\" dir=in action=allow protocol=TCP localport={port}";
                process.StartInfo.Verb = "runas";
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.UseShellExecute = false;
                process.Start();
                process.WaitForExit();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to add firewall rule: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }


        private async void DisconnectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (writer != null)
            {
                try
                {
                    await writer.WriteLineAsync("QUIT :Disconnecting");
                    writer.Close();
                    reader.Close();

                    // Clear all lists and dictionaries
                    users.Clear();
                    channels.Clear();
                    channelUsers.Clear();

                    Dispatcher.Invoke(() =>
                    {
                        UsersList.Items.Clear();
                        ChannelsList.Items.Clear();
                        ChatBox.Clear();

                        // Remove all tabs except General Chat
                        var generalTab = ChatTabs.Items.Cast<TabItem>()
                            .FirstOrDefault(t => t.Header.ToString() == "General Chat");

                        ChatTabs.Items.Clear();
                        if (generalTab != null)
                            ChatTabs.Items.Add(generalTab);
                    });

                    MessageBox.Show("Disconnected from server.", "Disconnected", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error while disconnecting: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


        public async Task SendNotice(string target, string message)
        {
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(message))
                return;

            await writer.WriteLineAsync($"NOTICE {target} :{message}");
            Dispatcher.Invoke(() =>
            {
                // Display the notice in the appropriate chat tab
                AppendMessageToTab(target, $"-> {target} NOTICE: {message}");
            });
        }

        // Add this button click handler to the context menu for users
        private async void SendNoticeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (UsersList.SelectedItem is string selectedUser)
            {
                var noticeWindow = new InputDialog("Send Notice", "Enter your notice message:");
                if (noticeWindow.ShowDialog() == true)
                {
                    await SendNotice(selectedUser, noticeWindow.ResponseText);
                }
            }
        }

        private async void SendChannelNoticeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ChannelsList.SelectedItem is string selectedChannel)
            {
                var noticeWindow = new InputDialog("Send Channel Notice", "Enter your notice message:");
                if (noticeWindow.ShowDialog() == true)
                {
                    await SendChannelNotice(selectedChannel, noticeWindow.ResponseText);
                }
            }
        }

        public async Task SendChannelNotice(string channel, string message)
        {
            if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(message))
                return;

            await writer.WriteLineAsync($"NOTICE {channel} :{message}");
            Dispatcher.Invoke(() =>
            {
                AppendMessageToTab(channel, $"-> {channel} NOTICE: {message}");
            });
        }


        private void InitializeTheme()
        {
            ApplyTheme(isDarkTheme);
        }

        private void ApplyTheme(bool isDark)
        {
            var app = Application.Current;
            var resources = app.Resources;

            var theme = isDark ? new Dictionary<string, string>
            {
                ["WindowBackground"] = "#1E1E1E",
                ["ControlBackground"] = "#2D2D2D",
                ["TextColor"] = "#E0E0E0",
                ["BorderColor"] = "#3F3F3F",
                ["AccentColor"] = "#007ACC",
                ["MenuBackground"] = "#252526",
                ["MenuItemBackground"] = "#2D2D2D",
                ["TabBackground"] = "#252526",
                ["TabItemBackground"] = "#2D2D2D",
                ["ListBoxBackground"] = "#2D2D2D"
            } : new Dictionary<string, string>
            {
                ["WindowBackground"] = "#F0F0F0",
                ["ControlBackground"] = "#FFFFFF",
                ["TextColor"] = "#000000",
                ["BorderColor"] = "#CCCCCC",
                ["AccentColor"] = "#0078D4",
                ["MenuBackground"] = "#F5F5F5",
                ["MenuItemBackground"] = "#FFFFFF",
                ["TabBackground"] = "#F5F5F5",
                ["TabItemBackground"] = "#FFFFFF",
                ["ListBoxBackground"] = "#FFFFFF"
            };

            // Update all resource brushes
            foreach (var (key, color) in theme)
            {
                resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            }

            // Update Window
            Background = (SolidColorBrush)resources["WindowBackground"];

            // Update Menu
            if (FindName("MainMenu") is Menu mainMenu)
            {
                mainMenu.Background = (SolidColorBrush)resources["MenuBackground"];
                foreach (var menuItem in mainMenu.Items.OfType<MenuItem>())
                {
                    menuItem.Foreground = (SolidColorBrush)resources["TextColor"];
                    foreach (var subMenuItem in menuItem.Items.OfType<MenuItem>())
                    {
                        subMenuItem.Background = (SolidColorBrush)resources["MenuItemBackground"];
                        subMenuItem.Foreground = (SolidColorBrush)resources["TextColor"];
                    }
                }
            }

            // Update all TabControls
            foreach (TabControl tabControl in FindVisualChildren<TabControl>(this))
            {
                tabControl.Background = (SolidColorBrush)resources["TabBackground"];
                foreach (TabItem tab in tabControl.Items)
                {
                    UpdateTabItem(tab, resources);
                }
            }

            // Update all ListBoxes
            foreach (ListBox listBox in FindVisualChildren<ListBox>(this))
            {
                listBox.Background = (SolidColorBrush)resources["ListBoxBackground"];
                listBox.Foreground = (SolidColorBrush)resources["TextColor"];
                listBox.BorderBrush = (SolidColorBrush)resources["BorderColor"];
            }

            // Update all TextBoxes
            foreach (TextBox textBox in FindVisualChildren<TextBox>(this))
            {
                textBox.Background = (SolidColorBrush)resources["ControlBackground"];
                textBox.Foreground = (SolidColorBrush)resources["TextColor"];
                textBox.BorderBrush = (SolidColorBrush)resources["BorderColor"];
            }

            // Update all Buttons
            foreach (Button button in FindVisualChildren<Button>(this))
            {
                if (button.Background != Brushes.Transparent) // Skip transparent buttons
                {
                    button.Background = (SolidColorBrush)resources["AccentColor"];
                }
                button.Foreground = (SolidColorBrush)resources["TextColor"];
            }
        }

        private void UpdateTabItem(TabItem tab, ResourceDictionary resources)
        {
            if (tab.Content is Grid grid)
            {
                grid.Background = (SolidColorBrush)resources["ControlBackground"];
                foreach (var child in grid.Children)
                {
                    if (child is TextBox textBox)
                    {
                        textBox.Background = (SolidColorBrush)resources["ControlBackground"];
                        textBox.Foreground = (SolidColorBrush)resources["TextColor"];
                        textBox.BorderBrush = (SolidColorBrush)resources["BorderColor"];
                    }
                }
            }
        }

        private IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }


        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            isDarkTheme = !isDarkTheme;
            ApplyTheme(isDarkTheme);
        }

    }
}





public class FileTransferInfo
{
    public string FileName { get; set; }
    public long FileSize { get; set; }
    public string SenderName { get; set; }
    public string ReceiverName { get; set; }
    public string SenderIP { get; set; }
    public int Port { get; set; }
}