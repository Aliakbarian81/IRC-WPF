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




        public MainWindow()
        {
            InitializeComponent();
        }

        // حلقه نامحدود برای دریافت پاسخ از سرور
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
                        else if (response.Contains("353"))
                        {
                            string usersPart = response.Substring(response.LastIndexOf(':') + 1).Trim();
                            string[] usersArray = usersPart.Split(' ');
                            AddUsers(usersArray);
                        }
                        else if (response.Contains("JOIN"))
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
                        else if (response.Contains("PART") || response.Contains("QUIT"))
                        {
                            string user = GetUserFromResponse(response);
                            RemoveUser(user);
                        }
                    }
                    if (response.Contains("PRIVMSG") && response.Contains("DCC SEND"))
                    {
                        try
                        {
                            // پردازش پیام DCC
                            string sender = GetUserFromResponse(response);
                            int startIndex = response.IndexOf("DCC SEND") + 9;
                            int endIndex = response.LastIndexOf("\u0001");
                            if (endIndex == -1) endIndex = response.Length;

                            string dccData = response.Substring(startIndex, endIndex - startIndex).Trim();
                            string[] dccParams = dccData.Split(' ');

                            if (dccParams.Length >= 4)
                            {
                                string fileName = dccParams[0];
                                string ipAddressInt = dccParams[1];
                                string port = dccParams[2];
                                string fileSize = dccParams[3].Replace("\u0001", "");

                                string ipAddress = IntegerToIP(long.Parse(ipAddressInt));

                                Dispatcher.Invoke(() =>
                                {
                                    var result = MessageBox.Show(
                                        $"Accept file '{fileName}' from {sender}?\nSize: {long.Parse(fileSize) / 1024} KB",
                                        "File Transfer Request",
                                        MessageBoxButton.YesNo,
                                        MessageBoxImage.Question
                                    );

                                    if (result == MessageBoxResult.Yes)
                                    {
                                        _ = ReceiveFileAsync(fileName, ipAddress, int.Parse(port), long.Parse(fileSize));
                                    }
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                ChatBox.AppendText($"Error processing DCC request: {ex.Message}\n");
                            });
                        }
                    }

                }
            }
        }

        private async Task ConnectToServer(string server, int port, string nickname, string username = null, string password = null)
        {
            try
            {
                // ایجاد اتصال TCP
                TcpClient client = new TcpClient(server, port);
                NetworkStream stream = client.GetStream();
                writer = new StreamWriter(stream) { AutoFlush = true };
                reader = new StreamReader(stream);

                // ارسال رمز عبور اگر وارد شده باشد
                if (!string.IsNullOrEmpty(password))
                {
                    await writer.WriteLineAsync($"PASS {password}");
                }

                // ارسال نیک‌نیم
                await writer.WriteLineAsync($"NICK {nickname}");

                // ارسال دستور USER
                string realName = username ?? nickname; // اگر یوزرنیم وارد نشده باشد، از نیک‌نیم استفاده می‌کنیم
                await writer.WriteLineAsync($"USER {realName} 0 * :{realName}");

                // شروع گوش دادن به پیام‌ها
                _ = Task.Run(() => ListenForMessages());

                MessageBox.Show("Connected to server successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async Task ConnectToServer3(string server, int port, string nickname, string username = null, string password = null)
        {
            try
            {
                TcpClient client = new TcpClient(server, port);
                NetworkStream stream = client.GetStream();
                writer = new StreamWriter(stream) { AutoFlush = true };
                reader = new StreamReader(stream);

                await writer.WriteLineAsync($"NICK {nickname}");
                string realname = username ?? nickname;
                await writer.WriteLineAsync($"USER {nickname} 0 * :{realname}");
                await writer.WriteLineAsync($"USER {nickname} 0 * :{nickname}");
                _ = Task.Run(() => ListenForMessages());

                MessageBox.Show("Connected to server successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }               

        }




        private async Task ConnectToServer2(string server, int port, string nickname, string username = null, string password = null)
        {
            try
            {
                TcpClient client = new TcpClient(server, port);
                NetworkStream stream = client.GetStream();
                writer = new StreamWriter(stream) { AutoFlush = true };
                reader = new StreamReader(stream);

                // اول پسورد رو می‌فرستیم (اگر وجود داشته باشه)
                if (!string.IsNullOrEmpty(password))
                {
                    await writer.WriteLineAsync($"PASS {password}");
                    await Task.Delay(1000); // تاخیر 1 ثانیه‌ای
                }

                // بعد نیک‌نیم رو می‌فرستیم
                await writer.WriteLineAsync($"NICK {nickname}");
                await Task.Delay(1000);

                // و در نهایت اطلاعات یوزر رو با جزئیات بیشتر می‌فرستیم
                //string realname = username ?? nickname;
                //await writer.WriteLineAsync($"USER {nickname} 8 * :{realname}");
                //await Task.Delay(1000);

                _ = Task.Run(() => ListenForMessages());

                MessageBox.Show("Connected to server successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TabItem tab)
            {
                if (tab.Tag is bool canClose && canClose)
                {
                    ChatTabs.Items.Remove(tab);
                }
            }
        }



        // منوی چت برای کلیک راست کاربران
        private void UserChatMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (UsersList.SelectedItem is string selectedUser)
            {
                CreateChatTab($"{selectedUser}");
            }
        }

        // منوی چت برای کلیک راست کانال ها
        private async void ChannelChatMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ChannelsList.SelectedItem is string selectedChannel)
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



        // ارسال فایل
        public async Task SendFileRequestAsync(string filePath, string recipient)
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



        // دریافت فایل
        public async Task ReceiveFileAsync(string fileName, string ipAddress, int port, long fileSize)
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
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
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



    }
}