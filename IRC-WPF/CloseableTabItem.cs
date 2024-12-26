using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;

namespace IRC_WPF
{
    public class CloseableTabItem : TabItem
    {
        public static readonly DependencyProperty IsCloseableProperty =
            DependencyProperty.Register("IsCloseable", typeof(bool), typeof(CloseableTabItem), new PropertyMetadata(true));

        public bool IsCloseable
        {
            get { return (bool)GetValue(IsCloseableProperty); }
            set { SetValue(IsCloseableProperty, value); }
        }
    }
}
