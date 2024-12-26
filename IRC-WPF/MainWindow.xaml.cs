﻿using System;
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

        // اتصال به سرور
        private async void ConnectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var connectWindow = new ConnectWindow();
            if (connectWindow.ShowDialog() == true)
            {
                string serverAddress = connectWindow.ServerAddress;
                int port = connectWindow.Port;
                string nickname = connectWindow.Nickname;

                // اتصال به سرور با اطلاعات وارد شده
                try
                {
                    TcpClient client = new TcpClient(serverAddress, port);
                    NetworkStream stream = client.GetStream();
                    writer = new StreamWriter(stream) { AutoFlush = true };
                    reader = new StreamReader(stream);

                    await writer.WriteLineAsync($"NICK {nickname}");
                    await writer.WriteLineAsync($"USER {nickname} 0 * :{nickname}");
                    _ = Task.Run(() => ListenForMessages());

                    MessageBox.Show("Connected to server successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to connect: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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
                Content = chatGrid
            };

            ChatTabs.Items.Add(newTab);
            ChatTabs.SelectedItem = newTab;
        }



        // بستن تب ها
        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TabItem tab)
            {
                ChatTabs.Items.Remove(tab);
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
            try
            {
                int port = 8001;
                string fileName = Path.GetFileName(filePath);
                long fileSize = new FileInfo(filePath).Length;

                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();

                string localIP = GetLocalIPAddress();
                long ipAsLong = IPToInteger(localIP);

                string dccMessage = $"\u0001DCC SEND {fileName} {ipAsLong} {port} {fileSize}\u0001";
                await writer.WriteLineAsync($"PRIVMSG {recipient} :{dccMessage}");

                Dispatcher.Invoke(() =>
                {
                    AppendMessageToTab(recipient, $"Waiting for {recipient} to accept file transfer...");
                });

                // تنظیم تایم‌اوت برای پذیرش اتصال
                using var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromMinutes(1));

                TcpClient client;
                try
                {
                    client = await Task.Run(() => listener.AcceptTcpClientAsync(), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Dispatcher.Invoke(() =>
                    {
                        AppendMessageToTab(recipient, "File transfer timed out.");
                    });
                    listener.Stop();
                    return;
                }

                using (client)
                using (NetworkStream stream = client.GetStream())
                using (FileStream fileStream = File.OpenRead(filePath))
                {
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

                    // اطمینان از ارسال تمام داده‌ها
                    await stream.FlushAsync();
                }

                listener.Stop();

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
        }



        // دریافت فایل
        public async Task ReceiveFileAsync(string fileName, string ipAddress, int port, long fileSize)
        {
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

                if (dialogResult != true)
                {
                    return;
                }

                savePath = saveDialog.FileName;

                using var client = new TcpClient();

                // تنظیم تایم‌اوت برای اتصال
                using var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromSeconds(30));

                try
                {
                    await client.ConnectAsync(ipAddress, port).WaitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Dispatcher.Invoke(() =>
                    {
                        ChatBox.AppendText("Connection timed out.\n");
                    });
                    return;
                }

                client.ReceiveTimeout = 30000; // 30 seconds timeout
                client.SendTimeout = 30000;

                using (NetworkStream stream = client.GetStream())
                using (FileStream fileStream = File.Create(savePath))
                {
                    byte[] buffer = new byte[8192];
                    int bytesRead;
                    long totalBytesRead = 0;
                    DateTime lastRead = DateTime.Now;

                    while (totalBytesRead < fileSize)
                    {
                        if (stream.DataAvailable)
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
                        }
                        else
                        {
                            if (DateTime.Now - lastRead > TimeSpan.FromSeconds(30))
                            {
                                throw new TimeoutException("No data received for 30 seconds.");
                            }
                            await Task.Delay(100);
                        }
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



    }
}