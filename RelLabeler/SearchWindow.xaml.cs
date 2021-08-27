using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Data.Sqlite;

namespace RelLabeler
{
    /// <summary>
    /// SearchWindow.xaml 的交互逻辑
    /// </summary>
    public partial class SearchWindow : Window
    {
        readonly MainWindow mainWindow;
        public MainWindow secondaryWindow = null;

        readonly List<Tuple<string, int>> result = new List<Tuple<string, int>>();

        public SearchWindow(MainWindow mainWindow)
        {
            InitializeComponent();

            this.mainWindow = mainWindow;
        }

        void Search()
        {
            if (SearchBox.Text == "")
                return;
            ResultList.Items.Clear();
            result.Clear();
            FileInfo fileInfo = new FileInfo(mainWindow.filePath);
            foreach (var f in fileInfo.Directory.GetFiles("*.db"))
            {
                using (var connection = new SqliteConnection($"Data Source={f.FullName}"))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        select line_id from data
                            where line_id is not null and sentence like $pattern order by line_id;
                    ";
                    command.Parameters.AddWithValue("$pattern", "%" + SearchBox.Text + "%");
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new Tuple<string, int>(f.FullName, reader.GetInt32(0)));
                            ResultList.Items.Add(f.Name + " - " + reader.GetInt32(0));
                        }
                    }
                }
            }
        }

        Tuple<string, int> mainWindowCurrent
        {
            get
            {
                return new Tuple<string, int>(
                    mainWindow.filePath,
                    mainWindow.SelectedSentence.SelectedItem != null ? (int)mainWindow.SelectedSentence.SelectedItem : -1
                    );
            }
        }

        Tuple<string, int> secondaryWindowCurrent
        {
            get
            {
                return new Tuple<string, int>(
                    secondaryWindow.filePath,
                    secondaryWindow.SelectedSentence.SelectedItem != null ? (int)secondaryWindow.SelectedSentence.SelectedItem : -1
                    );
            }
        }

        public bool CheckOrDisplay(MainWindow window, Tuple<string, int> tuple)
        {
            if (secondaryWindow == null) return false;
            if (window == mainWindow && secondaryWindowCurrent.Equals(tuple))
            {
                secondaryWindow.Activate();
                return true;
            }
            else if (window == secondaryWindow && mainWindowCurrent.Equals(tuple))
            {
                mainWindow.Activate();
                return true;
            }
            return false;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            mainWindow.searchWindow = null;
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            Search();
        }

        private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultList.SelectedIndex != -1)
            {
                if (mainWindowCurrent.Equals(result[ResultList.SelectedIndex]))
                {
                    mainWindow.Activate();
                }
                else
                {
                    if (secondaryWindow == null)
                    {
                        secondaryWindow = new MainWindow(this);
                    }
                    secondaryWindow.OpenFile(result[ResultList.SelectedIndex].Item1, result[ResultList.SelectedIndex].Item2, true);
                    secondaryWindow.Show();
                    secondaryWindow.Activate();
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (secondaryWindow != null)
            {
                if (MessageBox.Show(
                    "This is the search window. Closing it will close the secondary window as well. Are you sure you want to close it anyway?",
                    "Closing Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Exclamation,
                    MessageBoxResult.No) == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }
                else
                {
                    secondaryWindow.Close();
                }
            }
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Search();
            }
        }
    }
}
