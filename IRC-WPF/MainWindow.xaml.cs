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
                    if (response.Contains("DCC SEND"))
                    {
                        try
                        {
                            // حذف کاراکترهای کنترل در ابتدای و انتهای پیام
                            string cleanResponse = response.Trim().Trim('\u0001');

                            // تقسیم بر اساس فاصله
                            string[] parts = cleanResponse.Split(' ');

                            // اطمینان از اینکه پیام حداقل 7 بخش دارد
                            if (parts.Length >= 7)
                            {
                                string sender = GetUserFromResponse(response); // استخراج فرستنده
                                string fileName = parts[5].TrimStart(':'); // استخراج نام فایل
                                string ipAddress = IntegerToIP(Convert.ToInt64(parts[6])); // تبدیل IP
                                int port = int.Parse(parts[7]); // استخراج پورت
                                long fileSize = long.Parse(parts[8]); // استخراج اندازه فایل

                                Dispatcher.Invoke(() =>
                                {
                                    AppendMessageToTab(sender, $"Incoming file: {fileName} ({fileSize / 1024} KB) from {sender}");

                                    // نمایش دیالوگ برای تایید دریافت فایل
                                    if (MessageBox.Show($"Do you want to accept the file '{fileName}' from {sender}?",
                                                        "File Transfer Request", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                                    {
                                        _ = ReceiveFile(fileName, ipAddress, port, fileSize, sender);
                                    }
                                });
                            }
                            else
                            {
                                AppendMessageToTab("System", "Invalid DCC SEND format.");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendMessageToTab("System", $"Failed to process DCC SEND: {ex.Message}");
                        }
                    }

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
                    if (users.Add(user) && !UsersList.Items.Contains(user)) // بررسی اضافه شدن به HashSet
                    {

                        UsersList.Items.Add(user);
                    }
                }
                SortListBox(UsersList); // مرتب کردن لیست کاربران
            });
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

            // TextBox برای نمایش پیام‌ها
            TextBox chatBox = new TextBox
            {
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(5)
            };
            Grid.SetRow(chatBox, 0);

            // Grid برای قسمت پایین (ورودی پیام و دکمه‌ها)
            Grid messageGrid = new Grid
            {
                Margin = new Thickness(5)
            };
            messageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // برای آیکون گیره
            messageGrid.ColumnDefinitions.Add(new ColumnDefinition()); // برای اینپوت پیام
            messageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // برای دکمه ارسال

            // آیکون گیره برای ارسال فایل
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
            fileButton.Click += (sender, e) =>
            {
                SendFileWithDCC(header);
            };
            Grid.SetColumn(fileButton, 0);

            // TextBox برای ورودی پیام
            TextBox messageInput = new TextBox
            {
                Name = "MessageInput",
                Height = 50,
                Margin = new Thickness(5, 0, 5, 0)
            };
            Grid.SetColumn(messageInput, 1);

            // دکمه ارسال پیام
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

            // افزودن آیتم‌ها به Grid
            messageGrid.Children.Add(fileButton);
            messageGrid.Children.Add(messageInput);
            messageGrid.Children.Add(sendButton);

            // افزودن Gridها به ChatGrid
            chatGrid.Children.Add(chatBox);
            Grid.SetRow(chatBox, 0);

            chatGrid.Children.Add(messageGrid);
            Grid.SetRow(messageGrid, 1);

            // افزودن تب جدید
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


        private async void ChannelChatMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ChannelsList.SelectedItem is string selectedChannel)
            {
                // ارسال دستور JOIN به سرور
                await writer.WriteLineAsync($"JOIN {selectedChannel}");
                currentChannel = selectedChannel;

                // ایجاد تب جدید برای کانال
                Dispatcher.Invoke(() =>
                {
                    ChatBox.AppendText($"Joining channel: {selectedChannel}\n");
                    CreateChatTab(selectedChannel);
                });

                // درخواست لیست کاربران کانال
                await writer.WriteLineAsync($"NAMES {selectedChannel}");
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

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("IRC Chat Application\nVersion 1.0", "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void SendFileWithDCC(string recipient)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string fileName = Path.GetFileName(filePath);
                byte[] fileData = File.ReadAllBytes(filePath);

                // ایجاد یک سرور TCP برای انتقال فایل
                TcpListener tcpListener = new TcpListener(System.Net.IPAddress.Any, 0);
                tcpListener.Start();

                // دریافت پورت محلی
                int localPort = ((System.Net.IPEndPoint)tcpListener.LocalEndpoint).Port;
                //string localIPAddress = GetLocalIPAddress();
                string localIPAddress = GetPublicIPAddress();

                // ارسال درخواست DCC به گیرنده
                string dccRequest = $"PRIVMSG {recipient} :\u0001DCC SEND {fileName} {IPToInteger(localIPAddress)} {localPort} {fileData.Length}\u0001";
                await writer.WriteLineAsync(dccRequest);

                Dispatcher.Invoke(() =>
                {
                    AppendMessageToTab(recipient, $"DCC request sent for file: {fileName}");
                });

                // منتظر اتصال گیرنده
                using (TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync())
                {
                    // ارسال فایل
                    using (NetworkStream networkStream = tcpClient.GetStream())
                    {
                        int bufferSize = 8192;
                        byte[] buffer = new byte[bufferSize];
                        int bytesSent = 0;

                        using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                        {
                            int bytesRead;
                            while ((bytesRead = fileStream.Read(buffer, 0, bufferSize)) > 0)
                            {
                                await networkStream.WriteAsync(buffer, 0, bytesRead);
                                bytesSent += bytesRead;

                                // نمایش پیشرفت ارسال
                                Dispatcher.Invoke(() =>
                                {
                                    AppendMessageToTab(recipient, $"Sending file: {fileName} ({bytesSent * 100 / fileData.Length}%)");
                                });
                            }
                        }
                        await networkStream.FlushAsync();

                    }
                }

                tcpListener.Stop();

                Dispatcher.Invoke(() =>
                {
                    AppendMessageToTab(recipient, $"File {fileName} sent successfully!");
                });
            }
        }
        private int IPToInteger(string ipAddress)
        {
            string[] ipParts = ipAddress.Split('.');
            return (int.Parse(ipParts[0]) << 24) |
                   (int.Parse(ipParts[1]) << 16) |
                   (int.Parse(ipParts[2]) << 8) |
                   int.Parse(ipParts[3]);
        }

        private string GetLocalIPAddress()
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("Local IP Address Not Found!");
        }


        private string IntegerToIP(long ipAddress)
        {
            return string.Join(".", new[]
            {
        (ipAddress >> 24) & 0xFF,
        (ipAddress >> 16) & 0xFF,
        (ipAddress >> 8) & 0xFF,
        ipAddress & 0xFF
    });
        }


        private string GetPublicIPAddress()
        {
            using (var client = new WebClient())
            {
                return client.DownloadString("https://api.ipify.org").Trim();
            }
        }



        private async Task ReceiveFile(string fileName, string ipAddress, int port, long fileSize, string sender)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    AppendMessageToTab(sender, $"Connecting to IP: {ipAddress}, Port: {port}");


                    // تنظیم Timeout برای جلوگیری از قفل شدن
                    client.ReceiveTimeout = 30000; // 30 ثانیه
                    client.SendTimeout = 30000;   // 30 ثانیه

                    await client.ConnectAsync(ipAddress, port);
                    AppendMessageToTab(sender, "Connected to server");


                    using (NetworkStream networkStream = client.GetStream())
                    {
                        // مسیر ذخیره فایل
                        string savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);

                        using (FileStream fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                        {
                            byte[] buffer = new byte[8192];
                            long totalReceived = 0;

                            while (totalReceived < fileSize)
                            {
                                int bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length);
                                if (bytesRead == 0)
                                    break;

                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalReceived += bytesRead;

                                // نمایش پیشرفت دانلود
                                Dispatcher.Invoke(() =>
                                {
                                    AppendMessageToTab(sender, $"Receiving file: {fileName} ({totalReceived * 100 / fileSize}%)");
                                });
                            }
                        }

                        Dispatcher.Invoke(() =>
                        {
                            AppendMessageToTab(sender, $"File received and saved as: {fileName}");
                            MessageBox.Show($"File '{fileName}' received and saved in Documents.", "File Received", MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    AppendMessageToTab(sender, $"Failed to receive file '{fileName}': {ex.Message}");
                    MessageBox.Show($"Failed to receive file '{fileName}': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
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
